using System;
using System.Runtime.InteropServices;

namespace Heathen.DataLens
{
    /// <summary>
    /// One operation in a DataLens IR program (A4): a System (column transform) described as data,
    /// referencing its store by INDEX into the table passed to <see cref="Lens.Execute"/> /
    /// <see cref="Lens.Tick"/>. Build with the static factories. Blittable; mirrors native
    /// <c>dl_ir_op</c> exactly (fixed 96-byte layout: 12×int32 + 2×double + 4×int32 + 4×float).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct IrOp
    {
        internal int StoreIndex;
        internal int ElemType;
        internal int TargetCol;
        internal int Op;
        internal int OperandIsColumn;
        internal int OperandCol;
        internal int HasPredicate;
        internal int CompareCol;
        internal int Cmp;
        internal int MinLod;
        internal int MaxLod;
        internal int Pad;
        internal double Operand;
        internal double Threshold;
        // Response curve (A3.11): when ApplyCurve, the per-row cross-column operand is normalised over
        // [CurveMin,CurveMax] and passed through CurveType before the combine. MUST mirror the native
        // dl_ir_op tail exactly (4×int32 then 4×float) — the struct is marshalled by value to the C ABI.
        internal int ApplyCurve;
        internal int CurveType;
        internal int CurveInvert;
        internal int Pad2;
        internal float CurveMin;
        internal float CurveMax;
        internal float CurveP0;
        internal float CurveP1;

        private static IrOp Base(int storeIndex, DataLensValueType elem, int targetCol, SystemOp op)
            => new IrOp
            {
                StoreIndex = storeIndex, ElemType = (int)elem, TargetCol = targetCol, Op = (int)op,
                MinLod = 0, MaxLod = 255,
            };

        /// <summary>Scalar op: target = target OP operand (over store `storeIndex`).</summary>
        public static IrOp Int(int storeIndex, int targetCol, SystemOp op, int operand)
        {
            var o = Base(storeIndex, DataLensValueType.Int32, targetCol, op); o.Operand = operand; return o;
        }

        /// <summary>Cross-column op: target = target OP operandCol.</summary>
        public static IrOp IntColumn(int storeIndex, int targetCol, SystemOp op, int operandCol)
        {
            var o = Base(storeIndex, DataLensValueType.Int32, targetCol, op);
            o.OperandIsColumn = 1; o.OperandCol = operandCol; return o;
        }

        /// <summary>Scalar Float op.</summary>
        public static IrOp Float(int storeIndex, int targetCol, SystemOp op, float operand)
        {
            var o = Base(storeIndex, DataLensValueType.Float, targetCol, op); o.Operand = operand; return o;
        }

        /// <summary>Cross-column Float op.</summary>
        public static IrOp FloatColumn(int storeIndex, int targetCol, SystemOp op, int operandCol)
        {
            var o = Base(storeIndex, DataLensValueType.Float, targetCol, op);
            o.OperandIsColumn = 1; o.OperandCol = operandCol; return o;
        }

        /// <summary>Scalar op on a column of ANY element type (A8: UInt8..UInt64 / Int8..Int64 / Float /
        /// Double, not just Int32/Float) — so HATE can run effects/aggregation on narrow-packed attribute
        /// columns. The operand is carried as a double and cast to the column's type at execution.</summary>
        public static IrOp Typed(int storeIndex, DataLensValueType elem, int targetCol, SystemOp op, double operand)
        {
            var o = Base(storeIndex, elem, targetCol, op); o.Operand = operand; return o;
        }

        /// <summary>Cross-column op on a column of ANY element type (A8): the per-row operand is read from
        /// <paramref name="operandCol"/> (interpreted as the same type). Used for the HATE clamp primitive
        /// (Current = Min(Current, Max)) on narrow columns.</summary>
        public static IrOp TypedColumn(int storeIndex, DataLensValueType elem, int targetCol, SystemOp op, int operandCol)
        {
            var o = Base(storeIndex, elem, targetCol, op);
            o.OperandIsColumn = 1; o.OperandCol = operandCol; return o;
        }

        /// <summary>
        /// Cross-column op whose per-row operand is passed through a response curve before the combine
        /// (A3.11) — one HATE §8 consideration: <c>score COMBINE= curve(metricCol)</c>.
        /// </summary>
        public static IrOp CurvedColumn(int storeIndex, DataLensValueType elem, int targetCol, SystemOp op, int operandCol, Curve curve)
        {
            var o = Base(storeIndex, elem, targetCol, op);
            o.OperandIsColumn = 1; o.OperandCol = operandCol;
            return o.WithCurve(curve);
        }

        /// <summary>Float cross-column curved op.</summary>
        public static IrOp FloatCurvedColumn(int storeIndex, int targetCol, SystemOp op, int operandCol, Curve curve)
            => CurvedColumn(storeIndex, DataLensValueType.Float, targetCol, op, operandCol, curve);

        /// <summary>Int32 cross-column curved op.</summary>
        public static IrOp IntCurvedColumn(int storeIndex, int targetCol, SystemOp op, int operandCol, Curve curve)
            => CurvedColumn(storeIndex, DataLensValueType.Int32, targetCol, op, operandCol, curve);

        /// <summary>Gate this op on (compareCol CMP threshold).</summary>
        public IrOp WithPredicate(int compareCol, CompareOp cmp, double threshold)
        {
            HasPredicate = 1; CompareCol = compareCol; Cmp = (int)cmp; Threshold = threshold; return this;
        }

        /// <summary>Restrict this op to the Simulation LOD band [minLod, maxLod].</summary>
        public IrOp WithLodBand(int minLod, int maxLod) { MinLod = minLod; MaxLod = maxLod; return this; }

        /// <summary>
        /// Attach a response curve to a cross-column op (<see cref="OperandIsColumn"/> must already be set,
        /// i.e. built via <see cref="IntColumn"/>/<see cref="FloatColumn"/>). The per-row operand is
        /// normalised over the curve's range and passed through its shape before the combine (A3.11).
        /// </summary>
        public IrOp WithCurve(Curve curve)
        {
            ApplyCurve = 1;
            CurveType = (int)curve.Type;
            CurveInvert = curve.Invert ? 1 : 0;
            CurveMin = curve.Min; CurveMax = curve.Max;
            CurveP0 = curve.P0; CurveP1 = curve.P1;
            return this;
        }
    }

    /// <summary>
    /// A DataLens IR program (A4): an ordered, pointer-free, serialisable set of System ops the
    /// <see cref="Lens"/> executes (directly or on a cadence). Stores are referenced by index into the
    /// table supplied at execution.
    /// </summary>
    internal sealed class IrProgram : IDisposable
    {
        private IntPtr _handle;
        internal IntPtr Handle => _handle;

        public IrProgram()
        {
            _handle = DataLensNative.dl_ir_create();
            if (_handle == IntPtr.Zero) throw new InvalidOperationException("Native dl_ir_create failed.");
        }

        private IrProgram(IntPtr handle) { _handle = handle; }

        /// <summary>Append an op to the program.</summary>
        public void Add(IrOp op) => DataLensNative.dl_ir_add_system(_handle, ref op);

        /// <summary>Number of ops in the program.</summary>
        public ulong Count => DataLensNative.dl_ir_count(_handle);

        /// <summary>Serialise to a flat byte buffer (storage / transport / replay — the networking seam).</summary>
        public byte[] Serialize()
        {
            ulong size = DataLensNative.dl_ir_serialize(_handle, null, 0);
            var buf = new byte[size];
            if (size > 0) DataLensNative.dl_ir_serialize(_handle, buf, size);
            return buf;
        }

        /// <summary>Parse a program from a buffer produced by <see cref="Serialize"/>; null on a bad buffer.</summary>
        public static IrProgram Deserialize(byte[] data)
        {
            if (data == null) return null;
            IntPtr h = DataLensNative.dl_ir_deserialize(data, (ulong)data.Length);
            return h == IntPtr.Zero ? null : new IrProgram(h);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                DataLensNative.dl_ir_destroy(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~IrProgram() => Dispose();
    }
}
