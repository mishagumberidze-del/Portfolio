"""Create a temporary browser test page, using independent SQL-export count expectations."""
import csv
import json
import calendar
from collections import defaultdict
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
expected={f'{s}-{m}':{'n':0,'dn':0,'ds':0,'mn':0,'mi':0,'sm':0,'ss':0,'monday0':0} for s in range(5) for m in [0]+list(range(202401,202413))}
with (ROOT/'data/processed/dashboard/time.tsv').open(encoding='utf-8',newline='') as source:
    for row in csv.reader(source,delimiter='\t'):
        service,month,count=int(row[0]),int(row[1]),int(row[4])
        for s,m in [(0,0),(0,month),(service,0),(service,month)]:
            e=expected[f'{s}-{m}'];e['n']+=count
            for key,col in [('dn',5),('ds',6),('mn',7),('mi',8),('sm',10),('ss',11)]:e[key]+=float(row[col])
            if row[2]=='0' and row[3]=='0':e['monday0']+=count
for key,e in expected.items():
    month=int(key.split('-')[1]);months=[month%100] if month else list(range(1,13))
    e['days']=sum(calendar.monthrange(2024,m)[1] for m in months)
    e['mondays']=sum(week[0]!=0 for m in months for week in calendar.monthcalendar(2024,m))
sources=list(csv.reader((ROOT/'data/processed/dashboard/source.tsv').open(encoding='utf-8'),delimiter='\t'))
for key,e in expected.items():
    s,m=map(int,key.split('-'))
    e['distanceQualityDenominator']=sum(int(r[5]) for r in sources if (not s or int(r[1])==s) and (not m or int(r[2])==m) and r[1]!='3')
script=(ROOT/'tests/browser_qa.js').read_text(encoding='utf-8')
extra=f'<script id="qa-expected" type="application/json">{json.dumps(expected)}</script><script>{script}</script>'
html=(ROOT/'site/index.html').read_text(encoding='utf-8').replace('</body>',extra+'</body>')
destination=ROOT/'data/processed/dashboard/browser-qa.html'
destination.write_text(html,encoding='utf-8')
print(destination)
