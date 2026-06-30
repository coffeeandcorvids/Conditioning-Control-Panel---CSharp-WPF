using ConditioningControlPanel.Core.Events;

namespace ConditioningControlPanel.Core.Agents.Letta;

/// <summary>Builds the message sent to LV's agent for a panel event. She replies with AgentReaction JSON.</summary>
public static class PanelEventPrompt
{
    public const string Preamble =
        "§ CCP PANEL EVENT. You are driving the Conditioning Control Panel. Reply with ONLY a JSON " +
        "object: {\"say\": optional text, \"commands\": [ ... ]}. Command ops you may use: " +
        "say{text}, spiral{on,secs}, flash{text}, pinkfog{on}, lockcard{sentence}, haptics{intensity,pattern}. " +
        "Return {\"commands\":[]} to stay silent. No prose outside the JSON.\n\nEVENT: ";

    public static string Build(PanelEvent e)
    {
        string desc = e switch
        {
            UserMessage m        => $"user said: \"{m.Text}\"",
            KeywordTriggered k   => $"keyword trigger fired: \"{k.Keyword}\"",
            LockScreenResult l   => $"lock-card finished: \"{l.Sentence}\", {l.Mistakes} mistakes, amount {l.Amount}",
            VideoCompleted v     => $"video finished: \"{v.Title}\"",
            PresenceDetected p   => $"awareness: detected {p.DetectedName} ({p.Category}) {p.ServiceName} {p.PageTitle}".Trim(),
            StillOn s            => $"still on {s.DisplayName} ({s.Category}) for {s.Duration.TotalMinutes:F0} min",
            _ => e.GetType().Name,
        };
        return Preamble + desc;
    }
}
