using System.Runtime.InteropServices;

namespace Heathen.DataLens
{
    /// <summary>
    /// Raw P/Invoke surface over the native DataLens C ABI (libdatalens). Internal:
    /// gameplay code uses <see cref="DataStore"/>. Mirrors <c>datalens/c_api.h</c>.
    /// </summary>
    internal static class DataLensNative
    {
        // Unity resolves "datalens" to libdatalens.so / datalens.dll / libdatalens.dylib.
        private const string Lib = "datalens";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_abi_version();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_smallest_uint_type(ulong maxValue);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_smallest_int_type(long minValue, long maxValue);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern System.IntPtr dl_store_create(
            [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)] string[] colNames,
            [In] int[] colTypes,
            int colCount,
            ulong preallocRows);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_store_destroy(System.IntPtr store);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_store_row_count(System.IntPtr store);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_store_column_count(System.IntPtr store);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_store_row_stride(System.IntPtr store);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_store_set_f32(System.IntPtr store, ulong row, ulong col, float value);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_store_get_f32(System.IntPtr store, ulong row, ulong col, out float value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_store_set_i32(System.IntPtr store, ulong row, ulong col, int value);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_store_get_i32(System.IntPtr store, ulong row, ulong col, out int value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_store_set_f64(System.IntPtr store, ulong row, ulong col, double value);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_store_get_f64(System.IntPtr store, ulong row, ulong col, out double value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_store_set_valid(System.IntPtr store, ulong row, int valid);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_store_is_valid(System.IntPtr store, ulong row);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_store_alloc_row(System.IntPtr store);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_store_free_row(System.IntPtr store, ulong row);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_store_live_count(System.IntPtr store);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_store_run_f32(System.IntPtr store, ulong targetCol, int op, float operand,
            int hasPredicate, ulong compareCol, int cmp, float threshold);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_store_run_i32(System.IntPtr store, ulong targetCol, int op, int operand,
            int hasPredicate, ulong compareCol, int cmp, int threshold);

        // ── Lens (parallel Systems) ──────────────────────────────────────────
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern System.IntPtr dl_lens_create(int threadCount);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_lens_destroy(System.IntPtr lens);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_lens_thread_count(System.IntPtr lens);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_f32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, float operand, int hasPredicate, ulong compareCol, int cmp, float threshold);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_i32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, int operand, int hasPredicate, ulong compareCol, int cmp, int threshold);
    }
}
