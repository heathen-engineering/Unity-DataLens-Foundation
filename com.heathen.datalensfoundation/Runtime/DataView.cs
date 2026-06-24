using System;
using System.Runtime.InteropServices;

namespace Heathen.DataLens
{
    /// <summary>
    /// A read-only materialised view (A5): a row-major SNAPSHOT of selected columns of a store's live
    /// rows. It is a copy, never an alias of the store, so it can be read while the Lens mutates the
    /// store. <see cref="Refresh"/> rebuilds it from the current store state; the Lens can also refresh
    /// it on a cadence (<see cref="Lens.AddScheduledView"/>).
    /// </summary>
    internal sealed class DataView : IDisposable
    {
        private IntPtr _handle;
        internal IntPtr Handle => _handle;

        /// <summary>Create a view mirroring the given store columns (view column i == sourceColumns[i]).</summary>
        public DataView(ulong[] sourceColumns)
        {
            if (sourceColumns == null) throw new ArgumentNullException(nameof(sourceColumns));
            _handle = DataLensNative.dl_view_create(sourceColumns, (ulong)sourceColumns.Length);
            if (_handle == IntPtr.Zero) throw new InvalidOperationException("Native dl_view_create failed.");
        }

        /// <summary>Re-materialise the snapshot from every live row of the store.</summary>
        public void Refresh(DataStore store) => DataLensNative.dl_view_refresh(_handle, store.Handle);

        /// <summary>Re-materialise, including only rows whose Simulation LOD is within [minLod, maxLod].</summary>
        public void RefreshInLodBand(DataStore store, int minLod, int maxLod)
            => DataLensNative.dl_view_refresh_lod(_handle, store.Handle, minLod, maxLod);

        public ulong RowCount    => DataLensNative.dl_view_row_count(_handle);
        public ulong ColumnCount => DataLensNative.dl_view_column_count(_handle);
        public ulong RowStride   => DataLensNative.dl_view_row_stride(_handle);

        /// <summary>The store row a view row was materialised from.</summary>
        public ulong SourceRow(ulong viewRow) => DataLensNative.dl_view_source_row(_handle, viewRow);

        public bool TryGetFloat(ulong viewRow, ulong viewCol, out float value)
            => DataLensNative.dl_view_get_f32(_handle, viewRow, viewCol, out value) != 0;
        public bool TryGetInt(ulong viewRow, ulong viewCol, out int value)
            => DataLensNative.dl_view_get_i32(_handle, viewRow, viewCol, out value) != 0;
        public bool TryGetDouble(ulong viewRow, ulong viewCol, out double value)
            => DataLensNative.dl_view_get_f64(_handle, viewRow, viewCol, out value) != 0;

        /// <summary>Base pointer of the row-major snapshot (valid until the next Refresh) — for bulk reads.</summary>
        public IntPtr DataPointer => DataLensNative.dl_view_data(_handle);

        /// <summary>Total byte size of the snapshot (RowCount * RowStride).</summary>
        public ulong ByteSize => DataLensNative.dl_view_byte_size(_handle);

        /// <summary>
        /// Bulk-copy the row-major snapshot into <paramref name="dst"/> as floats in a single marshalled
        /// copy (the scalable read path — avoids per-cell interop). Returns the number of floats copied.
        /// For an all-float view of columns this is the rows laid out [r0c0, r0c1, …, r1c0, …].
        /// </summary>
        public int CopyFloats(float[] dst)
        {
            if (dst == null) return 0;
            int available = (int)(ByteSize / sizeof(float));
            int n = Math.Min(available, dst.Length);
            if (n > 0) Marshal.Copy(DataPointer, dst, 0, n);
            return n;
        }

        /// <summary>
        /// Bulk-copy the row-major snapshot into <paramref name="dst"/> as Int32s in a single marshalled
        /// copy. Returns the number of ints copied. For an all-Int32 view, the rows laid out
        /// [r0c0, r0c1, …, r1c0, …].
        /// </summary>
        public int CopyInts(int[] dst)
        {
            if (dst == null) return 0;
            int available = (int)(ByteSize / sizeof(int));
            int n = Math.Min(available, dst.Length);
            if (n > 0) Marshal.Copy(DataPointer, dst, 0, n);
            return n;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                DataLensNative.dl_view_destroy(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~DataView() => Dispose();
    }
}
