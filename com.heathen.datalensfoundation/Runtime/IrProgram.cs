using System;
using System.Runtime.InteropServices;

namespace Heathen.DataLens
{
    /// <summary>
    /// One operation in a DataLens IR program (A4): a System (column transform) described as data,
    /// referencing its store by INDEX into the table passed to <see cref="Lens.Execute"/> /
    /// <see cref="Lens.Tick"/>. Build with the static factories. Blittable; mirrors native
    /// <c>dl_ir_op</c> exactly (fixed 64-byte layout).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IrOp
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

        /// <summary>Gate this op on (compareCol CMP threshold).</summary>
        public IrOp WithPredicate(int compareCol, CompareOp cmp, double threshold)
        {
            HasPredicate = 1; CompareCol = compareCol; Cmp = (int)cmp; Threshold = threshold; return this;
        }

        /// <summary>Restrict this op to the Simulation LOD band [minLod, maxLod].</summary>
        public IrOp WithLodBand(int minLod, int maxLod) { MinLod = minLod; MaxLod = maxLod; return this; }
    }

    /// <summary>
    /// A DataLens IR program (A4): an ordered, pointer-free, serialisable set of System ops the
    /// <see cref="Lens"/> executes (directly or on a cadence). Stores are referenced by index into the
    /// table supplied at execution.
    /// </summary>
    public sealed class IrProgram : IDisposable
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
