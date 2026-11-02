"""Create a temporary browser test page, using independent SQL-export count expectations."""
import csv
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
expected={f'{s}-{m}':0 for s in range(5) for m in [0]+list(range(202401,202413))}
with (ROOT/'data/processed/dashboard/time.tsv').open(encoding='utf-8',newline='') as source:
    for row in csv.reader(source,delimiter='\t'):
        service,month,count=int(row[0]),int(row[1]),int(row[4])
        for s,m in [(0,0),(0,month),(service,0),(service,month)]:expected[f'{s}-{m}']+=count
script=(ROOT/'tests/browser_qa.js').read_text(encoding='utf-8')
extra=f'<script id="qa-expected" type="application/json">{json.dumps(expected)}</script><script>{script}</script>'
html=(ROOT/'site/index.html').read_text(encoding='utf-8').replace('</body>',extra+'</body>')
destination=ROOT/'data/processed/dashboard/browser-qa.html'
destination.write_text(html,encoding='utf-8')
print(destination)
