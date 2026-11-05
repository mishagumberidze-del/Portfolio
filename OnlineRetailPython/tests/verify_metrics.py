"""Check report metrics independently against every untouched CSV transaction line."""
from collections import Counter, defaultdict
from datetime import datetime, timedelta
from decimal import Decimal
from pathlib import Path
from statistics import median, mean
import csv
import json
import math

ROOT = Path(__file__).resolve().parents[1]
Z = Decimal(0)
source = Counter()
customer = defaultdict(lambda: {'gross': Z, 'returns': Z, 'net': Z, 'invoices': {}})
invoice_values = defaultdict(lambda: Z)
gross = returns = Z
dates = []
invoices = set()
with (ROOT/'data/raw/online_retail_II.csv').open(encoding='latin1', newline='') as f:
    for r in csv.DictReader(f):
        quantity, price = int(r['Quantity']), Decimal(r['Price'])
        day = datetime.strptime(r['InvoiceDate'], '%m/%d/%y %H:%M').date()
        who = str(int(float(r['Customer ID']))) if r['Customer ID'] else ''
        amount = price*quantity
        sale = amount if quantity > 0 and price > 0 else Z
        credit = -amount if quantity < 0 and price > 0 else Z
        label = 'NEGATIVE_PRICE' if price < 0 else 'ZERO_PRICE' if price == 0 else 'RETURN_OR_ADJUST' if quantity < 0 else 'SALE'
        # SQL's product key uses case-insensitive, trailing-space-insensitive
        # equality; keep value/quantity/date checks exact at the line grain.
        source[(r['Invoice'], who, r['StockCode'].rstrip().casefold(), r['Country'], day.isoformat(), quantity, price, label, sale-credit, sale, credit)] += 1
        gross += sale; returns += credit; dates.append(day); invoices.add(r['Invoice'])
        if who:
            c = customer[who]; c['gross'] += sale; c['returns'] += credit; c['net'] += sale-credit
            if sale > 0:
                c['invoices'][r['Invoice']] = min(c['invoices'].get(r['Invoice'], day), day)
                invoice_values[who, r['Invoice']] += sale

export = Counter()
with (ROOT/'data/processed/report_lines.tsv').open(encoding='utf-8', newline='') as f:
    for r in csv.reader(f, delimiter='\t'):
        # BCP character exports encode an empty varchar as a NUL byte.
        export[(r[1], r[2].replace('\x00',''), r[3].rstrip().casefold(), r[5], r[6], int(r[7]), Decimal(r[8]), r[9], Decimal(r[10]), Decimal(r[11]), Decimal(r[12]))] += 1
assert export == source, 'Exported transaction facts differ from original source'
with (ROOT/'data/processed/customer_value.tsv').open(encoding='utf-8', newline='') as f:
    seen = set()
    for r in csv.reader(f, delimiter='\t'):
        r = [value.replace('\x00','') for value in r]
        c = customer[r[0]]; seen.add(r[0])
        assert int(r[1]) == len(c['invoices'])
        assert tuple(map(Decimal, r[4:7])) == (c['net'], c['gross'], c['returns'])
        assert r[2] == (min(c['invoices'].values()).isoformat() if c['invoices'] else '')
        assert r[3] == (max(c['invoices'].values()).isoformat() if c['invoices'] else '')
    assert seen == customer.keys()

summary = json.loads((ROOT/'report/analysis_summary.json').read_text())
assert summary['source_rows'] == sum(source.values()) == 541910
assert summary['invoices'] == len(invoices) == 25900
assert Decimal(str(summary['gross_value'])) == round(gross, 2) == Decimal('10666702.54')
assert Decimal(str(summary['return_adjustment_value'])) == round(returns, 2) == Decimal('896812.49')
assert Decimal(str(summary['net_value'])) == round(gross-returns, 2) == Decimal('9769890.05')
purchasers = sorted([(who, c) for who,c in customer.items() if c['invoices']], key=lambda pair: (-pair[1]['gross'], pair[0]))
top_n = math.ceil(len(purchasers)/10)
assert summary['identified_purchasers'] == len(purchasers) == 4338
assert summary['top_decile_customers'] == top_n == 434
for group, entries in zip(summary['customer_value_groups'], [purchasers[:top_n], purchasers[top_n:]]):
    assert group['customers'] == len(entries)
    for field in ['gross', 'returns', 'net']:
        expected = sum((c[field] for _,c in entries), Z)
        assert abs(Decimal(str(group[field]))-expected) < Decimal('0.000001')
quarter = defaultdict(lambda: [0, 0])
last_day = max(dates)
for _, c in purchasers:
    purchases = sorted(c['invoices'].values())
    first = purchases[0]
    if first + timedelta(days=90) <= last_day:
        key = f'{first.year}Q{(first.month-1)//3+1}'
        quarter[key][0] += 1
        quarter[key][1] += int(len(purchases)>1 and purchases[1] <= first+timedelta(days=90))
assert [(r['quarter'], int(r['count']), int(r['sum'])) for r in summary['ninety_day_repeat']] == [(k,*v) for k,v in sorted(quarter.items())]
for r in summary['ninety_day_repeat']:
    assert math.isclose(r['mean'], r['sum']/r['count'])
eligible = sum(counts[0] for counts in quarter.values())
repeated = sum(counts[1] for counts in quarter.values())
assert summary['ninety_day_eligible_customers'] == eligible == 3370
assert summary['ninety_day_repeat_customers'] == repeated == 1607
assert math.isclose(summary['ninety_day_repeat_rate'], repeated/eligible)
values = list(invoice_values.values())
assert math.isclose(summary['invoice_value_median'], float(median(values)))
assert math.isclose(summary['invoice_value_mean'], float(mean(values)))
assert math.isclose(summary['top_decile_gross_share'], float(sum((c['gross'] for _,c in purchasers[:top_n]), Z)/sum((c['gross'] for _,c in purchasers), Z)))
assert math.isclose(summary['identified_gross_share'], float(sum((c['gross'] for c in customer.values()), Z)/gross))
assert summary['return_only_customers'] == sum(not c['invoices'] for c in customer.values()) == 34
assert math.isclose(summary['return_only_credit_value'], float(sum((c['returns'] for c in customer.values() if not c['invoices']), Z)))
html = (ROOT/'report/online_retail_analysis.html').read_text(encoding='utf-8')
assert 'Wilson' not in html and 'RFM' not in html and 'reference interval' not in html.lower()
print(json.dumps({'source_lines': sum(source.values()), 'customers': len(customer), 'identified_purchasers': len(purchasers), 'top_decile_customers': top_n, 'ninety_day_counts': quarter}, indent=2))
print('PASS: all raw/export facts; exact value, concentration, customer frequency, invoice distribution and 90-day denominators')
