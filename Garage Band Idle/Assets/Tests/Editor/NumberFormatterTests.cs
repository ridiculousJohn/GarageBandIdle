using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.UI;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class NumberFormatterTests
    {
        [TestCase(0, "0.00")]
        [TestCase(5, "5.00")]
        [TestCase(5.5, "5.50")]          // the slots are fixed, so the zero stays
        [TestCase(5.25, "5.25")]
        [TestCase(5.256, "5.26")]        // rounded to two decimals
        [TestCase(999.99, "999.99")]     // last two-decimal value
        [TestCase(1000, "1000.0")]       // first one-decimal value
        [TestCase(1234.56, "1234.6")]
        [TestCase(9999.9, "9999.9")]     // last plain value
        [TestCase(10000, "1.00e4")]      // first scientific value
        [TestCase(12345, "1.23e4")]
        [TestCase(1000000, "1.00e6")]
        [TestCase(-5.5, "-5.50")]
        [TestCase(-10000, "-1.00e4")]
        public void Format_follows_the_display_rules(double value, string expected)
        {
            Assert.AreEqual(expected, NumberFormatter.Format(value));
        }

        [Test]
        public void Format_handles_values_beyond_double_range()
        {
            var huge = BigNumber.Pow(10, 320) * 1.5;

            Assert.AreEqual("1.50e320", NumberFormatter.Format(huge));
        }
    }
}
