"""Verify canonical text hashes, raw projectile observations and restoration receipt."""
import collections
import datetime
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parent
for name, expected in json.loads((root / 'SHA256.json').read_text(encoding='utf-8')).items():
    assert hashlib.sha256((root / name).read_bytes().replace(b'\r\n', b'\n')).hexdigest() == expected, name
summary = json.loads((root / 'summary.json').read_text(encoding='utf-8'))
raw = []
for name in ('projectile-events-before-map.json', 'projectile-events-after-map.json'):
    raw.extend(json.loads((root / name).read_text(encoding='utf-8'))['samples'])
assert len(raw) == len(summary['projectile_samples']) == 10
assert len({s['raw'] for s in raw}) == 10
assert dict(collections.Counter(s['name'] for s in raw)) == summary['projectile_counts']
for native, published in zip(raw, summary['projectile_samples']):
    assert native == {k: v for k, v in published.items() if k != 'saratov_time'}
    assert native['model'] == 'models/chicken/chicken.vmdl'
    assert native['thrower'] == native['original_thrower'] and native['thrower'] not in (0, 4294967295)
    local = datetime.datetime.fromisoformat(native['utc']).astimezone(datetime.timezone(datetime.timedelta(hours=4)))
    assert local.isoformat() == published['saratov_time']
for key in ('all_original_plugin_dlls_restored', 'config_restored', 'gamecfg_restored',
            'data_preserved', 'helpers_removed', 'entry_open', 'profiler_idle'):
    assert summary['restoration'][key] is True, key
assert summary['cases']['existing_instance_first_person_after_selection_and_pickup'] == 'failed'
print('Verified 10 projectile observations, UTC/Saratov times, hashes and restoration; first-person failure retained')
