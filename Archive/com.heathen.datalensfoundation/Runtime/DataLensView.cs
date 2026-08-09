using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Heathen.GameplayTags;

namespace Heathen.DataLens
{
    /// <summary>
    /// A dynamic, column-addressed read/write view: the data-driven consumer surface (DataLens-Spec §6.4.1).
    /// Unlike <see cref="DataView{TRow}"/> there is no compile-time row struct; cells are read and written by
    /// column <see cref="GameplayTag"/> at a row index, so a field is exactly <c>(columnId, rowIndex, value)</c>.
    /// This is what a data-driven engine (e.g. HATE) rides: it builds a filtered view, then sets the cells it
    /// is responsible for and commits; DataLens just writes each value back to its target column at its row.
    /// Created via <see cref="Lens.View(DataLensFrom, GameplayTag[], bool[], int)"/>.
    /// </summary>
    public sealed unsafe class DataLensView : IDisposable, ILensView
    {
        private IntPtr _handle;
        private readonly IntPtr[] _stores;
        private readonly int _weight;
        private readonly Dictionary<ulong, int> _colIndex; // column tag -> view column index (projection order)
        private int _rowStride;
        private int[] _colOffset;   // byte offset of each view column within a row
        private int[] _colStride;   // byte stride of each view column
        private bool _layout;

        internal IntPtr Handle => _handle;
        int ILensView.Weight => _weight;
        bool ILensView.IsLive => _handle != IntPtr.Zero;
        ulong ILensView.CommitInternal() => Commit();

        /// <summary>Commit precedence when committed via <see cref="Lens.Commit"/> (heavier weight wins per cell).</summary>
        public int Weight => _weight;

        internal DataLensView(IntPtr handle, IntPtr[] stores, int weight, GameplayTag[] select)
        {
            _handle = handle;
            _stores = stores;
            _weight = weight;
            _colIndex = new Dictionary<ulong, int>(select.Length);
            for (int k = 0; k < select.Length; k++)
                _colIndex[(ulong)select[k]] = k;

            DataLensNative.dl_rwview_refresh(_handle, _stores, _stores.Length); // first refresh fixes the layout
            CacheLayout();
        }

        private void CacheLayout()
        {
            int n = _colIndex.Count;
            _rowStride = (int)DataLensNative.dl_rwview_row_stride(_handle);
            _colOffset = new int[n];
            _colStride = new int[n];
            for (int c = 0; c < n; c++)
            {
                _colOffset[c] = (int)DataLensNative.dl_rwview_column_offset(_handle, (ulong)c);
                _colStride[c] = (int)DataLensNative.dl_rwview_column_stride(_handle, (ulong)c);
            }
            _layout = true;
        }

        public int RowCount => (int)DataLensNative.dl_rwview_row_count(_handle);

        private int ColumnIndex(GameplayTag column)
        {
            if (!_colIndex.TryGetValue((ulong)column, out int c))
                throw new ArgumentException($"Column {(ulong)column} is not in this view's projection.");
            return c;
        }

        /// <summary>Write a cell by column tag (stride-bounded raw copy; the consumer writes the column's type).</summary>
        public void Set<T>(int row, GameplayTag column, T value) where T : unmanaged
        {
            int c = ColumnIndex(column);
            byte* p = (byte*)DataLensNative.dl_rwview_mutable_data(_handle) + row * _rowStride + _colOffset[c];
            int n = Math.Min(Unsafe.SizeOf<T>(), _colStride[c]);
            Buffer.MemoryCopy(&value, p, _colStride[c], n);
        }

        /// <summary>Read a cell by column tag (stride-bounded, zero-extended into <typeparamref name="T"/>).</summary>
        public T Get<T>(int row, GameplayTag column) where T : unmanaged
        {
            int c = ColumnIndex(column);
            T value = default;
            byte* p = (byte*)DataLensNative.dl_rwview_data(_handle) + row * _rowStride + _colOffset[c];
            int n = Math.Min(Unsafe.SizeOf<T>(), _colStride[c]);
            Buffer.MemoryCopy(p, &value, Unsafe.SizeOf<T>(), n);
            return value;
        }

        public ViewRowState GetState(int row) => (ViewRowState)DataLensNative.dl_rwview_get_state(_handle, (ulong)row);
        public void SetState(int row, ViewRowState state) => DataLensNative.dl_rwview_set_state(_handle, (ulong)row, (byte)state);

        /// <summary>Append a New row (for an Insert). Returns the view row index.</summary>
        public int AddRow() => (int)DataLensNative.dl_rwview_add_row(_handle);

        /// <summary>The prime-store row a view row sources from (e.g. the EntityCatalog index = the EntityId).</summary>
        public long SourceRow(int viewRow)
        {
            ulong r = DataLensNative.dl_rwview_source_base_row(_handle, (ulong)viewRow);
            return r == ulong.MaxValue ? -1 : (long)r;
        }

        /// <summary>The resolved target row of the given dereference join, or -1 when absent (trait lacked).</summary>
        public long SourceJoinRow(int viewRow, int join)
        {
            ulong r = DataLensNative.dl_rwview_source_join_row(_handle, (ulong)viewRow, (ulong)join);
            return r == ulong.MaxValue ? -1 : (long)r;
        }

        public void Refresh()
        {
            DataLensNative.dl_rwview_refresh(_handle, _stores, _stores.Length);
            if (!_layout) CacheLayout();
        }

        public ulong Commit() => DataLensNative.dl_rwview_commit(_handle, _stores, _stores.Length);

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                DataLensNative.dl_rwview_destroy(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~DataLensView() => Dispose();
    }
}
