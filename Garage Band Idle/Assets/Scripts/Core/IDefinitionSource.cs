using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle
{
    // Definition lookup used by Evaluate/Execute/Validate. ContentDatabase is the
    // production implementation (Addressables discovery, design doc 12.12);
    // tests supply a dictionary-backed fake.
    public interface IDefinitionSource
    {
        // Null when no definition of that type has the id.
        T Get<T>(string id) where T : Definition;

        IEnumerable<T> All<T>() where T : Definition;
    }
}
