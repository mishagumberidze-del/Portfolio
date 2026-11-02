import { test } from 'node:test'
import assert from 'node:assert/strict'
import { strongestMonth, changePercent, artistChoices, monthLabel, stepChapter, rankTimeline, rankMovement, safeSpotifyUrl } from '../src/story-math.ts'

test('busiest month uses actual listening time, keeps first on ties and handles empty data', () => {
  assert.equal(strongestMonth([]), 0)
  assert.equal(strongestMonth([{ totalMs: 0 }, { totalMs: 120 }, { totalMs: 120 }]), 1)
})
test('year comparisons do not invent percentages without a baseline', () => {
  assert.equal(changePercent(0, 100), null)
  assert.equal(changePercent(100, 125), 25)
  assert.equal(changePercent(100, 0), -100)
  assert.equal(changePercent(100, 100), 0)
})
test('quiz options are stable, distinct and do not change original rank order', () => {
  const original = [{ id: 2, name: 'Z artist' }, { id: 1, name: 'A artist' }, { id: 2, name: 'Z artist' }]
  assert.deepEqual(artistChoices(original).map(a => a.id), [1, 2])
  assert.equal(original[0].id, 2)
  assert.deepEqual(artistChoices(original), artistChoices(original))
})
test('chapter movement is bounded for manual navigation and playback', () => {
  assert.equal(stepChapter(0, -1, 15), 0)
  assert.equal(stepChapter(14, 1, 15), 14)
  assert.equal(stepChapter(3, 1, 15), 4)
  assert.equal(stepChapter(0, 1, 0), 0)
})
test('month labels use UTC and preserve invalid input instead of inventing a date', () => {
  assert.match(monthLabel('2026-01'), /2026/)
  assert.equal(monthLabel('2026-99'), '2026-99')
})
test('artist timeline preserves top-20 gaps rather than zero or fabricated ranks', () => {
  const rows = [{ period: '2025', artistId: 1, rank: 3 }, { period: '2024', artistId: 2, rank: 1 }, { period: '2023', artistId: 1, rank: 10 }]
  const timeline = rankTimeline(rows, 1)
  assert.deepEqual(timeline.map(point => point.period), ['2023', '2024', '2025'])
  assert.deepEqual(timeline.map(point => point.entry?.rank ?? null), [10, null, 3])
  assert.equal(rankMovement(3, null), null)
  assert.equal(rankMovement(null, 3), null)
  assert.equal(rankMovement(3, 10), 7)
  assert.equal(rankMovement(10, 3), -7)
  assert.equal(rankMovement(3, 3), 0)
})
test('Spotify links accept only exact known provider resource URLs', () => {
  for (const kind of ['track', 'artist', 'album']) {
    const url = `https://open.spotify.com/${kind}/${'a'.repeat(22)}`
    assert.equal(safeSpotifyUrl(url), url)
  }
  for (const value of [null, undefined, '', 'javascript:alert(1)', 'https://open.spotify.com.evil/track/' + 'a'.repeat(22), 'https://open.spotify.com/track/123']) assert.equal(safeSpotifyUrl(value), null)
})
