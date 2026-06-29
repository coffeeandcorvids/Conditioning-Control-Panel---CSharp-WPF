namespace ConditioningControlPanel.Core.Ai;

/// <summary>
/// Portable AI contract for the Core (mirrors the WPF IAiService, minus IDisposable/WPF coupling).
/// Implementations: VesperAiBackend (Letta/Vesper — THE point of our fork), or upstream OpenRouter/Ollama.
/// </summary>
public interface IAiBackend
{
    bool IsAvailable { get; }
    int DailyRequestsRemaining { get; }

    Task<string> GetReplyAsync(string userInput, bool isUserMessage = false);
    Task<AiReplyResult> GetReplyExAsync(string userInput, bool isUserMessage = false);
    Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "");
    Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration);
    Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null);
    Task<string?> GetLockScreenReactionAsync(string sentence, int mistakes, int amount, string? promptTemplate = null);
    Task<string?> GetVideoDoneReactionAsync(string title, string? promptTemplate = null);
}
