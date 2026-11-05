const test=require('node:test');
const assert=require('node:assert/strict');
const {calendarExposure,qualityDenominator}=require('../site/metrics.js');

test('calendar normalization includes leap day and unequal weekday exposure',()=>{
 assert.deepEqual(calendarExposure(202402),{days:29,weekdays:[4,4,4,5,4,4,4]});
 assert.deepEqual(calendarExposure(202401),{days:31,weekdays:[5,5,5,4,4,4,4]});
 assert.deepEqual(calendarExposure(),{days:366,weekdays:[53,53,52,52,52,52,52]});
 // Equal daily demand must remain equal despite different monthly totals.
 assert.equal(2900/calendarExposure(202402).days,3100/calendarExposure(202401).days);
 // 500 Monday / 5 Mondays and 400 Thursday / 4 Thursdays are equal exposure.
 assert.equal(500/calendarExposure(202401).weekdays[0],400/calendarExposure(202401).weekdays[3]);
 assert.throws(()=>calendarExposure(202413),RangeError);
});

test('unavailable FHV fields cannot dilute quality failure percentages',()=>{
 const sources=[{service:1,n:100},{service:2,n:50},{service:3,n:850},{service:4,n:100}];
 for(const check of ['invalid_distance','invalid_base_fare','speed_above_100mph']) {
  assert.equal(qualityDenominator(check,sources),250);
  assert.equal(qualityDenominator(check,[sources[2]]),0);
 }
 assert.equal(qualityDenominator('invalid_duration',sources),1100);
 assert.equal(qualityDenominator('unmatched_pickup_zone',sources),1100);
 assert.equal(qualityDenominator('invalid_distance',[]),0);
});
