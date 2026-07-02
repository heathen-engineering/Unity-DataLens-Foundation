using System;
using System.Collections.Generic;
using Heathen.GameplayTags;

namespace Heathen.DataLens
{
    /// <summary>
    /// The Lens owns a native worker pool and runs Systems over a <see cref="DataStore"/> in
    /// parallel. Results are identical to a single-threaded run regardless of thread count, because
    /// each thread processes a disjoint, independent row range. A1/A3 walking-skeleton slice — view
    /// refresh and commit consolidation arrive in later phases.
    /// </summary>
    public sealed class Lens : IDisposable
    {
        private IntPtr _handle;
        private DataLensSchema _schema;        // set when built from a schema (the orchestrator path)
        private DataStore[] _ownedStores;      // the native stores the Lens created from the schema
        private readonly List<ILensView> _views = new List<ILensView>(); // registered views for Lens.Commit

        /// <summary>Create a Lens with a worker pool. <paramref name="threadCount"/> of 0 uses hardware concurrency.
        /// Internal: a schema-less Lens only drives the primitive (Layer B) store/System surface used by the
        /// Foundation's own tests; consumers build a Lens from a <see cref="DataLensSchema"/> and work through Views.</summary>
        internal Lens(int threadCount = 0)
        {
            _handle = DataLensNative.dl_lens_create(threadCount);
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Native dl_lens_create failed.");
            DataLensSubsystem.RegisterLens();
        }

        /// <summary>
        /// Build a Lens from a <see cref="DataLensSchema"/>: it creates the native stores (type-blind:
        /// strides + defaults derived from each <see cref="DataColumn"/>) and owns them, ready for
        /// <see cref="CreateView{TRow}"/> and <see cref="Tick()"/>. This is the consumer entry point —
        /// gameplay code works through Views, never the stores.
        /// </summary>
        public Lens(DataLensSchema schema, int threadCount = 0) : this(threadCount)
        {
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));
            _ownedStores = new DataStore[schema.Stores.Count];
            for (int i = 0; i < schema.Stores.Count; i++)
                _ownedStores[i] = BuildStore(schema.Stores[i]);
        }

        private static DataStore BuildStore(DataStoreSchema s)
        {
            int n = s.Columns.Count;
            var tags = new ulong[n];
            var strides = new ulong[n];
            int total = 0;
            bool anyDefault = false;
            for (int c = 0; c < n; c++)
            {
                tags[c] = s.Columns[c].Id;
                strides[c] = (ulong)s.Columns[c].Stride;
                total += s.Columns[c].Stride;
                if (s.Columns[c].Default != null) anyDefault = true;
            }

            byte[] defaults = null;
            if (anyDefault)
            {
                defaults = new byte[total]; // columns without a default stay zero
                int off = 0;
                for (int c = 0; c < n; c++)
                {
                    var d = s.Columns[c].Default;
                    if (d != null) Array.Copy(d, 0, defaults, off, d.Length);
                    off += s.Columns[c].Stride;
                }
            }

            return new DataStore(tags, strides, defaults, (ulong)s.Capacity);
        }

        // CLR type -> Core value-type code (only needed for a scope predicate's typed comparison).
        private static DataLensValueType TypeCode(Type t)
        {
            if (t == typeof(float))  return DataLensValueType.Float;
            if (t == typeof(double)) return DataLensValueType.Double;
            if (t == typeof(int))    return DataLensValueType.Int32;
            if (t == typeof(uint))   return DataLensValueType.UInt32;
            if (t == typeof(long))   return DataLensValueType.Int64;
            if (t == typeof(ulong))  return DataLensValueType.UInt64;
            if (t == typeof(short))  return DataLensValueType.Int16;
            if (t == typeof(ushort)) return DataLensValueType.UInt16;
            if (t == typeof(sbyte))  return DataLensValueType.Int8;
            if (t == typeof(byte))   return DataLensValueType.UInt8;
            if (t == typeof(bool))   return DataLensValueType.Bool;
            return DataLensValueType.Int32;
        }

        /// <summary>
        /// Open a read/write <see cref="DataView{TRow}"/> for a record type: the consumer entry point. The
        /// record's <c>[DataLensColumn]</c> fields are the projection (in declaration order), its read-only
        /// flags drive write-back, and its static <c>From()</c> supplies the prime store, dereference joins
        /// and filters (DataLens-Spec §6.4.1). The consumer never names a store or column index.
        /// </summary>
        public DataView<TRow> View<TRow>(int weight = 0) where TRow : unmanaged, IDataLensViewRecord
        {
            if (_schema == null) throw new InvalidOperationException("This Lens was not built from a schema.");

            GameplayTag[] select = ViewRecordMeta<TRow>.SelectTags;
            bool[] readOnly = ViewRecordMeta<TRow>.ReadOnly;
            Type[] fieldTypes = ViewRecordMeta<TRow>.FieldTypes;
            DataLensFrom from = ViewRecordMeta<TRow>.From();
            if (from == null) throw new InvalidOperationException($"{typeof(TRow).Name}.From() returned null.");

            // Build the marshalling plan: null when every field is byte-compatible with its column (zero-copy),
            // otherwise a converter kept in step with the payload. Build validates the record's layout and
            // throws a clear, record-named error on a slip.
            ViewMarshaller<TRow> marshaller = ViewMarshaller<TRow>.Build(select, fieldTypes, _schema);

            return CreateView<TRow>(from.PrimeStore, select, from.JoinsArray, scope: null,
                writeBack: true, readOnly: readOnly, filter: from.Filter, weight: weight, marshaller: marshaller);
        }

        /// <summary>
        /// Compile a read/write <see cref="DataView{TRow}"/> over this Lens's stores: a base store + index
        /// joins + an ordered projection (column tags in <typeparamref name="TRow"/> field order) + scope.
        /// When <paramref name="writeBack"/> is true, edits commit back to each projected column's own
        /// record (the edit-in-place case); columns flagged in <paramref name="readOnly"/> are read but never
        /// written. This is the internal tag-addressed builder; consumers use the record-based
        /// <see cref="View{TRow}"/>.
        /// </summary>
        internal DataView<TRow> CreateView<TRow>(GameplayTag baseStore, GameplayTag[] select,
            ViewJoin[] joins = null, ViewScope[] scope = null, bool writeBack = true, bool[] readOnly = null,
            DataLensPredicate filter = null, int weight = 0, ViewMarshaller<TRow> marshaller = null) where TRow : unmanaged
        {
            IntPtr handle = BuildViewHandle(baseStore, select, joins, scope, writeBack, readOnly, filter);
            var view = new DataView<TRow>(handle, Handles(_ownedStores), weight, marshaller); // refreshes + checks layout
            _views.Add(view);
            return view;
        }

        // Build + configure a native rwview handle (joins / projection / scope / filter / write-back) from
        // GameplayTags. Shared by the typed CreateView<TRow> and the dynamic View.
        private IntPtr BuildViewHandle(GameplayTag baseStore, GameplayTag[] select, ViewJoin[] joins,
            ViewScope[] scope, bool writeBack, bool[] readOnly, DataLensPredicate filter)
        {
            if (_schema == null) throw new InvalidOperationException("This Lens was not built from a schema.");
            if (select == null || select.Length == 0) throw new ArgumentException("A view needs at least one projected column.", nameof(select));
            joins = joins ?? Array.Empty<ViewJoin>();
            scope = scope ?? Array.Empty<ViewScope>();

            int baseIdx = _schema.FindStore(baseStore);
            if (baseIdx < 0) throw new ArgumentException($"Unknown base store {(ulong)baseStore}.");

            // joins -> native joins; track store index -> view source index (base = 0, join k -> k+1).
            var nJoins = new DataLensNative.dl_view_join[joins.Length];
            var sourceOf = new Dictionary<int, int> { [baseIdx] = 0 };
            for (int j = 0; j < joins.Length; j++)
            {
                int targetIdx = _schema.FindStore(joins[j].TargetStore);
                if (targetIdx < 0) throw new ArgumentException($"Unknown join target store {(ulong)joins[j].TargetStore}.");
                ulong idxCol = 0;
                if (!joins[j].IsAligned)
                {
                    if (!_schema.ResolveColumn(joins[j].IndexColumn, out int s, out int c) || s != baseIdx)
                        throw new ArgumentException("A dereference join's index column must belong to the base store.");
                    idxCol = (ulong)c;
                }
                nJoins[j] = new DataLensNative.dl_view_join
                {
                    target_store = (ulong)targetIdx,
                    aligned = joins[j].IsAligned ? 1 : 0,
                    index_column = idxCol,
                    absent_sentinel = joins[j].AbsentSentinel
                };
                sourceOf[targetIdx] = j + 1;
            }

            // select -> native columns (resolve each to (store,col) -> (source, col)).
            var nCols = new DataLensNative.dl_view_column[select.Length];
            var srcStore = new int[select.Length];
            var srcCol = new int[select.Length];
            for (int k = 0; k < select.Length; k++)
            {
                if (!_schema.ResolveColumn(select[k], out int s, out int c))
                    throw new ArgumentException($"Unknown projected column {(ulong)select[k]}.");
                if (!sourceOf.TryGetValue(s, out int src))
                    throw new ArgumentException("A projected column's store is neither the base nor a join target.");
                nCols[k] = new DataLensNative.dl_view_column { source = (ulong)src, column = (ulong)c };
                srcStore[k] = s;
                srcCol[k] = c;
            }

            // scope -> native scope (base store columns only).
            var nScope = new DataLensNative.dl_view_scope[scope.Length];
            for (int k = 0; k < scope.Length; k++)
            {
                if (!_schema.ResolveColumn(scope[k].Column, out int s, out int c) || s != baseIdx)
                    throw new ArgumentException("A scope column must belong to the base store.");
                _schema.TryGetColumn(scope[k].Column, out DataColumn dc);
                nScope[k] = new DataLensNative.dl_view_scope
                {
                    column = (ulong)c,
                    type = (int)TypeCode(dc.Type),
                    op = (int)scope[k].Op,
                    ivalue = scope[k].IValue,
                    dvalue = scope[k].DValue
                };
            }

            IntPtr handle = DataLensNative.dl_rwview_create((ulong)baseIdx,
                nJoins, nJoins.Length, nCols, nCols.Length, nScope, nScope.Length);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Native dl_rwview_create failed.");

            // Compile the boolean filter tree to the Core's RPN scope program (post-order). A leaf's column
            // resolves to a (source, column) via the same sourceOf map the projection uses, so leaves may
            // address prime-store OR dereferenced columns; the program evaluates per base row after joins.
            if (filter != null)
            {
                var prog = new List<DataLensNative.dl_view_predicate>();
                CompilePredicate(filter, sourceOf, prog);
                var program = prog.ToArray();
                DataLensNative.dl_rwview_set_scope_program(handle, program, program.Length);
            }

            if (writeBack)
            {
                // Each writable projected column commits back to its own (store, column): Update for edited
                // rows, Insert for added rows. readOnly columns are excluded from the maps (read-only fields).
                // (Multi-store linked Insert — writing a new trait row index back into the catalogue — is
                // deferred; single-store Insert works.) Delete frees a Removed row's source record regardless
                // of readOnly, so every sourced store is in the delete set.
                var map = new List<DataLensNative.dl_view_write>(select.Length);
                var deleteSet = new List<ulong>(); // distinct source stores a Removed row frees its record from
                for (int k = 0; k < select.Length; k++)
                {
                    if (readOnly == null || !readOnly[k])
                        map.Add(new DataLensNative.dl_view_write
                        {
                            view_column = (ulong)k,
                            target_store = (ulong)srcStore[k],
                            target_column = (ulong)srcCol[k]
                        });
                    if (!deleteSet.Contains((ulong)srcStore[k])) deleteSet.Add((ulong)srcStore[k]);
                }
                var writeMap = map.ToArray();
                var deleteStores = deleteSet.ToArray();
                DataLensNative.dl_rwview_set_writeback(handle, writeMap, writeMap.Length, writeMap, writeMap.Length,
                    deleteStores, deleteStores.Length);
            }

            return handle;
        }

        /// <summary>
        /// Open a dynamic, column-addressed read/write view (no compile-time row struct): the data-driven
        /// consumer surface (DataLens-Spec §6.4.1). <paramref name="from"/> supplies the prime store, any
        /// dereference joins and the filter; <paramref name="select"/> is the projected columns. Cells are
        /// read/written by column tag — a field is <c>(columnId, rowIndex, value)</c>. This is what an engine
        /// like HATE rides; hand-written/codegen'd-struct consumers use <see cref="View{TRow}"/>.
        /// </summary>
        public DataLensView View(DataLensFrom from, GameplayTag[] select, bool[] readOnly = null, int weight = 0)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            IntPtr handle = BuildViewHandle(from.PrimeStore, select, from.JoinsArray, scope: null,
                writeBack: true, readOnly: readOnly, filter: from.Filter);
            var view = new DataLensView(handle, Handles(_ownedStores), weight, select);
            _views.Add(view);
            return view;
        }

        /// <summary>
        /// Commit every registered view together, ascending by weight: a heavier-weight view commits later
        /// and so wins any cell two views both wrote (DataLens-Spec §6.4.1 flat weighted commit). Returns the
        /// total store ops applied. (The contention-free one-worker-per-column parallel blit is a deferred
        /// optimisation; the result here is identical.)
        /// </summary>
        public ulong Commit()
        {
            _views.RemoveAll(v => !v.IsLive);
            var live = new List<ILensView>(_views);
            live.Sort((a, b) => a.Weight.CompareTo(b.Weight));
            ulong ops = 0;
            for (int i = 0; i < live.Count; i++)
                ops += live[i].CommitInternal();
            return ops;
        }

        // ── Tag-addressed Store Systems (the columnar fast path consumers ride) ──
        // A Store System is an elementwise streaming kernel over ONE store: targetCol = targetCol OP operand
        // (or operandCol), across all live rows, optionally gated by a predicate column. Addressed by store +
        // column GameplayTags; dispatched to the typed kernel by the column's declared type.

        private DataStore ResolveStore(GameplayTag store, out int storeIdx)
        {
            if (_schema == null) throw new InvalidOperationException("This Lens was not built from a schema.");
            storeIdx = _schema.FindStore(store);
            if (storeIdx < 0) throw new ArgumentException($"Unknown store {(ulong)store}.");
            return _ownedStores[storeIdx];
        }

        private int ResolveSystemColumn(GameplayTag column, int storeIdx, out DataLensValueType type)
        {
            if (!_schema.ResolveColumn(column, out int s, out int c) || s != storeIdx)
                throw new ArgumentException($"Column {(ulong)column} does not belong to the target store.");
            _schema.TryGetColumn(column, out DataColumn dc);
            type = TypeCode(dc.Type);
            return c;
        }

        /// <summary>Scalar Store System: <c>targetCol = targetCol OP operand</c> over all live rows of a store.</summary>
        public ulong RunSystem(GameplayTag store, GameplayTag targetCol, SystemOp op, double operand)
        {
            DataStore ds = ResolveStore(store, out int si);
            int c = ResolveSystemColumn(targetCol, si, out DataLensValueType t);
            switch (t)
            {
                case DataLensValueType.Float: return RunFloat(ds, (ulong)c, op, (float)operand);
                case DataLensValueType.Int32: return RunInt(ds, (ulong)c, op, (int)operand);
                default: throw new NotSupportedException($"RunSystem supports Float/Int32 columns (got {t}).");
            }
        }

        /// <summary>Scalar Store System gated where (compareCol CMP threshold).</summary>
        public ulong RunSystem(GameplayTag store, GameplayTag targetCol, SystemOp op, double operand,
            GameplayTag compareCol, CompareOp cmp, double threshold)
        {
            DataStore ds = ResolveStore(store, out int si);
            int c = ResolveSystemColumn(targetCol, si, out DataLensValueType t);
            int pc = ResolveSystemColumn(compareCol, si, out DataLensValueType _);
            switch (t)
            {
                case DataLensValueType.Float: return RunFloat(ds, (ulong)c, op, (float)operand, (ulong)pc, cmp, (float)threshold);
                case DataLensValueType.Int32: return RunInt(ds, (ulong)c, op, (int)operand, (ulong)pc, cmp, (int)threshold);
                default: throw new NotSupportedException($"RunSystem supports Float/Int32 columns (got {t}).");
            }
        }

        /// <summary>Cross-column Store System: <c>targetCol = targetCol OP operandCol</c> (e.g. clamp, regen).</summary>
        public ulong RunSystemColumn(GameplayTag store, GameplayTag targetCol, SystemOp op, GameplayTag operandCol)
        {
            DataStore ds = ResolveStore(store, out int si);
            int tc = ResolveSystemColumn(targetCol, si, out DataLensValueType t);
            int oc = ResolveSystemColumn(operandCol, si, out DataLensValueType _);
            switch (t)
            {
                case DataLensValueType.Float: return RunFloatColumn(ds, (ulong)tc, op, (ulong)oc);
                case DataLensValueType.Int32: return RunIntColumn(ds, (ulong)tc, op, (ulong)oc);
                default: throw new NotSupportedException($"RunSystemColumn supports Float/Int32 columns (got {t}).");
            }
        }

        // Lower a filter tree to the Core's RPN scope program (post-order: children then the connective).
        // sourceOf maps a resolved store index -> view source (0 = base, k>=1 = the (k-1)th join).
        private void CompilePredicate(DataLensPredicate node, Dictionary<int, int> sourceOf,
            List<DataLensNative.dl_view_predicate> outp)
        {
            switch (node.Kind)
            {
                case DataLensPredicate.NodeKind.Leaf:
                {
                    if (!_schema.ResolveColumn(node.Column, out int s, out int c))
                        throw new ArgumentException($"Filter references unknown column {(ulong)node.Column}.");
                    if (!sourceOf.TryGetValue(s, out int src))
                        throw new ArgumentException("A filter column's store is neither the base nor a join target.");
                    _schema.TryGetColumn(node.Column, out DataColumn dc);
                    var leaf = new DataLensNative.dl_view_predicate
                    {
                        kind = (int)DataLensPredicate.NodeKind.Leaf,
                        is_range = node.IsRange ? 1 : 0,
                        source = src,
                        column = c,
                        type = (int)TypeCode(dc.Type),
                        op = (int)node.Op
                    };
                    if (node.IsFloat) { leaf.dvalue = node.DLo; leaf.dvalue_hi = node.DHi; }
                    else { leaf.ivalue = node.ILo; leaf.ivalue_hi = node.IHi; }
                    outp.Add(leaf);
                    break;
                }
                case DataLensPredicate.NodeKind.Not:
                    CompilePredicate(node.Children[0], sourceOf, outp);
                    outp.Add(new DataLensNative.dl_view_predicate { kind = (int)DataLensPredicate.NodeKind.Not });
                    break;
                default: // And / Or: left-fold n children into binary connectives
                {
                    CompilePredicate(node.Children[0], sourceOf, outp);
                    for (int i = 1; i < node.Children.Length; i++)
                    {
                        CompilePredicate(node.Children[i], sourceOf, outp);
                        outp.Add(new DataLensNative.dl_view_predicate { kind = (int)node.Kind });
                    }
                    break;
                }
            }
        }

        /// <summary>Schedule a view so this Lens's <see cref="Tick()"/> commits then re-hydrates it on cadence.</summary>
        public void Schedule<TRow>(DataView<TRow> view, ulong period, ulong phase = 0) where TRow : unmanaged
            => DataLensNative.dl_lens_add_scheduled_rwview(_handle, view.Handle, period, phase);

        /// <summary>Advance one tick over this Lens's owned stores (commit due views, run Systems, re-hydrate).</summary>
        public ulong Tick() => Tick(_ownedStores ?? Array.Empty<DataStore>());

        /// <summary>Number of threads the pool runs with (including the calling thread).</summary>
        public int ThreadCount => DataLensNative.dl_lens_thread_count(_handle);

        // Run a conditional column System over the store in parallel. Returns rows affected.

        internal ulong RunFloat(DataStore store, ulong targetCol, SystemOp op, float operand)
            => DataLensNative.dl_lens_run_f32(_handle, store.Handle, targetCol, (int)op, operand, 0, 0, 0, 0f);

        internal ulong RunFloat(DataStore store, ulong targetCol, SystemOp op, float operand, ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_f32(_handle, store.Handle, targetCol, (int)op, operand, 1, compareCol, (int)cmp, threshold);

        internal ulong RunInt(DataStore store, ulong targetCol, SystemOp op, int operand)
            => DataLensNative.dl_lens_run_i32(_handle, store.Handle, targetCol, (int)op, operand, 0, 0, 0, 0);

        internal ulong RunInt(DataStore store, ulong targetCol, SystemOp op, int operand, ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_i32(_handle, store.Handle, targetCol, (int)op, operand, 1, compareCol, (int)cmp, threshold);

        // Parallel cross-column Systems (A3.3): operand read per-row from operandCol.

        internal ulong RunFloatColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol)
            => DataLensNative.dl_lens_run_col_f32(_handle, store.Handle, targetCol, (int)op, operandCol, 0, 0, 0, 0f);

        internal ulong RunFloatColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_col_f32(_handle, store.Handle, targetCol, (int)op, operandCol, 1, compareCol, (int)cmp, threshold);

        internal ulong RunIntColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol)
            => DataLensNative.dl_lens_run_col_i32(_handle, store.Handle, targetCol, (int)op, operandCol, 0, 0, 0, 0);

        internal ulong RunIntColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_col_i32(_handle, store.Handle, targetCol, (int)op, operandCol, 1, compareCol, (int)cmp, threshold);

        // ── Curved cross-column Systems (A3.11) ──────────────────────────────
        // targetCol = targetCol OP curve(operandCol[r]): the per-row operand is normalised over the
        // curve's range and passed through its shape before the combine — one HATE §8 consideration
        // (e.g. score *= curve(distance)). Run in parallel across the pool. Returns rows affected.

        /// <summary>Curved cross-column Float System (A3.11). See <see cref="Curve"/>.</summary>
        internal ulong RunFloatCurved(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, Curve curve)
            => DataLensNative.dl_lens_run_curved_f32(_handle, store.Handle, targetCol, (int)op, operandCol,
                (int)curve.Type, curve.Min, curve.Max, curve.P0, curve.P1, curve.Invert ? 1 : 0, 0, 0, 0, 0f);

        /// <summary>Curved cross-column Float System gated on (compareCol CMP threshold).</summary>
        internal ulong RunFloatCurved(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, Curve curve,
            ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_curved_f32(_handle, store.Handle, targetCol, (int)op, operandCol,
                (int)curve.Type, curve.Min, curve.Max, curve.P0, curve.P1, curve.Invert ? 1 : 0, 1, compareCol, (int)cmp, threshold);

        /// <summary>Curved cross-column Int32 System (A3.11). See <see cref="Curve"/>.</summary>
        internal ulong RunIntCurved(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, Curve curve)
            => DataLensNative.dl_lens_run_curved_i32(_handle, store.Handle, targetCol, (int)op, operandCol,
                (int)curve.Type, curve.Min, curve.Max, curve.P0, curve.P1, curve.Invert ? 1 : 0, 0, 0, 0, 0);

        /// <summary>Curved cross-column Int32 System gated on (compareCol CMP threshold).</summary>
        internal ulong RunIntCurved(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, Curve curve,
            ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_curved_i32(_handle, store.Handle, targetCol, (int)op, operandCol,
                (int)curve.Type, curve.Min, curve.Max, curve.P0, curve.P1, curve.Invert ? 1 : 0, 1, compareCol, (int)cmp, threshold);

        // ── Counter-based noise (A3.12) ──────────────────────────────────────
        // `target = target OP noise` (fill) or `target = target OP (operandCol[r] * noise)` (perturb),
        // noise = lo + (hi-lo)*u01(row, tick, seed). The PRNG is stateless and keyed on the GLOBAL row
        // index, so results are reproducible across runs/machines/replay and identical serial-vs-parallel.
        // `tick` is the caller's sim clock (pass the same tick for a reproducible per-tick draw). The
        // perturb form is the HATE §8.4 `Score += Variance * Noise`: zero-Variance rows are unchanged.

        /// <summary>Counter-based noise fill (A3.12): targetCol = targetCol OP noise over [noiseLo,noiseHi).</summary>
        internal ulong RunFloatNoise(DataStore store, ulong targetCol, SystemOp op, float noiseLo, float noiseHi, ulong seed, ulong tick)
            => DataLensNative.dl_lens_run_noise_f32(_handle, store.Handle, targetCol, (int)op, noiseLo, noiseHi, seed, tick, 0, 0, 0, 0f);

        /// <summary>Counter-based Float noise fill gated on (compareCol CMP threshold).</summary>
        internal ulong RunFloatNoise(DataStore store, ulong targetCol, SystemOp op, float noiseLo, float noiseHi, ulong seed, ulong tick,
            ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_noise_f32(_handle, store.Handle, targetCol, (int)op, noiseLo, noiseHi, seed, tick, 1, compareCol, (int)cmp, threshold);

        /// <summary>Counter-based Int32 noise fill.</summary>
        internal ulong RunIntNoise(DataStore store, ulong targetCol, SystemOp op, int noiseLo, int noiseHi, ulong seed, ulong tick)
            => DataLensNative.dl_lens_run_noise_i32(_handle, store.Handle, targetCol, (int)op, noiseLo, noiseHi, seed, tick, 0, 0, 0, 0);

        /// <summary>Counter-based Int32 noise fill gated on (compareCol CMP threshold).</summary>
        internal ulong RunIntNoise(DataStore store, ulong targetCol, SystemOp op, int noiseLo, int noiseHi, ulong seed, ulong tick,
            ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_noise_i32(_handle, store.Handle, targetCol, (int)op, noiseLo, noiseHi, seed, tick, 1, compareCol, (int)cmp, threshold);

        /// <summary>Counter-based noise perturb (A3.12 / HATE §8.4): targetCol = targetCol OP (operandCol[r] * noise).</summary>
        internal ulong RunFloatNoisePerturb(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, float noiseLo, float noiseHi, ulong seed, ulong tick)
            => DataLensNative.dl_lens_run_noise_perturb_f32(_handle, store.Handle, targetCol, (int)op, operandCol, noiseLo, noiseHi, seed, tick, 0, 0, 0, 0f);

        /// <summary>Float noise perturb gated on (compareCol CMP threshold).</summary>
        internal ulong RunFloatNoisePerturb(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, float noiseLo, float noiseHi, ulong seed, ulong tick,
            ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_noise_perturb_f32(_handle, store.Handle, targetCol, (int)op, operandCol, noiseLo, noiseHi, seed, tick, 1, compareCol, (int)cmp, threshold);

        /// <summary>Int32 noise perturb: targetCol = targetCol OP (operandCol[r] * noise).</summary>
        internal ulong RunIntNoisePerturb(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, int noiseLo, int noiseHi, ulong seed, ulong tick)
            => DataLensNative.dl_lens_run_noise_perturb_i32(_handle, store.Handle, targetCol, (int)op, operandCol, noiseLo, noiseHi, seed, tick, 0, 0, 0, 0);

        /// <summary>Int32 noise perturb gated on (compareCol CMP threshold).</summary>
        internal ulong RunIntNoisePerturb(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, int noiseLo, int noiseHi, ulong seed, ulong tick,
            ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_noise_perturb_i32(_handle, store.Handle, targetCol, (int)op, operandCol, noiseLo, noiseHi, seed, tick, 1, compareCol, (int)cmp, threshold);

        // ── Argmax-across-columns (A3.13) ────────────────────────────────────
        // Reduce K score columns to a per-row Choice index column (the HATE §8.5 AI selection "pick"):
        // for each live actor, write the index of the largest score into choiceCol. Ties resolve to the
        // lowest index; a winning score below minScore writes noChoice (default -1 = "do nothing", which
        // composes with the §8.5 Choice>=0 Command override). Runs in parallel across the pool.

        /// <summary>Argmax over Float score columns -> Choice index column (A3.13).</summary>
        internal ulong RunFloatArgmax(DataStore store, ulong choiceCol, ulong[] scoreCols,
            float minScore = float.NegativeInfinity, int noChoice = -1)
            => DataLensNative.dl_lens_run_argmax_f32(_handle, store.Handle, choiceCol,
                scoreCols ?? System.Array.Empty<ulong>(), (ulong)(scoreCols?.Length ?? 0), minScore, noChoice);

        /// <summary>Argmax over Int32 score columns -> Choice index column (A3.13).</summary>
        internal ulong RunIntArgmax(DataStore store, ulong choiceCol, ulong[] scoreCols,
            int minScore = int.MinValue, int noChoice = -1)
            => DataLensNative.dl_lens_run_argmax_i32(_handle, store.Handle, choiceCol,
                scoreCols ?? System.Array.Empty<ulong>(), (ulong)(scoreCols?.Length ?? 0), minScore, noChoice);

        // ── Batched Systems (A3.4) ───────────────────────────────────────────
        // Run a whole batch of data-described Systems in one call. The Lens runs non-conflicting
        // Systems concurrently and preserves submission order for conflicting ones, so the result is
        // deterministic and identical to running them one by one. Returns total rows affected.

        internal ulong RunBatch(params SystemDesc[] systems)
        {
            if (systems == null || systems.Length == 0) return 0;
            return DataLensNative.dl_lens_run_batch(_handle, systems, (ulong)systems.Length);
        }

        /// <summary>
        /// Run a batch but only over rows whose Simulation LOD is within [minLod, maxLod] — the
        /// "this tick runs at fidelity band [min,max]" model (A3.5). The band applies to every System
        /// in the batch. Set per-row LOD with <see cref="DataStore.SetLod"/>.
        /// </summary>
        internal ulong RunBatchInLodBand(SystemDesc[] systems, int minLod, int maxLod)
        {
            if (systems == null || systems.Length == 0) return 0;
            return DataLensNative.dl_lens_run_batch_lod(_handle, systems, (ulong)systems.Length, minLod, maxLod);
        }

        // ── IR execution (A4.2) ──────────────────────────────────────────────

        private static IntPtr[] Handles(DataStore[] stores)
        {
            if (stores == null) return System.Array.Empty<IntPtr>();
            var h = new IntPtr[stores.Length];
            for (int i = 0; i < stores.Length; i++) h[i] = stores[i].Handle;
            return h;
        }

        /// <summary>Execute an IR program against a store table (op store-indices resolve into it).</summary>
        internal ulong Execute(IrProgram program, params DataStore[] stores)
        {
            var h = Handles(stores);
            return DataLensNative.dl_lens_execute(_handle, program.Handle, h, (ulong)h.Length);
        }

        // ── Tick / cadence scheduler (A5) ────────────────────────────────────

        /// <summary>Register a program to run every <paramref name="period"/> ticks, scoped to a LOD band.</summary>
        internal ulong AddScheduledProgram(IrProgram program, ulong period, int minLod = 0, int maxLod = 255, ulong phase = 0)
            => DataLensNative.dl_lens_add_scheduled_program(_handle, program.Handle, period, minLod, maxLod, phase);

        internal void ClearSchedule() => DataLensNative.dl_lens_clear_schedule(_handle);
        internal ulong ScheduledProgramCount => DataLensNative.dl_lens_scheduled_program_count(_handle);

        /// <summary>Register a view to refresh from store <paramref name="storeIndex"/> every <paramref name="period"/> ticks.</summary>
        internal ulong AddScheduledView(DataView view, ulong storeIndex, ulong period, ulong phase = 0)
            => DataLensNative.dl_lens_add_scheduled_view(_handle, view.Handle, storeIndex, period, phase);

        /// <summary>As <see cref="AddScheduledView"/>, but each refresh is restricted to a LOD band.</summary>
        internal ulong AddScheduledViewInLodBand(DataView view, ulong storeIndex, ulong period, int minLod, int maxLod, ulong phase = 0)
            => DataLensNative.dl_lens_add_scheduled_view_lod(_handle, view.Handle, storeIndex, period, minLod, maxLod, phase);

        internal void ClearScheduledViews() => DataLensNative.dl_lens_clear_scheduled_views(_handle);
        internal ulong ScheduledViewCount => DataLensNative.dl_lens_scheduled_view_count(_handle);

        public ulong CurrentTick => DataLensNative.dl_lens_current_tick(_handle);
        public void ResetTick(ulong tick = 0) => DataLensNative.dl_lens_reset_tick(_handle, tick);

        /// <summary>Advance one tick: run due Systems, then refresh due Views. Returns rows affected by Systems.</summary>
        internal ulong Tick(params DataStore[] stores)
        {
            var h = Handles(stores);
            return DataLensNative.dl_lens_tick(_handle, h, (ulong)h.Length);
        }

        /// <summary>
        /// Refresh a view from a store, materialised in parallel across the Lens pool (identical result
        /// to <see cref="DataView.Refresh"/>, much faster for large views). Prefer this over
        /// <see cref="DataView.Refresh"/> at scale.
        /// </summary>
        internal void RefreshView(DataView view, DataStore store)
            => DataLensNative.dl_lens_refresh_view(_handle, view.Handle, store.Handle);

        /// <summary>As <see cref="RefreshView"/>, restricted to the LOD band [minLod, maxLod].</summary>
        internal void RefreshViewInLodBand(DataView view, DataStore store, int minLod, int maxLod)
            => DataLensNative.dl_lens_refresh_view_lod(_handle, view.Handle, store.Handle, minLod, maxLod);

        /// <summary>
        /// Mixed-type predicate System: apply a float op (<paramref name="targetCol"/> = targetCol OP
        /// operand) to every live row where an <b>Int32</b> predicate column satisfies (cmp threshold),
        /// in one branchless parallel pass. The fused "gate a float-attribute effect by an int
        /// tag-bitmask column" primitive (use <see cref="CompareOp.HasAllBits"/>/<c>HasAnyBits</c>/<c>LacksBits</c>).
        /// Returns rows affected.
        /// </summary>
        internal ulong RunFloatWhereInt(DataStore store, ulong targetCol, SystemOp op, float operand,
            ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_f32_pred_i32(_handle, store.Handle, targetCol, (int)op, operand,
                compareCol, (int)cmp, threshold);

        /// <summary>
        /// Mixed-type predicate System (mirror of <see cref="RunFloatWhereInt"/>): an Int32 op gated by a
        /// <b>float</b> predicate column — e.g. knock out an int eligibility flag where a float resource
        /// column is below a threshold. Returns rows affected.
        /// </summary>
        internal ulong RunIntWhereFloat(DataStore store, ulong targetCol, SystemOp op, int operand,
            ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_i32_pred_f32(_handle, store.Handle, targetCol, (int)op, operand,
                compareCol, (int)cmp, threshold);

        public void Dispose()
        {
            for (int i = 0; i < _views.Count; i++)
                if (_views[i].IsLive) _views[i].Dispose();
            _views.Clear();

            if (_ownedStores != null)
            {
                for (int i = 0; i < _ownedStores.Length; i++)
                    _ownedStores[i]?.Dispose();
                _ownedStores = null;
            }
            if (_handle != IntPtr.Zero)
            {
                DataLensNative.dl_lens_destroy(_handle);
                _handle = IntPtr.Zero;
                DataLensSubsystem.UnregisterLens();
            }
            GC.SuppressFinalize(this);
        }

        ~Lens() => Dispose();
    }
}
