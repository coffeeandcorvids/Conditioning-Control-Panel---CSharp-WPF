namespace ConditioningControlPanel.Core.Models
{
    // Extracted enum shims — upstream defines these inside SessionDefinition.cs and
    // AppSettings.cs, whose full files drag WPF-side dependencies we don't port.
    // Definitions match upstream v6.2.8 exactly so serialized session JSON round-trips.

    public enum SessionSource
    {
        BuiltIn,    // Shipped with the app in Assets/Sessions
        Custom,     // User-created and saved locally
        Imported    // Dropped in via drag & drop
    }

    public enum ContentMode
    {
        BambiSleep,
        SissyHypno
    }
}
