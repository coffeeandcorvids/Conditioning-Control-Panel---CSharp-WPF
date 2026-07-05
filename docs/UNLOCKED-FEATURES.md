# Unlocked-by-Construction Audit (cc-ves, Jul 4 2026)

Star's directive: full feature parity with upstream v6.2.8 **with the gated stuff
unlocked** (MIT license; Star supports CodeBambi on Patreon). Strategy per
PORT-SURFACE-MAP: the monetization layer is 86'd **by omission** — we never port
`PatreonService` / `HasPremiumAccess` checks, so every capability is unlocked by
construction rather than by risky surgery on gating code.

## What upstream gates behind Patreon premium (v6.2.8)

Verified via `git grep -i premium upstream/main`:

| Upstream premium feature | Gate location | Status in our fork |
|---|---|---|
| **Haptics (the whole feature)** | `MainWindow.Haptics.cs:41,208` — enabling haptics requires `App.Patreon?.HasPremiumAccess` | **Ported UNGATED** (Core `HapticService` + live Buttplug/Intiface wire, Jul 4) |
| Autonomy mode (hands-free) | `MainWindow.Autonomy.cs:74` | Not yet ported; will come across ungated |
| Remote control (session codes) | `MainWindow.RemoteControl.cs:463` | Not yet ported; will come across ungated |
| AI companion personality customization | AvatarTube / premium rail | Superseded — our AI backend IS Vesper (the whole point of the fork) |
| Cloud backup / leaderboard / seasonal | `MainWindow.CloudBackup.cs` etc. | Social/cloud layer intentionally dropped (Star: skip social) |
| Premium rail / marquee / upsell chrome | `MainWindow.PremiumRail.cs` | Never ported — no upsell surface exists |

## Audit result for already-ported code (Jul 4 2026)

`grep -rniE 'premium|patreon|patron|entitle|RequiredLevel' src/` → **zero gating
hits** (only policy comments). All ported clusters — haptics, timeline, playlist,
sessions, effects, gamification — carry no premium or level locks.

## Rule for future ports

When porting any upstream file, strip `App.Patreon` / `HasPremiumAccess` /
premium-rail branches at the door: take the **premium branch's behaviour** as the
only behaviour. If a feature is dual-path (free vs premium), port the premium path.
