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

Local validation: Release build with zero warnings/errors; 58 xUnit tests with
the actual MySQL test enabled (`python tests/run_website_mysql.py` creates and
drops a disposable schema on the separate clean Docker instance); 144 managed
runtime checks (`dotnet run --project tests/Runtime/Runtime.csproj -c Release`).
These are not a native CS2 or CSSProfiler acceptance result for this candidate.

The website uses actual compiled Workshop meshes/materials. Those current custom
bodies have only UV0 and no confirmed native sticker projection layer. Their
body sticker controls are disabled. Native keychain anchors are exported, but
game/client parity and custom material effects still require acceptance. No
Workshop addon is modified or published by this integration.

Rollback: disable the website feature first, then restore plugin configuration,
binary and JSON backup together. Retain the new table for diagnosis; disabling
the plugin flag intentionally returns to legacy JSON behavior. Do not drop or
truncate shared data as an automatic rollback step.
