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
            [In] ulong[] colTags,
            [In] ulong[] colStrides,
            [In] byte[] colDefaults, // concatenated defaults (Sum(strides) bytes); null = all-zero
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

        // Per-row Simulation LOD (A3.5).
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_store_set_lod(System.IntPtr store, ulong row, int level);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_store_get_lod(System.IntPtr store, ulong row);

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

        // Cross-column Systems (A3.3): operand read per-row from operandCol.
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_store_run_col_f32(System.IntPtr store, ulong targetCol, int op, ulong operandCol,
            int hasPredicate, ulong compareCol, int cmp, float threshold);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_store_run_col_i32(System.IntPtr store, ulong targetCol, int op, ulong operandCol,
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

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_col_f32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, ulong operandCol, int hasPredicate, ulong compareCol, int cmp, float threshold);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_col_i32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, ulong operandCol, int hasPredicate, ulong compareCol, int cmp, int threshold);

        // Parallel curved cross-column Systems (A3.11): rhs = curve(operandCol[r]) before the combine.
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_curved_f32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, ulong operandCol, int curveType, float curveMin, float curveMax, float curveP0, float curveP1,
            int curveInvert, int hasPredicate, ulong compareCol, int cmp, float threshold);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_curved_i32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, ulong operandCol, int curveType, float curveMin, float curveMax, float curveP0, float curveP1,
            int curveInvert, int hasPredicate, ulong compareCol, int cmp, int threshold);

        // Counter-based noise (A3.11/A3.12): fill `target = target OP noise` and perturb `target = target OP
        // (operandCol[r] * noise)`, noise = lo + (hi-lo)*u01(row,tick,seed). Stateless, reproducible PRNG.
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_noise_f32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, float noiseLo, float noiseHi, ulong seed, ulong tick,
            int hasPredicate, ulong compareCol, int cmp, float threshold);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_noise_i32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, int noiseLo, int noiseHi, ulong seed, ulong tick,
            int hasPredicate, ulong compareCol, int cmp, int threshold);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_noise_perturb_f32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, ulong operandCol, float noiseLo, float noiseHi, ulong seed, ulong tick,
            int hasPredicate, ulong compareCol, int cmp, float threshold);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_noise_perturb_i32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, ulong operandCol, int noiseLo, int noiseHi, ulong seed, ulong tick,
            int hasPredicate, ulong compareCol, int cmp, int threshold);

        // Argmax-across-columns (A3.13): reduce K score columns to a per-row Choice index column.
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_argmax_f32(System.IntPtr lens, System.IntPtr store, ulong choiceCol,
            [In] ulong[] scoreCols, ulong scoreColCount, float minScore, int noChoice);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_argmax_i32(System.IntPtr lens, System.IntPtr store, ulong choiceCol,
            [In] ulong[] scoreCols, ulong scoreColCount, int minScore, int noChoice);

        // Batched Systems (A3.4): an array of blittable SystemDesc marshals across in one call.
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_batch(System.IntPtr lens,
            [In] SystemDesc[] descs, ulong count);

        // Batched Systems over a LOD band (A3.5): the band applies to every System in the batch.
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_batch_lod(System.IntPtr lens,
            [In] SystemDesc[] descs, ulong count, int minLod, int maxLod);

        // ── Query/Update IR (A4.2) ───────────────────────────────────────────
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern System.IntPtr dl_ir_create();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_ir_destroy(System.IntPtr program);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_ir_add_system(System.IntPtr program, ref IrOp op);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_ir_count(System.IntPtr program);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_ir_serialize(System.IntPtr program, [Out] byte[] buf, ulong bufLen);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern System.IntPtr dl_ir_deserialize([In] byte[] data, ulong size);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_execute(System.IntPtr lens, System.IntPtr program,
            [In] System.IntPtr[] stores, ulong storeCount);

        // ── Tick / cadence scheduler (A5) ────────────────────────────────────
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_add_scheduled_program(System.IntPtr lens, System.IntPtr program,
            ulong period, int minLod, int maxLod, ulong phase);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_lens_clear_schedule(System.IntPtr lens);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_scheduled_program_count(System.IntPtr lens);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_add_scheduled_view(System.IntPtr lens, System.IntPtr view,
            ulong storeIndex, ulong period, ulong phase);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_add_scheduled_view_lod(System.IntPtr lens, System.IntPtr view,
            ulong storeIndex, ulong period, int minLod, int maxLod, ulong phase);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_lens_clear_scheduled_views(System.IntPtr lens);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_scheduled_view_count(System.IntPtr lens);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_current_tick(System.IntPtr lens);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_lens_reset_tick(System.IntPtr lens, ulong tick);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_tick(System.IntPtr lens, [In] System.IntPtr[] stores, ulong storeCount);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_lens_refresh_view(System.IntPtr lens, System.IntPtr view, System.IntPtr store);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_lens_refresh_view_lod(System.IntPtr lens, System.IntPtr view, System.IntPtr store, int minLod, int maxLod);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_f32_pred_i32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, float operand, ulong compareCol, int cmp, int threshold);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_run_i32_pred_f32(System.IntPtr lens, System.IntPtr store, ulong targetCol,
            int op, int operand, ulong compareCol, int cmp, float threshold);

        // ── Read-only DataView (A5) ──────────────────────────────────────────
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern System.IntPtr dl_view_create([In] ulong[] sourceColumns, ulong columnCount);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_view_destroy(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_view_refresh(System.IntPtr view, System.IntPtr store);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_view_refresh_lod(System.IntPtr view, System.IntPtr store, int minLod, int maxLod);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_view_row_count(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_view_column_count(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_view_row_stride(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_view_source_row(System.IntPtr view, ulong viewRow);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_view_get_f32(System.IntPtr view, ulong viewRow, ulong viewCol, out float value);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_view_get_i32(System.IntPtr view, ulong viewRow, ulong viewCol, out int value);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int dl_view_get_f64(System.IntPtr view, ulong viewRow, ulong viewCol, out double value);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern System.IntPtr dl_view_data(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_view_byte_size(System.IntPtr view);

        // ---- Read/write View (DataLens-Spec.md §6.4). POD mirrors of the view IR (Sequential layout
        // matches the C structs under natural alignment). ----

        [StructLayout(LayoutKind.Sequential)]
        internal struct dl_view_join { public ulong target_store; public int aligned; public ulong index_column; public ulong absent_sentinel; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct dl_view_column { public ulong source; public ulong column; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct dl_view_scope { public ulong column; public int type; public int op; public long ivalue; public double dvalue; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct dl_view_write { public ulong view_column; public ulong target_store; public ulong target_column; }
        // RPN predicate node (§6.4.1). Field order/widths mirror the C dl_view_predicate exactly.
        [StructLayout(LayoutKind.Sequential)]
        internal struct dl_view_predicate
        {
            public int kind;       // 0 leaf, 1 and, 2 or, 3 not
            public int is_range;
            public int source;     // 0 = base, k>=1 = the (k-1)th join's target
            public int column;
            public int type;       // DataLensValueType
            public int op;         // DataCompareOp (ignored when is_range)
            public long ivalue;    // threshold / range lo
            public long ivalue_hi; // range hi (integer)
            public double dvalue;  // threshold / range lo (float/double)
            public double dvalue_hi; // range hi (float/double)
        }

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern System.IntPtr dl_rwview_create(ulong baseStore,
            [In] dl_view_join[] joins, int joinCount,
            [In] dl_view_column[] columns, int columnCount,
            [In] dl_view_scope[] scope, int scopeCount);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_rwview_destroy(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_rwview_set_writeback(System.IntPtr view,
            [In] dl_view_write[] insert, int insertCount,
            [In] dl_view_write[] update, int updateCount,
            [In] ulong[] deleteStores, int deleteCount);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_rwview_set_scope_program(System.IntPtr view,
            [In] dl_view_predicate[] preds, int predCount);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_rwview_refresh(System.IntPtr view, [In] System.IntPtr[] stores, int storeCount);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_rwview_commit(System.IntPtr view, [In] System.IntPtr[] stores, int storeCount);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_rwview_row_count(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_rwview_column_count(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_rwview_row_stride(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_rwview_byte_size(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern System.IntPtr dl_rwview_data(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern System.IntPtr dl_rwview_mutable_data(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_rwview_column_offset(System.IntPtr view, ulong col);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_rwview_column_stride(System.IntPtr view, ulong col);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte dl_rwview_get_state(System.IntPtr view, ulong viewRow);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_rwview_set_state(System.IntPtr view, ulong viewRow, byte state);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_rwview_add_row(System.IntPtr view);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_rwview_source_base_row(System.IntPtr view, ulong viewRow);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_rwview_source_join_row(System.IntPtr view, ulong viewRow, ulong join);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_add_scheduled_rwview(System.IntPtr lens, System.IntPtr view, ulong period, ulong phase);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void dl_lens_clear_scheduled_rwviews(System.IntPtr lens);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong dl_lens_scheduled_rwview_count(System.IntPtr lens);
    }
}
