(async()=>{
 const results=[];
 const assert=(condition,message)=>{if(!condition)throw new Error(message);};
 const expected=JSON.parse(document.getElementById('qa-expected').textContent);
 const change=(id,value)=>{const node=document.getElementById(id);node.value=value;node.dispatchEvent(new Event('change',{bubbles:true}));};
 try{
  for(const service of [0,1,2,3,4])for(const month of [0,...Array.from({length:12},(_,i)=>202401+i)]){
   change('service',service);change('month',month);
   const count=Number(document.getElementById('trips').textContent.replaceAll(',',''));
   assert(count===expected[`${service}-${month}`],`Count mismatch at ${service}/${month}: ${count}`);
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
  document.documentElement.setAttribute('data-qa-result',JSON.stringify({status:'pass',filter_combinations:results.length,geometry_features:263,download:true,layout}));
 }catch(error){document.documentElement.setAttribute('data-qa-result',JSON.stringify({status:'failed',error:error.message,completed:results.length}));}
})();
