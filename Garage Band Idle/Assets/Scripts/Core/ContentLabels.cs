namespace RidiculousGaming.GarageBandIdle
{
    // Addressables labels, one per definition type. The importer stamps these
    // onto content assets; runtime discovery loads by label, so the content set
    // stays open — new assets are picked up with no code or registration changes.
    // Addresses follow "<label>/<asset id>".
    public static class ContentLabels
    {
        public const string Currency = "currency";
        public const string CurrencyGroup = "currency-group";
        public const string Chapter = "chapter";
        public const string Generator = "generator";
        public const string Upgrade = "upgrade";
        public const string Cover = "cover";
        public const string Event = "event";
        public const string Reward = "reward";
        public const string Module = "module";
    }
}
