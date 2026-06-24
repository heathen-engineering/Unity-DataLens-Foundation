using System;
using System.Runtime.CompilerServices;

namespace Heathen.DataLens
{
    // The Lens commits its registered views together, ascending by weight (heavier weight commits later, so
    // it wins per cell - DataLens-Spec §6.4.1 flat weighted commit). DataView exposes this hook so the Lens
    // need not know TRow.
    internal interface ILensView : IDisposable
    {
        int Weight { get; }
        bool IsLive { get; }
        ulong CommitInternal();
    }

    /// <summary>
    /// The read/write View surface (DataLens-Spec §6.4) as a strongly-typed row struct. When every field is
    /// byte-compatible with its column the rows are a <b>zero-copy</b> <see cref="Span{TRow}"/> straight over
    /// the native payload; when a field's type differs (width or float/int kind) the view marshals a managed
    /// <c>TRow[]</c> and converts both ways on refresh/commit. Either way the consumer sees <see cref="Rows"/>
    /// as <c>Span&lt;TRow&gt;</c>. Created via <see cref="Lens.View{TRow}"/>.
    /// </summary>
    public sealed class DataView<TRow> : IDisposable, ILensView where TRow : unmanaged
    {
        private IntPtr _handle;             // dl_rwview
        private readonly IntPtr[] _stores;  // store-handle table for refresh / commit
        private readonly int _weight;
        private readonly ViewMarshaller<TRow> _marshaller; // null => zero-copy
        private TRow[] _managed;            // marshalled mode only

        internal IntPtr Handle => _handle;
        int ILensView.Weight => _weight;
        bool ILensView.IsLive => _handle != IntPtr.Zero;
        ulong ILensView.CommitInternal() => Commit();

        /// <summary>Commit precedence: when committed via <see cref="Lens.Commit"/>, heavier weight wins per cell.</summary>
        public int Weight => _weight;

        internal DataView(IntPtr handle, IntPtr[] stores, int weight, ViewMarshaller<TRow> marshaller)
        {
            _handle = handle;
            _stores = stores;
            _weight = weight;
            _marshaller = marshaller;

            // First refresh populates the column layout (and hydrates existing rows).
            DataLensNative.dl_rwview_refresh(_handle, _stores, _stores.Length);

            int rowStride = (int)DataLensNative.dl_rwview_row_stride(_handle);
            if (_marshaller == null)
            {
                int size = Unsafe.SizeOf<TRow>();
                if (rowStride != size)
                {
                    DataLensNative.dl_rwview_destroy(_handle);
                    _handle = IntPtr.Zero;
                    throw new InvalidOperationException(
                        $"DataView<{typeof(TRow).Name}> layout mismatch: row stride {rowStride} != sizeof(TRow) {size}. " +
                        "Fields must match the projection columns and order; use [StructLayout(Sequential, Pack = 1)].");
                }
            }
            else
            {
                _managed = Array.Empty<TRow>();
                SyncFromNative();
            }
        }

        public int RowCount => (int)DataLensNative.dl_rwview_row_count(_handle);

        /// <summary>
        /// Typed window over the rows (zero-copy over the native payload, or over the marshalled array).
        /// Re-fetch after a <see cref="Refresh"/>, <see cref="AddRow"/> or <see cref="Commit"/>.
        /// </summary>
        public unsafe Span<TRow> Rows
        {
            get
            {
                int n = RowCount;
                if (_marshaller == null)
                    return new Span<TRow>((void*)DataLensNative.dl_rwview_mutable_data(_handle), n);
                return _managed.AsSpan(0, n);
            }
        }

        public ViewRowState GetState(int row) => (ViewRowState)DataLensNative.dl_rwview_get_state(_handle, (ulong)row);
        public void SetState(int row, ViewRowState state) => DataLensNative.dl_rwview_set_state(_handle, (ulong)row, (byte)state);

        /// <summary>Append a New row (for an Insert). Returns the view row index. Re-fetch <see cref="Rows"/> after.</summary>
        public int AddRow()
        {
            int r = (int)DataLensNative.dl_rwview_add_row(_handle);
            if (_marshaller != null)
            {
                EnsureManaged(r + 1);
                _managed[r] = default;
            }
            return r;
        }

        /// <summary>Re-hydrate the snapshot from the stores.</summary>
        public void Refresh()
        {
            DataLensNative.dl_rwview_refresh(_handle, _stores, _stores.Length);
            SyncFromNative();
        }

        /// <summary>Commit the edited rows back to the stores by their change flags. Returns the store ops applied.</summary>
        public ulong Commit()
        {
            SyncToNative();
            return DataLensNative.dl_rwview_commit(_handle, _stores, _stores.Length);
        }

        // ── marshalled-mode sync (no-ops in zero-copy mode) ──
        private void SyncFromNative()
        {
            if (_marshaller == null) return;
            int n = RowCount;
            EnsureManaged(n);
            if (n > 0) _marshaller.NativeToManaged(DataLensNative.dl_rwview_data(_handle), _managed, n);
        }

        private void SyncToNative()
        {
            if (_marshaller == null) return;
            int n = RowCount;
            if (n > 0) _marshaller.ManagedToNative(_managed, DataLensNative.dl_rwview_mutable_data(_handle), n);
        }

        private void EnsureManaged(int n)
        {
            if (_managed.Length < n)
                Array.Resize(ref _managed, n < 4 ? 4 : n * 2);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                DataLensNative.dl_rwview_destroy(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~DataView() => Dispose();
    }
}
