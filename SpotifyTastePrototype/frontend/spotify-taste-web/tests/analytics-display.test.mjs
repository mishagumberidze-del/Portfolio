import test from 'node:test'
import assert from 'node:assert/strict'
import { calendarContext, scoreSignalRows } from '../src/analytics-display.ts'

test('rolling charts preserve calendar totals and explicitly identify context', () => {
  const points = ['2026-06', '2026-07', '2026-08', '2026-09'].map(period => ({ period, listeningMinutes: 100 }))
  const result = calendarContext(points, 'last30', '2026-08-15T12:00:00Z')
  assert.equal(result.rolling, true)
  assert.deepEqual(result.points.map(p => p.period), ['2026-07', '2026-08'])
  assert.equal(result.points[0].listeningMinutes, 100)
  assert.equal(calendarContext(points, 'year:2026', '2026-08-15T12:00:00Z').rolling, false)
  assert.equal(calendarContext(points, 'year:2025', '2026-08-15T12:00:00Z').points.length, 0)
})

test('unavailable neutral components are not presented as measured matches', () => {
  const score = { components: { featureMatch: 8, savedTrackMatch: 5, historicalBehaviorMatch: 5 }, availableSignals: ['featureMatch'] }
  assert.deepEqual(scoreSignalRows(score), [{ label: 'Feature match', value: 8 }])
  assert.deepEqual(scoreSignalRows({ ...score, availableSignals: [] }), [])
  assert.deepEqual(scoreSignalRows({ components: score.components }), [])
})
