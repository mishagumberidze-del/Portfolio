"""Independent raw-to-report checks for Olist commercial populations and cohort math."""
import csv
import hashlib
import json
import re
from collections import Counter, defaultdict
from datetime import datetime
from decimal import Decimal
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DATA=ROOT/'data/processed/powerbi'
def raw(name):
 with (ROOT/'data/raw'/f'olist_{name}_dataset.csv').open(encoding='utf-8',newline='') as f:return list(csv.DictReader(f))
def exported(name):
 with (DATA/f'{name}.tsv').open(encoding='utf-8',newline='') as f:return [[v.replace('\0','') for v in row] for row in csv.reader(f,delimiter='\t')]
manifest=json.loads((DATA/'manifest.json').read_text(encoding='utf-8-sig'))
for entry in manifest['exports']:
 assert hashlib.sha256((DATA/entry['file']).read_bytes()).hexdigest()==entry['sha256'],entry['file']
orders={r['order_id']:r for r in raw('orders')}
identity={r['customer_id']:r['customer_unique_id'] for r in raw('customers')}
customers={r[0]:r for r in exported('customer')}
out=exported('order');assert len(orders)==len(out)==99441
assert len({r[1] for r in out})==len(out)
commercial=defaultdict(list);completed=Counter();gmv=defaultdict(lambda:Decimal(0));freight=defaultdict(lambda:Decimal(0))
for r in orders.values():
 who=identity[r['customer_id']]
 if r['order_status'] not in ('canceled','unavailable'):commercial[who].append((r['order_purchase_timestamp'],r['order_id']))
 if r['order_status']=='delivered':completed[who]+=1
for r in raw('order_items'):
 gmv[r['order_id']]+=Decimal(r['price']);freight[r['order_id']]+=Decimal(r['freight_value'])
sequences={};cohorts={};activity=defaultdict(set)
for who,purchases in commercial.items():
 purchases.sort();cohorts[who]=purchases[0][0][:7]+'-01'
 for sequence,(stamp,oid) in enumerate(purchases,1):
  sequences[oid]=sequence;activity[cohorts[who],stamp[:7]+'-01'].add(who)
for r in out:
 source=orders[r[1]];who=identity[source['customer_id']]
 assert customers[r[3]][1]==who
 assert r[6]==source['order_status']
 assert datetime.fromisoformat(r[7])==datetime.fromisoformat(source['order_purchase_timestamp'])
 for col,key in [(10,'order_delivered_customer_date'),(11,'order_estimated_delivery_date')]:
  assert (datetime.fromisoformat(r[col]).date() if r[col] else None)==(datetime.fromisoformat(source[key]).date() if source[key] else None)
 assert (int(r[15]) if r[15] else None)==sequences.get(r[1])
 assert r[18]==(cohorts[who] if r[1] in sequences else '')
 assert Decimal(r[19])==gmv[r[1]] and Decimal(r[20])==freight[r[1]]
for r in customers.values():
 who=r[1];n=len(commercial[who]);done=completed[who]
 assert int(r[8])==n and int(r[12])==done
 assert r[18]==('No completed purchases' if done==0 else f'{done} completed purchase'+('s' if done==2 else '') if done<3 else '3+ completed purchases')
def next_month(s):
 y,m=map(int,s[:7].split('-'));return f'{y+(m==12):04d}-{m%12+1:02d}-01'
first=next_month(min(cohorts.values()))
last=max(stamp for purchases in commercial.values() for stamp,_ in purchases)[:7]+'-01'
sizes=Counter(cohorts.values());expected={}
for cohort,n in sizes.items():
 if cohort<first:continue
 month=cohort;age=0
 while month<last:
  expected[cohort,month]=(age,len(activity[cohort,month]),n)
  age+=1;month=next_month(month)
actual={}
for c,m,age,n,den,rate in exported('cohort_retention'):
 actual[c,m]=(int(age),int(n),int(den))
 assert abs(Decimal(rate)-Decimal(n)/Decimal(den))<=Decimal('.0000005')
assert actual==expected,'Cohort cells disagree with original commercial orders'
for r in exported('review'):
 score=int(r[3]);assert r[4]==('1-2 stars' if score<=2 else '3 stars' if score==3 else '4-5 stars')

model=ROOT/'powerbi/OlistEcommercePBIP/OlistEcommerce.SemanticModel/definition/tables/KPI Measures.tmdl'
text=model.read_text(encoding='utf-8-sig')
measures=set(a or b for a,b in re.findall(r"^\tmeasure (?:'([^']+)'|(\w+)) =",text,re.M))
reference=set(re.findall(r'^(.+?) :=', (ROOT/'powerbi/measures.dax').read_text(encoding='utf-8-sig'),re.M))
assert measures==reference,'DAX reference measure names differ from executable model'
pages=ROOT/'powerbi/OlistEcommercePBIP/OlistEcommerce.Report/definition/pages'
def check_fields(value):
 if isinstance(value,dict):
  if 'Measure' in value:assert value['Measure']['Property'] in measures,value['Measure']['Property']
  for child in value.values():check_fields(child)
 elif isinstance(value,list):
  for child in value:check_fields(child)
visuals=list(pages.glob('*/visuals/*/visual.json'))
for path in visuals:
 v=json.loads(path.read_text(encoding='utf-8-sig'));pos=v['position'];check_fields(v)
 assert 0<=pos['x'] and pos['x']+pos['width']<=1920
 assert 0<=pos['y'] and pos['y']+pos['height']<=1080
executive=json.loads((pages/'pages.json').read_text())['pageOrder'][0]
cards=[json.loads(p.read_text()) for p in (pages/executive/'visuals').glob('*/visual.json')]
headlines=[m['field']['Measure']['Property'] for v in cards if v['visual']['visualType']=='cardVisual' for m in v['visual']['query']['queryState']['Data']['projections']]
assert headlines==['Sales / GMV','Sales Orders','Average Order Value','Cancellation Rate','On-Time Delivery %','Average Review Score'] or set(headlines)=={'Sales / GMV','Sales Orders','Average Order Value','Cancellation Rate','On-Time Delivery %','Average Review Score'}
summary={'orders':len(out),'commercial_orders':len(sequences),'commercial_customers':len(cohorts),'repeat_commercial_customers':sum(len(p)>1 for p in commercial.values()),'cohort_cells':len(actual),'measures':len(measures),'visuals':len(visuals),'executive_kpis':len(headlines)}
print(json.dumps(summary,indent=2))
print('PASS: source identity, order facts, value, commercial sequences, customer groups, cohorts, export hashes and report field/boundary checks')
