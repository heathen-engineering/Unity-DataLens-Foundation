namespace Heathen.DataLens
{
    /// <summary>
    /// Helpers for declaring fixed-width columns by value range (A2 range-narrowing). The native
    /// core owns the rule, so the smallest byte-aligned type for a range is identical everywhere.
    /// </summary>
    public static class Column
    {
        /// <summary>Smallest unsigned type (UInt8/16/32/64) that holds <paramref name="maxValue"/>.</summary>
        public static DataLensValueType SmallestUnsigned(ulong maxValue)
            => (DataLensValueType)DataLensNative.dl_smallest_uint_type(maxValue);

        /// <summary>Smallest signed type (Int8/16/32/64) that holds the inclusive range.</summary>
        public static DataLensValueType SmallestSigned(long minValue, long maxValue)
            => (DataLensValueType)DataLensNative.dl_smallest_int_type(minValue, maxValue);
    }
}
