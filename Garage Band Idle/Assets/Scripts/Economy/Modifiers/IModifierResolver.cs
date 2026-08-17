namespace RidiculousGaming.GarageBandIdle.Economy
{
    // The READ half of the modifier store, alone: everything modifying one
    // number, composed. Split from ModifierSystem because the two halves have
    // different reach in a scope tree (design doc section 12, rules 11-12) -
    // a GRANT lands in the store of the scope whose fact it is, while a
    // composition folds every store from its own scope outward. A contributor
    // composing its lines holds this and cannot write, so "reads go outward"
    // is a property of the type it was handed rather than of its discipline.
    //
    // ModifierSystem implements it as one store; ScopeChain implements it as
    // the fold over every store in scope.
    public interface IModifierResolver
    {
        ModifierComposition For(in ModifierSubject subject);
    }
}
