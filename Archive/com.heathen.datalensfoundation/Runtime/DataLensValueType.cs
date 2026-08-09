namespace Heathen.DataLens
{
    /// <summary>
    /// Fixed-width column value types. Integer codes mirror the native
    /// <c>DataLensValueType</c> enum exactly and cross the C ABI as <c>int</c>.
    /// </summary>
    public enum DataLensValueType
    {
        Bool   = 0,
        Int8   = 1,
        UInt8  = 2,
        Int16  = 3,
        UInt16 = 4,
        Int32  = 5,
        UInt32 = 6,
        Int64  = 7,
        UInt64 = 8,
        Float  = 9,
        Double = 10,
        Guid   = 11,
    }
}
