"""Independently reconcile all raw TLC files to dashboard metrics and route rankings.

Uses NumPy calendar arithmetic/bincount rather than the ETL's pandas groupbys.
Read-only: no SQL changes, no rewriting sources or checkpoints.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path

import numpy as np
import pyarrow.parquet as pq

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--raw-dir',type=Path)
args=parser.parse_args()
config=ROOT/'source.local.json'
raw=args.raw_dir or (Path(json.loads(config.read_text())['raw_dir']) if config.exists() else ROOT/'data/raw')
data=ROOT/'data/processed/dashboard'
manifest=json.loads((ROOT/'data/processed/analytics_manifest.json').read_text())
fields={
 1:('tpep_pickup_datetime','tpep_dropoff_datetime','PULocationID','DOLocationID','trip_distance','fare_amount'),
 2:('lpep_pickup_datetime','lpep_dropoff_datetime','PULocationID','DOLocationID','trip_distance','fare_amount'),
 3:('pickup_datetime','dropOff_datetime','PUlocationID','DOlocationID',None,None),
 4:('pickup_datetime','dropoff_datetime','PULocationID','DOLocationID','trip_miles','base_passenger_fare'),
}
def read(name):
 with (data/f'{name}.tsv').open(encoding='utf-8',newline='') as f:return list(csv.reader(f,delimiter='\t'))

time=np.zeros((4,12*7*24,10))
routes=np.zeros((4,12,266,266),dtype=np.int64)
bands=np.zeros((4,12,2,5),dtype=np.int64)
quality={}
zone_ids=np.array([int(row[0]) for row in read('zones') if int(row[0])!=0])
month_origin=np.datetime64('2024-01','M').astype(int)
source_count=0
for source in manifest['files']:
 service=source['service_id'];pfield,dfield,pufield,dofield,mfield,ffield=fields[service]
 path=raw/source['service']/source['filename']
 with path.open('rb') as stream:
  assert hashlib.file_digest(stream,'sha256').hexdigest()==source['sha256'],path.name
 observed=0;in_year_count=0;flags={}
 for batch in pq.ParquetFile(path).iter_batches(batch_size=500000,columns=[f for f in fields[service] if f]):
  def column(name):return batch.column(batch.schema.get_field_index(name)).to_numpy(zero_copy_only=False)
  pickup=column(pfield).astype('datetime64[us]');dropoff=column(dfield).astype('datetime64[us]')
  keep=(pickup>=np.datetime64('2024-01-01'))&(pickup<np.datetime64('2025-01-01'))
  duration=(dropoff-pickup)/np.timedelta64(1,'s')
  miles=column(mfield).astype(float) if mfield else np.full(len(batch),np.nan)
  fare=column(ffield).astype(float) if ffield else np.full(len(batch),np.nan)
  dur_ok=np.isfinite(duration)&(duration>0)&(duration<=14400)
  dist_ok=np.isfinite(miles)&(miles>0)&(miles<=200)
  with np.errstate(divide='ignore',invalid='ignore'):
   speed_ok=dur_ok&dist_ok&(miles*3600/duration<=100)
  fare_ok=np.isfinite(fare)&(fare>=0)&(fare<=1000)
  pu=column(pufield);do=column(dofield)
  pu_ok=np.isin(pu,zone_ids);do_ok=np.isin(do,zone_ids)
  checks={'outside_2024_pickup':~keep,'invalid_duration':~dur_ok,
   'unmatched_pickup_zone':~pu_ok,'unmatched_dropoff_zone':~do_ok}
  if mfield:checks.update(invalid_distance=~dist_ok,speed_above_100mph=dur_ok&dist_ok&~speed_ok)
  if ffield:checks['invalid_base_fare']=~fare_ok
  for check,mask in checks.items():flags[check]=flags.get(check,0)+int(mask.sum())
  dates=pickup[keep]
  month=dates.astype('datetime64[M]').astype(int)-month_origin
  dow=(dates.astype('datetime64[D]').astype(int)+3)%7
  hour=dates.astype('datetime64[h]').astype(int)%24
  key=(month*7+dow)*24+hour
  values=[np.ones(len(batch)),dur_ok,np.where(dur_ok,duration,0),dist_ok,np.where(dist_ok,miles,0),
   speed_ok,np.where(speed_ok,miles,0),np.where(speed_ok,duration,0),fare_ok,np.where(fare_ok,fare,0)]
  for i,value in enumerate(values):time[service-1,:,i]+=np.bincount(key,weights=value[keep],minlength=12*7*24)
  pu=np.where(pu_ok,pu,0).astype(int)[keep];do=np.where(do_ok,do,0).astype(int)[keep]
  routes[service-1]+=np.bincount(month*266*266+pu*266+do,minlength=12*266*266).reshape(12,266,266)
  for metric,(value,valid,edges) in enumerate([(duration/60,dur_ok,[10,20,40,60]),(miles,dist_ok,[1,3,5,10])]):
   valid=valid[keep];band=np.searchsorted(edges,value[keep][valid],side='left')
   bands[service-1,:,metric,:]+=np.bincount(month[valid]*5+band,minlength=60).reshape(12,5)
  observed+=len(batch);in_year_count+=int(keep.sum())
 assert observed==source['rows'] and in_year_count==source['in_year_rows'],path.name
 for check,n in flags.items():
  assert n==source['flags'][check],(path.name,check,n,source['flags'][check])
  quality[service,source['partition'],check]=n
 source_count+=observed
 print(f'{source["file_id"]:02d}/48 verified {path.name}: {observed:,} source rows',flush=True)

assert source_count==manifest['source_rows']
export_time=np.zeros_like(time)
for row in read('time'):
 service,month,dow,hour=map(int,row[:4])
 export_time[service-1,((month%100-1)*7+dow)*24+hour]=list(map(float,row[4:]))
for i in [0,1,3,5,8]:assert np.array_equal(time[:,:,i],export_time[:,:,i]),f'Count column {i}'
for i in [2,4,6,7,9]:
 # Exports round sums to three decimals; permit that rounding plus floating accumulation.
 assert np.allclose(time[:,:,i],export_time[:,:,i],rtol=0,atol=.002),(i,float(np.max(np.abs(time[:,:,i]-export_time[:,:,i]))))
for service,month,zone,role,n in read('zone'):
 r=routes[int(service)-1,int(month)%100-1]
 actual=r[int(zone),:].sum() if role=='PU' else r[:,int(zone)].sum()
 assert actual==int(n),(service,month,zone,role)
export_bands=np.zeros_like(bands)
for service,month,metric,band,n in read('band'):
 export_bands[int(service)-1,int(month)%100-1,int(metric=='distance'),int(band[:2])-1]=int(n)
assert np.array_equal(bands,export_bands)
for service,month,check,n in read('quality'):
 if check!='projected_duplicate_candidates':assert quality[int(service),int(month),check]==int(n)
states={}
for service in range(5):
 for month in [0]+list(range(202401,202413)):
  r=routes[service-1:service] if service else routes
  r=r[:,month%100-1:month%100] if month else r
  r=r.sum(axis=(0,1))[1:264,1:264]
  flat=r.ravel();ids=np.arange(flat.size)
  ranked=np.lexsort((ids,-flat))[:10]
  states[service,month]=[(int(i//263+1),int(i%263+1),int(flat[i])) for i in ranked]
for service,month,pu,do,n,rank in read('routes'):
 assert states[int(service),int(month)][int(rank)-1]==(int(pu),int(do),int(n)),(service,month,rank)
print(f'PASS: all {source_count:,} raw rows, 48 hashes, metric numerators/denominators, zones, bands, quality and 65 route rankings')
