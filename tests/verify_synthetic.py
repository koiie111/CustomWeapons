"""Check comparable fixtures and deterministic work counts, without time thresholds."""
import json
import hashlib
import zipfile
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
root = repo / 'docs/performance/review-20261110'
manifest = json.loads((root / 'inputs.json').read_text())
# These measurements describe the recorded perf.2 commit, not later revisions.
# Keep its original source/hash inputs reproducible as production code evolves.
archive = repo / manifest['source_archive']['path']
assert hashlib.sha256(archive.read_bytes()).hexdigest() == manifest['source_archive']['sha256']
with zipfile.ZipFile(archive) as inputs:
    assert set(inputs.namelist()) == set(manifest['inputs'])
    for name, expected in manifest['inputs'].items():
        assert hashlib.sha256(inputs.read(name)).hexdigest() == expected, name
        if name.endswith('.json'):
            assert inputs.read(name) == (repo / name).read_bytes().replace(b'\r\n', b'\n'), name
baseline, candidate = [json.loads((root / f'{name}.json').read_text()) for name in ('baseline', 'candidate')]
assert baseline['synthetic'] and candidate['synthetic']
assert baseline['runtime'] == candidate['runtime']
assert len(baseline['rows']) == len(candidate['rows']) == 24
for old, new in zip(baseline['rows'], candidate['rows']):
    for field in ('active', 'name', 'window', 'iterations', 'model_writes'):
        assert old[field] == new[field], (field, old, new)
    assert old['model_writes'] == new['model_writes'] == 32
    assert old['definition_reads'] == 32 * old['iterations']
    assert new['definition_reads'] == 0
    assert old['player_scans'] == old['iterations']
    assert new['player_scans'] == (0 if new['name'] == 'tick' else new['iterations'])
    assert 0 <= new['bytes_per_call'] < old['bytes_per_call']
print('Verified 24 synthetic baseline/candidate window pairs, model results, scans and definition counts')
