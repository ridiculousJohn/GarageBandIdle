using NUnit.Framework;
using RidiculousGaming.GarageBandIdle.EditorTools;

namespace RidiculousGaming.GarageBandIdle.Tests
{
    // The qualifier dropdown resolves its definition family from a sibling
    // ModifierTarget, which means walking from the qualifier's serialized path back
    // to the object owning it. That path arithmetic is the only part of the drawer
    // that can be wrong in a way compilation would not catch, and an inspector is
    // not available in a headless run - so it is a pure function and tested here.
    // Getting it wrong fails soft: the dropdown falls back to a plain string field,
    // so a regression would be silent in the editor.
    public class ModifierQualifierIdDrawerTests
    {
        // a GrantModifierEffect held by [SerializeReference], which is how every
        // imported upgrade payload and reward effect is actually stored
        [Test]
        public void SiblingPath_WalksOutOfASerializeReferenceListElement()
        {
            Assert.AreEqual(
                "references.RefIds.Array.data[0].data._target",
                ModifierQualifierIdDrawer.SiblingPath(
                    "references.RefIds.Array.data[0].data._qualifiers.Array.data[2]", "_target"));
        }

        // only the LAST array suffix is the qualifier's own: the managed reference
        // it lives in carries one too, and stripping that instead would walk out of
        // the effect entirely and resolve no target
        [Test]
        public void SiblingPath_StripsOnlyTheQualifiersOwnArraySuffix()
        {
            Assert.AreEqual(
                "references.RefIds.Array.data[3].data._target",
                ModifierQualifierIdDrawer.SiblingPath(
                    "references.RefIds.Array.data[3].data._qualifiers.Array.data[0]", "_target"));
        }

        // a list field directly on the serialized object, and a bare non-list field,
        // both resolve the sibling at the root
        [TestCase("_qualifiers.Array.data[0]")]
        [TestCase("_qualifiers")]
        public void SiblingPath_ResolvesAtTheRootWhenTheOwnerIsTheObject(string path)
        {
            Assert.AreEqual("_target", ModifierQualifierIdDrawer.SiblingPath(path, "_target"));
        }

        [Test]
        public void SiblingPath_OnAnEmptyPathIsJustTheSiblingName()
        {
            Assert.AreEqual("_target", ModifierQualifierIdDrawer.SiblingPath("", "_target"));
        }
    }
}
