'use strict';
const payload=JSON.parse(document.getElementById('dashboard-data').textContent);
const db=Object.fromEntries(Object.entries(payload.tables).map(([key,table])=>[key,table.rows.map(row=>Object.fromEntries(table.columns.map((col,i)=>[col,row[i]])))]));
const services={0:'All services',1:'Yellow taxi',2:'Green taxi',3:'FHV',4:'High-volume FHV'};
const monthNames=['January','February','March','April','May','June','July','August','September','October','November','December'];
const shortMonths=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
const days=['Mon','Tue','Wed','Thu','Fri','Sat','Sun'];
const zones=new Map(db.zones.map(z=>[z.id,z]));
const state={service:0,month:0,role:'PU',band:'duration',zone:null};
const $=id=>document.getElementById(id);
const fmt=n=>new Intl.NumberFormat('en-US').format(Math.round(n));
const dec=(n,d=1)=>Number.isFinite(n)?n.toLocaleString('en-US',{minimumFractionDigits:d,maximumFractionDigits:d}):'—';
const percent=(n,total)=>{if(!total)return '—';const p=100*n/total;if(p>0&&p<.01)return 'under 0.01%';if(p<100&&p>99.995)return 'over 99.99%';return `${dec(p,(p>99.9&&p<100)||(p>0&&p<.1)?2:1)}%`;};
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const sum=(rows,key)=>rows.reduce((total,row)=>total+row[key],0);
const match=(row,ignoreMonth=false)=>(!state.service||row.service===state.service)&&(ignoreMonth||!state.month||row.month===state.month);
const aggregate=(rows,field,value='n')=>{const map=new Map();for(const row of rows)map.set(row[field],(map.get(row[field])||0)+row[value]);return map;};
const svg=(content,width,height,label)=>`<svg viewBox="0 0 ${width} ${height}" role="img" aria-label="${esc(label)}">${content}</svg>`;
const title=s=>`<title>${esc(s)}</title>`;
const zoneName=id=>zones.get(id)?.name||'Unknown';
for(let month=1;month<=12;month++)$('month').insertAdjacentHTML('beforeend',`<option value="${202400+month}">${monthNames[month-1]} 2024</option>`);

function monthlyChart(){
 const counts=aggregate(db.time.filter(row=>match(row,true)),'month');
 const maximum=Math.max(...counts.values(),1);const W=640,H=255,left=48,top=15,plotH=185,barW=30,gap=17;
 let content='';
 const axis=n=>maximum>=1e6?`${dec(n/1e6,1)}m`:`${dec(n/1e3,0)}k`;
 for(let i=0;i<4;i++){const y=top+plotH*i/3;content+=`<line x1="${left}" x2="630" y1="${y}" y2="${y}" stroke="#e4ebed"/><text x="${left-9}" y="${y+4}" text-anchor="end" fill="#71848d" font-size="10">${axis(maximum*(1-i/3))}</text>`;}
 for(let i=0;i<12;i++){const n=counts.get(202401+i)||0;const h=plotH*n/maximum;const x=left+9+i*(barW+gap);const selected=!state.month||state.month===202401+i;
 content+=`<g class="month-hit" data-month="${202401+i}" role="button" tabindex="0" aria-label="Explore ${monthNames[i]}: ${fmt(n)} pickups"><rect x="${x-5}" y="0" width="${barW+10}" height="${H}" fill="transparent"/>${title(`${monthNames[i]}: ${fmt(n)} pickups`)}<rect x="${x}" y="${top+plotH-h}" width="${barW}" height="${Math.max(h,1)}" rx="3" fill="${state.month===202401+i?'#edbb24':selected?'#16746e':'#c6d8d6'}"/><text x="${x+barW/2}" y="222" text-anchor="middle" fill="#71848d" font-size="10">${shortMonths[i]}</text></g>`;}
 $('monthly').innerHTML=svg(content,W,H,'Monthly recorded pickups for the selected service');
 $('monthly').querySelectorAll('.month-hit').forEach(node=>{const select=()=>{state.month=+node.dataset.month;state.zone=null;$('month').value=state.month;render();};node.addEventListener('click',select);node.addEventListener('keydown',event=>{if(['Enter',' '].includes(event.key)){event.preventDefault();select();}});});
 return counts;
}
function heatmap(rows){
 const cells=new Map();for(const row of rows){const key=row.dow*24+row.hour;cells.set(key,(cells.get(key)||0)+row.n);}
 const maximum=Math.max(...cells.values(),1);let content='';const x0=40,y0=20,cw=24,ch=24;
 for(let dow=0;dow<7;dow++){content+=`<text x="31" y="${y0+dow*ch+15}" text-anchor="end" font-size="10" fill="#71848d">${days[dow]}</text>`;
 for(let hour=0;hour<24;hour++){const n=cells.get(dow*24+hour)||0;const alpha=.08+.92*n/maximum;content+=`<rect x="${x0+hour*cw}" y="${y0+dow*ch}" width="21" height="21" rx="3" fill="rgba(22,116,110,${alpha})">${title(`${days[dow]}, ${String(hour).padStart(2,'0')}:00 — ${fmt(n)} pickups`)}</rect>`;}}
 for(let hour=0;hour<24;hour+=3)content+=`<text x="${x0+hour*cw+9}" y="210" text-anchor="middle" font-size="10" fill="#71848d">${String(hour).padStart(2,'0')}</text>`;
 content+='<text x="614" y="239" text-anchor="end" font-size="10" fill="#71848d">NYC local clock · pickup hour</text>';
 $('heatmap').innerHTML=svg(content,640,255,'Total pickups by weekday and hour');
}
function geography(rows,total){
 const counts=aggregate(db.zone.filter(row=>match(row)&&row.role===state.role),'zone');
 const ranked=[...counts].filter(([id])=>id>=1&&id<=263).sort((a,b)=>b[1]-a[1]);
 const maximum=Math.max(...ranked.map(x=>x[1]),1);const mapped=ranked.reduce((s,x)=>s+x[1],0);
 if(state.zone===null)state.zone=ranked[0]?.[0]??null;
 let map='';
 for(const feature of payload.geometry){const n=counts.get(feature.id)||0;const t=n?Math.log1p(n)/Math.log1p(maximum):0;const color=n?`rgb(${Math.round(229-207*t)},${Math.round(239-123*t)},${Math.round(237-127*t)})`:'#edf0f1';
 map+=`<path data-zone="${feature.id}" class="${state.zone===feature.id?'selected':''}" d="${feature.d}" fill="${color}" fill-rule="evenodd" role="button" tabindex="0" aria-label="${esc(zoneName(feature.id))}: ${fmt(n)} ${state.role==='PU'?'pickups':'dropoffs'}">${title(`${zoneName(feature.id)} · ${fmt(n)} ${state.role==='PU'?'pickups':'dropoffs'}`)}</path>`;}
 $('map').innerHTML=svg(map,1000,payload.mapHeight,'TLC taxi-zone choropleth in the official projected coordinate system');
 const detail=()=>{const zone=zones.get(state.zone);const n=counts.get(state.zone)||0;$('zone-detail').innerHTML=zone?`<small>${esc(zone.borough)} · ZONE ${zone.id}</small><h3>${esc(zone.name)}</h3><strong>${fmt(n)}</strong><small>${percent(n,total)} of selected ${state.role==='PU'?'pickups':'dropoffs'}</small>`:'Select a zone on the map.';};
 const select=id=>{state.zone=id;detail();$('map').querySelectorAll('path').forEach(node=>node.classList.toggle('selected',+node.dataset.zone===id));};
 $('map').querySelectorAll('path').forEach(node=>{node.addEventListener('click',()=>select(+node.dataset.zone));node.addEventListener('keydown',event=>{if(['Enter',' '].includes(event.key)){event.preventDefault();select(+node.dataset.zone);}});});
 detail();$('map-coverage').textContent=`${percent(mapped,total)} of records have a mapped ${state.role==='PU'?'pickup':'dropoff'} zone. Unmapped and outside-NYC records remain in the overall count.`;
 $('role-word').textContent=state.role==='PU'?'begin':'end';
 $('zone-table').innerHTML=`<table><thead><tr><th></th><th>${state.role==='PU'?'Pickup':'Dropoff'} zone</th><th class="number">Trips</th></tr></thead><tbody>${ranked.slice(0,10).map(([id,n],i)=>`<tr><td class="rank">${String(i+1).padStart(2,'0')}</td><td><button class="zone-button" data-zone="${id}">${esc(zoneName(id))}</button><span class="borough">${esc(zones.get(id)?.borough)}</span><span class="mini-bar"><i style="width:${100*n/maximum}%"></i></span></td><td class="number">${fmt(n)}</td></tr>`).join('')}</tbody></table>`;
 $('zone-table').querySelectorAll('button').forEach(node=>node.addEventListener('click',()=>select(+node.dataset.zone)));
}
function routes(){
 const selected=db.routes.filter(row=>row.service===state.service&&row.month===state.month);
 $('routes').innerHTML=selected.length?`<table><thead><tr><th></th><th>Pickup → dropoff</th><th class="number">Trips</th></tr></thead><tbody>${selected.map(row=>`<tr><td class="rank">${String(row.rank).padStart(2,'0')}</td><td>${esc(zoneName(row.pickup))}<span class="route-arrow">→</span>${esc(zoneName(row.dropoff))}</td><td class="number">${fmt(row.n)}</td></tr>`).join('')}</tbody></table>`:'<div class="empty">No mapped routes for this selection.</div>';
}
function bands(){
 const rows=db.band.filter(row=>match(row)&&row.metric===state.band);const counts=aggregate(rows,'band');const total=sum(rows,'n');
 const labels=state.band==='duration'?['0–10 min','10–20 min','20–40 min','40–60 min','60–240 min']:['0–1 mi','1–3 mi','3–5 mi','5–10 mi','10–200 mi'];
 if(!total){$('bands').innerHTML='<div class="empty">Distance is not reported for FHV.</div>';}
 else{const entries=[...counts].sort((a,b)=>a[0].localeCompare(b[0]));const maximum=Math.max(...entries.map(x=>x[1]),1);let content='';entries.forEach(([label,n],i)=>{const y=12+i*41;const width=390*n/maximum;content+=`<text x="112" y="${y+17}" text-anchor="end" font-size="12" fill="#63747e">${labels[+label.slice(0,2)-1]}</text><rect x="127" y="${y}" width="${width}" height="24" rx="3" fill="${i===0?'#e5bb37':'#16746e'}">${title(`${labels[+label.slice(0,2)-1]}: ${fmt(n)} trips`)}</rect><text x="${137+width}" y="${y+17}" font-size="11" fill="#435963">${percent(n,total)}</text>`;});$('bands').innerHTML=svg(content,640,230,'Trip length distribution');}
 $('band-note').textContent=`${fmt(total)} eligible ${state.band==='duration'?'duration':'distance'} records. Upper band boundaries are inclusive. Invalid or unavailable values are excluded from this distribution.`;
 const time=db.time.filter(row=>match(row));const n=sum(time,'fareN');const amount=sum(time,'fare');
 $('fare-note').textContent=!state.service?'Fare definitions differ by service. Select Yellow, Green or High-volume FHV to inspect its own base-fare measure. No combined fare metric is calculated.':state.service===3?'FHV dispatch records do not include distance, passenger counts, fares or tips. These fields remain unavailable.':`${state.service===4?'Base passenger':'Metered'} fare averages $${dec(amount/n,2)} across ${fmt(n)} eligible records. This measure excludes tips and other separately reported charges and is not profit.`;
}
function quality(){
 const sources=db.source.filter(row=>match(row));const raw=sum(sources,'n');const checks=aggregate(db.quality.filter(row=>match(row)),'check');
 const labels={outside_2024_pickup:'Pickup outside 2024',invalid_duration:'Invalid duration',unmatched_pickup_zone:'Unmatched pickup zone',unmatched_dropoff_zone:'Unmatched dropoff zone',invalid_distance:'Invalid / missing distance',speed_above_100mph:'Speed above 100 mph',invalid_base_fare:'Invalid / missing base fare',projected_duplicate_candidates:'Projected duplicate candidates'};
 $('quality').innerHTML=Object.entries(labels).filter(([key])=>checks.has(key)).map(([key,label])=>`<div class="quality-item">${label}<strong>${fmt(checks.get(key))}</strong><small>${percent(checks.get(key),raw)} of selected source-partition rows</small></div>`).join('');
 $('source-total').textContent=fmt(payload.meta.sourceRows);
}
function render(){
 const rows=db.time.filter(row=>match(row));const total=sum(rows,'n');const dn=sum(rows,'durationN');const mn=sum(rows,'distanceN');const sn=sum(rows,'speedN');
 $('selection').textContent=`${services[state.service]} · ${state.month?monthNames[state.month%100-1]+' 2024':'Full year 2024'}`;
 $('trips').textContent=fmt(total);$('trips-note').textContent='Records with pickup timestamps in 2024';
 $('duration').textContent=dn?`${dec(sum(rows,'durationS')/dn/60)} min`:'—';$('duration-note').textContent=`${percent(dn,total)} have eligible duration`;
 $('distance').textContent=mn?`${dec(sum(rows,'distanceMi')/mn)} mi`:'—';$('distance-note').textContent=state.service===3?'Not reported for FHV':`${percent(mn,total)} have eligible distance; FHV unavailable`;
 $('speed').textContent=sn?`${dec(sum(rows,'speedMi')/sum(rows,'speedS')*3600)} mph`:'—';$('speed-note').textContent=`Eligible miles ÷ travel hours · ${percent(sn,total)} eligible`;
 const counts=monthlyChart();heatmap(rows);geography(rows,total);routes();bands();quality();
 const peak=[...counts].sort((a,b)=>b[1]-a[1])[0];const hour=[...aggregate(rows,'hour')].sort((a,b)=>b[1]-a[1])[0];
 $('insight').textContent=peak&&hour?`${monthNames[peak[0]%100-1]} was the busiest month for ${services[state.service].toLowerCase()} (${fmt(peak[1])} records). In the selected period, the busiest pickup hour begins at ${String(hour[0]).padStart(2,'0')}:00 NYC local time.`:'No records match this selection.';
 document.querySelectorAll('[data-role]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.role===state.role)));
 document.querySelectorAll('[data-band]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.band===state.band)));
}
$('service').addEventListener('change',event=>{state.service=+event.target.value;state.zone=null;render();});
$('month').addEventListener('change',event=>{state.month=+event.target.value;state.zone=null;render();});
$('reset').addEventListener('click',()=>{Object.assign(state,{service:0,month:0,role:'PU',band:'duration',zone:null});$('service').value=0;$('month').value=0;render();});
document.querySelectorAll('[data-role]').forEach(button=>button.addEventListener('click',()=>{state.role=button.dataset.role;state.zone=null;render();}));
document.querySelectorAll('[data-band]').forEach(button=>button.addEventListener('click',()=>{state.band=button.dataset.band;render();}));
$('download').addEventListener('click',()=>{const names=payload.tables.time.columns;const csv=[names.join(','),...db.time.filter(row=>match(row)).map(row=>names.map(name=>row[name]).join(','))].join('\r\n');const url=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));const link=document.createElement('a');link.href=url;link.download=`nyc-tlc-${state.service}-${state.month||2024}-aggregates.csv`;link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);});
render();
