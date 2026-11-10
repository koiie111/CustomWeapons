# O4 — CustomWeapons performance

## Plan and implementation

Baseline: `c52b926dab2e4c234d22e901b71d1709929837e7`, .NET SDK 10.0.400, `net10.0`, CounterStrikeSharp.API 1.0.375. Baseline Release DLL SHA256: `851d7f04eccdb7bcc63ec2a7c06815fa1744083dc72fe0bcff9f9fb87a4febf9`. The installed #7 DLL had SHA256 `557430f0195bc317a11a76ee57b9d298f8c38c78f7a1769f6d0bb34d9392c604` before testing, so source-to-binary matching is provisional.

The old `OnTick` iterated every player and searched all projectile aliases even when no grenade model was configured. The new build registers that listener only when the configuration contains a projectile weapon. For enabled grenades, it uses a precomputed name lookup and scans a set of grenade holders rather than all connected players. Equip, pickup, spawn, connect and the 0.5-second inventory reconciliation update the set; disconnect and map end clear it. The projectile-spawn callback still scans current players once to capture a newly granted grenade before the throwing weapon is removed. Unchanged samples refresh in place, and owner resolution remains on the next frame.

The 0.5-second inventory safety check now compares exact entity handles including serials before applying models. The full reconciliation still runs every five seconds, staggered by slot. Spawn and pickup schedule application on the next frame; selection applies to the current weapon entity; database refresh applies after its snapshot returns. Existing models remain on weapon instances after private access expires or is revoked; new applications remain subject to the original access rules.

One-client testing found that a model selected in the menu changes the weapon entity (visible when dropped), but the first-person viewmodel remains old even after picking it up. Buying a new instance displays the new model. Deferring application one frame did not fix this, so the experiment was removed. A [third-party implementation](https://github.com/exkludera-cssharp/cs2-force-custom-weapons/blob/main/src/weapon.cs) uses direct schema-offset memory access to change the client viewmodel; O4 does not adopt that engine-version-sensitive technique. Immediate first-person refresh remains an open functional issue.

The owner's [WeaponPaints fork](https://github.com/koiie111/cs2-WeaponPaints/blob/main/WeaponAction.cs#L259) explicitly forbids removal and re-giving inside `RefreshWeapons`: delayed removal previously duplicated weapons during repeated refreshes. Its [separate pickup handler](https://github.com/koiie111/cs2-WeaponPaints/blob/main/WeaponAction.cs#L493) takes an inventory snapshot before a narrow USP/knife grant; it is not a general replacement routine preserving ammo, weapon identity or other cosmetic data. O4 therefore does not copy it into the menu-selection path.

## Verification

- Baseline: `dotnet restore --locked-mode`, `dotnet build -c Release --no-restore` (0 warnings), `dotnet test -c Release --no-build` (54 passed, 1 skipped without disposable MySQL).
- PR review follow-up: `dotnet restore --locked-mode`, `dotnet build -c Release --no-restore` (0 warnings), `dotnet test -c Release --no-restore` (57 passed, 1 skipped without disposable MySQL), `git diff --check` and `pwsh -NoProfile -File scripts/package.ps1` passed on Windows with .NET SDK 10.0.400.
- #7 preflight: 0 players/0 bots via loopback A2S; config SHA256 `8fb3a30285ddade1a3dd4901e2164a4287f72b4f80e808596207b4010c14bcae`; seven weapon categories and no grenade category.
- #7 earlier candidate: backed up both DLLs at `/root/codex-customweapons-o4-20261109-7/`, then stopped and started only `cs2_7` using its LinuxGSM manager. That earlier DLL had SHA256 `839a20c8726b27e2e1c49e7c63498491da08ad94d48d0f6294d165d7bbea071a`; `CustomWeapons.Core.dll` had SHA256 `d90a8c4643e0d18c56d9ecd23b0358882a54ac37668acd1e9ed4a1afa61725db`. Each scoped restart followed a fresh loopback A2S result of 0 players/0 bots. The CSS log shows `Finished loading plugin CustomWeapons` and `runtime ready`, without a related load error. The holder-index follow-up has only offline verification.
- Reviewed release DLLs: `CustomWeapons.dll` SHA256 `0c5c56edc85aa185956a9e2f38fc806ef7949b11ae112992e25fa1dca117f93f`; `CustomWeapons.Core.dll` SHA256 `e0b5e3f6dcc30083217cfa5fbe3636c687b19e9536d8fda9a4e3c9b9266e8cc5`. The package hash is recorded in `artifacts/SHA256SUMS.txt` by `scripts/package.ps1`. The DLL hashes, rather than the unchanged module version, identify this candidate.

No percentage speedup is claimed from historical load profiles or empty-server data. A no-grenade configuration removes this plugin's grenade `OnTick` listener, but active-load full-frame improvement still needs a separate comparable measurement. No production rollout beyond the selected empty test instance is part of O4.

The #7 check does not exercise grenade models because that server has no configured grenade skin. One-client checks covered menu selection, purchase and held/dropped model behavior on the earlier candidate; two-client transfer, grenade visuals, and holder-index behavior remain untested in game. No CSS profiler is installed on #7; therefore there is no paired in-game timing series for baseline and candidate.

## Release and rollback

Deploy only `CustomWeapons.dll` and `CustomWeapons.Core.dll`; preserve config, `data/selections.json`, and other plugin files. Use a cold instance restart because prior CounterStrikeSharp hot-load failures left native delegates. Keep a backup outside the plugin directory and restore both DLLs on a newly verified empty instance if startup or behavior regresses.

## CSSProfiler follow-up

The later [empty-server comparison on #17](PERFORMANCE-CSSPROFILER-20261110.md) measures pre-O4, current main and the `1.0.3-perf.1` follow-up with CSSProfiler. It supersedes the absence of paired timing evidence for these empty-server scenarios only; the client/load limitations above remain.

