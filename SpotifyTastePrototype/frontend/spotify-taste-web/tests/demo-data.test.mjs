import test from 'node:test'
import assert from 'node:assert/strict'
import { DEMO_AS_OF, demoEvents, demoCatalog, demoTimeline, demoYearly, overviewFor, behaviorFor,
  tracksFor, artistsFor, albumsFor, storyFor, demoValidation, demoDiscover, demoEvaluation } from '../src/demo-data.ts'

test('demo overview, rankings and stories reconcile to the same fictional events', () => {
  for (const range of ['lifetime', 'last30', 'last90', 'last365', 'year:2022', 'year:2026', 'year:2027']) {
    const days = /^last(\d+)$/.exec(range)?.[1]
    const rows = demoEvents.filter(e => range === 'lifetime' ||
      (days ? Date.parse(e.at) >= Date.parse(DEMO_AS_OF) - Number(days) * 86400000 && e.at < DEMO_AS_OF : e.at.startsWith(range.slice(5))))
    const totalMs = rows.reduce((n,e) => n + e.ms, 0)
    const overview = overviewFor(range)
    assert.equal(overview.listeningEvents, rows.length, range)
    assert.equal(overview.totalListeningMinutes, Math.floor(totalMs / 60000), range)
    assert.equal(overview.totalListeningDays, new Set(rows.map(e => e.at.slice(0,10))).size, range)
    if (days) assert.ok(overview.totalListeningDays <= Number(days))
    const tracks = tracksFor(range,500), artists = artistsFor(range,500), albums = albumsFor(range,500)
    assert.equal(tracks.reduce((n,t) => n+t.rawEvents,0), rows.length)
    assert.equal(artists.reduce((n,t) => n+t.rawPlays,0), rows.length)
    for (const list of [tracks,artists,albums]) {
      assert.equal(list.reduce((n,t) => n+t.qualifiedPlays,0), overview.qualifiedPlays)
      assert.ok(Math.abs(list.reduce((n,t) => n+t.listeningMinutes,0) - totalMs/60000) <= list.length*.051)
    }
    assert.equal(new Set(artists.map(a => a.artistId)).size, artists.length)
    if (range === 'lifetime' || range.startsWith('year:')) {
      const story=storyFor(range === 'lifetime' ? null : Number(range.slice(5)))
      assert.equal(story.summary.totalMs,totalMs)
      assert.equal(story.journey.reduce((n,m) => n+m.totalMs,0),totalMs)
      assert.equal(story.summary.events,rows.length)
    }
  }
})

test('demo calendar periods and behavior use observed events and eligible denominators', () => {
  for(const period of [...demoTimeline,...demoYearly]) {
    const rows=demoEvents.filter(e=>e.at.startsWith(period.period))
    assert.ok(Math.abs(period.listeningMinutes-rows.reduce((n,e)=>n+e.ms,0)/60000)<=.051)
  }
  const known=demoEvents.filter(e=>demoCatalog[e.trackId-1].durationMs!==null)
  const behavior=behaviorFor('lifetime')
  assert.equal(behavior.eventsWithKnownDuration,known.length)
  const completions=known.filter(e=>e.ms>=.9*demoCatalog[e.trackId-1].durationMs).length
  assert.ok(Math.abs(behavior.completionRateEstimate-100*completions/known.length)<=.051)
  assert.equal(behaviorFor('year:2027').completionRateEstimate,null)
})

test('demo does not invent fitted-model accuracy, audio evidence or recommendations', () => {
  assert.equal(demoValidation.pairwiseAccuracy,null)
  assert.equal(demoValidation.meanPositiveScore,null)
  assert.equal(demoValidation.meanNegativeScore,null)
  assert.deepEqual(demoDiscover.neverHeard,[])
  assert.deepEqual(demoDiscover.notSaved,[])
  assert.equal(demoDiscover.savedLibraryAvailable,overviewFor('lifetime').savedLibraryAvailable)
  assert.equal(demoEvaluation,null)
  assert.ok(storyFor(null).soundProfile.every(axis=>axis.value===null && axis.coveragePercent===0))
})
