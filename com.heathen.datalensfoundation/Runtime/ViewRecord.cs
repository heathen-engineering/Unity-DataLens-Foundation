using System;
using System.Collections.Generic;
using System.Reflection;
using Heathen.GameplayTags;

namespace Heathen.DataLens
{
    /// <summary>
    /// Marker for a DataLens view record: a fixed-width <c>unmanaged</c> struct whose fields are bound to
    /// columns with <see cref="DataLensColumnAttribute"/> and which exposes a <c>public static DataLensFrom
    /// From()</c> describing its topology (prime store + dereference joins + filters). A consumer works
    /// entirely through such a record, a <see cref="Lens"/>, and a <see cref="DataView{TRow}"/>; it never
    /// touches a store or a column (DataLens-Spec §6.4.1, the Four Coding Laws).
    /// <para>
    /// The interface is a marker only: the static <c>From()</c> cannot be declared on it (Unity targets
    /// C# 9, which has no static abstract interface members), so it is discovered by reflection. Declare
    /// the record <c>[StructLayout(LayoutKind.Sequential, Pack = 1)]</c> with all fields blittable and
    /// fixed-width; a calculated value is a plain C# property (DataLens never sees it), not a field.
    /// </para>
    /// </summary>
    public interface IDataLensViewRecord { }

    /// <summary>
    /// Binds a view-record field to a DataLens column by its <see cref="GameplayTag"/> dot-path. The field
    /// is read on every refresh and, unless <see cref="ReadOnly"/>, written back to the same column on
    /// commit. The Foundation converts between the column's stored type and the field's type (slice C); in
    /// slice A the widths must match (zero-copy). The path is hashed to a tag via
    /// <see cref="GameplayTag.FromName"/> (no registration required for resolution).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class DataLensColumnAttribute : Attribute
    {
        /// <summary>The column's dot-path (e.g. <c>DataLens.HATE.CoreTrait.Health</c>).</summary>
        public string Column { get; }

        /// <summary>When true the field is read but never written back on commit.</summary>
        public bool ReadOnly { get; }

        public DataLensColumnAttribute(string column, bool readOnly = false)
        {
            Column = column ?? throw new ArgumentNullException(nameof(column));
            ReadOnly = readOnly;
        }
    }

    /// <summary>
    /// The topology + filters a view record selects over: a prime store, optional index dereference joins,
    /// and a set of filters. Built fluently from a record's static <c>From()</c>. The fluent calls are
    /// sugar that accumulate a definition the <see cref="Lens"/> compiles once; in slice A filters are
    /// AND-composed prime-store scope predicates (the full And/Or/Not predicate tree, post-join filters and
    /// the fused Range predicate arrive in slice B, DataLens-Spec §6.4.1).
    /// </summary>
    public sealed class DataLensFrom
    {
        private readonly GameplayTag _primeStore;
        private readonly List<ViewJoin> _joins = new List<ViewJoin>();
        private DataLensPredicate _filter; // accumulated filter tree (null = no filter); clauses AND together

        public DataLensFrom(GameplayTag primeStore) { _primeStore = primeStore; }

        internal GameplayTag PrimeStore => _primeStore;
        internal ViewJoin[] JoinsArray => _joins.ToArray();
        internal DataLensPredicate Filter => _filter;

        private void AndInto(DataLensPredicate p)
            => _filter = _filter == null ? p : _filter.And(p);

        /// <summary>
        /// Glue in another store by dereferencing a prime-store column that holds the target row index (the
        /// catalogue pattern). <paramref name="into"/> is the target store; <paramref name="via"/> is the
        /// prime-store index column; <paramref name="absentSentinel"/> (int32.Max) means "lacks it".
        /// </summary>
        public DataLensFrom Dereference(GameplayTag into, GameplayTag via, uint absentSentinel = 0x7FFFFFFFu)
        {
            _joins.Add(ViewJoin.Dereference(into, via, absentSentinel));
            return this;
        }

        /// <summary>Glue in a row-aligned store (view row i is target row i).</summary>
        public DataLensFrom Aligned(GameplayTag into)
        {
            _joins.Add(ViewJoin.Aligned(into));
            return this;
        }

        /// <summary>
        /// Keep only rows where the column compares <paramref name="op"/> against <paramref name="value"/>.
        /// The leaf carries the value's type; equality/bitmask ops are valid on any fixed-width column,
        /// ordered ops on numeric columns. Multiple filters AND together. The column may be a prime-store
        /// column (pre-pruned) or a dereferenced column (evaluated post-join).
        /// </summary>
        public DataLensFrom Where<T>(GameplayTag column, CompareOp op, T value) where T : unmanaged
        {
            AndInto(new DataLensFilter().Where(column, op, value));
            return this;
        }

        /// <summary>
        /// Keep only rows where the column is within [<paramref name="lo"/>, <paramref name="hi"/>]
        /// inclusive, compiled to one fused branchless Range predicate.
        /// </summary>
        public DataLensFrom WhereInRange<T>(GameplayTag column, T lo, T hi) where T : unmanaged
        {
            AndInto(new DataLensFilter().InRange(column, lo, hi));
            return this;
        }

        /// <summary>
        /// Add a full boolean filter built fluently: <c>.Where(p =&gt; p.Or(p.And(p.InRange(...), p.Eq(...)),
        /// p.HasAnyBits(...)))</c>. The resulting tree ANDs with any other filters on this <c>From</c>.
        /// </summary>
        public DataLensFrom Where(Func<DataLensFilter, DataLensPredicate> build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            AndInto(build(new DataLensFilter()));
            return this;
        }
    }

    /// <summary>
    /// Per-record-type reflection, computed once and cached: the ordered projection tags (declaration =
    /// physical order for a Sequential struct), the per-column read-only mask, and the record's static
    /// <c>From()</c> accessor. Generic statics give us a free per-type cache.
    /// </summary>
    internal static class ViewRecordMeta<TRow> where TRow : unmanaged
    {
        public static readonly GameplayTag[] SelectTags;
        public static readonly bool[] ReadOnly;
        public static readonly Type[] FieldTypes;   // CLR type per projected field (for marshalling/conversion)
        private static readonly MethodInfo _from;

        static ViewRecordMeta()
        {
            Type t = typeof(TRow);
            FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            // Declaration order == physical order for [StructLayout(Sequential)]; MetadataToken is monotonic
            // in declaration order, so it gives the projection order the native payload is laid out in.
            Array.Sort(fields, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

            var tags = new GameplayTag[fields.Length];
            var ro = new bool[fields.Length];
            var types = new Type[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                var attr = (DataLensColumnAttribute)Attribute.GetCustomAttribute(fields[i], typeof(DataLensColumnAttribute));
                if (attr == null)
                    throw new InvalidOperationException(
                        $"View record {t.Name}.{fields[i].Name} is missing [DataLensColumn]: every field of a view " +
                        "record must be a bound column. A calculated value must be a property, not a field.");
                tags[i] = GameplayTag.FromName(attr.Column);
                ro[i] = attr.ReadOnly;
                types[i] = fields[i].FieldType;
            }
            SelectTags = tags;
            ReadOnly = ro;
            FieldTypes = types;

            _from = t.GetMethod("From", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (_from == null || _from.ReturnType != typeof(DataLensFrom))
                throw new InvalidOperationException(
                    $"View record {t.Name} must declare 'public static DataLensFrom From()'.");
        }

        public static DataLensFrom From() => (DataLensFrom)_from.Invoke(null, null);
    }
}
