using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Core.Settings;

/// <summary>
/// Our clean settings model. No Patreon/premium gating, no Windows/DPAPI — just the knobs
/// that matter for the conditioning experience. Persisted as JSON in ~/.config/vesper-ccp/.
/// All features unlocked by construction; no tier checks anywhere in this model.
/// </summary>
public sealed class PanelSettings
{
    // ── Flash ──────────────────────────────────────────────────────────────────
    public bool   FlashEnabled      { get; set; } = true;
    /// Seconds between automatic flashes (1–60). 0 = manual-only.
    public int    FlashFrequency    { get; set; } = 8;
    /// How long each flash stays visible, in milliseconds.
    public int    FlashDurationMs   { get; set; } = 4_000;
    /// How many images to pop per flash event (1–6).
    public int    FlashAmount       { get; set; } = 1;
    /// 0–100 opacity.
    public int    FlashOpacity      { get; set; } = 95;
    /// Relative width of the flash window as a fraction of screen width (0.1–1.0).
    public double FlashSizeFraction { get; set; } = 0.45;
    public bool   FlashAudioEnabled { get; set; } = false;
    /// Folder to scan for flash images. Expanded at runtime (~, env vars).
    public string FlashImagesPath   { get; set; } = "~/ccp-assets/images";

    // ── Subliminals ────────────────────────────────────────────────────────────
    public bool   SubliminalEnabled   { get; set; } = true;
    /// Seconds between subliminal text flashes.
    public int    SubliminalFrequency { get; set; } = 12;
    /// Milliseconds the subliminal text is visible (100–3000).
    public int    SubliminalDurationMs{ get; set; } = 350;
    /// 0–100 opacity of the text.
    public int    SubliminalOpacity   { get; set; } = 85;
    public double SubliminalFontSize  { get; set; } = 64;
    /// Pool of active subliminal phrases.
    public List<string> SubliminalPhrases { get; set; } = new()
    {
        "good girl", "relax deeper", "obey", "mindless and happy",
        "let go", "surrender", "you belong", "empty and compliant",
        "feel it", "deeper", "you want this", "so deep",
    };

    // ── Spiral / Overlays ──────────────────────────────────────────────────────
    public bool SpiralEnabled  { get; set; } = true;
    public int  SpiralSpeedMs  { get; set; } = 33;   // rotation tick in ms (33 ≈ 30fps)
    public bool PinkFogEnabled { get; set; } = false;

    // ── Keywords ───────────────────────────────────────────────────────────────
    /// Words/phrases that trigger a flash+subliminal burst when spoken to LV.
    public List<string> Keywords { get; set; } = new()
        { "spiral", "blank", "obey", "good girl", "deeper", "sleep", "pink" };

    // ── Session ────────────────────────────────────────────────────────────────
    public bool StrictLockEnabled  { get; set; } = false;
    public bool PanicKeyEnabled    { get; set; } = true;
    public string PanicKey         { get; set; } = "Escape";  // cross-platform virtual key name

    // ── Haptics ────────────────────────────────────────────────────────────────
    public bool HapticsEnabled     { get; set; } = false;
    public int  HapticsDefaultIntensity { get; set; } = 50;

    // ── Assets ─────────────────────────────────────────────────────────────────
    public string AssetsRoot { get; set; } = "~/ccp-assets";

    // ── Companion / Vesper ─────────────────────────────────────────────────────
    /// Which Letta agent is LV (the controller).
    public string? LettaAgentId  { get; set; }
    public string? LettaBaseUrl  { get; set; } = "http://localhost:8283";

    // ── Gamification ───────────────────────────────────────────────────────────
    public int  Xp             { get; set; } = 0;
    public int  Level          { get; set; } = 1;
    public int  TotalSessions  { get; set; } = 0;
    public long TotalTimeMs    { get; set; } = 0;

    // ── Persistence ────────────────────────────────────────────────────────────
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "vesper-ccp");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    private static readonly JsonSerializerOptions _json = new()
        { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static PanelSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var text = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<PanelSettings>(text, _json) ?? new();
            }
        }
        catch { /* corrupt/missing — start fresh */ }
        return new PanelSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, _json));
    }

    /// Expand ~ and environment variables in a path string.
    public static string ExpandPath(string path)
    {
        if (path.StartsWith("~/") || path == "~")
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                path.Length > 2 ? path[2..] : "");
        return Environment.ExpandEnvironmentVariables(path);
    }
}
