(async()=>{
 const results=[];
 const assert=(condition,message)=>{if(!condition)throw new Error(message);};
 const expected=JSON.parse(document.getElementById('qa-expected').textContent);
 const change=(id,value)=>{const node=document.getElementById(id);node.value=value;node.dispatchEvent(new Event('change',{bubbles:true}));};
 try{
  for(const service of [0,1,2,3,4])for(const month of [0,...Array.from({length:12},(_,i)=>202401+i)]){
   change('service',service);change('month',month);
   const count=Number(document.getElementById('trips').textContent.replaceAll(',',''));
   const e=expected[`${service}-${month}`];
   assert(count===e.n,`Count mismatch at ${service}/${month}: ${count}`);
   const close=(id,value)=>assert(Math.abs(parseFloat(document.getElementById(id).textContent)-value)<=.051,`${id} mismatch at ${service}/${month}`);
   close('duration',e.ds/e.dn/60);
   if(e.mn)close('distance',e.mi/e.mn);
   assert(!document.getElementById('speed'),'Mixed-route speed should not be a headline performance metric');
   assert(document.getElementById('trips-note').textContent.includes(Math.round(e.n/e.days).toLocaleString('en-US')),'Daily average mismatch');
   const mondayTitle=document.querySelector('#heatmap rect title').textContent;
   assert(mondayTitle.includes(`${e.mondays} dates`),'Weekday exposure mismatch');
   assert(mondayTitle.includes(Math.round(e.monday0/e.mondays).toLocaleString('en-US')+' pickups per Mon'),'Normalized heatmap value mismatch');
   const quality=document.querySelector('[data-check="invalid_distance"]');
   if(service!==3)assert(quality.textContent.includes(e.distanceQualityDenominator.toLocaleString('en-US')),'Distance quality denominator includes unavailable services');
   assert(!document.getElementById('quality').textContent.includes('duplicate'),'Unproven duplicate count displayed');
   assert(document.querySelectorAll('#map path').length===263,'Missing zone geometry');
   assert(document.querySelectorAll('#routes tbody tr').length===10,'Incomplete top-route ranking');
   assert(!document.body.innerText.includes('NaN'),'NaN displayed');
   if(service===3)assert(document.getElementById('distance').textContent==='—','Unavailable FHV distance rendered as a value');
   results.push(`${service}-${month}`);
  }
  document.getElementById('reset').click();
  document.querySelector('[data-role="DO"]').click();
  assert(document.getElementById('role-word').textContent==='end','Dropoff map toggle failed');
  const path=document.querySelector('#map path[data-zone="1"]');path.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true}));
  assert(document.getElementById('zone-detail').textContent.includes('Newark'),'Keyboard zone selection failed');
  document.querySelector('.month-hit[data-month="202403"]').dispatchEvent(new Event('click',{bubbles:true}));
  assert(document.getElementById('month').value==='202403','Monthly chart selection failed');
  change('service',3);document.querySelector('[data-band="distance"]').click();
  assert(document.getElementById('bands').innerText.includes('not reported'),'FHV distribution availability failed');
  let downloadBlob=null;let downloaded=false;
  const originalCreate=URL.createObjectURL,originalClick=HTMLAnchorElement.prototype.click;
  URL.createObjectURL=blob=>{downloadBlob=blob;return 'blob:qa';};
  HTMLAnchorElement.prototype.click=function(){downloaded=this.download.endsWith('-aggregates.csv');};
  document.getElementById('download').click();
  const csv=await downloadBlob.text();
  assert(downloaded&&csv.startsWith('service,month,')&&csv.includes('202403'),'Aggregate download failed');
  URL.createObjectURL=originalCreate;HTMLAnchorElement.prototype.click=originalClick;
  document.getElementById('reset').click();
  assert(document.getElementById('trips').textContent==='298,930,636','Reset failed');
  assert(!document.body.innerText.includes('Â'),'Text encoding regression');
  const layout={viewport:innerWidth,scrollWidth:document.documentElement.scrollWidth,
   wide:[...document.querySelectorAll('header,main,section,.panel')].filter(node=>node.getBoundingClientRect().right>innerWidth+1).length};
  assert(layout.scrollWidth<=layout.viewport+1&&layout.wide===0,'Page overflow');
  const report={status:'pass',filter_combinations:results.length,geometry_features:263,download:true,metrics:true,calendar_exposure:true,quality_denominators:true,layout};
  document.documentElement.setAttribute('data-qa-result',JSON.stringify(report));
  document.title='PASS: NYC calculation and browser checks';
  const output=document.createElement('pre');output.id='qa-result';output.textContent=JSON.stringify(report,null,2);document.body.prepend(output);
 }catch(error){const report={status:'failed',error:error.message,completed:results.length};document.documentElement.setAttribute('data-qa-result',JSON.stringify(report));document.title='FAILED: NYC browser checks';const output=document.createElement('pre');output.id='qa-result';output.textContent=JSON.stringify(report,null,2);document.body.prepend(output);}
})();
