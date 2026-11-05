"""Verify file-based rebuilds in an isolated temporary folder; does not change SQL databases."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile

ROOT=Path(__file__).resolve().parent
def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def run(script,*args):
    subprocess.run([sys.executable,str(script),*map(str,args)],check=True,stdout=subprocess.DEVNULL)

with tempfile.TemporaryDirectory(prefix='.portfolio-verify-',dir=ROOT) as temp:
    scratch=Path(temp).resolve()
    # TemporaryDirectory cleanup is restricted to this verified workspace child.
    assert scratch.parent == ROOT
    for project,source,staging in [
        ('HotelBookingAnalytics','hotel_bookings.csv','hotel_staging.tsv'),
        ('OnlineRetailPython','online_retail_II.csv','retail_staging.tsv')]:
        original=ROOT/project
        clean=scratch/project
        (clean/'data/raw').mkdir(parents=True)
        (clean/'etl').mkdir()
        shutil.copy2(original/'etl/prepare.py',clean/'etl/prepare.py')
        shutil.copy2(original/'data/raw'/source,clean/'data/raw'/source)
        run(clean/'etl/prepare.py')
        assert digest(clean/'data/processed'/staging)==digest(original/'data/processed'/staging), project
        assert digest(clean/'data/raw'/source)==digest(original/'data/raw'/source)
        print(f'{project}: clean raw-to-staging rebuild matches')
    hotel=ROOT/'HotelBookingAnalytics'
    clean_hotel=scratch/'HotelBookingAnalytics'
    shutil.copytree(hotel/'powerbi/templates',clean_hotel/'powerbi/templates')
    shutil.copy2(hotel/'powerbi/build_pbip.py',clean_hotel/'powerbi/build_pbip.py')
    output=clean_hotel/'powerbi/HotelBookingPBIP'
    run(clean_hotel/'powerbi/build_pbip.py')
    initial={str(p.relative_to(output)):digest(p) for p in output.rglob('*') if p.is_file()}
    run(clean_hotel/'powerbi/build_pbip.py')
    second={str(p.relative_to(output)):digest(p) for p in output.rglob('*') if p.is_file()}
    assert initial==second,'PBIP rebuild is not deterministic'
    tmdl='\n'.join(p.read_text(encoding='utf-8-sig') for p in output.rglob('*.tmdl'))
    assert tmdl.count('expression DataRoot =')==1
    page_root=output/'HotelBooking.Report/definition/pages'
    pages=json.loads((page_root/'pages.json').read_text())['pageOrder']
    for page in pages:
        visuals=list((page_root/page/'visuals').glob('*/visual.json'))
        assert len(visuals)==9
        for path in visuals:
            v=json.loads(path.read_text()); p=v['position']
            assert 0<=p['x'] and p['x']+p['width']<=1920
            assert 0<=p['y'] and p['y']+p['height']<=1080
    print('Hotel: standalone PBIP build is deterministic; all visuals fit their pages')
    retail=ROOT/'OnlineRetailPython'
    clean_retail=scratch/'OnlineRetailPython'
    (clean_retail/'report').mkdir()
    shutil.copy2(retail/'report/build_report.py',clean_retail/'report/build_report.py')
    for file in ['report_lines.tsv','customer_value.tsv']:
        shutil.copy2(retail/'data/processed'/file,clean_retail/'data/processed'/file)
    run(clean_retail/'report/build_report.py')
    for file in ['online_retail_analysis.html','analysis_summary.json']:
        assert digest(clean_retail/'report'/file)==digest(retail/'report'/file), file
    summary=json.loads((retail/'report/analysis_summary.json').read_text())
    assert abs(summary['gross_value']-summary['return_adjustment_value']-summary['net_value'])<.01
    print('Retail: isolated report rebuild matches HTML and analysis summary')
print('PASS: source files unchanged; isolated rebuilds match')
