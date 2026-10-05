"""Print an OpenCode session's final text message (CLAUDE.md rule 19).

Usage: python scripts/read-opencode-session.py <ses_...> [--all]

The id is the "opencode: session ses_... started" line of an external-implement.ps1 or
external-review.ps1 log. The database is opened read-only; auth.json beside it is never read.
"""
import json
import os
import re
import sqlite3
import sys

if len(sys.argv) < 2 or not re.fullmatch(r'ses_[A-Za-z0-9]+', sys.argv[1]):
    sys.exit(__doc__)
sys.stdout.reconfigure(encoding='utf-8')
root = os.environ.get('IC2_OPENCODE_DATA') or os.path.join(
    os.path.expanduser('~'), '.local', 'share', 'ic2-opencode-1x', 'data')
db = os.path.join(root, 'opencode', 'opencode.db').replace('\\', '/')
con = sqlite3.connect(f'file:{db}?mode=ro', uri=True)
rows = con.execute(
    'select data from part where session_id=? order by time_created', (sys.argv[1],)).fetchall()
texts = [d for d in (json.loads(r) for (r,) in rows) if d.get('type') == 'text']
if not texts:
    print('no text part')
elif '--all' in sys.argv:
    print('\n\n---\n\n'.join(t['text'] for t in texts))
else:
    print(texts[-1]['text'])
