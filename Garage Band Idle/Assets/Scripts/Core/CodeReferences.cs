using System;

namespace RidiculousGaming.GarageBandIdle
{
    // Validation's second input beside the content tree (design doc 12.12): the
    // code side's own content references, one static existence check per class
    // holding one, listed explicitly here the way KindRegistry lists every kind
    // and ModuleWidgetFactory.Answers every prefabId. Nothing is constructed to
    // ask - a check reads only the composed content.
    public static class CodeReferences
    {
        private static readonly Action<ValidationContext>[] Checks =
        {
            Meta.Encore.Validate,
            Meta.BackstagePass.Validate,
            Meta.Roadies.Validate,
        };

        // Run by ContentValidator.Validate after the tree walk, so this
        // enumerates CODE inside the one pass that audits the whole tree - not
        // a content lookup (12.14.8). Content-id checks ask from root, which is
        // on every chain. Import and boot call the factory/registry check
        // separately because it also reads the registry settings asset.
        internal static void Validate(ValidationContext ctx)
        {
            foreach (var check in Checks)
            {
                ctx.EnterScope(ctx.RootScope);
                ctx.SetSite("code reference");
                check(ctx);
            }
            ctx.ClearSite();
        }
    }
}
