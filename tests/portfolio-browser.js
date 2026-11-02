(async()=>{
 const result={status:'running',projects:0,gallery_images:0,links:0};
 const assert=(value,message)=>{if(!value)throw new Error(message);};
 const waitImage=async image=>{const response=await fetch(image.src);assert(response.ok&&response.headers.get('content-type').startsWith('image/'),'Image failed: '+image.getAttribute('src'));assert((await response.arrayBuffer()).byteLength>0,'Empty image');};
 try{
  const links=[...document.querySelectorAll('a[href]')].map(link=>link.getAttribute('href'));
  const local=[...new Set(links.filter(href=>!href.startsWith('http')&&!href.startsWith('#')))];
  for(const href of local){const response=await fetch(href);assert(response.ok,'Broken local link: '+href);result.links++;}
  for(const image of document.querySelectorAll('main img')){image.loading='eager';await waitImage(image);}
  const dialog=document.getElementById('case-study');
  for(const key of ['olist','taxi','spotify','hotel','retail']){
   document.documentElement.setAttribute('data-portfolio-progress',key);
   const trigger=document.querySelector('[data-project="'+key+'"]');trigger.click();
   assert(dialog.open,'Case study did not open: '+key);
   assert(document.getElementById('case-title').textContent.length>4,'Missing title');
   assert(document.querySelectorAll('#case-approach li').length===3,'Missing implementation details');
   const buttons=[...document.querySelectorAll('#case-gallery button')];
   for(const button of buttons){button.click();await waitImage(document.getElementById('case-image'));assert(button.getAttribute('aria-pressed')==='true','Gallery selection state');result.gallery_images++;}
   assert(dialog.scrollWidth<=dialog.clientWidth+2,'Modal horizontal overflow at '+key);
   document.getElementById('close-case').click();await new Promise(resolve=>setTimeout(resolve,30));assert(!dialog.open,'Close failed');
   assert(document.activeElement===trigger,'Focus did not return to opener');result.projects++;
  }
  const layout={viewport:innerWidth,scrollWidth:document.documentElement.scrollWidth};
  assert(layout.scrollWidth<=layout.viewport+1,'Page horizontal overflow');
  assert(!document.body.innerText.includes('undefined'),'Undefined text');
  document.documentElement.setAttribute('data-portfolio-qa',JSON.stringify({...result,status:'pass',layout}));
 }catch(error){document.documentElement.setAttribute('data-portfolio-qa',JSON.stringify({...result,status:'failed',error:error.message}));}
})();
