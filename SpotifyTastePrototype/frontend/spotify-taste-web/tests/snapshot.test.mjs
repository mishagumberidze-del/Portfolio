import test from 'node:test'
import assert from 'node:assert/strict'
import { snapshotResponse } from '../src/snapshot.ts'

// Small test-only response fixtures; these are never imported into the UI.
const fixture = () => ({ ranges: { lifetime: { overview: { listeningEvents: 3 }, tracks: [{ trackId: 1 }, { trackId: 2 }] } }, stories: { lifetime: { year: null } }, evolution: { year: [{ rank: 1 }] }, catalog: [], discover: { neverHeard: [] }, evaluation: null })
test('snapshot returns exact stored fields and limits without scaling values', () => {
  const data = fixture()
  assert.deepEqual(snapshotResponse(data, '/api/analytics/overview?range=lifetime'), { listeningEvents: 3 })
  assert.deepEqual(snapshotResponse(data, '/api/analytics/top-tracks?limit=1&range=lifetime'), [{ trackId: 1 }])
  assert.equal(data.ranges.lifetime.tracks.length, 2)
})
test('snapshot refuses writes instead of generating a response', () => {
  for (const path of ['/api/model/rebuild', '/api/discover/generate', '/api/scorer', '/api/feedback', '/api/import/spotify']) {
    assert.throws(() => snapshotResponse(fixture(), path, 'POST'), /Read-only/)
  }
})
test('snapshot missing dates and endpoints fail without a synthetic fallback', () => {
  for (const path of ['/api/analytics/overview?year=2040', '/api/story?year=2040', '/api/spotify/status']) {
    assert.throws(() => snapshotResponse(fixture(), path), /No substitute data/)
  }
})
test('empty stored discovery and evaluation are honest empty states', () => {
  assert.deepEqual(snapshotResponse(fixture(), '/api/discover'), { neverHeard: [] })
  assert.equal(snapshotResponse(fixture(), '/api/evaluation'), null)
})
