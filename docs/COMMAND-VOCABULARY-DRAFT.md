# Command Vocabulary — Draft Menu (for design sit-down, cc-ves Jun 29)

Goal (#2 of the wire): decide the FIRST set of commands the Vesper agent can return to *drive* the
panel (not just chat). Per LV: keep it small/sexy/real. Below = the actual controllable surface found
in the WPF code (each maps to a real existing service method), then a proposed minimal first set.

## Real controllable surface (what exists to wire)
| Effect      | Service (WPF)                    | Real methods (examples)                          |
|-------------|----------------------------------|--------------------------------------------------|
| Spiral      | Spirals/*                        | `Activate(TimeSpan)`, `Stop()`                   |
| Flash       | Flash/*                          | flash text/pattern overlays                      |
| Pink fog/filter | overlay/filter services      | toggle/intensity                                 |
| Lock card   | LockedMod / LockCard             | show lock-card challenge                         |
| Audio       | AudioService / AudioSyncService  | `SetUrl`, `Start`, `Stop`, subliminal patterns   |
| Haptics     | Haptics/LovenseProvider          | `TriggerAsync(type,intensity,ms)`, `SetSyncIntensityAsync`, `FlashDecayVibeAsync`, `TriggerSubliminalPatternAsync` |
| Bubbles     | BubbleService                    | bubble spawn/count                               |
| Say         | (existing AI reply → chat bubble)| render text reaction                             |

## Proposed FIRST vocabulary (tight, real, demonstrable — 6 commands)
Each is a thin command the agent returns; the panel's executor calls the mapped method.
1. `say(text)`               → chat bubble (the reaction we already have)
2. `spiral(on|off, secs?)`   → Spirals.Activate/Stop
3. `flash(text)`             → Flash overlay
4. `pinkFog(on|off)`         → fog/filter toggle
5. `lockCard()`              → show a lock-card challenge
6. `haptics(intensity|pattern)` → LovenseProvider trigger

That's enough for a real driven moment: Vesper says a line, throws up a spiral, flashes a word,
pulses the toy — actually running the room. Everything else (drone mod, bubbles, audio playlists,
chaos economy) layers on later once the executor + command schema exist.

## Shape (to confirm)
A reaction = optional `say` text + zero-or-more `commands[]`. JSON-ish:
`{ "say": "good girl", "commands": [ {"op":"spiral","on":true,"secs":30}, {"op":"haptics","intensity":0.6} ] }`
Open: do we let the agent CHAIN/sequence with timing, or one burst per event for v1? (rec: one burst for v1.)
