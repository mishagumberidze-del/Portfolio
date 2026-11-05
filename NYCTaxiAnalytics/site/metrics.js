'use strict';
// Calendar exposure is independent of observed trips, including days with zero trips.
const taxiMetrics = (() => {
 function calendarExposure(month=0) {
  const year=2024;
  if(month!==0 && (!Number.isInteger(month)||month<202401||month>202412))throw new RangeError('Expected a 2024 pickup month or 0 for the full year');
  const first=month?month%100-1:0,last=month?first+1:12;
  const weekdays=Array(7).fill(0);
  for(let m=first;m<last;m++)for(let day=1;day<=new Date(Date.UTC(year,m+1,0)).getUTCDate();day++) {
   const weekday=(new Date(Date.UTC(year,m,day)).getUTCDay()+6)%7;
   weekdays[weekday]++;
  }
  return {days:weekdays.reduce((a,b)=>a+b,0),weekdays};
 }
 function qualityDenominator(check,sources) {
  const requiresDistanceOrFare=['invalid_distance','invalid_base_fare','speed_above_100mph'].includes(check);
  return sources.filter(row=>!requiresDistanceOrFare||row.service!==3).reduce((total,row)=>total+row.n,0);
 }
 return {calendarExposure,qualityDenominator};
})();
if(typeof module!=='undefined')module.exports=taxiMetrics;
