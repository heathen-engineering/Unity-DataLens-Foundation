using System.Runtime.InteropServices;

namespace Heathen.DataLens
{
    /// <summary>
    /// A data-described System: one column transform the <see cref="Lens"/> can schedule and run as
    /// part of a batch (<see cref="Lens.RunBatch"/>). This is "a System as data" rather than a method
    /// call — the form the Lens batches, runs concurrently where safe, and (later) compiles to/from
    /// the IR. Build instances with the static factory helpers (<see cref="Int"/>,
    /// <see cref="IntColumn"/>, <see cref="Float"/>, <see cref="FloatColumn"/>) rather than setting
    /// fields directly.
    /// <para>
    /// The layout mirrors the native <c>dl_system_desc</c> exactly (blittable, fixed padding) so an
    /// array marshals across the C ABI with no per-element copy. Only Int32 and Float element types
    /// are supported today; scalar operand/threshold are carried as <c>double</c> (Int32 fits exactly).
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemDesc
    {
        internal System.IntPtr Store;
        internal int ElemType;
        internal int Op;
        internal int OperandIsColumn;
        internal int HasPredicate;
        internal int Cmp;
        internal int Pad;
        internal ulong TargetCol;
        internal ulong OperandCol;
        internal ulong CompareCol;
        internal double Operand;
        internal double Threshold;
        // Response curve (A3.11): when ApplyCurve, the per-row cross-column operand is normalised over
        // [CurveMin,CurveMax] and passed through CurveType before the combine. These MUST mirror the
        // native dl_system_desc tail exactly (4×int32 then 4×float) — the struct is marshalled as a
        // blittable array, so a layout mismatch silently corrupts every element's stride.
        internal int ApplyCurve;
        internal int CurveType;
        internal int CurveInvert;
        internal int Pad2;
        internal float CurveMin;
        internal float CurveMax;
        internal float CurveP0;
        internal float CurveP1;

        private static SystemDesc Make(DataStore store, DataLensValueType elem, ulong targetCol, SystemOp op,
            bool operandIsColumn, ulong operandCol, double operand,
            bool hasPredicate, ulong compareCol, CompareOp cmp, double threshold)
        {
            if (store == null) throw new System.ArgumentNullException(nameof(store));
            return new SystemDesc
            {
                Store = store.Handle,
                ElemType = (int)elem,
                TargetCol = targetCol,
                Op = (int)op,
                OperandIsColumn = operandIsColumn ? 1 : 0,
                OperandCol = operandCol,
                Operand = operand,
                HasPredicate = hasPredicate ? 1 : 0,
                CompareCol = compareCol,
                Cmp = (int)cmp,
                Threshold = threshold,
                Pad = 0,
            };
        }

        // ── Int32 ────────────────────────────────────────────────────────────

        /// <summary>Scalar Int32 System: targetCol = targetCol OP operand (over all live rows).</summary>
        public static SystemDesc Int(DataStore store, ulong targetCol, SystemOp op, int operand)
            => Make(store, DataLensValueType.Int32, targetCol, op, false, 0, operand, false, 0, CompareOp.Always, 0);

        /// <summary>Predicated scalar Int32 System: apply where (compareCol CMP threshold).</summary>
        public static SystemDesc Int(DataStore store, ulong targetCol, SystemOp op, int operand,
            ulong compareCol, CompareOp cmp, int threshold)
            => Make(store, DataLensValueType.Int32, targetCol, op, false, 0, operand, true, compareCol, cmp, threshold);

        /// <summary>Cross-column Int32 System: operand read per-row from operandCol.</summary>
        public static SystemDesc IntColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol)
            => Make(store, DataLensValueType.Int32, targetCol, op, true, operandCol, 0, false, 0, CompareOp.Always, 0);

        /// <summary>Predicated cross-column Int32 System.</summary>
        public static SystemDesc IntColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol,
            ulong compareCol, CompareOp cmp, int threshold)
            => Make(store, DataLensValueType.Int32, targetCol, op, true, operandCol, 0, true, compareCol, cmp, threshold);

        // ── Float ────────────────────────────────────────────────────────────

        /// <summary>Scalar Float System: targetCol = targetCol OP operand (over all live rows).</summary>
        public static SystemDesc Float(DataStore store, ulong targetCol, SystemOp op, float operand)
            => Make(store, DataLensValueType.Float, targetCol, op, false, 0, operand, false, 0, CompareOp.Always, 0);

        /// <summary>Predicated scalar Float System: apply where (compareCol CMP threshold).</summary>
        public static SystemDesc Float(DataStore store, ulong targetCol, SystemOp op, float operand,
            ulong compareCol, CompareOp cmp, float threshold)
            => Make(store, DataLensValueType.Float, targetCol, op, false, 0, operand, true, compareCol, cmp, threshold);

        /// <summary>Cross-column Float System: operand read per-row from operandCol.</summary>
        public static SystemDesc FloatColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol)
            => Make(store, DataLensValueType.Float, targetCol, op, true, operandCol, 0, false, 0, CompareOp.Always, 0);

        /// <summary>Predicated cross-column Float System.</summary>
        public static SystemDesc FloatColumn(DataStore store, ulong targetCol, SystemOp op, ulong operandCol,
            ulong compareCol, CompareOp cmp, float threshold)
            => Make(store, DataLensValueType.Float, targetCol, op, true, operandCol, 0, true, compareCol, cmp, threshold);

        // ── Response curve (A3.11) ───────────────────────────────────────────

        /// <summary>
        /// Attach a response curve to a cross-column System (A3.11): the per-row operand is normalised
        /// over the curve's range and passed through its shape before the combine — one HATE §8
        /// consideration. The System must already be cross-column (built via <see cref="IntColumn"/> /
        /// <see cref="FloatColumn"/>); applying a curve to a scalar System has no defined meaning.
        /// </summary>
        public SystemDesc WithCurve(Curve curve)
        {
            ApplyCurve = 1;
            CurveType = (int)curve.Type;
            CurveInvert = curve.Invert ? 1 : 0;
            CurveMin = curve.Min;
            CurveMax = curve.Max;
            CurveP0 = curve.P0;
            CurveP1 = curve.P1;
            return this;
        }
    }
}
