using NUnit.Framework;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // CurrencyManager behavior: balances, the lifetime-earned stat backing
    // earned-total gates, change events, and the group-driven prestige reset.
    public class CurrencyManagerTests
    {
        [OneTimeTearDown]
        public void OneTimeTearDown() => TestContent.DestroyAll();

        [Test]
        public void StartingValue_SeedsBalance_ButNotLifetimeEarned()
        {
            var groups = new[] { TestContent.MakeGroup("run", true) };
            var currencies = new[] { TestContent.MakeCurrency("cash", "run", startingValue: 25) };
            var manager = new CurrencyManager(groups, currencies);

            Assert.AreEqual(25.0, manager.Get("cash").ToDouble(), 1e-9);
            Assert.AreEqual(0.0, manager.GetLifetimeEarned("cash").ToDouble(), 1e-9);
        }

        [Test]
        public void Add_AccruesLifetimeEarned_AndSpendingNeverLowersIt()
        {
            var manager = TestContent.MakeEconomy();

            manager.Add("cash", 100);
            manager.Add("cash", -40); // a spend
            manager.Add("cash", 50);

            Assert.AreEqual(110.0, manager.Get("cash").ToDouble(), 1e-9);
            Assert.AreEqual(150.0, manager.GetLifetimeEarned("cash").ToDouble(), 1e-9);
        }

        [Test]
        public void BalanceChanged_FiresWithIdAndNewBalance()
        {
            var manager = TestContent.MakeEconomy();
            string reportedId = null;
            var reportedBalance = BigNumber.Zero;
            manager.BalanceChanged += (id, balance) => { reportedId = id; reportedBalance = balance; };

            manager.Add("cash", 42);

            Assert.AreEqual("cash", reportedId);
            Assert.AreEqual(42.0, reportedBalance.ToDouble(), 1e-9);
        }

        [Test]
        public void AlbumReleaseReset_ResetsRunGroups_KeepsPermanentGroups()
        {
            var groups = new[] { TestContent.MakeGroup("run", true), TestContent.MakeGroup("permanent", false) };
            var currencies = new[]
            {
                TestContent.MakeCurrency("cash", "run", startingValue: 5),
                TestContent.MakeCurrency("records", "permanent"),
            };
            var manager = new CurrencyManager(groups, currencies);
            manager.Add("cash", 1000);
            manager.Add("records", 7);

            manager.ResetCurrenciesOnAlbumRelease();

            Assert.AreEqual(5.0, manager.Get("cash").ToDouble(), 1e-9, "run-scoped currency returns to its starting value");
            Assert.AreEqual(7.0, manager.Get("records").ToDouble(), 1e-9, "permanent currency is untouched");
        }

        [Test]
        public void ValidateReference_TrueForKnownId()
        {
            var manager = TestContent.MakeEconomy();

            Assert.IsTrue(manager.ValidateReference("cash", "test"));
        }
    }
}
