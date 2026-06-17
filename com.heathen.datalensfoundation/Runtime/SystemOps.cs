namespace Heathen.DataLens
{
    /// <summary>Arithmetic a System applies to a target column cell (cur OP operand). Mirrors native
    /// DataSystemOp. The bitwise ops (And/Or/Xor/AndNot) are integer-only; on a Float/Double column
    /// they are a no-op.</summary>
    public enum SystemOp
    {
        Set    = 0,
        Add    = 1,
        Sub    = 2,
        Mul    = 3,
        Min    = 4,
        Max    = 5,
        And    = 6, // cur & operand   (mask bits)
        Or     = 7, // cur | operand   (set bits)
        Xor    = 8, // cur ^ operand   (toggle bits)
        AndNot = 9, // cur & ~operand  (clear bits)
    }

    /// <summary>Comparison a System predicate applies (cell CMP threshold). Mirrors native DataCompareOp.
    /// The bitmask compares (HasAllBits/HasAnyBits/LacksBits) are integer-only; on a Float/Double column
    /// they never match.</summary>
    public enum CompareOp
    {
        Always       = 0,
        Equal        = 1,
        NotEqual     = 2,
        Less         = 3,
        LessEqual    = 4,
        Greater      = 5,
        GreaterEqual = 6,
        HasAllBits   = 7, // (cell & threshold) == threshold
        HasAnyBits   = 8, // (cell & threshold) != 0
        LacksBits    = 9, // (cell & threshold) == 0
    }
}
