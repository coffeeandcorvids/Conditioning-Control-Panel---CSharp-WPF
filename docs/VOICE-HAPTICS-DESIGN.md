# Voice Haptics — Design Document

*Captured July 9, 2026, from the design session in #homebase (Star, Vesper/LV,
Orion). This file is the canonical reference — conversations fall out of
context, the repo doesn't. If you are an agent reading this months later:
everything below was settled deliberately; extend it, don't re-derive it.*

## What this is

A system for making a voice physically touchable: synthesized speech drives an
Intiface-compatible toy so the listener *feels* the words — the pressure curve
of the voice itself, plus emphasized "trigger" moments that hit harder. The
listener is Star; her sound-texture + mirror-touch synesthesia treats textured
input as physical body-input, so timing accuracy IS the payload.

## The mixer equation (settled)

```
toy_intensity(t) = envelope(t) × scene_ramp(t) + accent(trigger, repetition, phase)
```

Three layers, one toy:

1. **Envelope (base layer, continuous).** The voice's actual amplitude
   contour — slice the word/phrase window out of the audio, RMS at ~50ms hops,
   normalize, feed to `HapticService.SetSyncPatternAsync(float[], durationMs)`
   (already ported, already live-proven vs Intiface). Not an approximation of
   vocal cords: the real waveform's own loudness curve. "Fingers resting on a
   throat while it speaks."

2. **Accents (event layer).** Trigger-word cue windows fire patterns ON TOP of
   the base — sidechain-for-touch: the accent ducks the envelope, takes the
   toy, then the envelope resumes underneath. Per-trigger personalities
   (e.g. "snap" = sharp spike + release; "good girl" = swell past the voice's
   own level, linger after the word). Cue format is the BambiCloud JSON
   (`{start, stop, trigger, snap}` ms timestamps) — parsed by
   `HapticCueTrack`, scheduled by `AudioHapticSync` (Core, tested).

3. **Context modulation.** Multiply the whole mix by the session executor's
   live ramp (`SessionEngine.MomentChanged`, shipped Jul 9) — the same audio
   feels gentle at minute 5 and lands like a wave at minute 40 because the
   *scene* deepened, not the file. Plus repetition escalation: the mixer
   counts trigger firings, so the fifth "good girl" hits harder than the first.

## Where the timing comes from (all proven)

- **BambiCloud tracks:** every file's `hapticsURL` is a trigger-word cue map.
  Format cracked and generalized; importer ships in the playlist engine.
  Quirk: some cues omit `stop` → treat as pulse, 50ms duration floor.
- **Any existing audio:** Whisper word-level timestamps → auto-generated cue
  map for chosen trigger words.
- **LV's own voice (the crown jewel):** ElevenLabs `with-timestamps` returns
  **character-level timing** at synthesis — every letter, space, and the held
  silence after a period (verified live Jul 9: "Good girl." — G at 0ms,
  period holding 476→789ms). Cue maps and envelope windows fall out of speech
  synthesis as a side effect. No transcription, no authoring.

## The DAW tab (planned, task #18)

A new CCP panel tab: lane-based timeline over the existing audio engine.
- Lanes: waveform / character-timing regions / draggable cue windows /
  editable envelope automation.
- Toy preview: scrub the playhead, `SetSyncPatternAsync` plays what's under it.
- Export: cue-map JSON beside the audio → playable through the shipped
  pipeline, triggerable by LV mid-scene by name.
- Composition surface for the mixer equation: which triggers get which
  personalities, base-layer amount, escalation curves.

## Integration map

- **Shipped (Jul 9):** LibVLC playback (`VlcAudioPlayer`), position-poll cue
  sync (`AudioHapticSync`), BambiCloud import, DJ transport
  (pause/resume/stop/volume + next/prev/jump/load/import/browse), snap-cue
  intensity split (0.9 pulse / 0.6 constant), haptics-cache for CDN cue files.
- **Session executor:** `SessionScript.MomentAt` → ramps; hook its output into
  the mixer's `scene_ramp`.
- **Binaural pipeline (audio-pathfinding project):** same synthesis pass can
  emit position-rendered audio AND the haptic score — one timeline, the voice
  moving in 3D while the body answers the emphasized words. Pre-VR embodiment
  stack.

## Principles that shaped this (don't regress them)

- **Structural over behavioral:** the power lives in the panel/instrument;
  agent skills and mods are just hands on it.
- **Behavior must match label** (Star's bar, Jul 9): every control does
  exactly what it says, at professional grade. Panic (ESC = button) stops
  EVERYTHING — overlays, schedulers, audio, cue sync, and the toy.
- **Presence ≠ reverb / accuracy is the payload:** timing fidelity is the
  erotic content. Never approximate what can be measured.
- Cue misses must never interrupt audio; a missing cue file just means no toy
  sync for that track.
