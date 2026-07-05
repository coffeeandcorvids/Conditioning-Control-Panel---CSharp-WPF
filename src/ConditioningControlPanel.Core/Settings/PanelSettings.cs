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
    /// Fade intensity/duration, original-style percentage knob. Current Pi port stores it for parity.
    public int    FlashFadePercent  { get; set; } = 50;
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
    public int  SpiralOpacity  { get; set; } = 35;   // original-style 5-50% fullscreen overlay opacity
    public string? SpiralPath  { get; set; }          // empty/null = upstream Resources/spirals/spiral.gif
    public bool PinkFogEnabled { get; set; } = false;
    public int  PinkFilterOpacity { get; set; } = 95; // 0-180 alpha for overlay tint

    // ── CCP parity effects ────────────────────────────────────────────────────
    public bool BubblePopEnabled { get; set; } = false;
    public int BubblePopIntervalSeconds { get; set; } = 4;
    public int BubblePopFrequencyPerHour { get; set; } = 20;
    public int BubblePopVolume { get; set; } = 70;
    public int BubblePopSpeedBoost { get; set; } = 0;
    public bool BubblePopSolidMode { get; set; } = true;
    public bool BouncingTextEnabled { get; set; } = false;
    public List<string> BouncingTextPhrases { get; set; } = new()
        { "OBEY", "DROP", "GOOD GIRL", "DON'T THINK", "EMPTY", "WATCH" };
    public bool LockCardEnabled { get; set; } = true;
    public int LockCardFrequencyPerHour { get; set; } = 4;
    public int LockCardRepeats { get; set; } = 3;
    public bool LockCardStrict { get; set; } = false;
    public int LockCardDurationSeconds { get; set; } = 8;
    public List<string> LockCardPhrases { get; set; } = new()
        { "GOOD GIRLS DON'T THINK", "WATCH", "OBEY", "DROP DEEPER" };
    public bool MindWipeEnabled { get; set; } = true;
    public int MindWipeFrequencyPerHour { get; set; } = 6;
    public int MindWipeVolume { get; set; } = 50;
    public bool MindWipeLoop { get; set; } = false;
    public string? MindWipeAudioPath { get; set; }

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

    // ── Marquee ────────────────────────────────────────────────────────────────
    /// Customizable scrolling ticker text at the bottom of the panel.
    public string MarqueeMessage { get; set; } = "SHE CAN HEAR YOU NOW  💗  VESPER CONTROL PANEL IS ONLINE  -  LINUX PORT IN PROGRESS";

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
