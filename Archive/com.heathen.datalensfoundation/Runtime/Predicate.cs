using System;
using Heathen.GameplayTags;

namespace Heathen.DataLens
{
    /// <summary>
    /// A node in a view's filter predicate tree (DataLens-Spec §6.4.1): a leaf comparison or an And/Or/Not
    /// of children. The tree is data (serialisable, inspectable) the <see cref="Lens"/> compiles once to the
    /// Core's RPN scope program. Build trees with <see cref="DataLensFilter"/> (the <c>p</c> passed to
    /// <see cref="DataLensFrom.Where(Func{DataLensFilter, DataLensPredicate})"/>); the leaf carries its
    /// value's type, so equality/bitmask ops are valid on any fixed-width column and ordered/Range ops on
    /// numeric columns.
    /// </summary>
    public sealed class DataLensPredicate
    {
        internal enum NodeKind : byte { Leaf = 0, And = 1, Or = 2, Not = 3 }

        internal readonly NodeKind Kind;
        internal readonly DataLensPredicate[] Children;  // And/Or/Not
        internal readonly GameplayTag Column;            // leaf
        internal readonly CompareOp Op;                  // leaf (ignored when IsRange)
        internal readonly bool IsRange;
        internal readonly bool IsFloat;
        internal readonly long ILo, IHi;                 // integer threshold / range bounds
        internal readonly double DLo, DHi;               // float/double threshold / range bounds

        internal DataLensPredicate(GameplayTag col, CompareOp op, bool isFloat, long iv, double dv)
        { Kind = NodeKind.Leaf; Column = col; Op = op; IsFloat = isFloat; ILo = iv; DLo = dv; }

        internal DataLensPredicate(GameplayTag col, bool isFloat, long ilo, long ihi, double dlo, double dhi)
        { Kind = NodeKind.Leaf; Column = col; IsRange = true; IsFloat = isFloat; ILo = ilo; IHi = ihi; DLo = dlo; DHi = dhi; }

        internal DataLensPredicate(NodeKind kind, DataLensPredicate[] children)
        { Kind = kind; Children = children; }

        /// <summary>This predicate AND another.</summary>
        public DataLensPredicate And(DataLensPredicate other) => new DataLensPredicate(NodeKind.And, new[] { this, other });
        /// <summary>This predicate OR another.</summary>
        public DataLensPredicate Or(DataLensPredicate other) => new DataLensPredicate(NodeKind.Or, new[] { this, other });
    }

    /// <summary>
    /// The builder handed to a <c>Where(p =&gt; ...)</c> lambda: leaf comparisons and And/Or/Not combinators
    /// that assemble a <see cref="DataLensPredicate"/> tree. Stateless.
    /// </summary>
    public sealed class DataLensFilter
    {
        /// <summary>Create a predicate builder. Used both by <c>From().Where(p =&gt; ...)</c> and standalone by
        /// consumers (e.g. an engine building a filter to pass to a view) in another assembly.</summary>
        public DataLensFilter() { }

        /// <summary>A leaf comparison (op inferred-typed from <paramref name="value"/>).</summary>
        public DataLensPredicate Where<T>(GameplayTag column, CompareOp op, T value) where T : unmanaged => Leaf(column, op, value);

        public DataLensPredicate Eq<T>(GameplayTag column, T value) where T : unmanaged => Leaf(column, CompareOp.Equal, value);
        public DataLensPredicate NotEq<T>(GameplayTag column, T value) where T : unmanaged => Leaf(column, CompareOp.NotEqual, value);
        public DataLensPredicate Less<T>(GameplayTag column, T value) where T : unmanaged => Leaf(column, CompareOp.Less, value);
        public DataLensPredicate LessEq<T>(GameplayTag column, T value) where T : unmanaged => Leaf(column, CompareOp.LessEqual, value);
        public DataLensPredicate Greater<T>(GameplayTag column, T value) where T : unmanaged => Leaf(column, CompareOp.Greater, value);
        public DataLensPredicate GreaterEq<T>(GameplayTag column, T value) where T : unmanaged => Leaf(column, CompareOp.GreaterEqual, value);
        public DataLensPredicate HasAllBits<T>(GameplayTag column, T mask) where T : unmanaged => Leaf(column, CompareOp.HasAllBits, mask);
        public DataLensPredicate HasAnyBits<T>(GameplayTag column, T mask) where T : unmanaged => Leaf(column, CompareOp.HasAnyBits, mask);
        public DataLensPredicate LacksBits<T>(GameplayTag column, T mask) where T : unmanaged => Leaf(column, CompareOp.LacksBits, mask);

        /// <summary>Inclusive interval test, compiled to one fused branchless Range leaf.</summary>
        public DataLensPredicate InRange<T>(GameplayTag column, T lo, T hi) where T : unmanaged
        {
            if (lo is float || lo is double)
                return new DataLensPredicate(column, true, 0, 0, Convert.ToDouble(lo), Convert.ToDouble(hi));
            return new DataLensPredicate(column, false, Convert.ToInt64(lo), Convert.ToInt64(hi), 0, 0);
        }

        public DataLensPredicate And(params DataLensPredicate[] children) => Connective(DataLensPredicate.NodeKind.And, children);
        public DataLensPredicate Or(params DataLensPredicate[] children) => Connective(DataLensPredicate.NodeKind.Or, children);
        public DataLensPredicate Not(DataLensPredicate child) => new DataLensPredicate(DataLensPredicate.NodeKind.Not, new[] { child });

        private static DataLensPredicate Connective(DataLensPredicate.NodeKind kind, DataLensPredicate[] children)
        {
            if (children == null || children.Length == 0)
                throw new ArgumentException("And/Or needs at least one child.", nameof(children));
            return children.Length == 1 ? children[0] : new DataLensPredicate(kind, children);
        }

        private static DataLensPredicate Leaf<T>(GameplayTag column, CompareOp op, T value) where T : unmanaged
        {
            if (value is float f)  return new DataLensPredicate(column, op, true, 0, f);
            if (value is double d) return new DataLensPredicate(column, op, true, 0, d);
            return new DataLensPredicate(column, op, false, Convert.ToInt64(value), 0);
        }
    }
}
