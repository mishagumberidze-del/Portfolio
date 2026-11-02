import type { Ranking, Story } from './story-api'
import type { RankEvolution } from './api'

export function strongestMonth(journey: Story['journey']) {
  return journey.reduce((best, month, index) => month.totalMs > (journey[best]?.totalMs ?? -1) ? index : best, 0)
}
export function changePercent(before: number, after: number): number | null {
  return before > 0 ? (after - before) / before * 100 : null
}
export function artistChoices(items: Ranking[]) {
  // Stable presentation independent of listening rank; no random changes on rerender.
  return [...new Map(items.map(item => [item.id, item])).values()].sort((a, b) => a.name.localeCompare(b.name) || a.id - b.id)
}
export function monthLabel(period: string) {
  const [year, month] = period.split('-').map(Number)
  if (!year || !month || month > 12) return period
  return new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, 1)))
}
export function stepChapter(current: number, delta: number, count: number) {
  return Math.max(0, Math.min(Math.max(0, count - 1), current + delta))
}

export function rankTimeline(rows: RankEvolution[], artistId: number) {
  const periods = [...new Set(rows.map(row => row.period))].sort()
  return periods.map(period => ({ period, entry: rows.find(row => row.period === period && row.artistId === artistId) ?? null }))
}
export function rankMovement(current: number | null, previous: number | null) {
  return current !== null && previous !== null ? previous - current : null
}
export function safeSpotifyUrl(value: string | null | undefined) {
  return typeof value === 'string' && /^https:\/\/open\.spotify\.com\/(track|album|artist)\/[a-zA-Z0-9]{22}$/.test(value) ? value : null
}
