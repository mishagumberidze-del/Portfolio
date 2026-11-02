import { test } from 'node:test'
import assert from 'node:assert/strict'
import { formatCount, formatMetric, formatChartValue } from '../src/number-format.ts'

test('counts have consistent comma separators and integer rounding', () => {
  for (const [value, expected] of [[0, '0'], [999, '999'], [1000, '1,000'], [61110, '61,110'], [166440.4, '166,440'], [1000000, '1,000,000'], [-1234, '-1,234']]) {
    assert.equal(formatCount(value), expected)
  }
})
test('metrics retain decimal precision instead of changing scores or percentages', () => {
  assert.equal(formatMetric(2744.125), '2,744.125')
  assert.equal(formatMetric(8.4), '8.4')
  assert.equal(formatMetric(35.7), '35.7')
})
test('chart formatting handles numbers, ranges and nonnumeric labels', () => {
  assert.equal(formatChartValue(11403), '11,403')
  assert.equal(formatChartValue([1000, 2000]), '1,000 – 2,000')
  assert.equal(formatChartValue('2026'), '2026')
  assert.equal(formatChartValue(undefined), '—')
})
