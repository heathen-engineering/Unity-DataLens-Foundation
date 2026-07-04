using System;

namespace Heathen.DataLens
{
    /// <summary>
    /// Managed handle to a native DataLens column store. THIN SLICE (A1 walking skeleton):
    /// wraps the native <c>DataStore</c> to prove the managed&lt;-&gt;native boundary. The
    /// full world/Lens/view surface arrives in later phases.
    /// </summary>
    internal sealed class DataStore : IDisposable
    {
        private IntPtr _handle;

        /// <summary>Native handle, for sibling Foundation types (e.g. <see cref="Lens"/>) to pass across the ABI.</summary>
        internal IntPtr Handle => _handle;

        /// <summary>The native C ABI version the loaded library reports.</summary>
        public static int NativeAbiVersion => DataLensNative.dl_abi_version();

        /// <summary>
        /// Create a native store with the given column ids + byte strides, optional concatenated default
        /// bytes (Sum(strides), or null for all-zero), and a preallocated row capacity. Core is type-blind:
        /// the Foundation derives strides/defaults from the schema (see <see cref="DataStoreSchema"/>).
        /// Internal: the <see cref="Lens"/> creates stores from a <see cref="DataLensSchema"/>.
        /// </summary>
        internal DataStore(ulong[] columnTags, ulong[] columnStrides, byte[] columnDefaults, ulong preallocRows)
        {
            if (columnTags == null) throw new ArgumentNullException(nameof(columnTags));
            if (columnStrides == null) throw new ArgumentNullException(nameof(columnStrides));
            if (columnTags.Length != columnStrides.Length)
                throw new ArgumentException("columnTags and columnStrides must be the same length.");

            _handle = DataLensNative.dl_store_create(columnTags, columnStrides, columnDefaults, columnTags.Length, preallocRows);
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Native dl_store_create failed (check column definitions).");
        }

        public ulong RowCount    => DataLensNative.dl_store_row_count(_handle);
        public ulong ColumnCount => DataLensNative.dl_store_column_count(_handle);
        public ulong RowStride   => DataLensNative.dl_store_row_stride(_handle);

        public bool SetFloat(ulong row, ulong col, float value)  => DataLensNative.dl_store_set_f32(_handle, row, col, value) != 0;
        public bool TryGetFloat(ulong row, ulong col, out float value)  => DataLensNative.dl_store_get_f32(_handle, row, col, out value) != 0;

        public bool SetInt(ulong row, ulong col, int value)      => DataLensNative.dl_store_set_i32(_handle, row, col, value) != 0;
        public bool TryGetInt(ulong row, ulong col, out int value)      => DataLensNative.dl_store_get_i32(_handle, row, col, out value) != 0;

        public bool SetDouble(ulong row, ulong col, double value) => DataLensNative.dl_store_set_f64(_handle, row, col, value) != 0;
        public bool TryGetDouble(ulong row, ulong col, out double value) => DataLensNative.dl_store_get_f64(_handle, row, col, out value) != 0;

        public void SetValid(ulong row, bool valid) => DataLensNative.dl_store_set_valid(_handle, row, valid ? 1 : 0);
        public bool IsValid(ulong row) => DataLensNative.dl_store_is_valid(_handle, row) != 0;

        /// <summary>
        /// Set a row's Simulation LOD level (0 = highest fidelity / always runs, higher = coarser).
        /// Per-row relevance metadata a System can scope its work to via <see cref="Lens.RunBatchInLodBand"/>.
        /// </summary>
        public void SetLod(ulong row, int level) => DataLensNative.dl_store_set_lod(_handle, row, level);

        /// <summary>Get a row's Simulation LOD level.</summary>
        public int GetLod(ulong row) => DataLensNative.dl_store_get_lod(_handle, row);

        /// <summary>Sentinel returned by <see cref="AllocRow"/> when the store is at capacity.</summary>
        public const ulong InvalidRow = ulong.MaxValue;

        /// <summary>Allocate the next free row and mark it valid. Returns <see cref="InvalidRow"/> when full.</summary>
        public ulong AllocRow() => DataLensNative.dl_store_alloc_row(_handle);

        /// <summary>Release a row so it can be reused by a later <see cref="AllocRow"/>.</summary>
        public void FreeRow(ulong row) => DataLensNative.dl_store_free_row(_handle, row);

        /// <summary>Number of currently-valid (live) rows.</summary>
        public ulong LiveCount => DataLensNative.dl_store_live_count(_handle);

        // ── Replication (DataLens-Spec §10) ──────────────────────────────────
        /// <summary>The store's monotonic replication revision (the host advances it once per network tick).</summary>
        public ulong Revision => DataLensNative.dl_store_revision(_handle);
        /// <summary>Adopt a revision (e.g. after applying a payload out of band).</summary>
        public void SetRevision(ulong revision) => DataLensNative.dl_store_set_revision(_handle, revision);
        /// <summary>Increment and return the revision (start of a network tick, before applying its writes).</summary>
        public ulong BumpRevision() => DataLensNative.dl_store_bump_revision(_handle);
        /// <summary>Stamp a column as changed at the current revision (the Lens does this for committed writes).</summary>
        public void MarkColumnDirty(ulong col) => DataLensNative.dl_store_mark_column_dirty(_handle, col);

        // ── Systems (A3) ─────────────────────────────────────────────────────
        // Run a conditional column transform over all live rows: where the optional predicate
        // (compareCol CMP threshold) holds, apply (targetCol = targetCol OP operand). Returns rows affected.

        public ulong RunFloat(ulong targetCol, SystemOp op, float operand)
            => DataLensNative.dl_store_run_f32(_handle, targetCol, (int)op, operand, 0, 0, 0, 0f);

        public ulong RunFloat(ulong targetCol, SystemOp op, float operand, ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_store_run_f32(_handle, targetCol, (int)op, operand, 1, compareCol, (int)cmp, threshold);

        public ulong RunInt(ulong targetCol, SystemOp op, int operand)
            => DataLensNative.dl_store_run_i32(_handle, targetCol, (int)op, operand, 0, 0, 0, 0);

        public ulong RunInt(ulong targetCol, SystemOp op, int operand, ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_store_run_i32(_handle, targetCol, (int)op, operand, 1, compareCol, (int)cmp, threshold);

        // Cross-column Systems (A3.3): the operand is read per-row from operandCol instead of being a
        // scalar — e.g. (targetCol = targetCol + operandCol) or a per-row clamp (current = min(current, maxCol)).

        public ulong RunFloatColumn(ulong targetCol, SystemOp op, ulong operandCol)
            => DataLensNative.dl_store_run_col_f32(_handle, targetCol, (int)op, operandCol, 0, 0, 0, 0f);

        public ulong RunFloatColumn(ulong targetCol, SystemOp op, ulong operandCol, ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_store_run_col_f32(_handle, targetCol, (int)op, operandCol, 1, compareCol, (int)cmp, threshold);

        public ulong RunIntColumn(ulong targetCol, SystemOp op, ulong operandCol)
            => DataLensNative.dl_store_run_col_i32(_handle, targetCol, (int)op, operandCol, 0, 0, 0, 0);

        public ulong RunIntColumn(ulong targetCol, SystemOp op, ulong operandCol, ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_store_run_col_i32(_handle, targetCol, (int)op, operandCol, 1, compareCol, (int)cmp, threshold);

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                DataLensNative.dl_store_destroy(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~DataStore() => Dispose();
    }
}
