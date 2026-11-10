"""Check comparable fixtures and deterministic work counts, without time thresholds."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'docs/performance/review-20261110'
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
