"""Validate dashboard grains, metric denominators, and deterministic HTML rebuilding."""
import csv
import hashlib
import json
import subprocess
import sys
from collections import defaultdict
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'data/processed'
def rows(name):
    with (OUT/'dashboard'/f'{name}.tsv').open(encoding='utf-8',newline='') as f:return list(csv.reader(f,delimiter='\t'))
manifest=json.loads((OUT/'analytics_manifest.json').read_text(encoding='utf-8'))
assert len(manifest['files'])==48
assert manifest['source_rows']==298930712 and manifest['in_year_rows']==298930636
assert sum(f['flags']['outside_2024_pickup'] for f in manifest['files'])==76
by_service_month=defaultdict(int);duration=defaultdict(int);distance=defaultdict(int)
for r in rows('time'):
    s,m,d,h,n,dn,ds,mn,mi,sn,sm,ss,fn,fare=map(float,r)
    assert 0<=d<=6 and 0<=h<=23 and n>0
    assert 0<=dn<=n and 0<=mn<=n and 0<=sn<=min(dn,mn) and 0<=fn<=n
    assert 0<=ds<=dn*14400+.001 and 0<=mi<=mn*200+.001
    if s==3:assert mn==0 and fn==0
    by_service_month[int(s),int(m)]+=int(n)
    duration[int(s),int(m)]+=int(dn);distance[int(s),int(m)]+=int(mn)
assert sum(by_service_month.values())==manifest['in_year_rows']
zone=defaultdict(int)
for s,m,z,role,n in rows('zone'):zone[int(s),int(m),role]+=int(n)
for key,count in by_service_month.items():
    for role in ['PU','DO']:assert zone[*key,role]==count
band=defaultdict(int)
for s,m,metric,label,n in rows('band'):band[int(s),int(m),metric]+=int(n)
for key in by_service_month:
    assert band[*key,'duration']==duration[key]
    assert band[*key,'distance']==distance[key]
rankings=defaultdict(list)
for s,m,pu,do,n,rank in rows('routes'):
    assert 1<=int(pu)<=263 and 1<=int(do)<=263 and int(n)>0
    rankings[int(s),int(m)].append(int(rank))
assert len(rankings)==65 and all(sorted(v)==list(range(1,11)) for v in rankings.values())
index=ROOT/'site/index.html'
before=hashlib.sha256(index.read_bytes()).hexdigest()
subprocess.run([sys.executable,str(ROOT/'etl/build_dashboard.py')],check=True,stdout=subprocess.DEVNULL)
assert before==hashlib.sha256(index.read_bytes()).hexdigest(),'Non-deterministic dashboard build'
print('PASS: counts, metric denominators, zone roles, bands, all route states, deterministic HTML')
