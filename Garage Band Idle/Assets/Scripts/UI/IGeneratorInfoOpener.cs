using RidiculousGaming.GarageBandIdle.Economy;

namespace RidiculousGaming.GarageBandIdle.UI
{
    // The one thing a generator row asks of the host (12.11): open the info
    // screen for a generator, at the scope that declares it. The host is the
    // overlay's owner; the row never sees it.
    public interface IGeneratorInfoOpener
    {
        void OpenGeneratorInfo(GeneratorDefinition generator, ScopeState scope);
    }
}
