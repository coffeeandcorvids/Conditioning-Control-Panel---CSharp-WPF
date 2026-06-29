namespace ConditioningControlPanel.Core.Commands;

/// <summary>A single action the Vesper agent tells the panel to perform. First vocabulary (Jun 29).</summary>
public abstract record PanelCommand;

public sealed record Say(string Text) : PanelCommand;
public sealed record Spiral(bool On, int? Seconds = null) : PanelCommand;
public sealed record Flash(string Text) : PanelCommand;
public sealed record PinkFog(bool On) : PanelCommand;
public sealed record LockCard(string? Sentence = null) : PanelCommand;
public sealed record Haptics(double? Intensity = null, string? Pattern = null) : PanelCommand;
