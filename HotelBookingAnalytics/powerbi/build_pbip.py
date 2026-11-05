"""Create a Git-friendly Hotel Booking PBIP from SQL-produced extracts."""
from __future__ import annotations

import copy
import hashlib
import json
import shutil
import uuid
import argparse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TEMPLATE = ROOT / "powerbi/templates"
parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, default=ROOT / 'powerbi/HotelBookingPBIP')
DEST = parser.parse_args().output.resolve()
REPORT = DEST / "HotelBooking.Report"
MODEL = DEST / "HotelBooking.SemanticModel"
DEFINITION = REPORT / "definition"
PAGES = DEFINITION / "pages"
for path in [PAGES, MODEL / "definition/tables", MODEL / "definition/cultures", REPORT / "StaticResources"]:
    path.mkdir(parents=True, exist_ok=True)

def read_json(path): return json.loads(path.read_text(encoding="utf-8-sig"))
def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")

write_json(DEST/"HotelBooking.pbip", {"$schema":"https://developer.microsoft.com/json-schemas/fabric/pbip/pbipProperties/1.0.0/schema.json","version":"1.0","artifacts":[{"report":{"path":"HotelBooking.Report"}}],"settings":{"enableAutoRecovery":True}})
for directory,typ,name in [(REPORT,"Report","Hotel Booking Analytics"),(MODEL,"SemanticModel","Hotel Booking Model")]:
    write_json(directory/".platform",{"$schema":"https://developer.microsoft.com/json-schemas/fabric/gitIntegration/platformProperties/2.0.0/schema.json","metadata":{"type":typ,"displayName":name},"config":{"version":"2.0","logicalId":str(uuid.uuid5(uuid.NAMESPACE_DNS,name))}})
write_json(REPORT/"definition.pbir",{"$schema":"https://developer.microsoft.com/json-schemas/fabric/item/report/definitionProperties/2.0.0/schema.json","version":"4.0","datasetReference":{"byPath":{"path":"../HotelBooking.SemanticModel"}}})
write_json(MODEL/'definition.pbism', {'$schema':'https://developer.microsoft.com/json-schemas/fabric/item/semanticModel/definitionProperties/1.0.0/schema.json','version':'4.2','settings':{}})
(MODEL/'definition/cultures/en-US.tmdl').write_text('cultureInfo en-US\n', encoding='utf-8')
write_json(DEFINITION/'version.json', {'$schema':'https://developer.microsoft.com/json-schemas/fabric/item/report/definition/versionMetadata/1.0.0/schema.json','version':'2.0.0'})
resources=REPORT/"StaticResources/RegisteredResources"
resources.mkdir(parents=True,exist_ok=True)
hotel_theme=resources/'HotelExecutive-63219f6b-8ed1-4dad-809f-6fbf529a3af4.json'
shutil.copy2(TEMPLATE/'theme.json',hotel_theme)
base=REPORT/'StaticResources/SharedResources/BaseThemes'
base.mkdir(parents=True,exist_ok=True)
shutil.copy2(TEMPLATE/'baseTheme.json',base/'Fluent2-CY26SU08.json')
report_metadata=read_json(TEMPLATE/'report.json')
report_metadata=json.loads(json.dumps(report_metadata).replace("OlistExecutive","HotelExecutive"))
write_json(DEFINITION/"report.json",report_metadata)
(MODEL/"definition/database.tmdl").write_text("database\n\tcompatibilityLevel: 1606\n",encoding="utf-8")
root_str=str(ROOT/"data/processed/powerbi")
(MODEL/"definition/model.tmdl").write_text(f'''model Model
\tculture: en-US
\tdefaultPowerBIDataSourceVersion: powerBI_V3
\tsourceQueryCulture: en-US
\tdataAccessOptions
\t\tlegacyRedirects
\t\treturnErrorValuesAsNull

annotation __PBI_TimeIntelligenceEnabled = 0
annotation PBI_ProTooling = ["DevMode"]
ref table Date
ref table Booking
ref table 'KPI Measures'
ref cultureInfo en-US
''',encoding="utf-8")
(MODEL/'definition/expressions.tmdl').write_text(f'expression DataRoot = "{root_str}" meta [IsParameterQuery=true, Type="Text", IsParameterQueryRequired=true]\n',encoding='utf-8')

booking_cols=[
 ("BookingId","int64"),("ArrivalDateKey","int64"),("HotelName","string"),("SegmentName","string"),
 ("CustomerType","string"),("DistributionChannel","string"),("IsCanceled","int64"),
 ("LeadTime","int64"),("StayNights","int64"),("ADR","decimal"),
 ("RealizedRoomValue","decimal"),("GrossRoomValue","decimal"),("RoomChanged","int64"),
 ("CountryCode","string"),("IsRepeatedGuest","int64"),("DepositType","string"),
 ("ReservationStatus","string"),("DataQualityFlag","int64"),("BookingChanges","int64"),
 ("SpecialRequests","int64"),("LeadTimeBand","string"),("StayBand","string"),
 ("ReservedRoomType","string"),("AssignedRoomType","string")]
date_cols=[("DateKey","int64"),("CalendarDate","dateTime"),("CalendarYear","int64"),
           ("CalendarMonth","int64"),("MonthName","string"),("QuarterNumber","int64"),
           ("YearMonth","string"),("YearMonthSort","int64")]
def table_tmdl(name,cols,filename):
    result=[f"table {name}"]
    for col,typ in cols:
        result += [f"\tcolumn {col}",f"\t\tdataType: {typ}","\t\tsummarizeBy: none",f"\t\tsourceColumn: {col}",""]
        if col in ('BookingId','ArrivalDateKey','DateKey','YearMonthSort'):
            result.insert(len(result)-1,'\t\tisHidden')
        if col == 'YearMonth': result.insert(len(result)-1,'\t\tsortByColumn: YearMonthSort')
        if col in ('LeadTimeBand', 'StayBand'):
            result.insert(len(result)-1,f'\t\tsortByColumn: {col}Sort')
    if name == 'Booking':
        for col, expression in [
            ('LeadTimeBandSort', 'SWITCH(TRUE(), Booking[LeadTime] < 30, 1, Booking[LeadTime] < 90, 2, Booking[LeadTime] < 180, 3, 4)'),
            ('StayBandSort', 'SWITCH(TRUE(), Booking[StayNights] = 0, 0, Booking[StayNights] <= 3, 1, Booking[StayNights] <= 7, 2, 3)'),
        ]:
            result += [f'\tcolumn {col} = {expression}', '\t\tdataType: int64', '\t\tisHidden', '\t\tsummarizeBy: none', '']
    names=",".join(f'{{{{"Column{i+1}","{col}"}}}}' for i,(col,_) in enumerate(cols))
    mtypes={"int64":"Int64.Type","string":"type text","decimal":"type number","dateTime":"type date"}
    types=",".join(f'{{{{"{col}", {mtypes[typ]}}}}}' for col,typ in cols)
    # Double braces above are escaped only in the f-string; remove the extra layer.
    names=names.replace("{{","{").replace("}}","}")
    types=types.replace("{{","{").replace("}}","}")
    result += [f"\tpartition {name} = m","\t\tmode: import","\t\tsource =",
               "\t\t\tlet",f'\t\t\t\tSource = Csv.Document(File.Contents(#"DataRoot" & "\\{filename}"), [Delimiter="#(tab)", Columns={len(cols)}, Encoding=65001, QuoteStyle=QuoteStyle.None]),',
               f"\t\t\t\tNamed = Table.RenameColumns(Source, {{{names}}}),",
               f'\t\t\t\tTyped = Table.TransformColumnTypes(Named, {{{types}}}, "en-US")',
               "\t\t\tin","\t\t\t\tTyped",""]
    (MODEL/f"definition/tables/{name}.tmdl").write_text("\n".join(result),encoding="utf-8")
table_tmdl("Booking",booking_cols,"booking.tsv")
table_tmdl("Date",date_cols,"date.tsv")
(MODEL/"definition/relationships.tmdl").write_text("relationship 'Booking to Date'\n\tfromColumn: Booking.ArrivalDateKey\n\ttoColumn: Date.DateKey\n",encoding="utf-8")
measures=[
 ("Bookings","COUNTROWS(Booking)","#,##0"),
 ("Cancellations","CALCULATE([Bookings], KEEPFILTERS(Booking[IsCanceled] = 1))","#,##0"),
 ("Cancellation Rate","DIVIDE([Cancellations], [Bookings])","0.0%"),
 ("Room Value Proxy","CALCULATE(SUM(Booking[RealizedRoomValue]), KEEPFILTERS(Booking[IsCanceled] = 0), KEEPFILTERS(Booking[StayNights] > 0), KEEPFILTERS(Booking[ADR] >= 0))","#,##0.00"),
 ("Stay-weighted ADR","DIVIDE([Room Value Proxy], CALCULATE(SUM(Booking[StayNights]), KEEPFILTERS(Booking[IsCanceled] = 0), KEEPFILTERS(Booking[StayNights] > 0), KEEPFILTERS(Booking[ADR] >= 0)))","#,##0.00"),
 ("Average Lead Time","AVERAGE(Booking[LeadTime])","#,##0.0"),
 ("Stayed Bookings","CALCULATE([Bookings], KEEPFILTERS(Booking[IsCanceled] = 0), KEEPFILTERS(Booking[StayNights] > 0))","#,##0"),
 ("Stay Nights","CALCULATE(SUM(Booking[StayNights]), KEEPFILTERS(Booking[IsCanceled] = 0), KEEPFILTERS(Booking[StayNights] > 0))","#,##0"),
 ("Average Stay Nights","CALCULATE(AVERAGE(Booking[StayNights]), KEEPFILTERS(Booking[IsCanceled] = 0), KEEPFILTERS(Booking[StayNights] > 0))","#,##0.0"),
 ("Room Assignment Difference","DIVIDE(CALCULATE([Stayed Bookings], KEEPFILTERS(Booking[RoomChanged] = 1)), [Stayed Bookings])","0.0%")]
body=["table 'KPI Measures'",""]
for name,formula,fmt in measures:
    body += [f"\tmeasure '{name}' = {formula}",f"\t\tformatString: {fmt}",""]
body += ["\tcolumn Value","\t\tisHidden","\t\tsourceColumn: [Value]","", "\tpartition 'KPI Measures' = calculated","\t\tmode: import",'\t\tsource = ROW("Value", BLANK())',""]
(MODEL/"definition/tables/KPI Measures.tmdl").write_text("\n".join(body),encoding="utf-8")

src_visuals=TEMPLATE/'visuals'
def field(entity,property,kind="Column"):
    return {"field":{kind:{"Expression":{"SourceRef":{"Entity":entity}},"Property":property}},"queryRef":f"{entity}.{property}","nativeQueryRef":property}
def make_visual(page,key,kind,title,x,y,w,h,fields):
    vis=copy.deepcopy(read_json(src_visuals/f'{kind}.json'))
    vis["name"]=hashlib.sha1(f"{page}:{key}".encode()).hexdigest()[:20]
    vis["position"]={"x":x,"y":y,"z":100+len(key)+x+y,"height":h,"width":w,"tabOrder":100+len(key)+x+y}
    query={}
    for bucket,items in fields.items():
        query[bucket]={"projections":[field(*item) for item in items]}
    vis["visual"]["query"]={"queryState":query}
    if kind=='slicer':
        vis['visual']['objects']['header'][0]['properties']['text']={'expr':{'Literal':{'Value':f"'{title}'"}}}
    if kind in ('bar','line'):
        if 'Tooltips' not in query:
            query['Tooltips']={'projections':[field('KPI Measures','Bookings','Measure'),field('KPI Measures','Cancellation Rate','Measure')]}
        cat=query.get('Category',{}).get('projections',[])
        if cat:
            vis['visual']['query']['sortDefinition']={'sort':[{'field':cat[0]['field'],'direction':'Ascending'}],'isDefaultSort':False}
    if kind=='textbox':
        vis['visual'].pop('query',None)
        vis['visual']['objects']['general'][0]['properties']['paragraphs']=[{'textRuns':[{'value':title,'textStyle':{'fontFamily':'Segoe UI Semibold','fontSize':'24px' if key=='header' else '15px','color':'#18324A'}}]}]
    box=vis["visual"].get("visualContainerObjects",{})
    if box.get("title") and kind not in ('slicer','textbox'):
        box["title"][0]["properties"]["text"]={"expr":{"Literal":{"Value":f"'{title}'"}}}
    target=PAGES/page/"visuals"/vis["name"]/"visual.json"
    write_json(target,vis)

M=lambda x:("KPI Measures",x,"Measure")
C=lambda x:("Booking",x,"Column")
D=lambda x:("Date",x,"Column")
pages=[
 ("Executive Overview",[
  ("card","kpis","Commercial snapshot",24,40,1872,180,{"Data":[M("Bookings"),M("Cancellation Rate"),M("Room Value Proxy"),M("Stay-weighted ADR")]}),
  ("line","trend","Bookings by arrival month",24,244,1200,365,{"Category":[D("YearMonth")],"Y":[M("Bookings")]}),
  ("bar","hotel","Bookings by hotel",1245,244,651,365,{"Category":[C("HotelName")],"Y":[M("Bookings")]}),
  ("table","segment","Market segment performance",24,632,1872,415,{"Values":[C("SegmentName"),M("Bookings"),M("Cancellations"),M("Cancellation Rate"),M("Room Value Proxy")]}),
 ]),
 ("Cancellation Patterns",[
  ("card","kpis","Cancellation indicators",24,40,1872,180,{"Data":[M("Bookings"),M("Cancellations"),M("Cancellation Rate"),M("Average Lead Time")]}),
  ("bar","lead","Cancellation rate by lead-time band",24,245,900,340,{"Category":[C("LeadTimeBand")],"Y":[M("Cancellation Rate")]}),
  ("bar","deposit","Cancellation rate by deposit type",946,245,950,340,{"Category":[C("DepositType")],"Y":[M("Cancellation Rate")]}),
  ("table","channel","Channel and customer type",24,607,1872,440,{"Values":[C("DistributionChannel"),C("CustomerType"),M("Bookings"),M("Cancellation Rate"),M("Stay-weighted ADR")]}),
 ]),
 ("Stay & Operations",[
  ("card","kpis","Stay volume and room assignment",24,40,1872,180,{"Data":[M("Stayed Bookings"),M("Stay Nights"),M("Average Stay Nights"),M("Room Assignment Difference")]}),
  ("bar","stay","Non-canceled bookings by stay length",24,245,900,340,{"Category":[C("StayBand")],"Y":[M("Stayed Bookings")],"Tooltips":[M("Stayed Bookings"),M("Average Stay Nights")]}),
  ("bar","room","Assigned vs reserved room type — non-canceled stays",946,245,950,340,{"Category":[C("ReservedRoomType")],"Y":[M("Room Assignment Difference")],"Tooltips":[M("Stayed Bookings"),M("Room Assignment Difference")]}),
  ("table","ops","Hotel and segment operations — non-canceled stays",24,607,1872,440,{"Values":[C("HotelName"),C("SegmentName"),M("Stayed Bookings"),M("Stay Nights"),M("Average Stay Nights"),M("Room Assignment Difference"),M("Stay-weighted ADR")]}),
 ])]
page_ids=[]
for display,visuals in pages:
    stable_name="Cancellation Drivers" if display=="Cancellation Patterns" else display
    pid=hashlib.sha1(stable_name.encode()).hexdigest()[:20]
    page_ids.append(pid)
    write_json(PAGES/pid/"page.json",{"$schema":"https://developer.microsoft.com/json-schemas/fabric/item/report/definition/page/2.1.0/schema.json","name":pid,"displayName":display,"displayOption":"FitToPage","height":1080,"width":1920})
    make_visual(pid,'header','textbox',f'Hotel Booking Analytics  /  {display}',24,16,1872,65,{})
    for i,(label,projection) in enumerate([('Hotel',C('HotelName')),('Arrival year',D('CalendarYear')),('Market segment',C('SegmentName'))]):
        make_visual(pid,f'filter-{i}','slicer',label,24+i*624,100,606,88,{'Values':[projection]})
    for kind,key,title,x,y,w,h,fields in visuals:
        if kind=='card': y,h=212,145
        elif kind in ('line','bar'): y,h=381,300
        elif kind=='table': y,h=705,315
        make_visual(pid,key,kind,title,x,y,w,h,fields)
    note={
        'Executive Overview': 'Room value and ADR use non-canceled positive-night stays with ADR ≥ 0. ADR is weighted by stay nights; source currency units. Filters apply on this page.',
        'Cancellation Patterns': 'Rates describe observed bookings, not causal effects. Deposit type may reflect later booking events. Lead time is in days. Filters apply on this page.',
        'Stay & Operations': 'Stay volume follows arrival cohorts; it is not nightly occupancy. Canceled/zero-night bookings are excluded. Room difference is assigned vs reserved type, not a recorded move.',
    }[display]
    make_visual(pid,'note','textbox',note,24,1033,1872,38,{})
write_json(PAGES/"pages.json",{"$schema":"https://developer.microsoft.com/json-schemas/fabric/item/report/definition/pagesMetadata/1.1.0/schema.json","pageOrder":page_ids,"activePageName":page_ids[0]})
print(f"Built {DEST} with {len(pages)} pages and {sum(len(x[1])+5 for x in pages)} visuals")
