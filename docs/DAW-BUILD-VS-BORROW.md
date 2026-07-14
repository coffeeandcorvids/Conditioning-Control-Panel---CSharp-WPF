# DAW: build vs. borrow — MIT-library scouting

Star's idea (Jul 13 2026): rather than hand-build every DAW feature, pull code from
a well-liked, recent, **MIT-licensed** DAW/library.

## The licensing reality (read this first)

Almost every serious open-source DAW is **GPL**, not MIT:

- **Ardour**, **Audacity**, **LMMS**, **MuseScore** — all GPL.

GPL is viral: linking GPL code forces *our entire project* to become GPL. That
directly conflicts with the monetization seed (selling the generic voice-haptics
core as a standalone plugin). So whole-DAW borrowing is mostly off the table on
licensing grounds alone.

**Where borrowing actually pays off: the library level**, for specific pieces we
don't have yet (chiefly a real audio **waveform display** — the timeline currently
draws the haptic envelope, not the audio).

## Candidates found (MIT unless noted)

| Library | License | Fit | Notes |
|---|---|---|---|
| **MediaPlayerUI.NET** (mysteryx93) | MIT | ★★★ best fit | Media-player UI that **supports Avalonia** (and WPF). Closest thing to drop-in transport/seek UI for our stack. Evaluate first. |
| **NAudio.WaveFormRenderer** (naudio) | MIT | ★★ borrow the algorithm | Peak-extraction + waveform rendering, but renders via System.Drawing and decodes via NAudio (Windows-centric). Take the *peak-bucketing logic*, not the code as-is. |
| **NAudio** core | MIT | ✗ decode | Windows-centric decoding. We already decode cross-platform via **LibVLC**, so we don't need it. |
| **NWaveform** (awesome-inc) | check | ? | .NET waveform lib — verify license before use. |
| **libsoundio-sharp** | MIT | ✗ | Low-level cross-platform audio I/O, not waveform. Not needed (LibVLC covers I/O). |

## Recommendation

1. **Waveform display** is the one real gap worth borrowing for. Our decode is
   already LibVLC (cross-platform, fine). We just need **PCM peaks → a lane behind
   the cues**. Options, cheapest first:
   - Extract peaks ourselves from a decoded PCM stream (small, MIT-clean, no dep).
   - Adopt **MediaPlayerUI.NET** if its Avalonia waveform/seek control fits.
   - Port NAudio.WaveFormRenderer's peak-bucketing math (MIT) into our renderer.
2. **Everything else** (transport, cues, envelope, haptic sync, presets) is already
   ours and working — no borrowing needed.
3. **Do not** pull GPL DAW code; it would relicense the sellable core.

Decision owner: **Star.** Nothing here is wired in yet — this is a menu.
