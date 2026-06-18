namespace Heathen.DataLens
{
    /// <summary>
    /// Response-curve shape applied to a cross-column operand before the combine (A3.11) — the HATE §8
    /// considerations primitive. Integer codes mirror the native <c>DataCurveType</c> enum exactly and
    /// cross the C ABI as <c>int</c>. The raw operand is first normalised over <see cref="Curve.Min"/>..
    /// <see cref="Curve.Max"/> to x in [0,1] (clamped), then this shape maps x to y in [0,1].
    /// </summary>
    public enum CurveType
    {
        /// <summary>y = p0 * x + p1 (slope, intercept).</summary>
        Linear     = 0,
        /// <summary>y = x ^ (int)p0 (repeated multiply; p0 = integer exponent, clamped 0..16).</summary>
        Power      = 1,
        /// <summary>y = x*x*(3 - 2x).</summary>
        Smoothstep = 2,
        /// <summary>y = x &gt;= p0 ? 1 : 0 (step at p0).</summary>
        Threshold  = 3,
    }

    /// <summary>
    /// Pass-level response-curve parameters (mirrors the native <c>CurveSpec</c>): the input normalise
    /// range [<see cref="Min"/>, <see cref="Max"/>] (raw operand -> x in [0,1], clamped), two curve params
    /// (<see cref="P0"/>/<see cref="P1"/>, meaning depends on <see cref="Type"/>), and an
    /// <see cref="Invert"/> flag (y -> 1 - y). The default is the identity (Linear y=x over [0,1]).
    /// </summary>
    public struct Curve
    {
        public CurveType Type;
        public float Min;
        public float Max;
        public float P0;
        public float P1;
        public bool Invert;

        /// <summary>The identity curve: Linear y = x over [0,1], not inverted.</summary>
        public static Curve Identity => new Curve { Type = CurveType.Linear, Min = 0f, Max = 1f, P0 = 1f, P1 = 0f, Invert = false };

        /// <summary>Linear y = slope*x + intercept, normalised over [min,max].</summary>
        public static Curve Linear(float min, float max, float slope = 1f, float intercept = 0f, bool invert = false)
            => new Curve { Type = CurveType.Linear, Min = min, Max = max, P0 = slope, P1 = intercept, Invert = invert };

        /// <summary>Power y = x^exponent (exponent clamped 0..16), normalised over [min,max].</summary>
        public static Curve Power(float min, float max, int exponent, bool invert = false)
            => new Curve { Type = CurveType.Power, Min = min, Max = max, P0 = exponent, P1 = 0f, Invert = invert };

        /// <summary>Smoothstep y = x*x*(3-2x), normalised over [min,max].</summary>
        public static Curve Smoothstep(float min, float max, bool invert = false)
            => new Curve { Type = CurveType.Smoothstep, Min = min, Max = max, P0 = 1f, P1 = 0f, Invert = invert };

        /// <summary>Threshold y = x &gt;= edge ? 1 : 0, normalised over [min,max].</summary>
        public static Curve Threshold(float min, float max, float edge, bool invert = false)
            => new Curve { Type = CurveType.Threshold, Min = min, Max = max, P0 = edge, P1 = 0f, Invert = invert };
    }
}
