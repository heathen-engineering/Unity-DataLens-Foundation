namespace Heathen.DataLens
{
    /// <summary>Arithmetic a System applies to a target column cell (cur OP operand). Mirrors native DataSystemOp.</summary>
    public enum SystemOp
    {
        Set = 0,
        Add = 1,
        Sub = 2,
        Mul = 3,
        Min = 4,
        Max = 5,
    }

    /// <summary>Comparison a System predicate applies (cell CMP threshold). Mirrors native DataCompareOp.</summary>
    public enum CompareOp
    {
        Always       = 0,
        Equal        = 1,
        NotEqual     = 2,
        Less         = 3,
        LessEqual    = 4,
        Greater      = 5,
        GreaterEqual = 6,
    }
}
