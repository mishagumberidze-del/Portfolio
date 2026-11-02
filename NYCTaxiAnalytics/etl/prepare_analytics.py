"""Stream every TLC record into validated, file-traceable analytical aggregates.

Trip-level originals remain Parquet. The SQL model intentionally has aggregate grain
to fit SQL Server Express. No raw trip records are deduplicated or silently dropped.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import time
from pathlib import Path
import numpy as np
import pandas as pd
import pyarrow.parquet as pq

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--raw-dir',type=Path)
parser.add_argument('--batch-size',type=int,default=250000)
parser.add_argument('--force',action='store_true')
args=parser.parse_args()
config=ROOT/'source.local.json'
RAW=args.raw_dir or (Path(json.loads(config.read_text())['raw_dir']) if config.exists() else ROOT/'data/raw')
OUT=ROOT/'data/processed'; OUT.mkdir(parents=True,exist_ok=True)
CHECK=OUT/'checkpoints-v1'; CHECK.mkdir(exist_ok=True)
VERSION='1.0.0'
MAP={
 'yellow':('tpep_pickup_datetime','tpep_dropoff_datetime','PULocationID','DOLocationID','trip_distance','fare_amount'),
 'green':('lpep_pickup_datetime','lpep_dropoff_datetime','PULocationID','DOLocationID','trip_distance','fare_amount'),
 'fhv':('pickup_datetime','dropOff_datetime','PUlocationID','DOlocationID',None,None),
 'hvfhv':('pickup_datetime','dropoff_datetime','PULocationID','DOLocationID','trip_miles','base_passenger_fare'),
}
zones=pd.read_csv(RAW/'reference/taxi_zone_lookup.csv',keep_default_na=False)
zone_ids=set(zones.LocationID)
manifest=json.loads((ROOT/'reports/tlc_2024/acquisition_manifest.json').read_text())
acquired={x['relative_path']:x for x in manifest['files']}
TIME_KEYS=['MonthKey','Weekday','Hour']
MEASURES=['TripCount','DurationCount','DurationSeconds','DistanceCount','DistanceMiles','SpeedCount','SpeedMiles','SpeedSeconds','FareCount','FareAmount']
schemas={
 'time':(TIME_KEYS,MEASURES),
 'zone':(['MonthKey','ZoneId','Role'],['TripCount']),
 'route':(['MonthKey','PickupZoneId','DropoffZoneId'],['TripCount']),
 'band':(['MonthKey','Metric','Band'],['TripCount'])}
def sha(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for b in iter(lambda:f.read(8*1024*1024),b''): h.update(b)
    return h.hexdigest()
def combine(parts,keys,values):
    return pd.concat(parts,ignore_index=True).groupby(keys,sort=True,dropna=False,as_index=False)[values].sum()
sources=[]; all_parts={key:[] for key in schemas}; quality=[]; samples=[]
started=time.time()
for service_id,(service,fields) in enumerate(MAP.items(),1):
    files=sorted((RAW/service).glob('*2024-*.parquet'))
    if len(files)!=12: raise ValueError(f'{service}: expected 12 source files; found {len(files)}')
    for path in files:
        partition=int(path.stem[-7:].replace('-',''))
        file_id=(service_id-1)*12+partition%100
        checkpoint=CHECK/f'{file_id:02d}'
        checkpoint.mkdir(exist_ok=True)
        meta_path=checkpoint/'profile.json'
        current_sha=sha(path)
        relative=f'{service}/{path.name}'
        pin=acquired[relative]
        if current_sha!=pin['sha256']: raise ValueError(f'Source hash changed: {relative}')
        if meta_path.exists() and not args.force:
            profile=json.loads(meta_path.read_text())
            cached=profile.get('sha256')==current_sha and profile.get('version')==VERSION
        else: cached=False
        if not cached:
            parts={key:[] for key in schemas}; flags={}; flag_samples=[]; rows=0; kept=0
            pickup,dropoff,pu,do,distance,fare=fields
            selected=[x for x in fields if x]
            parquet=pq.ParquetFile(path)
            if parquet.metadata.num_rows!=pin['rows']: raise ValueError('Source row count changed')
            for batch in parquet.iter_batches(batch_size=args.batch_size,columns=selected):
                raw=batch.to_pandas()
                p=pd.to_datetime(raw[pickup],errors='coerce'); d=pd.to_datetime(raw[dropoff],errors='coerce')
                seconds=(d-p).dt.total_seconds()
                in_year=p.ge(pd.Timestamp('2024-01-01')) & p.lt(pd.Timestamp('2025-01-01'))
                duration_ok=seconds.gt(0)&seconds.le(14400)
                miles=pd.to_numeric(raw[distance],errors='coerce') if distance else pd.Series(np.nan,index=raw.index)
                fare_values=pd.to_numeric(raw[fare],errors='coerce') if fare else pd.Series(np.nan,index=raw.index)
                distance_ok=miles.gt(0)&miles.le(200)
                speed_ok=duration_ok&distance_ok&(miles*3600/seconds).le(100)
                fare_ok=fare_values.ge(0)&fare_values.le(1000)
                pu_values=pd.to_numeric(raw[pu],errors='coerce'); do_values=pd.to_numeric(raw[do],errors='coerce')
                pu_ok=pu_values.isin(zone_ids); do_ok=do_values.isin(zone_ids)
                checks={'outside_2024_pickup':~in_year,'invalid_duration':~duration_ok,
                    'unmatched_pickup_zone':~pu_ok,'unmatched_dropoff_zone':~do_ok,
                    'projected_duplicate_candidates':raw.duplicated()}
                if distance:
                    checks['invalid_distance']=~distance_ok
                    checks['speed_above_100mph']=duration_ok&distance_ok&~speed_ok
                if fare: checks['invalid_base_fare']=~fare_ok
                for name,mask in checks.items():
                    flags[name]=flags.get(name,0)+int(mask.sum())
                    if len([x for x in flag_samples if x['check']==name])<5:
                        for row in np.flatnonzero(mask.to_numpy())[:5]:
                            flag_samples.append({'check':name,'file_row':rows+int(row)+1})
                t=pd.DataFrame({'MonthKey':p.dt.year*100+p.dt.month,'Weekday':p.dt.dayofweek,'Hour':p.dt.hour,
                    'TripCount':1,'DurationCount':duration_ok.astype(int),'DurationSeconds':seconds.where(duration_ok,0),
                    'DistanceCount':distance_ok.astype(int),'DistanceMiles':miles.where(distance_ok,0),
                    'SpeedCount':speed_ok.astype(int),'SpeedMiles':miles.where(speed_ok,0),'SpeedSeconds':seconds.where(speed_ok,0),
                    'FareCount':fare_ok.astype(int),'FareAmount':fare_values.where(fare_ok,0)})
                t=t.loc[in_year].copy()
                t[TIME_KEYS]=t[TIME_KEYS].astype(int)
                parts['time'].append(t.groupby(TIME_KEYS,as_index=False)[MEASURES].sum())
                pu_clean=pu_values.where(pu_ok,0).astype(int)
                do_clean=do_values.where(do_ok,0).astype(int)
                r=pd.DataFrame({'MonthKey':t.MonthKey,'PickupZoneId':pu_clean[in_year],
                    'DropoffZoneId':do_clean[in_year],'TripCount':1})
                parts['route'].append(r.groupby(schemas['route'][0],as_index=False).TripCount.sum())
                for role,col in [('PU','PickupZoneId'),('DO','DropoffZoneId')]:
                    z=r.groupby(['MonthKey',col],as_index=False).TripCount.sum().rename(columns={col:'ZoneId'})
                    z['Role']=role;parts['zone'].append(z)
                for metric,values,ok,bins,labels in [
                    ('duration',seconds/60,duration_ok,[0,10,20,40,60,240],['01_under10','02_10to20','03_20to40','04_40to60','05_60plus']),
                    ('distance',miles,distance_ok,[0,1,3,5,10,200],['01_under1','02_1to3','03_3to5','04_5to10','05_10plus'])]:
                    if metric=='distance' and not distance:continue
                    band=pd.cut(values,bins=bins,labels=labels,include_lowest=True,right=True)
                    b=pd.DataFrame({'MonthKey':t.MonthKey,'Band':band.loc[in_year].astype(str),'TripCount':1})
                    b=b.loc[ok.loc[in_year]]
                    b=b.groupby(['MonthKey','Band'],as_index=False).TripCount.sum();b['Metric']=metric
                    parts['band'].append(b)
                rows+=len(raw);kept+=int(in_year.sum())
            for key,(keys,values) in schemas.items():
                grouped=combine(parts[key],keys,values)
                grouped.insert(0,'FileId',file_id)
                grouped.to_parquet(checkpoint/f'{key}.parquet',index=False)
            profile={'version':VERSION,'file_id':file_id,'service_id':service_id,'service':service,
                'partition':partition,'filename':path.name,'sha256':current_sha,'rows':rows,'in_year_rows':kept,
                'flags':flags,'flag_samples':flag_samples}
            if rows!=pin['rows']:raise ValueError('Decoded row count differs')
            meta_path.write_text(json.dumps(profile,indent=2),encoding='utf-8')
        else: profile=json.loads(meta_path.read_text())
        for key in schemas: all_parts[key].append(pd.read_parquet(checkpoint/f'{key}.parquet'))
        sources.append(profile)
        quality.extend([[file_id,name,count] for name,count in profile['flags'].items()])
        samples.extend([dict(x,file_id=file_id,filename=path.name) for x in profile['flag_samples']])
        print(f'{file_id:02d}/48 {service} {partition}: {profile["rows"]:,} rows; {profile["in_year_rows"]:,} in 2024; {"cached" if cached else "profiled"}; elapsed {time.time()-started:.0f}s',flush=True)

for key,(keys,values) in schemas.items():
    df=pd.concat(all_parts[key],ignore_index=True)[['FileId']+keys+values]
    if df.duplicated(['FileId']+keys).any():raise ValueError(f'Duplicate aggregate key in {key}')
    if key=='time' and int(df.TripCount.sum())!=sum(s['in_year_rows'] for s in sources):raise ValueError('Aggregate row reconciliation failed')
    df.to_csv(OUT/f'{key}.tsv',sep='\t',header=False,index=False,lineterminator='\n',float_format='%.8f')
pd.DataFrame(quality,columns=['FileId','CheckName','FailedRows']).to_csv(OUT/'quality.tsv',sep='\t',header=False,index=False,lineterminator='\n')
source_rows=[[s[k] for k in ['file_id','service_id','partition','filename','sha256','rows','in_year_rows']] for s in sources]
pd.DataFrame(source_rows).to_csv(OUT/'source.tsv',sep='\t',header=False,index=False,lineterminator='\n')
zones=zones.rename(columns={'LocationID':'ZoneId','Borough':'BoroughName','Zone':'ZoneName','service_zone':'ServiceZone'})
zones=pd.concat([pd.DataFrame([[0,'Unknown','Unmatched or null','Unknown']],columns=zones.columns),zones])
zones.to_csv(OUT/'zones.tsv',sep='\t',header=False,index=False,lineterminator='\n')
summary={'pipeline_version':VERSION,'source_root':str(RAW),'source_rows':sum(s['rows'] for s in sources),
    'in_year_rows':sum(s['in_year_rows'] for s in sources),'files':sources,'elapsed_seconds':round(time.time()-started,1)}
(OUT/'analytics_manifest.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
(ROOT/'docs/flag_samples.json').write_text(json.dumps(samples,indent=2),encoding='utf-8')
print(f'COMPLETE {summary["source_rows"]:,} source rows -> {summary["in_year_rows"]:,} in-year records',flush=True)
