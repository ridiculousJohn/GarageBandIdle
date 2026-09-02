using System.Globalization;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Idle display formatting, Ctrl C's rule: the character slots never change
    // while a value moves, so a number counting up at frame rate churns in
    // place instead of jumping. Below 1000 two fixed decimals (5.00, 137.80),
    // below 10000 one (1234.6), from 10000 scientific notation with a fixed
    // two-decimal mantissa (1.23e4).
    public static class NumberFormatter
    {
        public static string Format(BigNumber value)
        {
            if (value < BigNumber.Zero)
                return "-" + Format(-value);

            if (value < 1000)
                return value.ToDouble().ToString("0.00", CultureInfo.InvariantCulture);

            if (value < 10000)
                return value.ToDouble().ToString("0.0", CultureInfo.InvariantCulture);

            return value.Mantissa.ToString("0.00", CultureInfo.InvariantCulture) + "e" + value.Exponent;
        }
    }
}
