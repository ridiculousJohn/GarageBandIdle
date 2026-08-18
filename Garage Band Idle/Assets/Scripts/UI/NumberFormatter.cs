using System.Globalization;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Idle display formatting: below 1000 the plain number to at most two
    // decimals with trailing fractional zeros dropped (5, 5.5, 999.99); 1000 and
    // up scientific notation with a fixed two-decimal mantissa (1.00e3).
    public static class NumberFormatter
    {
        public static string Format(BigNumber value)
        {
            if (value < BigNumber.Zero)
                return "-" + Format(-value);

            if (value < 1000)
                return value.ToDouble().ToString("0.##", CultureInfo.InvariantCulture);

            return value.Mantissa.ToString("0.00", CultureInfo.InvariantCulture) + "e" + value.Exponent;
        }
    }
}
