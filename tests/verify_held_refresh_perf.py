"""Validate the frozen perf.2/held-refresh managed callback comparison."""
import hashlib
import json
from pathlib import Path
import zipfile

repo = Path(__file__).resolve().parents[1]
for name in ('held-refresh-20261110', 'held-replacement-20261110'):
    root = repo / 'docs/performance' / name
    manifest = json.loads((root / 'inputs.json').read_text())
    archive = root / 'source-inputs.zip'
    assert hashlib.sha256(archive.read_bytes()).hexdigest() == manifest['archive_sha256']
    with zipfile.ZipFile(archive) as source:
        assert set(source.namelist()) == set(manifest['sources'])
        for name, expected in manifest['sources'].items():
            assert hashlib.sha256(source.read(name)).hexdigest() == expected, name

    docs = []
    for name, expected in manifest['results'].items():
        data = (root / name).read_bytes().replace(b'\r\n', b'\n')
        assert hashlib.sha256(data).hexdigest() == expected, name
        docs.append(json.loads(data))
    old, new = docs
    assert old['synthetic'] and new['synthetic'] and old['runtime'] == new['runtime']
    assert len(old['rows']) == len(new['rows']) == 24
    for a, b in zip(old['rows'], new['rows']):
        for field in ('active', 'name', 'window', 'iterations', 'bytes_per_call',
                      'player_scans', 'definition_reads', 'model_writes'):
            assert a[field] == b[field], (field, a, b)
        assert b['model_writes'] == 32 and b['definition_reads'] == 0
        assert b['player_scans'] == (0 if b['name'] == 'tick' else b['iterations'])
        # Timing stays visible in raw files, but is not a machine-dependent CI gate.
    print(f'Verified 24 perf.2/{root.name} pairs: identical allocations and deterministic callback work')
