"""Reconcile the report's complete booking population and metrics to untouched CSV rows."""
from collections import Counter, defaultdict
from decimal import Decimal
from pathlib import Path
import csv
import json

ROOT = Path(__file__).resolve().parents[1]
Z = Decimal(0)

def aggregate(rows):
    totals = defaultdict(lambda: [0, 0, 0, 0, 0, 0, Z, 0, 0])
    for r in rows:
        # Each tuple is arrival key, hotel, segment, cancellation, stay, ADR,
        # assigned/reserved mismatch, repeat flag, quality flag.
        date, hotel, segment, canceled, nights, adr, changed, repeat, flagged = r
        for key in [('All',), (hotel, date // 10000, segment)]:
            a = totals[key]
            a[0] += 1; a[1] += canceled; a[7] += repeat; a[8] += flagged
            if not canceled and nights > 0:
                a[2] += 1; a[3] += nights; a[4] += changed
                if adr >= 0:
                    a[5] += nights; a[6] += adr * nights
    return totals

months = {m: i for i, m in enumerate('January February March April May June July August September October November December'.split(), 1)}
source = []
with (ROOT/'data/raw/hotel_bookings.csv').open(encoding='utf-8', newline='') as f:
    for r in csv.DictReader(f):
        nights = int(r['stays_in_week_nights']) + int(r['stays_in_weekend_nights'])
        adr = Decimal(r['adr'])
        guests = int(r['adults']) + int(r['babies']) + int(float(r['children'])) if r['children'] not in ('NA', '') else int(r['adults']) + int(r['babies'])
        source.append((int(r['arrival_date_year'])*10000 + months[r['arrival_date_month']]*100 + int(r['arrival_date_day_of_month']),
                       r['hotel'], r['market_segment'], int(r['is_canceled']), nights, adr,
                       int(r['reserved_room_type'] != r['assigned_room_type']), int(r['is_repeated_guest']), int(nights == 0 or guests == 0 or adr < 0)))
export = []
with (ROOT/'data/processed/powerbi/booking.tsv').open(encoding='utf-8', newline='') as f:
    for r in csv.reader(f, delimiter='\t'):
        assert len(r) == 24
        canceled, nights, adr = int(r[6]), int(r[8]), Decimal(r[9])
        assert Decimal(r[10]) == (adr*nights if not canceled else Z)
        assert Decimal(r[11]) == adr*nights
        assert int(r[12]) == int(r[22] != r[23])
        export.append((int(r[1]), r[2], r[3], canceled, nights, adr, int(r[12]), int(r[14]), int(r[17])))
assert Counter(source) == Counter(export), 'Exported booking facts differ from original source'
expected, actual = aggregate(source), aggregate(export)
assert expected == actual
all_metrics = actual[('All',)]
assert all_metrics[0:3] == [119390, 44224, 74486]
assert all_metrics[3] == 255040
assert all_metrics[5] == 255030 and all_metrics[6] == Decimal('25996324.21')
assert round(all_metrics[6]/all_metrics[5], 2) == Decimal('101.93')

# A short stay and a long stay must contribute different rate weights.
fixture = [(20160101, 'A', 'Direct', 0, 1, Decimal(100), 0, 0, 0),
           (20160101, 'A', 'Direct', 0, 9, Decimal(200), 1, 0, 0),
           (20160101, 'A', 'Direct', 1, 10, Decimal(900), 1, 0, 0),
           (20160101, 'A', 'Direct', 0, 2, Decimal(-10), 0, 0, 1),
           (20160101, 'A', 'Direct', 0, 0, Decimal(999), 1, 0, 1)]
f = aggregate(fixture)[('All',)]
assert f[2] == 3 and f[3] == 12 and f[4] == 1 and f[5] == 10 and f[6]/f[5] == 190

result = {'bookings': all_metrics[0], 'cancellations': all_metrics[1],
          'stayed_bookings': all_metrics[2], 'stay_nights': all_metrics[3],
          'room_assignment_differences': all_metrics[4], 'rate_eligible_nights': all_metrics[5],
          'room_value_proxy': str(all_metrics[6]), 'stay_weighted_adr': str(round(all_metrics[6]/all_metrics[5], 2)),
          'repeat_guest_bookings': all_metrics[7], 'flagged_bookings': all_metrics[8],
          'hotel_year_segment_groups': len(actual)-1}
print(json.dumps(result, indent=2))
print('PASS: all source/export facts, group metrics, eligible weights and operational populations')
