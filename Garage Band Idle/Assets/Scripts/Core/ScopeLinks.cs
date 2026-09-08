using System;
using System.Collections.Generic;

namespace RidiculousGaming.GarageBandIdle
{
    // What a static reference means in ONE tree, resolved when that tree is
    // built (design doc 12.14.8). A reference already names exactly one node
    // the moment construction finishes, so re-deriving it on every execution
    // was work the content had already decided - and a search where the rule
    // allows none. Nothing is written into an asset: the links live on the
    // runtime nodes, so two games built from the same assets hold their own.
    public static class ScopeLinker
    {
        // The one post-construction step Build takes. Scope references first;
        // changeset 1 compiles the gather after them, over a tree whose links
        // already exist.
        public static void Link(ScopeState root, IReadOnlyDictionary<ScopeDefinition, ScopeState> nodes)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (nodes == null)
                throw new ArgumentNullException(nameof(nodes));
            Visit(root);

            void Visit(ScopeState node)
            {
                var ctx = new LinkContext(nodes, node);

                // Every action list this scope declares, named once by the
                // definition (ScopeDefinition.ActionLists) - the same
                // enumeration the validator walks. Lists are not nested, so
                // this is one pass with no recursion of its own: an ExecuteRung
                // NAMES a rung, it does not contain one.
                foreach (var list in node.Definition.ActionLists())
                    foreach (var action in list.Actions)
                        action?.Link(ctx);

                // A chapter additionally links its screen (12.11): a section
                // and a module each evaluate at an authored scope, which is the
                // chapter or one of its descendants, and the reach rule is the
                // one every enclosed reference takes.
                if (node.Definition is ChapterDefinition chapter)
                {
                    foreach (var section in chapter.sections)
                    {
                        if (section == null)
                            continue;
                        ctx.Store(section, ctx.ResolveEnclosed(section.scope, "a section's evaluation scope"));
                        foreach (var module in section.modules)
                            if (module != null)
                                ctx.Store(module, ctx.ResolveEnclosed(module.scope, "a module's evaluation scope"));
                    }
                }

                foreach (var child in node.Children)
                    Visit(child);
            }
        }

        // "Does top enclose node" - self included - over whatever answers "how
        // do I reach a parent". The ONE implementation of the reach rule 12.12
        // states: the validator asks it of definitions against its parent map,
        // the link pass asks it of nodes against ScopeState.Parent, and a
        // second hand-written chain walk would be the copy that drifts.
        public static bool Encloses<T>(T top, T node, Func<T, T> parentOf) where T : class
        {
            if (top == null || node == null)
                return false;
            for (var current = node; current != null; current = parentOf(current))
                if (current == top)
                    return true;
            return false;
        }
    }

    // What a Link override sees: the node the holder executes or is evaluated
    // at, the resolver for the tree being built, and the store the link lands
    // in. Kinds only resolve, check, and store - the pass positions the
    // context, exactly as ValidationContext is positioned by the validator.
    public sealed class LinkContext
    {
        // The node this holder acts at: where its links are stored and the
        // scope every reach rule is measured from.
        public ScopeState ActingScope { get; }

        private readonly IReadOnlyDictionary<ScopeDefinition, ScopeState> nodes;

        internal LinkContext(IReadOnlyDictionary<ScopeDefinition, ScopeState> nodes, ScopeState actingScope)
        {
            this.nodes = nodes;
            ActingScope = actingScope;
        }

        // A reference to the acting scope or a scope it encloses (12.12), as
        // the two resets, ExecuteRung, and the screen's layout scopes all take
        // it. Failures are content faults and throw in every build (12.14.7):
        // the validation pass reports them as findings for a person, and this
        // is what stands when that pass has been skipped.
        public ScopeState ResolveEnclosed(ScopeDefinition scope, string use)
        {
            if (scope == null)
                throw new InvalidOperationException(
                    $"{use} at scope '{ActingScope.ScopeId}' names no scope (12.12).");
            if (!nodes.TryGetValue(scope, out var target))
                throw new InvalidOperationException(
                    $"{use} at scope '{ActingScope.ScopeId}' names '{scope.Id}', which is not a scope in this tree (12.12).");
            if (!ScopeLinker.Encloses(ActingScope, target, node => node.Parent))
                throw new InvalidOperationException(
                    $"{use} at scope '{ActingScope.ScopeId}' names '{scope.Id}', which is neither that scope nor one it encloses (12.12).");
            return target;
        }

        // Files the resolved node under the object that holds the reference.
        // The read is holder-keyed at this same node, so a site that stores
        // nothing is a site whose read throws.
        public void Store(object holder, ScopeState node) => ActingScope.StoreLink(holder, node);
    }
}
