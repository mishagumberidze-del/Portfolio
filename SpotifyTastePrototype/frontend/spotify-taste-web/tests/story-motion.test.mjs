import test from 'node:test'
import assert from 'node:assert/strict'
import { artistInitials, raceCast, monthlyGrid, featurePoints, strongestFeatureShift } from '../src/story-motion.ts'

test('race cast preserves artist identity when ranks cross and excludes never-shown actors', () => {
  const rows = [{ artistId: 2, rank: 1 }, { artistId: 1, rank: 2 }, { artistId: 2, rank: 2 }, { artistId: 1, rank: 1 }, { artistId: 3, rank: 7 }]
  assert.deepEqual(raceCast(rows).map(row => row.artistId), [1, 2])
  assert.equal(raceCast(rows).length, 2)
  assert.equal(artistInitials('Linkin Park'), 'LP')
  assert.equal(artistInitials(''), '?')
})
test('heatmap distinguishes zero covered listening from months outside source coverage', () => {
  const data = { summary: { firstListenUtc: '2025-02-14T00:00:00Z', lastListenUtc: '2025-04-05T00:00:00Z' }, journey: [{ period: '2025-01', totalMs: 0 }, { period: '2025-02', totalMs: 60000 }, { period: '2025-03', totalMs: 0 }, { period: '2025-04', totalMs: 120000 }] }
  const grid = monthlyGrid(data)
  assert.equal(grid[0].months[0].totalMs, null)
  assert.equal(grid[0].months[1].totalMs, 60000)
  assert.equal(grid[0].months[2].totalMs, 0)
  assert.equal(grid[0].months[4].totalMs, null)
  assert.equal(grid[0].months.length, 12)
  const missing = monthlyGrid({ ...data, journey: data.journey.filter(month => month.period !== '2025-03') })
  assert.equal(missing[0].months[2].totalMs, 0, 'no events inside imported range means zero, including absent aggregate rows')
})
test('feature chart retains genuine zero values and leaves coverage gaps unfilled', () => {
  const points = featurePoints([{ year: 2024, axes: [{ name: 'Energy', value: 0, coveragePercent: 50 }] }, { year: 2025, axes: [{ name: 'Energy', value: 90, coveragePercent: 49 }] }, { year: 2026, axes: [{ name: 'Energy', value: 90, coveragePercent: 80 }] }], 'Energy')
  assert.deepEqual(points.map(point => point.value), [0, null, 90])
  assert.equal(strongestFeatureShift(points), null)
  const missingYear = featurePoints([{ year: 2024, axes: [{ name: 'Energy', value: 0, coveragePercent: 50 }] }, { year: 2026, axes: [{ name: 'Energy', value: 90, coveragePercent: 80 }] }], 'Energy')
  assert.deepEqual(missingYear.map(point => point.value), [0, null, 90], 'missing years must break chart lines')
})
test('feature insights require an adjacent covered-year change of at least five', () => {
  assert.deepEqual(strongestFeatureShift([{ year: 2024, value: 80 }, { year: 2025, value: 65 }, { year: 2026, value: 69 }]), { from: 2024, to: 2025, change: -15 })
  assert.equal(strongestFeatureShift([{ year: 2024, value: 20 }, { year: 2026, value: 70 }]), null)
  assert.equal(strongestFeatureShift([{ year: 2024, value: 20 }, { year: 2025, value: 24 }]), null)
})
