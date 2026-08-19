using System.Linq;
using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Lookup semantics of the production IDefinitionSource. Addressables
    // discovery itself is exercised in build step 8, when the importer creates
    // the first labeled assets; until then LoadFromAddressables has nothing to
    // load against.
    public class ContentDatabaseTests
    {
        private static ContentDatabase Db(params Definition[] definitions)
        {
            var db = new ContentDatabase();
            db.AddRange(definitions);
            return db;
        }

        [Test]
        public void Get_ReturnsTypedDefinition()
        {
            var cash = TestTree.MakeDefinition<CurrencyDefinition>("cash");
            var db = Db(cash, TestTree.MakeDefinition<ModifierDefinition>("boost"));

            Assert.AreSame(cash, db.Get<CurrencyDefinition>("cash"));
        }

        [Test]
        public void Get_WrongType_ReturnsNull()
        {
            var db = Db(TestTree.MakeDefinition<CurrencyDefinition>("cash"));

            Assert.IsNull(db.Get<ModifierDefinition>("cash"));
        }

        [Test]
        public void Get_MissingOrNullId_ReturnsNull()
        {
            var db = Db(TestTree.MakeDefinition<CurrencyDefinition>("cash"));

            Assert.IsNull(db.Get<CurrencyDefinition>("ghost"));
            Assert.IsNull(db.Get<CurrencyDefinition>(null));
        }

        [Test]
        public void Get_BaseType_FindsSubtype()
        {
            var cash = TestTree.MakeDefinition<CurrencyDefinition>("cash");
            var db = Db(cash);

            Assert.AreSame(cash, db.Get<Definition>("cash"));
        }

        [Test]
        public void All_FiltersByType()
        {
            var db = Db(
                TestTree.MakeDefinition<CurrencyDefinition>("cash"),
                TestTree.MakeDefinition<CurrencyDefinition>("fans"),
                TestTree.MakeDefinition<ModifierDefinition>("boost"));

            Assert.AreEqual(2, db.All<CurrencyDefinition>().Count());
            Assert.AreEqual(3, db.All<Definition>().Count());
        }

        // Duplicate ids index leniently - the validation pass owns the refusal.
        [Test]
        public void DuplicateIds_AllVisible_GetReturnsFirstTypedMatch()
        {
            var first = TestTree.MakeDefinition<CurrencyDefinition>("cash");
            var second = TestTree.MakeDefinition<ModifierDefinition>("cash");
            var db = Db(first, second);

            Assert.AreSame(first, db.Get<Definition>("cash"));
            Assert.AreSame(second, db.Get<ModifierDefinition>("cash"));
            Assert.AreEqual(2, db.All<Definition>().Count(d => d.Id == "cash"));
        }

        [Test]
        public void Validate_RunsThePass_DuplicateIdRefused()
        {
            var db = Db(
                TestTree.MakeScope("root"),
                TestTree.MakeDefinition<CurrencyDefinition>("cash"),
                TestTree.MakeDefinition<CurrencyDefinition>("cash"));

            var report = db.Validate();

            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.OfCheck(ValidationCheck.DuplicateId).Any());
        }
    }
}
