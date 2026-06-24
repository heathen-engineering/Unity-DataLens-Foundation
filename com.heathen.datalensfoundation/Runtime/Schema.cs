using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Heathen.GameplayTags;

namespace Heathen.DataLens
{
    /// <summary>
    /// A column declaration: a <see cref="GameplayTag"/> id + a fixed byte stride + an optional default.
    /// Core is type-blind (it stores only the stride and the default bytes); the CLR <see cref="Type"/> is
    /// kept here for the Foundation's marshalling. Build with <see cref="Of{T}(GameplayTag)"/> — the
    /// <c>unmanaged</c> constraint guarantees <typeparamref name="T"/> is fixed-width with no reference
    /// fields (a string/array/list field is a compile error).
    /// </summary>
    public readonly struct DataColumn
    {
        public readonly GameplayTag Id;
        public readonly int Stride;
        public readonly Type Type;      // marshalling hint (Foundation only; Core never sees it)
        public readonly byte[] Default; // length == Stride, or null for an all-zero default

        private DataColumn(GameplayTag id, int stride, Type type, byte[] defaultBytes)
        {
            Id = id;
            Stride = stride;
            Type = type;
            Default = defaultBytes;
        }

        /// <summary>A column of type <typeparamref name="T"/> defaulting to zero.</summary>
        public static DataColumn Of<T>(GameplayTag id) where T : unmanaged
            => new DataColumn(id, Unsafe.SizeOf<T>(), typeof(T), null);

        /// <summary>A column of type <typeparamref name="T"/> seeded on each new row with <paramref name="defaultValue"/>.</summary>
        public static DataColumn Of<T>(GameplayTag id, T defaultValue) where T : unmanaged
        {
            int stride = Unsafe.SizeOf<T>();
            var bytes = new byte[stride];
            MemoryMarshal.Write(bytes, ref defaultValue);
            return new DataColumn(id, stride, typeof(T), bytes);
        }

        /// <summary>
        /// Build a column from a runtime <see cref="DataLensValueType"/> (for data-driven consumers like a
        /// codegen'd engine that does not know <c>T</c> at compile time). <paramref name="defaultBytes"/>, if
        /// given, must be <see cref="StrideOf"/> bytes (null = all-zero default).
        /// </summary>
        /// <remarks>
        /// Named <c>OfType</c>, not <c>Of</c>: a 2-arg <c>Of(tag, someValueType)</c> would silently bind to the
        /// generic <see cref="Of{T}(GameplayTag, T)"/> with <c>T = DataLensValueType</c>, making a 4-byte enum
        /// column whose default is the enum's integer value. <c>OfType</c> is unambiguous.
        /// </remarks>
        public static DataColumn OfType(GameplayTag id, DataLensValueType type, byte[] defaultBytes = null)
        {
            int stride = StrideOf(type);
            if (defaultBytes != null && defaultBytes.Length != stride)
                throw new ArgumentException($"default bytes length {defaultBytes.Length} != stride {stride} for {type}.", nameof(defaultBytes));
            return new DataColumn(id, stride, ClrTypeOf(type), defaultBytes);
        }

        /// <summary>Byte width of a <see cref="DataLensValueType"/>.</summary>
        public static int StrideOf(DataLensValueType type)
        {
            switch (type)
            {
                case DataLensValueType.Bool:
                case DataLensValueType.Int8:
                case DataLensValueType.UInt8:  return 1;
                case DataLensValueType.Int16:
                case DataLensValueType.UInt16: return 2;
                case DataLensValueType.Int32:
                case DataLensValueType.UInt32:
                case DataLensValueType.Float:  return 4;
                case DataLensValueType.Int64:
                case DataLensValueType.UInt64:
                case DataLensValueType.Double: return 8;
                case DataLensValueType.Guid:   return 16;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static Type ClrTypeOf(DataLensValueType type)
        {
            switch (type)
            {
                case DataLensValueType.Bool:   return typeof(bool);
                case DataLensValueType.Int8:   return typeof(sbyte);
                case DataLensValueType.UInt8:  return typeof(byte);
                case DataLensValueType.Int16:  return typeof(short);
                case DataLensValueType.UInt16: return typeof(ushort);
                case DataLensValueType.Int32:  return typeof(int);
                case DataLensValueType.UInt32: return typeof(uint);
                case DataLensValueType.Int64:  return typeof(long);
                case DataLensValueType.UInt64: return typeof(ulong);
                case DataLensValueType.Float:  return typeof(float);
                case DataLensValueType.Double: return typeof(double);
                case DataLensValueType.Guid:   return typeof(System.Guid);
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }
    }

    /// <summary>
    /// A store declaration: a <see cref="GameplayTag"/> id + a store-level capacity + its index-aligned
    /// columns. Authoring sugar that populates a <see cref="DataLensSchema"/>; a store is conceptual — it is
    /// just the knowledge that these columns share a row space, so index <c>i</c> is a valid record across
    /// them (which is what Systems and Views rely on). Devs never hold one at runtime; they use DataViews.
    /// </summary>
    public sealed class DataStoreSchema
    {
        public GameplayTag Id { get; }
        public int Capacity { get; }
        public IReadOnlyList<DataColumn> Columns { get; }

        public DataStoreSchema(GameplayTag id, int capacity, params DataColumn[] columns)
        {
            Id = id;
            Capacity = capacity;
            Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        }
    }

    /// <summary>
    /// The whole-database declaration plus the Foundation's <see cref="GameplayTag"/> → index resolution
    /// (the native ABI is index-addressed). A column id is trusted globally unique, so it resolves to exactly
    /// one (store, column). A <see cref="Lens"/> is built from one of these.
    /// </summary>
    public sealed class DataLensSchema
    {
        private readonly List<DataStoreSchema> _stores = new List<DataStoreSchema>();
        private readonly Dictionary<ulong, int> _storeIndex = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, (int store, int column)> _columnLoc = new Dictionary<ulong, (int, int)>();
        private readonly Dictionary<ulong, DataColumn> _columnById = new Dictionary<ulong, DataColumn>();

        public IReadOnlyList<DataStoreSchema> Stores => _stores;

        public DataLensSchema Add(DataStoreSchema store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            int si = _stores.Count;
            _stores.Add(store);
            _storeIndex[store.Id] = si;
            for (int c = 0; c < store.Columns.Count; c++)
            {
                DataColumn col = store.Columns[c];
                _columnLoc[col.Id] = (si, c);   // global uniqueness: column id -> its location
                _columnById[col.Id] = col;
            }
            return this;
        }

        /// <summary>Store index for a store id, or -1.</summary>
        public int FindStore(GameplayTag storeId)
            => _storeIndex.TryGetValue(storeId, out int i) ? i : -1;

        /// <summary>Resolve a column id to its (store, column) indices. False if unknown.</summary>
        public bool ResolveColumn(GameplayTag columnId, out int store, out int column)
        {
            if (_columnLoc.TryGetValue(columnId, out (int store, int column) loc))
            {
                store = loc.store;
                column = loc.column;
                return true;
            }
            store = -1;
            column = -1;
            return false;
        }

        /// <summary>The column declaration for an id (stride / type / default). False if unknown.</summary>
        public bool TryGetColumn(GameplayTag columnId, out DataColumn column)
            => _columnById.TryGetValue(columnId, out column);
    }
}
