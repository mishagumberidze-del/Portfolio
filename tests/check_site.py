"""Check local paths and build an ignored browser QA page for a GitHub Pages subpath."""
from html.parser import HTMLParser
from pathlib import Path
import urllib.parse

ROOT=Path(__file__).resolve().parents[1]
class Links(HTMLParser):
    def __init__(self):super().__init__();self.targets=[]
    def handle_starttag(self,tag,attributes):
        for name,value in attributes:
            if name in ('src','href') and value:self.targets.append(value)
html=(ROOT/'index.html').read_text(encoding='utf-8')
parser=Links();parser.feed(html)
checked=0
for href in parser.targets:
    if href.startswith(('https:','http:','mailto:','#')):continue
    assert not href.startswith('/'),'Root-absolute path would bypass /Portfolio/: '+href
    target=urllib.parse.unquote(href.split('#')[0].split('?')[0])
    assert (ROOT/target).exists(),'Missing site target: '+target
    checked+=1
assert (ROOT/'.nojekyll').is_file()
qa='<script src="tests/portfolio-browser.js"></script>'
(ROOT/'qa-portfolio.html').write_text(html.replace('</body>',qa+'</body>'),encoding='utf-8')
print(f'PASS: {checked} local asset/link references; Pages subpath and static publishing files')
