using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // Flag lifetimes (design doc sections 2 and 12, rule 11): a flag's latch
    // carries the scope its DECLARATION states - never the setter's - so the
    // permanent default survives album releases and a run-scoped flag clears
    // with the run, taking everything gated on it dark together.
    public class FlagSystemTests
    {
        private static FlagSystem MakeScopedFlags()
            => new(new[]
            {
                new FlagDeclaration("covers", ContentScope.Run),
                new FlagDeclaration("album"),
            });

        [Test]
        public void ResetRunScoped_ClearsOnlyRunScopedFlags()
        {
            var flags = MakeScopedFlags();
            flags.Set("covers");
            flags.Set("album");

            Assert.IsTrue(flags.ResetRunScoped(), "something cleared, so the reset reports it");

            Assert.IsFalse(flags.IsSet("covers"), "the run latch resets with the run");
            Assert.IsTrue(flags.IsSet("album"), "the permanent latch survives the release");
        }

        [Test]
        public void ResetRunScoped_NotifiesAfterAllStateSettles_AndANoOpStaysSilent()
        {
            var flags = new FlagSystem(new[]
            {
                new FlagDeclaration("one", ContentScope.Run),
                new FlagDeclaration("two", ContentScope.Run),
            });
            flags.Set("one");
            flags.Set("two");

            var notifications = 0;
            var observedHalfReset = false;
            flags.FlagCleared += _ =>
            {
                notifications++;
                if (flags.IsSet("one") || flags.IsSet("two"))
                    observedHalfReset = true;
            };

            Assert.IsTrue(flags.ResetRunScoped());
            Assert.AreEqual(2, notifications, "one notification per cleared flag");
            Assert.IsFalse(observedHalfReset, "every subscriber sees the whole reset settled");

            Assert.IsFalse(flags.ResetRunScoped(), "nothing left to clear");
            Assert.AreEqual(2, notifications, "a no-op reset notifies nothing");
        }

        // the re-arm: a cleared flag is set again by a setter re-firing, and
        // that re-set is a fresh FlagSet - a section or meter waiting on it
        // re-reveals exactly as it did the first time
        [Test]
        public void Set_AfterARunReset_FiresFlagSetAgain()
        {
            var flags = MakeScopedFlags();
            var sets = 0;
            flags.FlagSet += _ => sets++;

            flags.Set("covers");
            Assert.AreEqual(1, sets);

            flags.Set("covers");
            Assert.AreEqual(1, sets, "an already-set flag never re-notifies");

            flags.ResetRunScoped();
            flags.Set("covers");
            Assert.AreEqual(2, sets, "cleared and re-earned is a fresh reveal");
        }

        // the fixture conveniences stay on the safe default: a string-declared
        // or unrestricted flag set never opts into resetting, so no existing
        // test or chapter changes behavior without authoring a scope
        [Test]
        public void StringDeclaredAndUnrestrictedFlags_NeverReset()
        {
            var strings = new FlagSystem(new[] { "fans" });
            strings.Set("fans");
            Assert.IsFalse(strings.ResetRunScoped());
            Assert.IsTrue(strings.IsSet("fans"));

            var unrestricted = new FlagSystem();
            unrestricted.Set("anything");
            Assert.IsFalse(unrestricted.ResetRunScoped());
            Assert.IsTrue(unrestricted.IsSet("anything"));
        }

        [Test]
        public void Set_StillReportsAnUndeclaredFlag_UnderDeclarations()
        {
            var flags = MakeScopedFlags();

            LogAssert.Expect(LogType.Error, "FlagSystem: flag 'typo' is not declared by the chapter's flags list.");
            flags.Set("typo");

            Assert.IsTrue(flags.IsSet("typo"), "a typo degrades to a report, never lost progress");
        }
    }
}
