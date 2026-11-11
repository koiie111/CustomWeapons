# Website model selections (opt in, draft)

Linked website: https://github.com/koiie111/4ill.ru/pull/10
Linked weapon decoration: https://github.com/koiie111/cs2-WeaponPaints/pull/4

`WebsiteSelectionsEnabled` defaults to false. With it disabled the existing
read-only access queries and local JSON preferences retain their behavior.
With it enabled, the website and in-game menu share `cw_site_selections`.
This table stores model IDs, not resource paths, purchases or access grants.

Install `sql/20261111_website_selections.sql` into the same schema as `cw_access`
and `cw_skins` before enabling either website or plugin flag. It is identical
to the website's `20261111_0002_skins3d_custom_selection.sql`. Back up the schema,
plugin files/configuration and `data/selections.json` before a staged test.
The plugin account still needs SELECT on those access/catalogue tables, plus
SELECT, INSERT and UPDATE on the new selection table only. Do not grant writes
on access, prices or purchases. The website also writes the selection table
inside its existing item transaction alongside `wp_player_skins`.

Install the matching website and immutable custom asset sidecar, deploy the
candidate plugin with the flag disabled, then enable the plugin flag on the
isolated test server and the website flag `SKINS3D_CUSTOM_ENABLED=1` only on its
matching test site. Use the same configured ServerId/access group (the current
public6 configuration uses group1). Flags must be coordinated: a website that
writes shared preferences cannot coordinate with a legacy JSON-only plugin.

Teams2/3 represent T/CT. Team0 is a legacy global fallback. The first refresh
of each connected account imports existing explicit JSON choices with INSERT
IGNORE; it never replaces a website choice. Offline accounts migrate when they
connect. An explicit empty model overrides the legacy/automatic choice with
stock. If neither exists the existing first-available-model rule still applies.
In-game writes use compare-and-swap revisions and update only the active side;
spectators must join a side before changing a preference. Separate decorations
for every custom model are not stored: current weapon decoration is per side
and weapon definition, as in WeaponPaints.

`!wp`, `/wp` and `css_wp` request the existing coalesced access/preference batch
refresh, with a two-second per-session limit across command paths. No shared
static capability or extra tick listener is registered. Background SQL work
publishes through existing map/session/controller guards. A successful menu
commit overrides an older in-flight snapshot until a newer SQL revision arrives.
Changed models use the existing held-model replacement path, retaining ammo and
economy attributes. Team changes invalidate the inventory application cache.

In `1.0.5-skins3d.2`, an already-authorized menu choice is shown immediately for
its side while the same revision-checked SQL write runs. The chat status says
it is saving; a committed override and "saved" confirmation appear only after
SQL succeeds. A conflict or write error clears the temporary choice, reapplies
the confirmed preference and requests a fresh snapshot. A pending CT choice
cannot change T, repeated clicks cannot enqueue another same-weapon write, and
the existing map/session/SteamID guards still gate delayed acknowledgements.
The website continues to save only through its explicit Apply action.

In `1.0.5-skins3d.3`, batched website changes resolve the active weapon category
and pass that category to the held refresh. A forced check without a category
was rejected by the existing safety guard, so a changed world model alone did
not establish an updated view model. The regression test fails on `.2` and
requires a fresh held weapon before SQL acknowledgement, with ammunition and
kill-eater attribute bits preserved. Conflict rollback checks the held stock
model and the same retained state. Runtime coverage now contains 155 checks.

Local validation of `1.0.5-skins3d.3`: 58 xUnit tests, zero skipped, with the
actual MySQL test enabled (`python tests/run_website_mysql.py` creates and
drops a disposable schema on the separate clean Docker instance); 155 managed
runtime checks (`dotnet run --project tests/Runtime/Runtime.csproj -c Release`).
The Release build has zero warnings/errors. Native model/attribute acceptance
applied all 19 models through the website API, SQL and normal `css_wp` dispatch
for the real owner; all three custom AWPs retained kill-eater attribute 80 with
raw counter bits 321. This is state verification, not client-frame acceptance.

The matched server17 campaign `20261111-192115` completed all 12 windows with
WeaponPaints `.9` and CustomWeapons `.3`. Four repeatable p95/p99 threshold
flags remain in asynchronous WP/round-pet/SQL metrics. Separate read-only SQL
diagnosis found a transport effect from Nagle on the test reverse SSH tunnel.
It does not cancel those flags: a fresh complete baseline/candidate/repeat with
the same TCP_NODELAY settings throughout is required. Native held replacement
averaged about 32 ms in both versions; no distinguishable speedup is claimed.
The original server configuration and 1112 file hashes were restored, and
temporary diagnostic helpers removed. Raw profiles and limits are in the
website's `docs/measurements/skins3d-20261111/server-v9-custom3/report.md`.

The website uses actual compiled Workshop meshes/materials. Those current custom
bodies have only UV0 and no confirmed native sticker projection layer. Their
body sticker controls are disabled. All 19 models have a named keychain bone
attachment but no native KeychainMarkup or StickerMarkup. The owner rejected
the Daedalos slab visual check: the slab is absent even though its networked
attributes match. Exported bone anchors must not be treated as proof of native
accessory rendering. An isolated SHA-pinned copy with experimental native
markup was prepared in the website tools. After explicit owner approval the
exact pilot VPK was published as private Workshop item3838092382 and downloaded
by the owner's client with matching SHA256. A separate visual-only server17
pilot is staged; its native rendering is not accepted. The original Workshop
addon is unchanged. Keep the integration
opt-in and all related PRs Draft pending visual/lifecycle/performance acceptance.

Rollback: disable the website feature first, then restore plugin configuration,
binary and JSON backup together. Retain the new table for diagnosis; disabling
the plugin flag intentionally returns to legacy JSON behavior. Do not drop or
truncate shared data as an automatic rollback step.
