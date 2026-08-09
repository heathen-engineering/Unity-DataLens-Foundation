using Heathen.GameplayTags;

namespace Heathen.DataLens
{
    /// <summary>Per view-row change state (mirrors the Core <c>ViewRowState</c>).</summary>
    public enum ViewRowState : byte { Unchanged = 0, Modified = 1, New = 2, Removed = 3 }

    /// <summary>
    /// How a view's base store glues to one other store (index-based, GameplayTag-addressed): either
    /// row-aligned, or a dereference of a base-store column holding the target row index (the catalogue),
    /// with a sentinel that means "absent".
    /// </summary>
    internal readonly struct ViewJoin
    {
        public readonly GameplayTag TargetStore;
        public readonly bool IsAligned;
        public readonly GameplayTag IndexColumn;   // base-store column holding the target row index (dereference)
        public readonly uint AbsentSentinel;

        private ViewJoin(GameplayTag target, bool aligned, GameplayTag idx, uint sentinel)
        {
            TargetStore = target;
            IsAligned = aligned;
            IndexColumn = idx;
            AbsentSentinel = sentinel;
        }

        public static ViewJoin Aligned(GameplayTag targetStore)
            => new ViewJoin(targetStore, true, default, 0);

        public static ViewJoin Dereference(GameplayTag targetStore, GameplayTag indexColumn, uint absentSentinel = 0x7FFFFFFFu)
            => new ViewJoin(targetStore, false, indexColumn, absentSentinel);
    }

    /// <summary>A scope predicate gating which base rows hydrate (typed comparison on a base-store column).</summary>
    internal readonly struct ViewScope
    {
        public readonly GameplayTag Column;
        public readonly CompareOp Op;
        public readonly long IValue;
        public readonly double DValue;
        public readonly bool IsFloat;

        private ViewScope(GameplayTag col, CompareOp op, long iv, double dv, bool isFloat)
        {
            Column = col;
            Op = op;
            IValue = iv;
            DValue = dv;
            IsFloat = isFloat;
        }

        public static ViewScope Int(GameplayTag column, CompareOp op, long value)
            => new ViewScope(column, op, value, 0, false);

        public static ViewScope Float(GameplayTag column, CompareOp op, double value)
            => new ViewScope(column, op, 0, value, true);
    }
}
