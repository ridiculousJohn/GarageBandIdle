namespace RidiculousGaming.GarageBandIdle
{
    // Chain walks that answer with a typed scope. Extensions rather than
    // properties on ScopeState: the base class has no business naming the
    // classes derived from it.
    public static class ScopeStateExtensions
    {
        // The tree's root. Build always makes the top node a RootScopeState, so
        // the cast is total - a failure here is a build bug, not bad data.
        public static RootScopeState Root(this ScopeState state)
        {
            var node = state;
            while (node.Parent != null)
                node = node.Parent;
            return (RootScopeState)node;
        }

        // The chapter a scope resolves on: the last node before the root. Null
        // when called on the root itself, which is off any chapter's chain.
        public static ChapterScopeState Chapter(this ScopeState state)
        {
            for (var node = state; node != null; node = node.Parent)
                if (node.Parent != null && node.Parent.Parent == null)
                    return (ChapterScopeState)node;
            return null;
        }
    }
}
