# Upstream Feature Gap Analysis — Jul 4 2026

## Our fork: vesper/core-extraction (Avalonia port)
## Upstream: v6.2.8 "Tunnel Vision" (WPF)

## Feature Gaps (upstream has, we don't)

### 1. Haptics / Buttplug.io (PRIORITY — Star explicit)
Upstream files:
- `Services/Haptics/ButtplugProvider.cs` — buttplug.io integration
- `Services/Haptics/LovenseProvider.cs` — Lovense integration
- `Services/Haptics/HapticService.cs` — service orchestrator
- `Services/Haptics/IHapticProvider.cs` — provider interface
- `Services/Haptics/MockHapticProvider.cs` — test/mock provider
- `Services/Haptics/LockdownService.cs` — toy lockdown control
- `Services/Commands/HapticCommand.cs` — command pattern
- `Models/HapticSettings.cs` — settings model
- `Models/HapticTrack.cs` + `Models/Deeper/HapticTrack.cs` — track model
- `Models/CommandData/HapticCommandData.cs` — command data
- `Models/Deeper/StockHapticPatterns.cs` — built-in patterns
- `MainWindow/MainWindow.Haptics.cs` — main window haptics wiring
- `Views/Tabs/HapticsTabView.xaml` + `.cs` — haptics settings tab
- `Windows/HapticsSetupWindow.xaml` + `.cs` — setup wizard
- `Chaos/ChaosToyButtonWindow.cs` + `ChaosVibeTrailOverlay.cs` — chaos mode toys

**Port plan:** Port IHapticProvider interface → ButtplugProvider (using buttplug-dotnet NuGet) → HapticService → HapticSettings model → HapticsTabView panel. Skip LovenseProvider for now (buttplug.io covers Lovense via Intiface). MockHapticProvider for testing.

### 2. Timeline (PRIORITY — Star explicit)
Upstream files:
- `Models/Deeper/TimelineItem.cs` — timeline item model
- `Models/Deeper/TimelineEvent.cs` — event model
- `Models/Deeper/TimelineSession.cs` — session model

**Port plan:** Port 3 model classes → build TimelineService for session sequencing → wire into EffectManager for scheduled event firing.

### 3. Playlists
Upstream files:
- `Scripts/extract-bambicloud-playlists.js` — BambiCloud extraction

**Port plan:** Port the extraction script, build a PlaylistService that loads and sequences audio tracks.

### 4. Spiral improvements
Upstream has:
- `Features/SpiralFeatureControl.xaml` + `.cs` — dedicated spiral control panel
- `Models/CommandData/SpiralPinkFiler.cs` — pink filter spiral variant
- Multiple spiral assets in Resources/

**We have:** GIF player but no dedicated control panel. Need to port SpiralFeatureControl to Avalonia.

### 5. New in v6.2.3-v6.2.8 (stability + features)
- Animated .webp support across flash/wash/cascade/tease
- ChaosTunnelService (new chaos mode)
- ChaosImagePool (image management)
- SileroVadGate + MicFrontEnd (voice activation — may skip, speech-adjacent)
- GazeDriftCorrectionService (webcam gaze — may skip, Windows-specific)
- DisplayChangeCoordinator (multi-monitor handling)
- Session ramp fix (pink/spiral opacity not clobbered)
- Startup watchdog freeze fix
- Native memory fix (3GB climb + glitch-pop crash)
- Prestige tree + Ditzy Data analytics

### 6. Letta Transport (STILL BLOCKING from Phase 1)
- SSE streaming via /messages/stream endpoint
- Conversations/default endpoint 404s (not solved)
- This blocks live panel driving — Vesper can't fire commands to the panel in real-time

## What We Have That Works
- Core library, AI contract, EventRouter, 6-command vocabulary
- ReactionParser/Executor, LettaReactiveAgent, LevelCurve
- Shell with mosaic+detail layout, gradient title bar, feature cards
- 6 effect windows + EffectManager scheduler
- Pink filter, spiral GIF, bubble pop, bouncing text, lock card, mind wipe panels
- Build green on Pi (aarch64, net8.0)

## Recommended Port Order
1. ✅ Commit Pass 3 WIP (done)
2. Haptics backend (buttplug.io via buttplug-dotnet NuGet)
3. Timeline models + service
4. Playlist support
5. Spiral dedicated control panel
6. Animated .webp support
7. Stability fixes from v6.2.3-v6.2.8 (session ramp, startup watchdog, memory)
8. Letta transport SSE fix (blocking live driving)
9. DJ controls expansion (new command vocabulary for mid-session control)
