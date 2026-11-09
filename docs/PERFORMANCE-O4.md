# O4 — CustomWeapons performance

## Plan and implementation

Baseline: `c52b926dab2e4c234d22e901b71d1709929837e7`, .NET SDK 10.0.400, `net10.0`, CounterStrikeSharp.API 1.0.375. Baseline Release DLL SHA256: `851d7f04eccdb7bcc63ec2a7c06815fa1744083dc72fe0bcff9f9fb87a4febf9`. The installed #7 DLL had SHA256 `557430f0195bc317a11a76ee57b9d298f8c38c78f7a1769f6d0bb34d9392c604` before testing, so source-to-binary matching is provisional.

The old `OnTick` iterated every player and searched all projectile aliases even when no grenade model was configured. The new build registers that listener only when the configuration contains a projectile weapon. For enabled grenades, it uses a precomputed name lookup, skips players without an applied grenade or pending sample, and refreshes unchanged samples in place. Projectile spawn still performs a capture before resolving the thrower on the next frame.

The 0.5-second inventory safety check now compares exact entity handles including serials before applying models. The full reconciliation still runs every five seconds, staggered by slot. Spawn, pickup, selection, and database refresh still call `ApplyInventory` directly. Existing models remain on weapon instances after private access expires or is revoked; new applications remain subject to the original access rules.

## Verification

- Baseline: `dotnet restore --locked-mode`, `dotnet build -c Release --no-restore` (0 warnings), `dotnet test -c Release --no-build` (54 passed, 1 skipped without disposable MySQL).
- Candidate: `dotnet test -c Release` (57 passed, 1 skipped without disposable MySQL); `git diff --check` passed.
- #7 preflight: 0 players/0 bots via loopback A2S; config SHA256 `8fb3a30285ddade1a3dd4901e2164a4287f72b4f80e808596207b4010c14bcae`; seven weapon categories and no grenade category.
- #7 candidate: backed up both DLLs at `/root/codex-customweapons-o4-20261109-7/`, then stopped and started only `cs2_7` using its LinuxGSM manager. Installed `CustomWeapons.dll` SHA256 `20d6a58fd664c271273eea2e2bd11c02caf1f8d66d0792f2bec04cd1aa85f06a` and `CustomWeapons.Core.dll` SHA256 `d90a8c4643e0d18c56d9ecd23b0358882a54ac37668acd1e9ed4a1afa61725db`. The CSS log shows `Finished loading plugin CustomWeapons` and `runtime ready`, without a related load error. The server returned via A2S with 20 slots.
- Release package: `artifacts/CustomWeapons-v1.0.2.zip`, SHA256 `774d4506d84e9afbd857ab7471d0a5f3a202855bf65e63506da3c70897426901`. The DLL hashes, rather than the unchanged module version, identify this candidate.

No percentage speedup is claimed from historical load profiles or empty-server data. A no-grenade configuration removes this plugin's grenade `OnTick` listener, but active-load full-frame improvement still needs a separate comparable measurement. No production rollout beyond the selected empty test instance is part of O4.

The #7 check does not exercise grenade models because that server has no configured grenade skin. One-client gameplay acceptance and two-client transfer remain outside the completed automated checks. No CSS profiler is installed on #7; therefore there is no paired in-game timing series for baseline and candidate.

## Release and rollback

Deploy only `CustomWeapons.dll` and `CustomWeapons.Core.dll`; preserve config, `data/selections.json`, and other plugin files. Use a cold instance restart because prior CounterStrikeSharp hot-load failures left native delegates. Keep a backup outside the plugin directory and restore both DLLs on a newly verified empty instance if startup or behavior regresses.
