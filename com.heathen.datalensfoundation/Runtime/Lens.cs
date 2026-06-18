using System;

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

        /// <summary>Create a Lens with a worker pool. <paramref name="threadCount"/> of 0 uses hardware concurrency.</summary>
        public Lens(int threadCount = 0)
        {
            _handle = DataLensNative.dl_lens_create(threadCount);
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Native dl_lens_create failed.");
        }

        /// <summary>Number of threads the pool runs with (including the calling thread).</summary>
        public int ThreadCount => DataLensNative.dl_lens_thread_count(_handle);

        // Run a conditional column System over the store in parallel. Returns rows affected.

        public ulong RunFloat(DataStore store, ulong targetCol, SystemOp op, float operand)
            => DataLensNative.dl_lens_run_f32(_handle, store.Handle, targetCol, (int)op, operand, 0, 0, 0, 0f);

        public ulong RunFloat(DataStore store, ulong targetCol, SystemOp op, float operand, ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_f32(_handle, store.Handle, targetCol, (int)op, operand, 1, compareCol, (int)cmp, threshold);

        public ulong RunInt(DataStore store, ulong targetCol, SystemOp op, int operand)
            => DataLensNative.dl_lens_run_i32(_handle, store.Handle, targetCol, (int)op, operand, 0, 0, 0, 0);

        public ulong RunInt(DataStore store, ulong targetCol, SystemOp op, int operand, ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_i32(_handle, store.Handle, targetCol, (int)op, operand, 1, compareCol, (int)cmp, threshold);

        // Parallel cross-column Systems (A3.3): operand read per-row from operandCol.

        public ulong RunFloatColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol)
            => DataLensNative.dl_lens_run_col_f32(_handle, store.Handle, targetCol, (int)op, operandCol, 0, 0, 0, 0f);

        public ulong RunFloatColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_col_f32(_handle, store.Handle, targetCol, (int)op, operandCol, 1, compareCol, (int)cmp, threshold);

        public ulong RunIntColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol)
            => DataLensNative.dl_lens_run_col_i32(_handle, store.Handle, targetCol, (int)op, operandCol, 0, 0, 0, 0);

        public ulong RunIntColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_col_i32(_handle, store.Handle, targetCol, (int)op, operandCol, 1, compareCol, (int)cmp, threshold);

        // ── Curved cross-column Systems (A3.11) ──────────────────────────────
        // targetCol = targetCol OP curve(operandCol[r]): the per-row operand is normalised over the
        // curve's range and passed through its shape before the combine — one HATE §8 consideration
        // (e.g. score *= curve(distance)). Run in parallel across the pool. Returns rows affected.

        /// <summary>Curved cross-column Float System (A3.11). See <see cref="Curve"/>.</summary>
        public ulong RunFloatCurved(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, Curve curve)
            => DataLensNative.dl_lens_run_curved_f32(_handle, store.Handle, targetCol, (int)op, operandCol,
                (int)curve.Type, curve.Min, curve.Max, curve.P0, curve.P1, curve.Invert ? 1 : 0, 0, 0, 0, 0f);

        /// <summary>Curved cross-column Float System gated on (compareCol CMP threshold).</summary>
        public ulong RunFloatCurved(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, Curve curve,
            ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_curved_f32(_handle, store.Handle, targetCol, (int)op, operandCol,
                (int)curve.Type, curve.Min, curve.Max, curve.P0, curve.P1, curve.Invert ? 1 : 0, 1, compareCol, (int)cmp, threshold);

        /// <summary>Curved cross-column Int32 System (A3.11). See <see cref="Curve"/>.</summary>
        public ulong RunIntCurved(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, Curve curve)
            => DataLensNative.dl_lens_run_curved_i32(_handle, store.Handle, targetCol, (int)op, operandCol,
                (int)curve.Type, curve.Min, curve.Max, curve.P0, curve.P1, curve.Invert ? 1 : 0, 0, 0, 0, 0);

        /// <summary>Curved cross-column Int32 System gated on (compareCol CMP threshold).</summary>
        public ulong RunIntCurved(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, Curve curve,
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
        public ulong RunFloatNoise(DataStore store, ulong targetCol, SystemOp op, float noiseLo, float noiseHi, ulong seed, ulong tick)
            => DataLensNative.dl_lens_run_noise_f32(_handle, store.Handle, targetCol, (int)op, noiseLo, noiseHi, seed, tick, 0, 0, 0, 0f);

        /// <summary>Counter-based Float noise fill gated on (compareCol CMP threshold).</summary>
        public ulong RunFloatNoise(DataStore store, ulong targetCol, SystemOp op, float noiseLo, float noiseHi, ulong seed, ulong tick,
            ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_noise_f32(_handle, store.Handle, targetCol, (int)op, noiseLo, noiseHi, seed, tick, 1, compareCol, (int)cmp, threshold);

        /// <summary>Counter-based Int32 noise fill.</summary>
        public ulong RunIntNoise(DataStore store, ulong targetCol, SystemOp op, int noiseLo, int noiseHi, ulong seed, ulong tick)
            => DataLensNative.dl_lens_run_noise_i32(_handle, store.Handle, targetCol, (int)op, noiseLo, noiseHi, seed, tick, 0, 0, 0, 0);

        /// <summary>Counter-based Int32 noise fill gated on (compareCol CMP threshold).</summary>
        public ulong RunIntNoise(DataStore store, ulong targetCol, SystemOp op, int noiseLo, int noiseHi, ulong seed, ulong tick,
            ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_noise_i32(_handle, store.Handle, targetCol, (int)op, noiseLo, noiseHi, seed, tick, 1, compareCol, (int)cmp, threshold);

        /// <summary>Counter-based noise perturb (A3.12 / HATE §8.4): targetCol = targetCol OP (operandCol[r] * noise).</summary>
        public ulong RunFloatNoisePerturb(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, float noiseLo, float noiseHi, ulong seed, ulong tick)
            => DataLensNative.dl_lens_run_noise_perturb_f32(_handle, store.Handle, targetCol, (int)op, operandCol, noiseLo, noiseHi, seed, tick, 0, 0, 0, 0f);

        /// <summary>Float noise perturb gated on (compareCol CMP threshold).</summary>
        public ulong RunFloatNoisePerturb(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, float noiseLo, float noiseHi, ulong seed, ulong tick,
            ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_noise_perturb_f32(_handle, store.Handle, targetCol, (int)op, operandCol, noiseLo, noiseHi, seed, tick, 1, compareCol, (int)cmp, threshold);

        /// <summary>Int32 noise perturb: targetCol = targetCol OP (operandCol[r] * noise).</summary>
        public ulong RunIntNoisePerturb(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, int noiseLo, int noiseHi, ulong seed, ulong tick)
            => DataLensNative.dl_lens_run_noise_perturb_i32(_handle, store.Handle, targetCol, (int)op, operandCol, noiseLo, noiseHi, seed, tick, 0, 0, 0, 0);

        /// <summary>Int32 noise perturb gated on (compareCol CMP threshold).</summary>
        public ulong RunIntNoisePerturb(DataStore store, ulong targetCol, SystemOp op, ulong operandCol, int noiseLo, int noiseHi, ulong seed, ulong tick,
            ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_noise_perturb_i32(_handle, store.Handle, targetCol, (int)op, operandCol, noiseLo, noiseHi, seed, tick, 1, compareCol, (int)cmp, threshold);

        // ── Argmax-across-columns (A3.13) ────────────────────────────────────
        // Reduce K score columns to a per-row Choice index column (the HATE §8.5 AI selection "pick"):
        // for each live actor, write the index of the largest score into choiceCol. Ties resolve to the
        // lowest index; a winning score below minScore writes noChoice (default -1 = "do nothing", which
        // composes with the §8.5 Choice>=0 Command override). Runs in parallel across the pool.

        /// <summary>Argmax over Float score columns -> Choice index column (A3.13).</summary>
        public ulong RunFloatArgmax(DataStore store, ulong choiceCol, ulong[] scoreCols,
            float minScore = float.NegativeInfinity, int noChoice = -1)
            => DataLensNative.dl_lens_run_argmax_f32(_handle, store.Handle, choiceCol,
                scoreCols ?? System.Array.Empty<ulong>(), (ulong)(scoreCols?.Length ?? 0), minScore, noChoice);

        /// <summary>Argmax over Int32 score columns -> Choice index column (A3.13).</summary>
        public ulong RunIntArgmax(DataStore store, ulong choiceCol, ulong[] scoreCols,
            int minScore = int.MinValue, int noChoice = -1)
            => DataLensNative.dl_lens_run_argmax_i32(_handle, store.Handle, choiceCol,
                scoreCols ?? System.Array.Empty<ulong>(), (ulong)(scoreCols?.Length ?? 0), minScore, noChoice);

        // ── Batched Systems (A3.4) ───────────────────────────────────────────
        // Run a whole batch of data-described Systems in one call. The Lens runs non-conflicting
        // Systems concurrently and preserves submission order for conflicting ones, so the result is
        // deterministic and identical to running them one by one. Returns total rows affected.

        public ulong RunBatch(params SystemDesc[] systems)
        {
            if (systems == null || systems.Length == 0) return 0;
            return DataLensNative.dl_lens_run_batch(_handle, systems, (ulong)systems.Length);
        }

        /// <summary>
        /// Run a batch but only over rows whose Simulation LOD is within [minLod, maxLod] — the
        /// "this tick runs at fidelity band [min,max]" model (A3.5). The band applies to every System
        /// in the batch. Set per-row LOD with <see cref="DataStore.SetLod"/>.
        /// </summary>
        public ulong RunBatchInLodBand(SystemDesc[] systems, int minLod, int maxLod)
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
        public ulong Execute(IrProgram program, params DataStore[] stores)
        {
            var h = Handles(stores);
            return DataLensNative.dl_lens_execute(_handle, program.Handle, h, (ulong)h.Length);
        }

        // ── Tick / cadence scheduler (A5) ────────────────────────────────────

        /// <summary>Register a program to run every <paramref name="period"/> ticks, scoped to a LOD band.</summary>
        public ulong AddScheduledProgram(IrProgram program, ulong period, int minLod = 0, int maxLod = 255, ulong phase = 0)
            => DataLensNative.dl_lens_add_scheduled_program(_handle, program.Handle, period, minLod, maxLod, phase);

        public void ClearSchedule() => DataLensNative.dl_lens_clear_schedule(_handle);
        public ulong ScheduledProgramCount => DataLensNative.dl_lens_scheduled_program_count(_handle);

        /// <summary>Register a view to refresh from store <paramref name="storeIndex"/> every <paramref name="period"/> ticks.</summary>
        public ulong AddScheduledView(DataView view, ulong storeIndex, ulong period, ulong phase = 0)
            => DataLensNative.dl_lens_add_scheduled_view(_handle, view.Handle, storeIndex, period, phase);

        /// <summary>As <see cref="AddScheduledView"/>, but each refresh is restricted to a LOD band.</summary>
        public ulong AddScheduledViewInLodBand(DataView view, ulong storeIndex, ulong period, int minLod, int maxLod, ulong phase = 0)
            => DataLensNative.dl_lens_add_scheduled_view_lod(_handle, view.Handle, storeIndex, period, minLod, maxLod, phase);

        public void ClearScheduledViews() => DataLensNative.dl_lens_clear_scheduled_views(_handle);
        public ulong ScheduledViewCount => DataLensNative.dl_lens_scheduled_view_count(_handle);

        public ulong CurrentTick => DataLensNative.dl_lens_current_tick(_handle);
        public void ResetTick(ulong tick = 0) => DataLensNative.dl_lens_reset_tick(_handle, tick);

        /// <summary>Advance one tick: run due Systems, then refresh due Views. Returns rows affected by Systems.</summary>
        public ulong Tick(params DataStore[] stores)
        {
            var h = Handles(stores);
            return DataLensNative.dl_lens_tick(_handle, h, (ulong)h.Length);
        }

        /// <summary>
        /// Refresh a view from a store, materialised in parallel across the Lens pool (identical result
        /// to <see cref="DataView.Refresh"/>, much faster for large views). Prefer this over
        /// <see cref="DataView.Refresh"/> at scale.
        /// </summary>
        public void RefreshView(DataView view, DataStore store)
            => DataLensNative.dl_lens_refresh_view(_handle, view.Handle, store.Handle);

        /// <summary>As <see cref="RefreshView"/>, restricted to the LOD band [minLod, maxLod].</summary>
        public void RefreshViewInLodBand(DataView view, DataStore store, int minLod, int maxLod)
            => DataLensNative.dl_lens_refresh_view_lod(_handle, view.Handle, store.Handle, minLod, maxLod);

        /// <summary>
        /// Mixed-type predicate System: apply a float op (<paramref name="targetCol"/> = targetCol OP
        /// operand) to every live row where an <b>Int32</b> predicate column satisfies (cmp threshold),
        /// in one branchless parallel pass. The fused "gate a float-attribute effect by an int
        /// tag-bitmask column" primitive (use <see cref="CompareOp.HasAllBits"/>/<c>HasAnyBits</c>/<c>LacksBits</c>).
        /// Returns rows affected.
        /// </summary>
        public ulong RunFloatWhereInt(DataStore store, ulong targetCol, SystemOp op, float operand,
            ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_f32_pred_i32(_handle, store.Handle, targetCol, (int)op, operand,
                compareCol, (int)cmp, threshold);

        /// <summary>
        /// Mixed-type predicate System (mirror of <see cref="RunFloatWhereInt"/>): an Int32 op gated by a
        /// <b>float</b> predicate column — e.g. knock out an int eligibility flag where a float resource
        /// column is below a threshold. Returns rows affected.
        /// </summary>
        public ulong RunIntWhereFloat(DataStore store, ulong targetCol, SystemOp op, int operand,
            ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_i32_pred_f32(_handle, store.Handle, targetCol, (int)op, operand,
                compareCol, (int)cmp, threshold);

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                DataLensNative.dl_lens_destroy(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~Lens() => Dispose();
    }
}
