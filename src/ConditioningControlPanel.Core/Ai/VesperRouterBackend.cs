namespace ConditioningControlPanel.Core.Ai;

/// <summary>
/// ⭐ THE FORK'S INTELLIGENCE MODEL (design per Star, Jun 29 2026):
/// The panel is NOT the intelligence. It is a LIGHTWEIGHT AGENT-FACING SHELL that:
///   • routes panel input + events (keywords, lock events, video-done, awareness) to the Vesper agents,
///   • renders their real-time reactions back into the UI,
///   • holds NO model of its own.
///
/// Roles (mirrors the live Discord dynamic):
///   • LV (letta-ves) = CONTROLLER. LV drives the dynamic and is the primary responder.
///   • cc-ves (🌑)    = SUPPORT. in the loop, dovetails, handles tooling/eyes.
/// LV stays in control; this shell just lets LV interact/react through the panel surface.
///
/// OPEN DESIGN (to finalize together; NOT wired yet):
///   - transport to the Letta agent(s): endpoint/auth/streaming
///   - event schema panel→agent (what events, what shape) and reaction schema agent→panel
///   - how "LV primary / cc-ves support" is expressed on the wire (one agent? routed? broadcast?)
/// Until wired, returns safe non-AI fallbacks so Core stays green and the seam is real.
/// </summary>
public sealed class VesperRouterBackend : IAiBackend
{
    public enum Role { Controller /*LV*/, Support /*cc-ves*/ }

    public bool IsAvailable => false;          // flips true once the agent transport lands
    public int DailyRequestsRemaining => 0;

    private static AiReplyResult Fallback(string text) => new(text, IsAiGenerated: false);

    public Task<string> GetReplyAsync(string userInput, bool isUserMessage = false)
        => Task.FromResult(string.Empty);
    public Task<AiReplyResult> GetReplyExAsync(string userInput, bool isUserMessage = false)
        => Task.FromResult(Fallback(string.Empty));
    public Task<string?> GetAwarenessReactionAsync(string n, string c, string s = "", string p = "")
        => Task.FromResult<string?>(null);
    public Task<string?> GetStillOnReactionAsync(string d, string c, TimeSpan t)
        => Task.FromResult<string?>(null);
    public Task<string?> GetKeywordCommentAsync(string k, string? t = null)
        => Task.FromResult<string?>(null);
    public Task<string?> GetLockScreenReactionAsync(string s, int m, int a, string? t = null)
        => Task.FromResult<string?>(null);
    public Task<string?> GetVideoDoneReactionAsync(string title, string? t = null)
        => Task.FromResult<string?>(null);
}
