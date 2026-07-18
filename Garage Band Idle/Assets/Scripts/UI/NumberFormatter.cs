using System.Globalization;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // Idle display formatting: plain numbers with a fixed two-digit fraction below
    // 10000, scientific notation (1.23e45) above.
    public static class NumberFormatter
    {
        // adds the currency's symbol prefix
        public static string Format(BigNumber value, CurrencyDefinition definition)
            => definition.Symbol + Format(value);

        // every non-scientific value shows a fixed two-digit fraction for now; the
        // per-currency decimals hint (CurrencyDefinition.MaxDecimals) comes back
        // when a currency actually needs different precision
        public static string Format(BigNumber value)
        {
            if (value < BigNumber.Zero)
                return "-" + Format(-value);

            // below 10000 (exponent < 4) the value reads fine in full; fixed digits
            // rather than optional ones so a ticking number doesn't jitter in width
            if (value.Exponent < 4)
                return value.ToDouble().ToString("0.00", CultureInfo.InvariantCulture);

            return value.Mantissa.ToString("0.00", CultureInfo.InvariantCulture) + "e" + value.Exponent;
        }
    }
}
