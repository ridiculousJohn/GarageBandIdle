using NUnit.Framework;
using RidiculousGaming.GarageBandIdle;
using RidiculousGaming.GarageBandIdle.UI;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    public class NumberFormatterTests
    {
        [TestCase(0, "0")]
        [TestCase(5, "5")]
        [TestCase(5.5, "5.5")]
        [TestCase(5.25, "5.25")]
        [TestCase(5.10, "5.1")]          // trailing fractional zeros dropped
        [TestCase(5.256, "5.26")]        // rounded to two decimals
        [TestCase(999.99, "999.99")]     // last plain value
        [TestCase(1000, "1.00e3")]       // first scientific value
        [TestCase(1234, "1.23e3")]
        [TestCase(1000000, "1.00e6")]
        [TestCase(-5.5, "-5.5")]
        [TestCase(-1000, "-1.00e3")]
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
