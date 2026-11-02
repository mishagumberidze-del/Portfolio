"""Bundle SQL aggregates and the official projected zone polygons into one offline HTML file."""
from __future__ import annotations
import csv
import json
import math
import struct
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DATA=ROOT/'data/processed/dashboard'
columns={
 'time':['service','month','dow','hour','n','durationN','durationS','distanceN','distanceMi','speedN','speedMi','speedS','fareN','fare'],
 'zone':['service','month','zone','role','n'],
 'band':['service','month','metric','band','n'],
 'quality':['service','month','check','n'],
 'source':['id','service','month','file','sha','n','inYear'],
 'zones':['id','borough','name','serviceZone'],
 'routes':['service','month','pickup','dropoff','n','rank']}
text={'role','metric','band','check','file','sha','borough','name','serviceZone'}
tables={}
for key,names in columns.items():
    with (DATA/f'{key}.tsv').open(encoding='utf-8',newline='') as file:
        rows=list(csv.reader(file,delimiter='\t'))
    if any(len(row)!=len(names) for row in rows):raise ValueError(f'Invalid SQL export width: {key}')
    tables[key]={'columns':names,'rows':[[v if col in text else float(v) if '.' in v else int(v) for col,v in zip(names,row)] for row in rows]}
manifest=json.loads((ROOT/'data/processed/analytics_manifest.json').read_text())
assert sum(r[4] for r in tables['time']['rows'])==manifest['in_year_rows']
for role in ['PU','DO']:
    assert sum(r[4] for r in tables['zone']['rows'] if r[3]==role)==manifest['in_year_rows']
assert sum(r[5] for r in tables['source']['rows'])==manifest['source_rows']

# Read the official SHP/DBF pair directly. Retain its projected NY Long Island
# coordinates; the SVG is a local projected choropleth, not a lon/lat basemap.
reference=ROOT/'data/raw/reference/taxi_zones.zip'
with zipfile.ZipFile(reference) as archive:
    shp=archive.read(next(n for n in archive.namelist() if n.endswith('.shp')))
    dbf=archive.read(next(n for n in archive.namelist() if n.endswith('.dbf')))
records=struct.unpack_from('<I',dbf,4)[0]
header_size,record_size=struct.unpack_from('<HH',dbf,8)
fields=[];offset=1;pos=32
while dbf[pos]!=13:
    name=dbf[pos:pos+11].split(b'\x00')[0].decode('ascii')
    length=dbf[pos+16];fields.append((name,offset,length));offset+=length;pos+=32
location_field=next(f for f in fields if f[0].lower()=='locationid')
ids=[]
for i in range(records):
    start=header_size+i*record_size
    ids.append(int(dbf[start+location_field[1]:start+location_field[1]+location_field[2]].strip()))
xmin,ymin,xmax,ymax=struct.unpack_from('<4d',shp,36)
scale=1000/(xmax-xmin);height=(ymax-ymin)*scale
def simplify(points,tolerance=120):
    if len(points)<4:return points
    keep={0,len(points)-1};stack=[(0,len(points)-1)]
    while stack:
        a,b=stack.pop();x1,y1=points[a];x2,y2=points[b];dx=x2-x1;dy=y2-y1
        norm=dx*dx+dy*dy
        far=-1;index=-1
        for j in range(a+1,b):
            px,py=points[j]
            t=max(0,min(1,((px-x1)*dx+(py-y1)*dy)/norm)) if norm else 0
            distance=(px-x1-t*dx)**2+(py-y1-t*dy)**2
            if distance>far:far=distance;index=j
        if far>tolerance*tolerance:
            keep.add(index);stack.extend([(a,index),(index,b)])
    return [points[i] for i in sorted(keep)]
geometry=[];pos=100;record_index=0
while pos<len(shp):
    number,length=struct.unpack_from('>2i',shp,pos);pos+=8
    body=shp[pos:pos+length*2];pos+=length*2
    shape_type=struct.unpack_from('<i',body,0)[0]
    if shape_type==0:record_index+=1;continue
    if shape_type!=5:raise ValueError(f'Expected polygon shapefile; found {shape_type}')
    part_count,point_count=struct.unpack_from('<2i',body,36)
    parts=list(struct.unpack_from(f'<{part_count}i',body,44))+[point_count]
    points=[struct.unpack_from('<2d',body,44+4*part_count+16*i) for i in range(point_count)]
    paths=[]
    for a,b in zip(parts,parts[1:]):
        ring=simplify(points[a:b])
        paths.append('M'+'L'.join(f'{(x-xmin)*scale:.2f},{(ymax-y)*scale:.2f}' for x,y in ring)+'Z')
    geometry.append({'id':ids[record_index],'d':''.join(paths)})
    record_index+=1
assert len(geometry)==263 and len({x['id'] for x in geometry})==263
payload={'tables':tables,'geometry':geometry,'mapHeight':height,'meta':{
    'sourceRows':manifest['source_rows'],'inYearRows':manifest['in_year_rows'],'sourceFiles':48,
    'year':2024,'source':'https://www.nyc.gov/site/tlc/about/tlc-trip-record-data.page',
    'version':manifest['pipeline_version'],'geometryCRS':'NAD83 / New York Long Island (US survey feet)'}}
serialized=json.dumps(payload,separators=(',',':'),ensure_ascii=False).replace('<','\\u003c')
template=(ROOT/'site/template.html').read_text(encoding='utf-8')
html=template.replace('/*__APP_CSS__*/',(ROOT/'site/dashboard.css').read_text(encoding='utf-8-sig')).replace('/*__APP_JS__*/',(ROOT/'site/dashboard.js').read_text(encoding='utf-8-sig')).replace('__DASHBOARD_DATA__',serialized)
(ROOT/'site/index.html').write_text(html,encoding='utf-8')
summary={'source_rows':manifest['source_rows'],'in_year_rows':manifest['in_year_rows'],
    'excluded_outside_2024':manifest['source_rows']-manifest['in_year_rows'],
    'source_files':48,'sql_time_cells':len(tables['time']['rows']),'sql_zone_cells':len(tables['zone']['rows']),
    'dashboard_bytes':len(html.encode()),'map_features':len(geometry)}
(ROOT/'docs/dashboard_summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
time_rows=[dict(zip(columns['time'],row)) for row in tables['time']['rows']]
zone_names={r[0]:r[2] for r in tables['zones']['rows']}
service_names={1:'Yellow taxi',2:'Green taxi',3:'FHV',4:'High-volume FHV'}
findings=['# Verified findings','',
 f'- All 48 source files contributed {manifest["source_rows"]:,} records; {manifest["in_year_rows"]:,} pickups fall in 2024 and {summary["excluded_outside_2024"]} fall outside it.',
 '- Counts below are source submissions, not a count of all real-world transport activity.','',
 '| Service | 2024 pickup records | Mean eligible duration |',
 '|---|---:|---:|']
for service,name in service_names.items():
    rows=[r for r in time_rows if r['service']==service]
    count=sum(r['n'] for r in rows);dn=sum(r['durationN'] for r in rows);ds=sum(r['durationS'] for r in rows)
    findings.append(f'| {name} | {count:,} | {ds/dn/60:.2f} min |')
month_counts={month:sum(r['n'] for r in time_rows if r['month']==month) for month in range(202401,202413)}
peak=max(month_counts,key=month_counts.get)
hour_counts={hour:sum(r['n'] for r in time_rows if r['hour']==hour) for hour in range(24)}
peak_hour=max(hour_counts,key=hour_counts.get)
zone_counts={}
for row in tables['zone']['rows']:
    if row[3]=='PU' and row[2] in range(1,264):zone_counts[row[2]]=zone_counts.get(row[2],0)+row[4]
top_zone=max(zone_counts,key=zone_counts.get)
findings+=['',f'- The highest monthly pickup count is {peak//100}-{peak%100:02d}: {month_counts[peak]:,}. These are monthly totals, not day-normalized demand.',
 f'- The busiest pickup hour across all services begins at {peak_hour:02d}:00 NYC local clock time.',
 f'- The leading mapped pickup zone is {zone_names[top_zone]} with {zone_counts[top_zone]:,} records.',
 f'- {sum(zone_counts.values())/manifest["in_year_rows"]:.2%} of in-year pickups map to one of the 263 physical zones. Unknown/outside-NYC IDs remain in overall totals.',
 '', '## Actions to evaluate', '',
 '- Compare hourly patterns within each service and normalize weekday demand per calendar day before staffing or dispatch decisions.',
 '- Investigate unknown zone coverage by source population before interpreting geography or recommending service changes.',
 '- Use specific airport and neighborhood pairs for operational review; inspect both trip counts and service definitions rather than treating all fare concepts as interchangeable.',
 '- Validate proposed scheduling or service interventions using later periods or a controlled operational trial. These descriptive results do not establish causal effects.','']
(ROOT/'docs/findings.md').write_text('\n'.join(findings),encoding='utf-8')
print(json.dumps(summary,indent=2))
