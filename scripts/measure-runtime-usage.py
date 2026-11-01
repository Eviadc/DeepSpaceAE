"""Read only token_count metadata from this chat's local Codex rollout; never export messages."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--baseline', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
baseline = json.loads(args.baseline.read_text(encoding='utf-8'))
start = baseline['baseline']
if not start:
    raise SystemExit('No baseline token_count; usage unavailable, not zero.')
latest = None
with Path(baseline['session']).open(encoding='utf-8') as source:
    for line in source:
        try:
            row = json.loads(line)
        except ValueError:
            continue
        payload = row.get('payload', {})
        if row.get('type') == 'event_msg' and payload.get('type') == 'token_count' and payload.get('info'):
            latest = {'timestamp': row.get('timestamp'), 'info': payload['info']}
if not latest:
    raise SystemExit('No current token_count; usage unavailable, not zero.')
a = start['info']['total_token_usage']
b = latest['info']['total_token_usage']
delta = {key: b.get(key, 0) - a.get(key, 0) for key in set(a) | set(b)}
if any(value < 0 for value in delta.values()):
    raise SystemExit('Counters reset; do not report an invalid difference.')
result = {
    'scope': 'Current plugin-pilot task, through last locally recorded model call; not a monetary bill.',
    'taskStartUtc': baseline.get('taskStart', {}).get('timestamp'),
    'counterBaselineUtc': start['timestamp'], 'endUtc': latest['timestamp'], 'delta': delta,
    'nonCachedInputTokens': delta['input_tokens'] - delta.get('cached_input_tokens', 0),
    'notes': ['Cached input is included in input_tokens.',
              'Reasoning is included in output_tokens; do not add it again.',
              'Final reply and tool observations after this sample are excluded.',
              'This long existing chat has substantial cached-context input.'],
}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result, ensure_ascii=False))
