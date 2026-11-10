"""Verify frozen CSSProfiler exports and recompute the published aggregate (no server access)."""
import hashlib
import json
import math
from pathlib import Path
import statistics
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / 'docs' / 'performance'


def percentile(values, p):
    values = sorted(values)
    position = (len(values) - 1) * p
    low = int(position)
    return values[low] + (values[min(low + 1, len(values) - 1)] - values[low]) * (position - low)


def describe(values):
    return dict(calls=len(values), total_ms=sum(values), mean_us=statistics.mean(values) * 1000,
                p50_us=percentile(values, .5) * 1000, p95_us=percentile(values, .95) * 1000,
                p99_us=percentile(values, .99) * 1000, max_us=max(values) * 1000)


def parse(data):
    doc = json.loads(data)
    names = [frame['name'] for frame in doc['shared']['frames']]
    times = {}
    for profile in doc['profiles']:
        assert profile['type'] == 'evented' and profile['unit'] == 'milliseconds'
        stack = []
        previous = profile['startValue']
        for event in profile['events']:
            assert event['type'] in ('O', 'C')
            assert math.isfinite(event['at']) and previous <= event['at'] <= profile['endValue']
            assert isinstance(event['frame'], int) and 0 <= event['frame'] < len(names)
            previous = event['at']
            if event['type'] == 'O':
                stack.append(event)
            else:
                opened = stack.pop()
                assert opened['frame'] == event['frame']
                name = names[event['frame']]
                if name.startswith('CustomWeapons |'):
                    times.setdefault(name, []).append(event['at'] - opened['at'])
        assert not stack
    return times, [name for name in names if name.startswith('CustomWeapons |')]


def same(actual, expected):
    if isinstance(actual, dict):
        assert actual.keys() == expected.keys(), (actual.keys(), expected.keys())
        for key, value in actual.items():
            same(value, expected[key])
    elif isinstance(actual, list):
        assert len(actual) == len(expected)
        for a, e in zip(actual, expected):
            same(a, e)
    elif isinstance(actual, float):
        assert math.isclose(actual, expected, rel_tol=1e-10, abs_tol=1e-10), (actual, expected)
    else:
        assert actual == expected, (actual, expected)


def main():
    manifest = json.loads((DATA / 'cssprofiler-20261110-inputs.json').read_text())
    summary = json.loads((DATA / 'cssprofiler-20261110-summary.json').read_text())
    archive_path = DATA / manifest['archive']
    assert hashlib.sha256(archive_path.read_bytes()).hexdigest() == manifest['archive_sha256']
    grouped = {}
    with zipfile.ZipFile(archive_path) as archive:
        assert set(archive.namelist()) == set(manifest['profiles'])
        for window in summary['windows']:
            name = window['name'] + '.speedscope.json'
            data = archive.read(name)
            assert hashlib.sha256(data).hexdigest() == manifest['profiles'][name]
            times, registered = parse(data)
            same({key: describe(value) for key, value in times.items()}, window['handlers'])
            same(registered, window['registered_labels'])
            total = sum(map(sum, times.values()))
            same(total, window['total_profiled_ms'])
            group = grouped.setdefault((window['label'], window['scenario']), dict(totals=[], times={}, registered=registered))
            same(registered, group['registered'])
            group['totals'].append(total)
            for handler, values in times.items():
                group['times'].setdefault(handler, []).extend(values)
    for aggregate in summary['aggregate']:
        group = grouped.pop((aggregate['label'], aggregate['scenario']))
        same(dict(label=aggregate['label'], scenario=aggregate['scenario'], windows=len(group['totals']),
                  registered_labels=group['registered'],
                  handlers={key: describe(value) for key, value in group['times'].items()},
                  mean_profiled_ms_per_30s=statistics.mean(group['totals']), total_profiled_ms=sum(group['totals'])), aggregate)
    assert not grouped
    print(f"Verified {len(summary['windows'])} raw exports, hashes, O/C stacks and all published distributions")


if __name__ == '__main__':
    main()
