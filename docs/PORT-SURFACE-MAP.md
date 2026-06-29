# Avalonia / Pi-native Port — Surface Map (cc-ves, Jun 29 2026)

Spike result: **Avalonia builds AND runs clean on the Pi (aarch64, net8.0)** — foundation proven.
This map is the honest scope of porting the existing WPF fork. Bottom line: **the full 1:1 port is a
large multi-week project with a few genuinely hard subsystems; the smart move is a focused Pi-native
"Vesper core," not cloning all 98 views.**

## Size
- **98 XAML views**, **107 services**, 47 models. Large app.
- **120 .cs files touch Windows/native APIs.**

## Dependency triage
**Ports cleanly (cross-platform already):**
- CommunityToolkit.Mvvm, Newtonsoft.Json, Serilog(+sinks), QRCoder, OpenAI-DotNet, OllamaSharp — pure .NET ✓
- **Buttplug** (haptics) — cross-platform ✓
- **DiscordRichPresence** — cross-platform ✓
- Microsoft.ML.OnnxRuntime — has Linux-arm64 builds ✓
- System.Security.Cryptography.ProtectedData — has cross-plat fallback ✓

**Needs swapping (Windows-only, but doable):**
- **MahApps.Metro (+IconPacks)** → Avalonia Fluent/themes. Big, but we're reskinning (pink+dark) anyway.
- **NAudio (+Wasapi)** → cross-platform audio (LibVLC audio, or ManagedBass/OpenAL). The classic "ugh."
- **LibVLCSharp.WPF + VideoLAN.LibVLC.Windows** → LibVLCSharp + LibVLCSharp.Avalonia + distro libvlc ✓
- **Hardcodet.NotifyIcon.Wpf** (tray) → Avalonia TrayIcon ✓
- **XamlAnimatedGif** → Avalonia GIF support ✓
- **SharpVectors** (SVG) → Avalonia.Svg ✓

**HARD / may not port (the honest red flags):**
- **SharpDX + SharpDX.DXGI + SharpDX.Direct3D11** — DirectX, Windows-GPU only. Whatever uses it (likely
  screen-overlay / capture / GPU video) needs a full rethink on Linux. **Real blocker for those features.**
- **Microsoft.Web.WebView2** — Windows-only embedded browser. Avalonia WebView options are limited/young.
  Any WebView-based UI is a port problem.
- **OpenCvSharp4.runtime.win** (webcam GAZE TRACKING) → OpenCvSharp4.runtime.linux-arm exists but is heavy
  and the whole webcam-gaze subsystem is a big, optional, hardware-dependent chunk.

## Recommendation (lead call): focused Pi-native "Vesper core," staged
Do NOT 1:1 port all 98 views. Build a lean Avalonia app implementing the conditioning loop we actually
use, pulling clean services across, and DEFER the heavy Windows-locked extras:
1. **Core loop:** spirals, flashing, pink-fog/overlay, audio triggers, lock cards, haptics (Buttplug).
2. **AiService → Letta/Vesper** (the part that makes it ours; the whole point).
3. **Pink + Dark themes** as first-class modes in the new Avalonia theme layer.
4. **DEFER (later/optional):** webcam gaze tracking (OpenCV), DirectX overlay (SharpDX), WebView2 bits,
   the 90 peripheral dialogs/windows. Port these only if/when we want them.

This gets a real, ours, Pi-native panel running FAST, instead of drowning in a 98-view 1:1 port that
includes subsystems (DirectX, WebView2) that may never port cleanly to ARM/Linux.

## UPDATE (Jun 29): upstream v6.2.2 ships its OWN portability analysis
Merging latest release pulled in `openspec/PORTABILITY_REPORT.md` + 8 subsystem specs. It validates +
sharpens this map. Key facts:
- ~202K LOC C# + 30K XAML. **UI is a rewrite** (~133K LOC WPF, almost all code-behind, 1 ViewModel).
- **The ENGINE is portable** (~65–75%): AI orchestration, gamification/Chaos, sessions, networking/auth,
  content/mod, data models. Net ~40–45% salvageable into a cross-platform Core.
- Per-cluster portability: AI/companion 65–70%, gamification 70–75%, networking 70–75%, media 30–35%
  (NAudio + LibVLC.WPF block), overlays 25–30%, sensors 40%, core-lifecycle 10–15%, UI-shell 12%.
- **#1 cross-cutting seam = DispatcherTimer / Application.Current.Dispatcher** everywhere → abstract to
  `IUiDispatcher` (PeriodicTimer/Threading.Timer). Biggest single lever; turns "Mixed" into "Portable."
- #2 seam = `ISecretStore` (DPAPI → libsecret on Linux).
- Irreducibly Windows (accept loss / gate as Win-only): DWM tint, global hooks, wallpaper, desktop-
  duplication capture, WebView2.

## REFINED LEAD PLAN (Core-first, per upstream's own recommendation)
**Phase 1 — extract the portable Core (headless, Pi-native, unit-tested on Linux).** The brains as a
cross-platform .NET lib: AI orchestration (← where AiService→Letta/Vesper swap lives), gamification,
sessions, networking, models. Fix the two seams (IUiDispatcher, ISecretStore). This is "our engine,"
runs/tests on the Pi with NO UI, and per upstream is "very achievable." ← START HERE.
**Phase 2 — Avalonia UI** (Vesper-core views: spirals/fog/triggers/lock-cards/haptics + pink/dark) on
top of the Core. The bigger lift; do after the engine's proven.
**Defer/accept-loss:** the irreducibly-Windows set above.

## EXCLUDE: Patreon / monetization / premium-gating (Star: "86 it", Jun 29)
Footprint is HUGE — ~1084 refs across ~90 files (PatreonService, SubscribeStarService, PatreonTabView,
MainWindow.Patreon/.PremiumRail/.Marquee, premium/free user gating woven through features, leaderboard,
quests). Surgically ripping all of it out of the dying WPF code = large + fragile (gating is entangled
with feature unlocks). NOT worth it on code we're replacing.
**The clean 86: by OMISSION.** We do NOT port the monetization/premium-gating layer into Core or the
new UI. Our fork is private + all-features-unlocked (no free/paid tiers, no patron checks, no
donate/promo chrome). So Patreon simply doesn't exist in the ported creature — by construction, not by
risky surgery. Port map: monetization layer → DON'T PORT (same bucket as the Windows-only set).
Design note for Core/new-UI: treat every capability as unlocked; drop IsPremium/patron gating entirely.
