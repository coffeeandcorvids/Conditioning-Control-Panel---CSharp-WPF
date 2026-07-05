namespace ConditioningControlPanel.Core.Commands;

/// <summary>A single action the Vesper agent tells the panel to perform. First vocabulary (Jun 29).</summary>
public abstract record PanelCommand;

public sealed record Say(string Text) : PanelCommand;
/// <summary>Spiral overlay. Opacity 5–50 (%); Asset picks a gif from &lt;assets&gt;/spirals/ by name — both live-switchable mid-scene (DJ surface).</summary>
public sealed record Spiral(bool On, int? Seconds = null, int? Opacity = null, string? Asset = null) : PanelCommand;
public sealed record Flash(string Text) : PanelCommand;
public sealed record PinkFog(bool On) : PanelCommand;
public sealed record LockCard(string? Sentence = null) : PanelCommand;
public sealed record Haptics(double? Intensity = null, string? Pattern = null) : PanelCommand;

/// <summary>DJ control of the native playlist engine. Do: next|prev|shuffle|noshuffle|load|jump. Arg: playlist name (load) or title/path query (jump).</summary>
public sealed record PlaylistOp(string Do, string? Arg = null) : PanelCommand;
