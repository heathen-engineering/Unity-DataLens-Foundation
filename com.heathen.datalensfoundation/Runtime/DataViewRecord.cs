using Heathen.GameplayTags;

namespace Heathen.DataLens
{
    /// <summary>
    /// A lightweight, tag-addressed handle to <b>one flat record</b> — a single row of a <see cref="DataLensView"/>.
    /// It is what an application hands to a consumer that wants to read/write "its" fields without knowing (or
    /// caring) which view or row they came from: fields are just columns, addressed by their unique
    /// <see cref="GameplayTag"/>. The Lens keeps the underlying view hydrated on its own cadence, so a record is
    /// always current; the holder never fetches or flushes.
    /// </summary>
    /// <remarks>
    /// A record is a value type over <c>(view, row)</c> — copying it is free and it stays live as long as the view
    /// does. The same type serves a primary (entity/trait) record and a carried child (ability/effect) record; a
    /// composed "entity record" is simply a primary record plus the child records carried alongside it.
    /// A <see cref="Set{T}"/> writes the cell and marks the row modified, so the Lens commits it on its next pass —
    /// the write half of the two-way binding (Coding Law 4: mutation reaches the store only through the view).
    /// </remarks>
    public readonly struct DataViewRecord
    {
        private readonly DataLensView _view;
        private readonly int _row;

        public DataViewRecord(DataLensView view, int row)
        {
            _view = view;
            _row = row;
        }

        /// <summary>True when this record points at a real row of a live view.</summary>
        public bool IsValid => _view != null && _row >= 0;

        /// <summary>The row index within the backing view (rarely needed by consumers).</summary>
        public int Row => _row;

        /// <summary>Read a field by its column tag. The width must match the column (as with a typed view row).</summary>
        public T Get<T>(GameplayTag column) where T : unmanaged => _view.Get<T>(_row, column);

        /// <summary>Write a field by its column tag and mark the row modified so the Lens commits it on its cadence.</summary>
        public void Set<T>(GameplayTag column, T value) where T : unmanaged
        {
            _view.Set(_row, column, value);
            _view.SetState(_row, ViewRowState.Modified);
        }

        // Typed conveniences for the common column widths (so callers/codegen need no explicit type argument).
        public int GetInt(GameplayTag column) => _view.Get<int>(_row, column);
        public float GetFloat(GameplayTag column) => _view.Get<float>(_row, column);
        public double GetDouble(GameplayTag column) => _view.Get<double>(_row, column);
        public void SetInt(GameplayTag column, int value) => Set(column, value);
        public void SetFloat(GameplayTag column, float value) => Set(column, value);
        public void SetDouble(GameplayTag column, double value) => Set(column, value);
    }
}
