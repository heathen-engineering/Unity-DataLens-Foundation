using System;

namespace Heathen.DataLens
{
    /// <summary>
    /// The Lens owns a native worker pool and runs Systems over a <see cref="DataStore"/> in
    /// parallel. Results are identical to a single-threaded run regardless of thread count, because
    /// each thread processes a disjoint, independent row range. A1/A3 walking-skeleton slice — view
    /// refresh and commit consolidation arrive in later phases.
    /// </summary>
    public sealed class Lens : IDisposable
    {
        private IntPtr _handle;

        /// <summary>Create a Lens with a worker pool. <paramref name="threadCount"/> of 0 uses hardware concurrency.</summary>
        public Lens(int threadCount = 0)
        {
            _handle = DataLensNative.dl_lens_create(threadCount);
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("Native dl_lens_create failed.");
        }

        /// <summary>Number of threads the pool runs with (including the calling thread).</summary>
        public int ThreadCount => DataLensNative.dl_lens_thread_count(_handle);

        // Run a conditional column System over the store in parallel. Returns rows affected.

        public ulong RunFloat(DataStore store, ulong targetCol, SystemOp op, float operand)
            => DataLensNative.dl_lens_run_f32(_handle, store.Handle, targetCol, (int)op, operand, 0, 0, 0, 0f);

        public ulong RunFloat(DataStore store, ulong targetCol, SystemOp op, float operand, ulong compareCol, CompareOp cmp, float threshold)
            => DataLensNative.dl_lens_run_f32(_handle, store.Handle, targetCol, (int)op, operand, 1, compareCol, (int)cmp, threshold);

        public ulong RunInt(DataStore store, ulong targetCol, SystemOp op, int operand)
            => DataLensNative.dl_lens_run_i32(_handle, store.Handle, targetCol, (int)op, operand, 0, 0, 0, 0);

        public ulong RunInt(DataStore store, ulong targetCol, SystemOp op, int operand, ulong compareCol, CompareOp cmp, int threshold)
            => DataLensNative.dl_lens_run_i32(_handle, store.Handle, targetCol, (int)op, operand, 1, compareCol, (int)cmp, threshold);

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                DataLensNative.dl_lens_destroy(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~Lens() => Dispose();
    }
}
