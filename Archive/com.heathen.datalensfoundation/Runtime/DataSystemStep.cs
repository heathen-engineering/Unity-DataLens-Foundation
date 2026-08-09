using Heathen.GameplayTags;

namespace Heathen.DataLens
{
    /// <summary>
    /// One step of a fused System pipeline, addressed by column <see cref="GameplayTag"/> (the public,
    /// consumer-facing form of the internal <see cref="SystemDesc"/>). A consumer describes an ordered list of
    /// these and hands them to <see cref="Lens.RunSystemBatch"/>, which resolves the tags to native column
    /// indices, builds the <see cref="SystemDesc"/> array, and runs the whole pipeline in ONE dispatch — N steps,
    /// one pass, submission order preserved for conflicting steps (decision #17: HATE describes, DataLens fuses).
    /// <para>
    /// A step is a column transform <c>targetCol = targetCol OP operand</c>, where the operand is a scalar
    /// (<see cref="Scalar"/>) or read per-row from another column (<see cref="Column"/>), and may be gated by a
    /// per-row predicate (<c>where compareCol CMP threshold</c>). Only Float/Int32 columns are supported.
    /// </para>
    /// </summary>
    public readonly struct DataSystemStep
    {
        internal readonly GameplayTag TargetCol;
        internal readonly SystemOp Op;
        internal readonly bool OperandIsColumn;
        internal readonly GameplayTag OperandCol;
        internal readonly double Operand;
        internal readonly bool HasPredicate;
        internal readonly GameplayTag CompareCol;
        internal readonly CompareOp Cmp;
        internal readonly double Threshold;

        private DataSystemStep(GameplayTag targetCol, SystemOp op, bool operandIsColumn, GameplayTag operandCol,
            double operand, bool hasPredicate, GameplayTag compareCol, CompareOp cmp, double threshold)
        {
            TargetCol = targetCol;
            Op = op;
            OperandIsColumn = operandIsColumn;
            OperandCol = operandCol;
            Operand = operand;
            HasPredicate = hasPredicate;
            CompareCol = compareCol;
            Cmp = cmp;
            Threshold = threshold;
        }

        /// <summary>A scalar step: <c>targetCol = targetCol OP operand</c> over all live rows.</summary>
        public static DataSystemStep Scalar(GameplayTag targetCol, SystemOp op, double operand)
            => new DataSystemStep(targetCol, op, false, default, operand, false, default, CompareOp.Always, 0);

        /// <summary>A scalar step gated where <c>(compareCol CMP threshold)</c>.</summary>
        public static DataSystemStep Scalar(GameplayTag targetCol, SystemOp op, double operand,
            GameplayTag compareCol, CompareOp cmp, double threshold)
            => new DataSystemStep(targetCol, op, false, default, operand, true, compareCol, cmp, threshold);

        /// <summary>A cross-column step: <c>targetCol = targetCol OP operandCol</c> (operand read per-row).</summary>
        public static DataSystemStep Column(GameplayTag targetCol, SystemOp op, GameplayTag operandCol)
            => new DataSystemStep(targetCol, op, true, operandCol, 0, false, default, CompareOp.Always, 0);

        /// <summary>A cross-column step gated where <c>(compareCol CMP threshold)</c>.</summary>
        public static DataSystemStep Column(GameplayTag targetCol, SystemOp op, GameplayTag operandCol,
            GameplayTag compareCol, CompareOp cmp, double threshold)
            => new DataSystemStep(targetCol, op, true, operandCol, 0, true, compareCol, cmp, threshold);
    }
}
