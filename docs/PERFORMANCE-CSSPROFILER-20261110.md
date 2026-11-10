# CustomWeapons: empty-server CSSProfiler comparison — 2026-11-10

## Result

| Build | Idle: profiled ms / 30 s | Entity workload: profiled ms / 30 s | OnTick mean, us | OnEntitySpawned mean, us |
|---|---:|---:|---:|---:|
| Pre-O4 | 3.219 | 10.705 | 1.678 | 8.822 |
| Current main (O4) | 0.000 | 6.124 | no listener | 6.769 |
| 1.0.3-perf.1 | 0.000 | 2.069 | no listener | no listener |

The old idle OnTick averaged **1.678 us**, with p95 **2.212 us** and p99 **3.015 us**. O4 removes this listener entirely with the production gun-only configuration. "No listener" is a verified absence of registration, not a claimed zero-cost timed call.

In the synthetic entity scenario, measured CW callback time fell **42.8%** from pre-O4 to current main, and **66.2%** from current main to the follow-up. Pre-O4 to follow-up: **80.7%**, saving **8.636 ms per 30 seconds** in this specific experiment. These percentages describe only the covered callbacks and workload; the absolute cost on an empty server is small.

## Change

Current main already omits grenade OnTick for gun-only configurations, but still registers OnEntitySpawned for every entity. Matching projectile spawns also schedule an owner-resolution callback even though no configured grenade model can ever be applied. The follow-up registers OnEntitySpawned only in the same grenade-enabled branch as OnTick. Gun/knife inventory application and OnEntityDeleted model bookkeeping are unchanged. The follow-up has ModuleVersion/package version `1.0.3-perf.1` so its binary can be distinguished from the existing `1.0.2` builds.

## Protocol and scope

- Public instance #17, `de_mirage`, Intel Xeon E5-2670 v3, CSS 1.0.378, CSSProfiler 1.0.0, .NET SDK 10.0.400 builds, same dependencies and production configuration throughout the main comparison.
- Baseline source: `c52b926dab2e4c234d22e901b71d1709929837e7`; current main: `52c686a3ffc5e81f2f0d0d6b1512638bc0e541f1`. All three compared binaries were built locally for this experiment. The original installed DLL is a separate preserved artifact, not asserted to be byte-identical to the local main build.
- Three 30-second instrumented windows per build per scenario: 18 main windows. Build order was pre-O4 -> main -> follow-up, with idle then entity windows within each build; this is a sequential series, not randomized or simultaneous. Neighboring game instances remained running.
- Idle: no injected entities or clients. Entity scenario: temporary CWProfileProbe dispatches one `info_target` and one `flashbang_projectile` per 0.1 server seconds and removes both synchronously (nominally 20 entity spawns/s). Actual profiled call counts are listed below; helper counters cover additional time before/after the profiler window. No bots, human client, real throws, custom models or entitlements were introduced. Probe counters verified zero failures in all windows.
- A further two 30-second follow-up windows added only an empty `weapon_flashbang` category. Both grenade listeners registered and executed, with no probe failures. This verifies the enabled branch and invalid-projectile-handle guards, not held-grenade discovery, custom-model transfer or visual correctness.
- Zero humans and zero bots checked before every stage/window and periodically throughout. Main/follow-up stages used a temporary entry password in server memory. The baseline remained publicly joinable after the initial driver retry: an initial readiness check expected a `v` prefix absent from the actual plugin listing and was corrected by resuming the already-running baseline without another restart; that resume path skipped its entry-lock step. All baseline status observations still showed 0/0, and raw CW profiles contain no client connect/authorize/disconnect calls. This protocol difference is retained rather than describing every window as locked. Each binary/configuration stage used a scoped LinuxGSM cold stop/start after an empty-server check. No other instance was restarted; the original entry state was restored afterward.
- **Five scoped cold cycles**: baseline, main, follow-up, follow-up grenade branch, original restoration. The initial driver retry did not add a cold cycle.
- CSSProfiler commands: `css_prof_calls_start 30`, status/report/export. Sampling profiler remained idle. Source inspection confirms calls mode wraps events/listeners/commands; it does **not** measure AddTimer or NextFrame callbacks, native hooks outside the wrapped handlers, allocations or whole-frame cost. Stopwatch measures elapsed time and includes instrumentation and scheduler noise. No claim about total plugin CPU, server FPS, stalls, or performance with 1/8/32 real players follows from these data.
- Means/p95/p99 use every O/C interval from raw evented speedscope data, in milliseconds, then convert to microseconds. Percentiles use linear interpolation. Total CW time sums only CW callbacks in each window. Data and export hashes are preserved; no profiler overhead is subtracted.

## Verification and final server state

- Baseline Release: 0 build warnings/errors; 54 tests passed, 1 MySQL integration test skipped without a disposable database.
- Main and follow-up Release: 0 build warnings/errors; 57 tests passed, 1 MySQL integration test skipped. Package validation and `git diff --check` recorded separately.
- All original loaded plugins were present after each cold start. The helper was the only additional plugin during measurements.
- CustomWeapons/CSS core/CSSProfiler logs were collected from just before the first test cold start through final restoration. Matching error/exception/load/hook-restoration-failure lines: **0** in these logs. This is a bounded log check, not an assertion about every unrelated plugin log.
- Original CustomWeapons DLLs and all unrelated plugin DLL hashes restored after testing. Production config restored byte-for-byte. Probe removed from autoload; entry reopened; CSSProfiler left loaded and idle, as found. No fleet rollout, schema/entitlement/payment write, or cosmetic client preference change was part of this test.
- This experiment does not resolve the previously documented immediate first-person viewmodel refresh limitation. The follow-up only changes unnecessary callback registration.

## Individual windows

| Window | Profiled CW ms | OnTick calls | OnEntitySpawned calls | OnEntityDeleted calls |
|---|---:|---:|---:|---:|
| baseline-idle-0 | 3.2923 | 1919 | 0 | 0 |
| baseline-idle-1 | 3.1597 | 1919 | 0 | 0 |
| baseline-idle-2 | 3.2059 | 1919 | 0 | 0 |
| baseline-entities-0 | 9.3065 | 1919 | 600 | 600 |
| baseline-entities-1 | 12.6624 | 1919 | 598 | 598 |
| baseline-entities-2 | 10.1475 | 1919 | 598 | 598 |
| main-idle-0 | 0.0000 | 0 | 0 | 0 |
| main-idle-1 | 0.0000 | 0 | 0 | 0 |
| main-idle-2 | 0.0000 | 0 | 0 | 0 |
| main-entities-0 | 6.3878 | 0 | 600 | 600 |
| main-entities-1 | 5.8655 | 0 | 600 | 600 |
| main-entities-2 | 6.1188 | 0 | 598 | 598 |
| fixed-idle-0 | 0.0000 | 0 | 0 | 0 |
| fixed-idle-1 | 0.0000 | 0 | 0 | 0 |
| fixed-idle-2 | 0.0000 | 0 | 0 | 0 |
| fixed-entities-0 | 2.0965 | 0 | 0 | 600 |
| fixed-entities-1 | 2.0254 | 0 | 0 | 600 |
| fixed-entities-2 | 2.0851 | 0 | 0 | 600 |
| fixed-grenades-entities-0 | 10.5636 | 1919 | 598 | 598 |
| fixed-grenades-entities-1 | 8.6419 | 1919 | 598 | 598 |

## Per-handler distributions

| Build / scenario | Handler | Calls | Mean, us | p95, us | p99, us | Max, us |
|---|---|---:|---:|---:|---:|---:|
| baseline / idle | listener:OnTick | 5757 | 1.678 | 2.212 | 3.015 | 47.601 |
| baseline / entities | listener:OnTick | 5757 | 1.609 | 2.265 | 2.987 | 16.069 |
| baseline / entities | listener:OnEntitySpawned | 1796 | 8.822 | 12.397 | 19.639 | 1724.964 |
| baseline / entities | listener:OnEntityDeleted | 1796 | 3.903 | 5.508 | 9.346 | 395.725 |
| main / entities | listener:OnEntitySpawned | 1798 | 6.769 | 11.536 | 17.580 | 58.607 |
| main / entities | listener:OnEntityDeleted | 1798 | 3.449 | 5.469 | 9.550 | 242.132 |
| fixed / entities | listener:OnEntityDeleted | 1800 | 3.448 | 5.444 | 7.842 | 227.374 |
| fixed-grenades / entities | listener:OnTick | 3838 | 1.598 | 2.245 | 2.560 | 13.393 |
| fixed-grenades / entities | listener:OnEntitySpawned | 1196 | 6.980 | 11.632 | 15.422 | 53.886 |
| fixed-grenades / entities | listener:OnEntityDeleted | 1196 | 3.949 | 5.009 | 7.244 | 775.911 |

Maxima are observed elapsed-time outliers, not upper bounds or proof of a specific cause.

## Binary identity

```json
{
  "compared_builds": {
    "baseline": {
      "CustomWeapons.dll": "fed9a3adfcc3b7005c1eac29cc2cb258b6c1482e0e873e709b05d9834ff95bc8",
      "CustomWeapons.Core.dll": "5d19201ee9db3da4616343053374f60b753fe9aaca2fe794a15f5dbf9163ac21"
    },
    "main": {
      "CustomWeapons.dll": "30c5971172d80adeeb16ba677b847b9b54cb45deb44cf7ac814c8b6ba3912cdf",
      "CustomWeapons.Core.dll": "c7ee4022afc54974f7fc26ffe39f23e4d4a80472ee51dbc773b69d30634e386e"
    },
    "fixed": {
      "CustomWeapons.dll": "89249b016e6de1d6c5c5f3f6ec3afebe0c51e08174d7d899035a43bd54bfcc8d",
      "CustomWeapons.Core.dll": "cd332d3c69f6691a7a83756fde952a3d7480689a2575319c1c12ef3cf00e972a"
    }
  },
  "profiler": {
    "CSSProfiler/TraceReloggerLib.dll": "6830c0f1d3f3c17719181e73dd5cfa8ccbb3f3c575329d2120576545103467b5",
    "CSSProfiler/Microsoft.Diagnostics.Tracing.TraceEvent.dll": "2150c5bd3a19358e9871159272f6548ff0d1c4184a687ded8ddf0ffcdd93c00b",
    "CSSProfiler/CSSProfiler.dll": "62cb911b532094734c773b0f125e71dd72ee3b1c38759ff6cdf92bcac2baed91",
    "CSSProfiler/Microsoft.Diagnostics.FastSerialization.dll": "a216b032692e6b0effc7adac7b7591ed2c754e2c6764bb7127464b808d8bdfd5",
    "CSSProfiler/Dia2Lib.dll": "6c4cac68010fe032218efe5e9fcf46eef9f77bfaa5f3bd33f03c5ff77d5a8fac",
    "CSSProfiler/Microsoft.Diagnostics.NETCore.Client.dll": "e2cc9e1f6c97b3c4ee45ebabaa92408eb9b2fc81cb08ceeb342fe48bf79db15f"
  },
  "original_installed": {
    "CustomWeapons/Microsoft.Extensions.DependencyInjection.Abstractions.dll": "7c97c4463fe0fc11d1f22f4d160b2bd2288981adf1de2717a6ff3fb993ddf294",
    "CustomWeapons/MySqlConnector.dll": "e7734ef5db26f5e5626f8ef354831c60adff9a8e88447cc5e865cfe1f9e466bc",
    "CustomWeapons/Microsoft.Extensions.Logging.Abstractions.dll": "a76a735b63e6ef3b0bf41ff92423cbd16103b8952e8306140a9bd650346034dc",
    "CustomWeapons/CustomWeapons.dll": "9c8e48d61659919c311a72730803f44adcbf63a863e662f2990d840d7d65ab6d",
    "CustomWeapons/CustomWeapons.Core.dll": "ba7e44839456f415ab1f79781b12b48fba580ce6d982a0792bf4736c63f84143"
  }
}
```

The compact machine-readable aggregate is in `performance/cssprofiler-20261110-summary.json`. Raw speedscope exports, full observation metadata, probe source and the analysis script are retained in the private task evidence bundle; it contains no game/database credentials. The original production configuration backup remains only on the server, outside plugin autoload paths.
