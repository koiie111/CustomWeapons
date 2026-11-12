# PR4 review, 2026-11-12

Historical review of skins3d.5. Source findings are addressed by
[skins3d.6 follow-up](../pr4-followup-20261112/README.md); native acceptance remains pending.

Reviewed input: `108c0aca5c9d1f20061f6a04a64cf532ab1579d1` against
main `7855c3eb507b611978fb1adcf305cc8f8c2b87b4`.
Result: **keep Draft; do not merge yet**. Two correctness issues remain below.
The fixes in this review are `1.0.5-skins3d.5`; this is not native acceptance.

## Remaining findings

### P1: later paints bypass suppression and stock rollback restores stale accessories

`ApplyInventory` skips a full handle already present in `_applied`, while
`ApplyWebsiteChanges` skips an unchanged model ID. Neither cache represents the
economy attribute revision. An external painter can therefore reintroduce a
sticker/keychain after the initial suppression, and pickup, safety sweeps and
unchanged SQL refreshes never suppress it again.

This has a concrete integration path in the linked WeaponPaints PR4, inspected
at `1f3ef6d2655860b99320053fdd7ff806b9c2a2ad`: a customized knife pickup calls
`GiveOnItemPickup`, which repaints all weapons, including an already-custom gun.
CW's scheduled pickup callback then takes its applied-handle shortcut. If that
paint supplies a newer charm, `Revert` filters it out and restores the earlier
`_decorations` snapshot instead. A complete external removal has the same stale
snapshot problem; merging only present attribute IDs cannot express deletion.

The diagnostic uses the actual CW source with managed CSS boundary doubles,
emulating the completed external paint before the ordinary CW pickup handler:

```sh
dotnet run --project tests/Runtime/Runtime.csproj -c Release -- review-repaint
```

Observed: `customAfterPickupCharm=12` (expected absent/0), then
`stockAfterRevertCharm=37` (expected latest/12). The same test invokes the safety
deadline and an unchanged website snapshot before returning to stock.
Existing acceptance tests force `ApplyInventory(player, "weapon_ak47")`, which
does not exercise the normal skipped path.

Required: an explicit, ordered painter/application boundary with a decoration
revision or complete snapshot semantics, including empty removals. Preserve
partial updates and live non-decoration attributes. Do not mask this by adding
native attribute scans on every tick. Validate native ordering with the linked
WeaponPaints build after fixing the contract.

### P2: accepted legacy IDs can make every batched access refresh fail

`SkinRules.ValidateWeapons` accepts IDs such as `AK-12`, and existing JSON can
contain them. The new repository accepts only `[a-z0-9_]` model IDs. On enable,
`RequestRefresh` submits connected accounts' JSON choices in one legacy import.
One such row throws before SQL; the catch discards the otherwise successful
read and calls `Access.Fail()` for every captured session. This repeats on each
refresh and denies private model applications to unrelated connected players.

```sh
dotnet run --project tests/Runtime/Runtime.csproj -c Release -- review-legacy
```

Observed: `configAccepted=true`, `imports=1`,
`accessReadyAfterSuccessfulRead=false`. This diagnostic calls real repository
validation before a connection; the unused port is 1 and no live DB is touched.
Align enabled-mode config/import contracts and isolate invalid legacy rows
without destroying the rest of the access batch. Preserve the original JSON;
make any unsupported migration explicit to the operator.

## Small fixes committed by this review

- Successful side-specific SQL writes no longer rewrite the global JSON
  fallback. Previously a CT selection could change T fallback and subsequently
  be imported as team0. This also avoids serializing the entire legacy store
  on every website-mode menu acknowledgement.
- Team-change held refresh now supplies the changed category required by the
  existing safety guard. It also updates the retiring category when a team
  switch occurs during replacement. Tests cover stock/custom transitions,
  preserved ammunition and the mid-replacement switch.
- Release packages include `WEBSITE-INTEGRATION.md` and `sql/`; ZIP validation
  requires both the instructions and the new standalone SQL. Runtime, assembly
  and default package versions identify this review build as skins3d.5.

## Validation

- `dotnet restore --locked-mode` and Release build: 0 warnings/errors.
- `python tests/run_website_mysql.py`: 58 passed, 0 skipped, actual disposable
  schema on the separate local Docker MySQL instance; schema/users removed by
  the runner. No production database, access grant, purchase or payment writes.
- `dotnet run --project tests/Runtime/Runtime.csproj -c Release`: 169 passed
  (original head: 163). Both newly fixed behaviors first failed their added
  regression checks before the fixes were applied.
- Packaging including the SQL/docs ZIP assertions passed.
- All four existing raw profile/synthetic/held-refresh/native-evidence
  verification scripts passed. Those verify historical evidence, not a new
  native run of this head.
- Both explicit diagnostics above reproduce their remaining defects; they are
  deliberately separate from the passing acceptance suite and do not assert
  that those requirements have passed.

## Optimization assessment and limits

No new tick listener or timer is added by website mode. SQL is asynchronous,
reads are coalesced with one running plus one requested follow-up, the DB pool
is bounded to three connections, and menu SQL writes have cooldown/session
guards. Native mutations return to the world-update thread; entity references
use complete handles and map/session checks. The new selection primary key
supports connected-account reads and exact account/team/weapon writes.
Suppression is bounded to 256 attributes per list, but allocates snapshots and
rebuilds whole lists when decorations are present; it belongs on an ordered
paint/change path, not a polling or tick path.

Fresh `baseline-legacy.json` / `candidate-legacy.json` use the same current
managed runtime harness with source substitution for exact main, versus the
review build. Each has 24 windows: 3 x 10,000 calls for tick/safety with 32
synthetic players and 0/1/8/32 grenade holders. Website mode is **disabled**.
All deterministic scan/definition/model-write counters match. Tick allocations
match (0/24/192/768 bytes per call); safety medians are 2016 bytes except the
one-holder tiering-sensitive case (2045.5 baseline / 2031.7 candidate). Safety
still scans players once per call and performs no definition reads or model
writes in steady state. Timing differences, including a large one-holder warmup
difference, remain in the raw files: no speedup or exact allocation parity is
claimed. These are managed doubles, not CSSProfiler/server measurements and
not coverage of the enabled website path.

The existing [combined native report](https://github.com/koiie111/4ill.ru/blob/codex/skins3d-local/docs/measurements/skins3d-20261111/server-v9-custom3/report.md)
tested CW skins3d.3 with WP skins3d.9. Its four threshold flags concern joint
WP/round-pet/SQL latency; they are not proof of a CW CPU regression. Its held
replacement means are about 32 ms in both versions. It cannot certify .4/.5
suppression or its interaction ordering. A matched baseline/candidate/repeat
with identical TCP_NODELAY settings and enabled-feature workloads remains
necessary before performance acceptance. An idle server profile alone cannot
exercise account/team selection, held replacement or decoration restoration.

No game server was modified, restarted or profiled in this review. Correctness
blockers already reproduce without disturbing a server, and an empty idle
profile would not resolve them. Real-client visuals and current-head native
acceptance remain unverified. No merge or production rollout performed.

## CounterStrikeSharp references read before source review

- [Plugin lifecycle and hot reload](https://docs.cssharp.dev/docs/guides/hello-world-plugin.html)
- [Server: NextFrame versus NextWorldUpdate and hibernation](https://docs.cssharp.dev/api/CounterStrikeSharp.API.Server.html)
- [Thread model, main-thread native access](https://github.com/roflmuffin/CounterStrikeSharp/discussions/648)
- [Core configuration and per-frame task queues](https://docs.cssharp.dev/docs/reference/core-configuration.html)
- [Attribute lists](https://docs.cssharp.dev/api/CounterStrikeSharp.API.Core.CAttributeList.html)
