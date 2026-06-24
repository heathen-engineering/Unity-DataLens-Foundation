using System;
using System.Runtime.CompilerServices;
using Heathen.GameplayTags;

namespace Heathen.DataLens
{
    // Fixed-width value kinds for column<->field conversion. Raw = an unrecognised blittable type (custom
    // struct): copied byte-for-byte, never numerically converted.
    internal enum ValueKind { Raw, I8, U8, I16, U16, I32, U32, I64, U64, F32, F64 }

    /// <summary>
    /// Marshals a view's raw row-major Core payload to/from a managed <c>TRow[]</c> when any field's type
    /// differs (in width or float/int kind) from its column's stored type (DataLens-Spec §6.4.1). When every
    /// field is byte-compatible with its column, <see cref="Build"/> returns null and the
    /// <see cref="DataView{TRow}"/> stays zero-copy.
    /// </summary>
    internal sealed unsafe class ViewMarshaller<TRow> where TRow : unmanaged
    {
        private struct Col
        {
            public int FieldOffset, FieldSize, NativeOffset, NativeStride;
            public ValueKind FieldKind, NativeKind;
            public bool Raw;
        }

        private readonly Col[] _cols;
        private readonly int _nativeRowStride;
        private readonly int _fieldRowStride;

        private ViewMarshaller(Col[] cols, int nativeRowStride, int fieldRowStride)
        {
            _cols = cols;
            _nativeRowStride = nativeRowStride;
            _fieldRowStride = fieldRowStride;
        }

        public int NativeRowStride => _nativeRowStride;

        /// <summary>Build a marshaller, or return null when the view is byte-compatible (zero-copy).</summary>
        public static ViewMarshaller<TRow> Build(GameplayTag[] select, Type[] fieldTypes, DataLensSchema schema)
        {
            int n = select.Length;
            var cols = new Col[n];
            int nativeOff = 0, fieldOff = 0;
            bool allRaw = true;
            for (int k = 0; k < n; k++)
            {
                if (!schema.TryGetColumn(select[k], out DataColumn dc))
                    throw new ArgumentException($"View binds unknown column {(ulong)select[k]}.");
                MapKind(fieldTypes[k], out ValueKind fk, out int fs);
                MapKind(dc.Type, out ValueKind nk, out int _);
                int ns = dc.Stride; // the column's stride is authoritative

                bool bothRaw = fk == ValueKind.Raw || nk == ValueKind.Raw;
                bool raw;
                if (bothRaw)
                {
                    if (fs != ns)
                        throw new InvalidOperationException(
                            $"View record field of type {fieldTypes[k].Name} ({fs} B) cannot convert to/from column " +
                            $"{(ulong)select[k]} ({ns} B): non-numeric types must match the column stride exactly.");
                    raw = true;
                }
                else
                {
                    raw = fs == ns && IsFloat(fk) == IsFloat(nk);
                }

                cols[k] = new Col
                {
                    FieldOffset = fieldOff, FieldSize = fs, NativeOffset = nativeOff, NativeStride = ns,
                    FieldKind = fk, NativeKind = nk, Raw = raw
                };
                fieldOff += fs;
                nativeOff += ns;
                allRaw &= raw;
            }

            int sizeofRow = Unsafe.SizeOf<TRow>();
            if (fieldOff != sizeofRow)
                throw new InvalidOperationException(
                    $"View record {typeof(TRow).Name} is {sizeofRow} B but its fields total {fieldOff} B. " +
                    "Declare it [StructLayout(Sequential, Pack = 1)] with only fixed-width fields.");

            // All byte-compatible and same total => the DataView can map TRow directly over the payload.
            return allRaw ? null : new ViewMarshaller<TRow>(cols, nativeOff, sizeofRow);
        }

        public void NativeToManaged(IntPtr nativePayload, TRow[] managed, int rowCount)
        {
            byte* src = (byte*)nativePayload;
            fixed (TRow* mp = managed)
            {
                byte* dst = (byte*)mp;
                for (int r = 0; r < rowCount; r++)
                    for (int k = 0; k < _cols.Length; k++)
                        Convert(src + r * _nativeRowStride + _cols[k].NativeOffset, _cols[k].NativeKind, _cols[k].NativeStride,
                                dst + r * _fieldRowStride + _cols[k].FieldOffset, _cols[k].FieldKind, _cols[k].FieldSize,
                                _cols[k].Raw);
            }
        }

        public void ManagedToNative(TRow[] managed, IntPtr nativePayload, int rowCount)
        {
            byte* dst = (byte*)nativePayload;
            fixed (TRow* mp = managed)
            {
                byte* src = (byte*)mp;
                for (int r = 0; r < rowCount; r++)
                    for (int k = 0; k < _cols.Length; k++)
                        Convert(src + r * _fieldRowStride + _cols[k].FieldOffset, _cols[k].FieldKind, _cols[k].FieldSize,
                                dst + r * _nativeRowStride + _cols[k].NativeOffset, _cols[k].NativeKind, _cols[k].NativeStride,
                                _cols[k].Raw);
            }
        }

        private static void Convert(byte* src, ValueKind sk, int sSize, byte* dst, ValueKind dk, int dSize, bool raw)
        {
            if (raw)
            {
                int n = sSize < dSize ? sSize : dSize;
                Buffer.MemoryCopy(src, dst, dSize, n);
                return;
            }
            if (IsFloat(sk) || IsFloat(dk))
                WriteDouble(dst, dk, ReadDouble(src, sk));
            else
                WriteLong(dst, dk, ReadLong(src, sk));
        }

        private static bool IsFloat(ValueKind k) => k == ValueKind.F32 || k == ValueKind.F64;

        private static long ReadLong(byte* p, ValueKind k)
        {
            switch (k)
            {
                case ValueKind.I8:  return *(sbyte*)p;
                case ValueKind.U8:  return *p;
                case ValueKind.I16: return *(short*)p;
                case ValueKind.U16: return *(ushort*)p;
                case ValueKind.I32: return *(int*)p;
                case ValueKind.U32: return *(uint*)p;
                case ValueKind.I64: return *(long*)p;
                case ValueKind.U64: return unchecked((long)*(ulong*)p);
                default:            return 0;
            }
        }

        private static void WriteLong(byte* p, ValueKind k, long v)
        {
            switch (k)
            {
                case ValueKind.I8:  *(sbyte*)p = (sbyte)v; break;
                case ValueKind.U8:  *p = (byte)v; break;
                case ValueKind.I16: *(short*)p = (short)v; break;
                case ValueKind.U16: *(ushort*)p = (ushort)v; break;
                case ValueKind.I32: *(int*)p = (int)v; break;
                case ValueKind.U32: *(uint*)p = (uint)v; break;
                case ValueKind.I64: *(long*)p = v; break;
                case ValueKind.U64: *(ulong*)p = unchecked((ulong)v); break;
            }
        }

        private static double ReadDouble(byte* p, ValueKind k)
        {
            if (k == ValueKind.F32) return *(float*)p;
            if (k == ValueKind.F64) return *(double*)p;
            return ReadLong(p, k);
        }

        private static void WriteDouble(byte* p, ValueKind k, double v)
        {
            if (k == ValueKind.F32) { *(float*)p = (float)v; return; }
            if (k == ValueKind.F64) { *(double*)p = v; return; }
            WriteLong(p, k, (long)v);
        }

        private static void MapKind(Type t, out ValueKind kind, out int size)
        {
            if (t.IsEnum) t = Enum.GetUnderlyingType(t);
            if (t == typeof(sbyte))  { kind = ValueKind.I8;  size = 1; return; }
            if (t == typeof(byte))   { kind = ValueKind.U8;  size = 1; return; }
            if (t == typeof(bool))   { kind = ValueKind.U8;  size = 1; return; }
            if (t == typeof(short))  { kind = ValueKind.I16; size = 2; return; }
            if (t == typeof(ushort)) { kind = ValueKind.U16; size = 2; return; }
            if (t == typeof(int))    { kind = ValueKind.I32; size = 4; return; }
            if (t == typeof(uint))   { kind = ValueKind.U32; size = 4; return; }
            if (t == typeof(long))   { kind = ValueKind.I64; size = 8; return; }
            if (t == typeof(ulong))  { kind = ValueKind.U64; size = 8; return; }
            if (t == typeof(float))  { kind = ValueKind.F32; size = 4; return; }
            if (t == typeof(double)) { kind = ValueKind.F64; size = 8; return; }
            // Unrecognised but blittable (a custom [StructLayout] struct): treated as opaque bytes.
            kind = ValueKind.Raw;
            size = System.Runtime.InteropServices.Marshal.SizeOf(t);
        }
    }
}
