using System;

namespace Heathen.DataLens
{
    /// <summary>
    /// Managed handle to a native DataLens column store. THIN SLICE (A1 walking skeleton):
    /// wraps the native <c>DataStore</c> to prove the managed&lt;-&gt;native boundary. The
    /// full world/Lens/view surface arrives in later phases.
    /// </summary>
    public sealed class DataStore : IDisposable
    {
        private IntPtr _handle;

        /// <summary>Native handle, for sibling Foundation types (e.g. <see cref="Lens"/>) to pass across the ABI.</summary>
        internal IntPtr Handle => _handle;

        /// <summary>The native C ABI version the loaded library reports.</summary>
        public static int NativeAbiVersion => DataLensNative.dl_abi_version();

        /// <summary>
        /// Create a store with the given fixed-width columns and a preallocated row capacity.
        /// </summary>
        public DataStore(string[] columnNames, DataLensValueType[] columnTypes, ulong preallocRows)
        {
            if (columnNames == null) throw new ArgumentNullException(nameof(columnNames));
            if (columnTypes == null) throw new ArgumentNullException(nameof(columnTypes));
            if (columnNames.Length != columnTypes.Length)
                throw new ArgumentException("columnNames and columnTypes must be the same length.");

            var types = new int[columnTypes.Length];
            for (int i = 0; i < columnTypes.Length; i++)
                types[i] = (int)columnTypes[i];

            _handle = DataLensNative.dl_store_create(columnNames, types, columnNames.Length, preallocRows);
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

        /// <summary>Sentinel returned by <see cref="AllocRow"/> when the store is at capacity.</summary>
        public const ulong InvalidRow = ulong.MaxValue;

        /// <summary>Allocate the next free row and mark it valid. Returns <see cref="InvalidRow"/> when full.</summary>
        public ulong AllocRow() => DataLensNative.dl_store_alloc_row(_handle);

        /// <summary>Release a row so it can be reused by a later <see cref="AllocRow"/>.</summary>
        public void FreeRow(ulong row) => DataLensNative.dl_store_free_row(_handle, row);

        /// <summary>Number of currently-valid (live) rows.</summary>
        public ulong LiveCount => DataLensNative.dl_store_live_count(_handle);

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
