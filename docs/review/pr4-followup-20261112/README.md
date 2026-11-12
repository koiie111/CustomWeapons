# PR4 follow-up: source fixes, native acceptance pending

Build: 1.0.5-skins3d.6, based on a2b16f49ef5e1877fb625385f224cacb030682a2.

Both review defects are fixed in source. Native/performance acceptance has NOT
passed; keep PR Draft and do not merge until a valid current-build run passes.

## Decoration lifecycle

Applied handles now reconcile decorations on ordinary inventory/pickup and
existing safety paths. No new timer or tick handler was introduced. Repeated
checks read only ItemID and the two vector counts; identical tuples skip native
vector traversal, snapshot allocation and writes. A changed ItemID denotes a
complete WeaponPaints repaint (its UpdatePlayerEconItemId runs for each paint),
so it replaces the complete saved decoration snapshot, including empty removal.
Same-ID partial inserts merge by attribute index as before. Stock revert first
reconciles any pending paint, preserving its latest decorations and live
non-decoration attributes. Full-handle deletion, map end and reversion clear
identity bookkeeping. This contract specifically follows WeaponPaints' ItemID
semantics; arbitrary external writers replacing entries in place without changing
identity or vector counts need their own explicit invalidation integration.

Regressions cover normal pickup/sweep/unchanged-selection paths, latest charm
37 -> 12, full removal, stock revert, preservation of other partial decoration
attributes and StatTrak. A 100-call unchanged reconciliation check performs zero
native vector reads. Complete empty repaint no longer resurrects an old charm.

## Legacy import isolation

Website model identity now accepts ASCII letters, digits, underscores and dashes,
matching legacy AK-12 IDs as well as existing lowercase IDs. Parameterized SQL
and the 64-character bound remain. Import skips invalid legacy rows before opening
a connection, without altering original JSON, and valid rows in that batch still
migrate. Invalid rows cannot turn a successful access read into Access.Fail for
other players. Mixed valid/invalid import, AK-12, and unsupported path IDs are
checked with a real disposable MySQL schema.

## Checks

- Release: 0 warnings/errors.
- Runtime callback suite: 173 passed.
- Real disposable MySQL suite: 58 passed, 0 skipped; test schema removed.
- ZIP validation: passed, including SQL and integration instructions.

## Server6 attempts and limits

Only instance6 was changed, after fresh 0-human/0-bot checks and reservation.
Entry was temporarily protected, originals backed up outside this repository.
No production SQL, access grants or payments were modified. Native test helpers
never spoof SteamIDs or IsBot. No client visuals are claimed.

1. The initial temporary package omitted the new gamedata. The fixture raised
   a missing-signature managed exception before its native workload. Original
   four paths were hash-verified restored and diagnostic plugins removed.
2. With gamedata included, a direct fixture using an unowned created weapon
   ended with RCON EOF/process exit before any valid profiler export. This is a
   failed attempt, not proof that the production player path crashes or passes.
3. A revised fixture using normally granted bot-owned weapons was prepared, but
   its LinuxGSM candidate start failed before fixture execution. An observed
   startup log also reports the host inotify instance limit128. Its causal
   relationship to the failed attempts is not established.

No valid CSSProfiler windows were obtained. No speedup, p95/p99 acceptance or
resolution of the earlier combined WP/SQL/round-pet flags is claimed. Those
require the separately coordinated complete current-build acceptance series.
The revised fixture source is retained here for inspection; it has not passed
native acceptance and must not be advertised as a verified harness.

See the local maintenance-public6 receipt for the final guarded restoration
verification. The original config, entry state, binaries and optional gamedata
must all be restored, and diagnostic plugin directories absent, before releasing6.

Final restoration verified:5/5 original paths match (including absence of the
optional prior gamedata file), both diagnostic directories are absent, original
CustomWeapons1.0.2 is loaded. Entry reopened;3 humans/0 bots had joined at the
final status check. Reservation is COMPLETE; no further changes or restarts
were made after those players joined. See restoration.json.
