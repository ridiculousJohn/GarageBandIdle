using System;
using NUnit.Framework;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // A BigNumber is always a real number. Every door into the type is the same
    // constructor, so a NaN or an infinity is refused where it would be born
    // rather than checked by every consumer that might later read it.
    public class BigNumberTests
    {
        [Test]
        public void A_non_finite_literal_is_refused()
        {
            Assert.Throws<ArgumentException>(() => { var _ = new BigNumber(double.NaN); });
            Assert.Throws<ArgumentException>(() => { var _ = new BigNumber(double.PositiveInfinity); });
            Assert.Throws<ArgumentException>(() => { var _ = new BigNumber(double.NegativeInfinity); });
        }

        [Test]
        public void An_implicit_conversion_is_the_same_door()
        {
            Assert.Throws<ArgumentException>(() => { BigNumber _ = double.NaN; });
        }

        [Test]
        public void Arithmetic_that_would_produce_one_throws_where_it_happens()
        {
            // The case that matters in shipped code: no hand-edited file needed,
            // just a divisor that reached zero.
            Assert.Throws<ArgumentException>(() => { var _ = BigNumber.One / BigNumber.Zero; });
            Assert.Throws<ArgumentException>(() => { var _ = BigNumber.Zero / BigNumber.Zero; });
            Assert.Throws<ArgumentException>(() => { var _ = BigNumber.Pow(-8, 0.5); });
        }

        [Test]
        public void A_save_carrying_one_is_refused_at_reconstruction()
        {
            Assert.Throws<ArgumentException>(() => BigNumber.FromMantissaExponent(double.NaN, 0));
        }

        [Test]
        public void Ordinary_values_are_untouched()
        {
            Assert.AreEqual((BigNumber)0, BigNumber.Zero);
            Assert.AreEqual((BigNumber)1, BigNumber.One);
            Assert.AreEqual((BigNumber)6, (BigNumber)2 * 3);
            Assert.AreEqual((BigNumber)(-4), (BigNumber)1 - 5);           // negative is a real number
            Assert.AreEqual(BigNumber.FromMantissaExponent(1.5, 320), BigNumber.FromMantissaExponent(1.5, 320));
        }
    }
}
