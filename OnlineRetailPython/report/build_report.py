"""Build an offline, self-contained analytical report from SQL-produced extracts."""
from __future__ import annotations

import html
import json
from pathlib import Path

import pandas as pd

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data/processed"
lines = pd.read_csv(DATA / "report_lines.tsv", sep="\t", header=None, names=[
    "line_id", "invoice", "customer", "stock", "description", "country", "date", "qty",
    "price", "line_class", "net", "gross", "returns", "cancellation"],
    dtype={"invoice": str, "customer": str, "stock": str}, keep_default_na=False)
customers = pd.read_csv(DATA / "customer_value.tsv", sep="\t", header=None, names=[
    "customer", "purchase_invoices", "first_purchase", "last_purchase", "net", "gross", "returns"],
    dtype={"customer": str}, keep_default_na=False)
manifest = json.loads((DATA / "manifest.json").read_text(encoding="utf-8"))
assert len(lines) == manifest["rows"]
assert lines.line_id.is_unique

def num(n): return f"{n:,.0f}"
def money(n): return f"{n:,.0f} CU"
def pct(n): return f"{n:.1%}"
def esc(x): return html.escape(str(x), quote=True)

sales = lines[lines.line_class.eq("SALE")].copy()
returned = lines[lines.line_class.eq("RETURN_OR_ADJUST")]
known = sales[sales.customer.ne("")]
monthly = lines.assign(month=lines.date.str[:7]).groupby("month", sort=True)[["gross", "returns", "net"]].sum()
country = sales.groupby("country").gross.sum().sort_values(ascending=False)
merchandise_coded = sales[sales.stock.str.match(r"^\d", na=False)]
products = merchandise_coded.groupby(["stock", "description"]).gross.sum().sort_values(ascending=False).head(10)
customer_sales = customers[customers.purchase_invoices.gt(0)].copy()
customer_sales = customer_sales.sort_values("gross", ascending=False)
top10_count = max(1, int(len(customer_sales) * .1 + .999))
top10_share = customer_sales.head(top10_count).gross.sum() / customer_sales.gross.sum()
repeat = customer_sales.purchase_invoices.gt(1).mean()
snapshot = pd.Timestamp(lines.date.max())
invoice_sales = known.groupby(['customer','invoice'], as_index=False).agg(date=('date','min'),gross=('gross','sum'))
invoice_sales['date'] = pd.to_datetime(invoice_sales.date)
invoice_sales = invoice_sales.sort_values(['customer','date','invoice'])
invoice_sales['purchase_number'] = invoice_sales.groupby('customer').cumcount()+1
first_dates = invoice_sales.groupby('customer').date.min()
second_dates = invoice_sales[invoice_sales.purchase_number.eq(2)].set_index('customer').date
repeat90 = first_dates.to_frame('first')
repeat90['second'] = second_dates
repeat90 = repeat90[repeat90['first'].le(snapshot-pd.Timedelta(days=90))].copy()
repeat90['repeated'] = (repeat90['second']-repeat90['first']).dt.days.le(90)
repeat90['quarter'] = repeat90['first'].dt.to_period('Q').astype(str)
repeat90_summary = repeat90.groupby('quarter').repeated.agg(['count','sum','mean'])
def wilson(successes, total):
    z=1.959963984540054
    p=successes/total
    center=(p+z*z/(2*total))/(1+z*z/total)
    half=z*((p*(1-p)/total+z*z/(4*total*total))**.5)/(1+z*z/total)
    return center-half,center+half
repeat90_rows=''
for quarter,row in repeat90_summary.iterrows():
    lo,hi=wilson(row['sum'],row['count'])
    repeat90_rows+=f'<tr><td>{esc(quarter)}</td><td class="right">{num(row["count"])}</td><td class="right">{pct(row["mean"])}</td><td class="right">{pct(lo)}–{pct(hi)}</td></tr>'
order_values=invoice_sales.gross
largest_share=order_values.max()/sales.gross.sum()
identified_value_share=known.gross.sum()/sales.gross.sum()
customer_sales["recency"] = (snapshot - pd.to_datetime(customer_sales.last_purchase)).dt.days
customer_sales["value_quartile"] = pd.qcut(customer_sales.gross.rank(method="first"), 4, labels=["Low", "Mid-low", "Mid-high", "High"])
value_quartiles = customer_sales.groupby("value_quartile", observed=True).agg(customers=("customer", "count"), gross=("gross", "sum"))
def score(series, ascending=True):
    # Average tied ranks keep equal observed values in the same score band.
    return ((series.rank(method="average", ascending=ascending, pct=True) * 4 - 1e-9).astype(int) + 1).clip(1, 4)
customer_sales["R"] = score(customer_sales.recency, ascending=False)
customer_sales["F"] = score(customer_sales.purchase_invoices)
customer_sales["M"] = score(customer_sales.gross)
customer_sales["rfm_segment"] = "Other"
customer_sales.loc[(customer_sales.R >= 3) & (customer_sales.F >= 3) & (customer_sales.M >= 3), "rfm_segment"] = "Champions"
customer_sales.loc[(customer_sales.R < 3) & (customer_sales.F >= 3) & (customer_sales.M >= 3), "rfm_segment"] = "High value, lapsed"
customer_sales.loc[(customer_sales.R >= 3) & (customer_sales.F <= 2), "rfm_segment"] = "Recent, occasional"
rfm_segments = customer_sales.groupby("rfm_segment").agg(customers=("customer", "count"), gross=("gross", "sum"), median_recency=("recency", "median")).sort_values("gross", ascending=False)

purchase_months = known.assign(month=known.date.str[:7]).groupby("customer").month.unique()
first_month = known.groupby("customer").date.min().str[:7]
cohort_counts: dict[str, dict[int,int]] = {}
for customer, months in purchase_months.items():
    cohort = first_month[customer]
    base = pd.Period(cohort, freq="M")
    target = cohort_counts.setdefault(cohort, {})
    for month in months:
        age = (pd.Period(month, freq="M") - base).n
        if age <= 6:
            target[age] = target.get(age, 0) + 1
cohorts = sorted(cohort_counts)
last_month = pd.Period(lines.date.max()[:7], freq="M")
cohort_rows = []
for cohort in cohorts:
    if pd.Period(cohort, freq='M') == last_month:
        continue
    size = cohort_counts[cohort].get(0, 0)
    cells = []
    for age in range(7):
        # The final observed month stops on day nine and is not comparable to full months.
        eligible = pd.Period(cohort, freq="M") + age < last_month
        cells.append((cohort_counts[cohort].get(age, 0) / size) if eligible and size else None)
    cohort_rows.append((cohort, size, cells))

def bar_chart(items, color="#21ad9b", width=850, height=320):
    items = list(items)
    maximum = max((float(v) for _, v in items), default=1)
    left, right, top, bottom = 160, 120, 20, 22
    row = (height-top-bottom)/max(len(items),1)
    pieces = [f'<svg viewBox="0 0 {width} {height}" role="img" aria-label="Horizontal bar chart">']
    for i,(label,value) in enumerate(items):
        y = top+i*row+row*.18
        bw = (width-left-right)*float(value)/maximum
        pieces += [f'<text x="{left-12}" y="{y+row*.37:.1f}" text-anchor="end" class="chart-label">{esc(label)}</text>',
                   f'<rect x="{left}" y="{y:.1f}" width="{bw:.1f}" height="{row*.62:.1f}" rx="4" fill="{color}"/>',
                   f'<text x="{left+bw+9:.1f}" y="{y+row*.37:.1f}" class="chart-value">{esc(money(value))}</text>']
    return "".join(pieces)+"</svg>"

def trend_chart(frame, width=1050, height=310):
    months = list(frame.index)
    maxv = max(frame.gross.max(),1)
    left,right,top,bottom = 65,25,22,54
    xs = [left+i*(width-left-right)/(len(months)-1) for i in range(len(months))]
    def points(col):
        return " ".join(f"{x:.1f},{top+(height-top-bottom)*(1-v/maxv):.1f}" for x,v in zip(xs,frame[col]))
    pieces=[f'<svg viewBox="0 0 {width} {height}" role="img" aria-label="Monthly gross sales and net sales trend">']
    for i in range(5):
        y=top+i*(height-top-bottom)/4
        val=maxv*(1-i/4)
        pieces.append(f'<line x1="{left}" x2="{width-right}" y1="{y:.1f}" y2="{y:.1f}" stroke="#e7edf4"/><text x="{left-8}" y="{y+4:.1f}" text-anchor="end" class="chart-label">{val/1e6:.1f}m</text>')
    pieces += [f'<polyline points="{points("gross")}" fill="none" stroke="#b9c6d8" stroke-width="3"/>',
               f'<polyline points="{points("net")}" fill="none" stroke="#0c9f8d" stroke-width="4"/>']
    for i,m in enumerate(months):
        if i%2==0 or i==len(months)-1:
            pieces.append(f'<text x="{xs[i]:.1f}" y="{height-18}" text-anchor="middle" class="chart-label">{m}</text>')
    return "".join(pieces)+"</svg>"

cohort_html = "<tr><th>Cohort</th><th>Customers</th>"+"".join(f"<th>M+{i}</th>" for i in range(7))+"</tr>"
for label,size,cells in cohort_rows:
    cohort_html += f"<tr><th>{esc(label)}</th><td>{num(size)}</td>"
    for v in cells:
        if v is None:
            cohort_html += '<td class="na">—</td>'
        else:
            alpha=.11+min(v*7,.75)
            text_color='#ffffff' if alpha>.55 else '#17293c'
            cohort_html += f'<td style="background:rgba(12,159,141,{alpha:.3f});color:{text_color}">{pct(v)}</td>'
    cohort_html += "</tr>"

top_products = "".join(f"<tr><td>{esc(stock)}</td><td>{esc(desc[:60])}</td><td class='right'>{money(value)}</td></tr>" for (stock,desc),value in products.items())
quartile_rows = "".join(f"<tr><td>{esc(label)}</td><td class='right'>{num(row.customers)}</td><td class='right'>{money(row.gross)}</td></tr>" for label,row in value_quartiles.iterrows())
rfm_rows = "".join(f"<tr><td>{esc(label)}</td><td class='right'>{num(row.customers)}</td><td class='right'>{money(row.gross)}</td><td class='right'>{num(row.median_recency)} days</td></tr>" for label,row in rfm_segments.iterrows())
cards = [
    ("Gross positive line value",money(sales.gross.sum()),"Positive quantity × positive price"),
    ("Returns & adjustments",money(returned.returns.sum()),"Negative quantity × positive price"),
    ("Net line value",money(lines.net.sum()),"Gross less returns/adjustments"),
    ("Known-customer repeat rate",pct(repeat),"More than one positive-sale invoice"),
]
cards_html = "".join(f'<div class="card"><span>{esc(k)}</span><strong>{esc(v)}</strong><small>{esc(note)}</small></div>' for k,v,note in cards)
findings = [
    f"The top 10% of identifiable purchasing customers account for {pct(top10_share)} of identifiable-customer gross line value.",
    f"{pct(repeat)} of identifiable purchasing customers have more than one positive-sale invoice in the observed period.",
    f"Returns and negative-quantity adjustments total {money(returned.returns.sum())}, or {pct(returned.returns.sum()/sales.gross.sum())} of positive line value.",
    f"The United Kingdom contributes {pct(country.iloc[0]/country.sum())} of gross positive line value; geographic comparisons should account for this concentration.",
]
findings_html="".join(f"<li>{esc(x)}</li>" for x in findings)
country_chart=bar_chart(country.head(8).items(),height=350)
product_chart=bar_chart(((str(code),v) for (code,_),v in products.head(8).items()),color="#667eaf",height=350)

page=f'''<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Online Retail II | Customer & Sales Intelligence</title><style>
:root{{--ink:#17293c;--muted:#617287;--teal:#0c9f8d;--navy:#173957;--paper:#f4f7fa;--line:#dde6ee}}
*{{box-sizing:border-box}} body{{margin:0;background:var(--paper);color:var(--ink);font:15px/1.52 "Segoe UI",Arial,sans-serif}}
header{{background:linear-gradient(125deg,#102d46,#17576b);color:white;padding:52px max(5vw,24px) 48px}}
header .eyebrow{{text-transform:uppercase;letter-spacing:.16em;color:#7fe0cf;font-size:12px;font-weight:700}}
h1{{font-size:clamp(34px,4.5vw,62px);line-height:1.05;margin:12px 0}} header p{{max-width:760px;color:#d7e6ed;font-size:18px}}
main{{max-width:1380px;margin:auto;padding:30px 24px 80px}} .meta{{color:var(--muted);font-size:13px;margin:0 0 24px}}
.cards{{display:grid;grid-template-columns:repeat(4,1fr);gap:14px}} .card,.panel{{background:white;border:1px solid var(--line);border-radius:14px;box-shadow:0 7px 22px rgba(18,49,73,.035)}}
.card{{padding:22px}} .card span,.card small{{display:block;color:var(--muted)}} .card span{{font-weight:650;font-size:13px}} .card strong{{display:block;font-size:clamp(24px,2.2vw,35px);margin:11px 0 4px;color:var(--navy)}} .card small{{font-size:12px}}
section{{margin-top:30px}} h2{{font-size:25px;margin:0 0 13px}} .section-note{{color:var(--muted);margin:-5px 0 16px}}
.panel{{padding:24px;min-width:0;overflow-x:auto}} .grid2{{display:grid;grid-template-columns:1fr 1fr;gap:18px}} .panel h3{{font-size:17px;margin:0 0 12px}} .panel p{{color:var(--muted);margin:5px 0 18px}}
svg{{width:100%;height:auto}} .chart-label{{fill:#738296;font:11px "Segoe UI",Arial}} .chart-value{{fill:#34475c;font:11px "Segoe UI",Arial;font-weight:650}}
table{{width:100%;border-collapse:collapse;font-size:13px}} th,td{{padding:10px 12px;border-bottom:1px solid #e9eef3;text-align:left}} th{{color:#506379;font-weight:700;background:#f8fafc}} .right{{text-align:right}} .scroll{{overflow:auto}} .heat th,.heat td{{text-align:center;white-space:nowrap;min-width:72px}} .heat th:first-child{{text-align:left}} .na{{color:#aab5c0}}
ul{{padding-left:22px}} li{{margin:9px 0}} .callout{{border-left:4px solid var(--teal);padding:4px 18px;background:#f2faf8;border-radius:0 9px 9px 0}} .method{{color:#506379;font-size:13px}} footer{{margin-top:35px;color:#748397;font-size:12px}}
@media(max-width:900px){{.cards{{grid-template-columns:repeat(2,1fr)}} .grid2{{grid-template-columns:1fr}}}} @media(max-width:540px){{.cards{{grid-template-columns:1fr}} main{{padding:22px 14px}} .panel{{padding:15px}}}}
@media print{{body{{background:white}} header{{background:#173957!important;print-color-adjust:exact}} .panel,.card{{break-inside:avoid}}}}
</style></head><body><header><div class="eyebrow">Portfolio analytics / Python + SQL Server</div><h1>Online Retail II</h1><p>Customer value, repeat purchasing, products and returns in a line-level transaction dataset.</p></header>
<main><p class="meta">Observed {esc(manifest['date_min'][:10])} to {esc(manifest['date_max'][:10])} · {num(len(lines))} source lines · {num(lines.invoice.nunique())} invoices · built from <code>Portfolio_OnlineRetail</code> reporting views</p>
<div class="cards">{cards_html}</div>
<section><h2>Executive findings</h2><div class="panel callout"><ul>{findings_html}</ul></div></section>
<section><h2>Sales and returns over time</h2><p class="section-note">Positive line value and net line value by transaction month. December 2011 ends on day nine and is partial.</p><div class="panel"><h3>Monthly line value</h3>{trend_chart(monthly)}<p>Gross positive line value <span style="color:#b9c6d8">●</span> &nbsp; Net line value <span style="color:#0c9f8d">●</span></p></div></section>
<section class="grid2"><div class="panel"><h3>Leading countries</h3><p>Gross positive line value; country is attached to the source invoice.</p>{country_chart}</div><div class="panel"><h3>Leading merchandise-coded stocks</h3><p>Gross positive line value. Non-numeric service codes are excluded from this ranking.</p>{product_chart}</div></section>
<section class="grid2"><div class="panel"><h3>RFM segments</h3><p>Identified purchasers scored in quartiles for recency, invoice frequency and gross positive line value.</p><table><tr><th>Segment</th><th class="right">Customers</th><th class="right">Gross value</th><th class="right">Median recency</th></tr>{rfm_rows}</table><p>Scores run 1–4, with 4 strongest. Champions: R/F/M ≥3; high value, lapsed: R &lt;3, F/M ≥3; recent, occasional: R ≥3, F ≤2; all else: Other.</p></div><div class="panel"><h3>Customer value quartiles</h3><p>Gross positive line value among identified purchasers.</p><table><tr><th>Quartile</th><th class="right">Customers</th><th class="right">Gross value</th></tr>{quartile_rows}</table><p>Top-decile share: <strong>{pct(top10_share)}</strong>. Recency is measured through {esc(snapshot.date())}.</p></div></section>
<section><div class="panel"><h3>Top merchandise-coded stocks</h3><div class="scroll"><table><tr><th>Stock</th><th>Description</th><th class="right">Gross value</th></tr>{top_products}</table></div></div></section>
<section><h2>Purchase cohort retention</h2><p class="section-note">Share of customers in each first-observed purchase month who made a positive-sale purchase in month M+n. Customers with missing IDs are excluded. Unreached months and the incomplete December 2011 month are blank.</p><div class="panel scroll"><table class="heat">{cohort_html}</table></div></section>
<section><h2>Repeat purchasing with equal observation time</h2><p class="section-note">Each included customer has at least 90 days of follow-up. A repeat means a second distinct positive-sale invoice within 90 days of their first observed purchase.</p><div class="grid2"><div class="panel"><h3>90-day repeat by first-purchase quarter</h3><div class="scroll"><table><tr><th>Quarter</th><th class="right">Eligible customers</th><th class="right">Repeat rate</th><th class="right">95% reference interval</th></tr>{repeat90_rows}</table></div><p>Wilson intervals assume independent customer outcomes under a binomial model. They describe model-based precision; this historical convenience dataset does not support a population or causal claim. Only customers eligible for 90 days are included, so the latest quarter is truncated.</p></div><div class="panel"><h3>Distribution and coverage checks</h3><p>Identified customers represent <strong>{pct(identified_value_share)}</strong> of gross positive line value. Customer findings may differ for anonymous purchases.</p><table><tr><th>Positive-sale invoice value (identified)</th><th class="right">CU</th></tr><tr><td>Median</td><td class="right">{money(order_values.median())}</td></tr><tr><td>Mean</td><td class="right">{money(order_values.mean())}</td></tr><tr><td>90th percentile</td><td class="right">{money(order_values.quantile(.9))}</td></tr><tr><td>Largest invoice</td><td class="right">{money(order_values.max())}</td></tr></table><p>The largest identified positive-sale invoice represents {pct(largest_share)} of all gross positive line value. Compare median and mean before choosing a typical order value.</p></div></div></section>
<section><h2>Actions to evaluate</h2><div class="panel"><ul><li><strong>Protect valuable relationships:</strong> review service needs and account coverage for the top customer decile; use net value alongside gross value because credits can change priorities.</li><li><strong>Measure repeat activity consistently:</strong> use the fixed 90-day view for comparisons and evaluate a retention campaign with randomized assignment and a defined outcome window.</li><li><strong>Investigate credits before changing policy:</strong> reconcile large negative lines and the matching sales with the business. Negative quantities include adjustments, and the file does not provide return reasons.</li><li><strong>Improve data collection:</strong> increase customer-ID coverage and maintain a governed product catalog before using these segments for operational targeting.</li></ul></div></section>
<section><h2>Definitions and data quality</h2><div class="panel method"><p><strong>Gross positive line value:</strong> sum of quantity × unit price where both are positive. <strong>Returns and adjustments:</strong> absolute value of negative-quantity lines with positive price. <strong>Net line value:</strong> gross less returns/adjustments. These are transaction values, not audited revenue or profit.</p>
<p><strong>Observed source issues:</strong> {num(manifest['missing_customer_rows'])} lines lack customer ID; {num(manifest['exact_duplicate_rows'])} exact duplicate rows are preserved because there is no line ID in the source; {num(manifest['negative_quantity_without_c_prefix'])} negative-quantity rows have no cancellation prefix; {num(manifest['zero_price_rows'])} lines have zero price; {num(manifest['negative_price_rows'])} have negative price; and {num(manifest['invoices_with_multiple_timestamps'])} invoice numbers contain multiple timestamps. Negative-price and zero-price lines are excluded from gross/return/net value but retained in SQL.</p>
<p><strong>Interpretation:</strong> Returns are inferred from negative quantities and may include adjustments. Customer retention is observable only for identified customers, and the one-year window right-censors late cohorts. Product ranking uses stock codes beginning with a digit and the most frequent nonblank description; the ranking can still include anomalies or non-merchandise items. Gross and net line values include service codes such as postage. No causal claims are made.</p></div></section>
<footer>Generated from SQL Server reporting exports · Source: Kaggle Online Retail II archive · Original CSV remains unchanged</footer></main></body></html>'''
out=ROOT/"report/online_retail_analysis.html"
out.write_text(page,encoding="utf-8")
summary={
    'source_rows':len(lines), 'invoices':int(lines.invoice.nunique()),
    'identified_purchasers':len(customer_sales), 'gross_value':round(float(sales.gross.sum()),2),
    'return_adjustment_value':round(float(returned.returns.sum()),2),
    'net_value':round(float(lines.net.sum()),2), 'repeat_rate':float(repeat),
    'identified_gross_share':float(identified_value_share), 'top_decile_gross_share':float(top10_share),
    'ninety_day_repeat':repeat90_summary.reset_index().to_dict('records'),
    'partial_month_excluded_from_retention':str(last_month),
    'invoice_value_median':float(order_values.median()),'invoice_value_mean':float(order_values.mean()),
}
(ROOT/'report/analysis_summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
customer_sales.to_csv(DATA/'customer_rfm.csv',index=False)
print(json.dumps({"report":str(out),"bytes":out.stat().st_size,"rows":len(lines),"invoices":int(lines.invoice.nunique()),"customers":len(customer_sales),"gross":round(float(sales.gross.sum()),2),"returns":round(float(returned.returns.sum()),2),"net":round(float(lines.net.sum()),2)},indent=2))
