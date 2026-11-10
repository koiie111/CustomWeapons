"""Verify sanitized held-model acceptance artifacts, not unobserved client behavior."""
import hashlib
import json
from pathlib import Path
root=Path(__file__).resolve().parent
for name,want in json.loads((root/'SHA256.json').read_text()).items():
    assert hashlib.sha256((root/name).read_bytes().replace(b'\r\n',b'\n')).hexdigest()==want,name
summary=json.loads((root/'summary.json').read_text())
raw=json.loads((root/'native-state-25.json').read_text())
assert raw['status']=='passed' and raw['completed']==25
verify=[r for r in raw['logs'] if r.get('stage')=='verification']
results=[r for r in raw['logs'] if r.get('stage')!='verification']
assert len(verify)==len(results)==25
for a,b in zip(verify,results):
    assert a['owner']==a['expected_owner'] and a['ak_count']==1
    assert a['old']==b['old'] and a['fresh']==b['fresh'] and a['old']!=a['fresh']
    assert all(h['raw']!=a['old'] for h in a['inventory'])
    assert a['clip']==b['clip']==17 and a['reserve']==b['reserve']==[29,0]
    assert a['paint']==302 and a['seed']==571 and a['nametag']=='CW native preserve'
    assert a['ammo'][a['primary']]==73
    attrs={r['index']:r['bits'] for r in b['attributes']}
    assert attrs=={6:1133969408,7:1141817344,80:2143294004}
    assert len(b['dynamic_attributes'])==2
assert summary['client']['old_stock_handle']!=summary['client']['decorated_ground_handle']
assert summary['client']['count']==1 and not summary['client']['exact_final_ammo_equality_claimed']
assert not summary['fleet_deployed']
for key in ('all_original_plugin_dlls_restored','config_restored','gamecfg_restored','preferences_preserved','helper_removed','temporary_gamedata_removed','entry_open'):
    assert summary['restoration'][key],key
print('Verified 25 native replacements, source/evidence hashes and restoration; single-client reports kept within observed scope')
