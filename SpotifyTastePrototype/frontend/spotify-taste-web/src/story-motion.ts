import type { RankEvolution } from './api'
import type { Story } from './story-api'

export function raceCast(rows: RankEvolution[], limit = 6) {
  return [...new Map(rows.filter(row => row.rank <= limit).map(row => [row.artistId, row])).values()].sort((a, b) => a.artistId - b.artistId)
}
export function artistInitials(name: string) {
  return name.trim().split(/\s+/).slice(0, 2).map(word => Array.from(word)[0] ?? '').join('').toLocaleUpperCase() || '?'
}
export function monthlyGrid(data: Story) {
  const first = data.summary.firstListenUtc?.slice(0, 7), last = data.summary.lastListenUtc?.slice(0, 7)
  const firstYear = first ? Number(first.slice(0, 4)) : null, lastYear = last ? Number(last.slice(0, 4)) : null
  const years = firstYear !== null && lastYear !== null ? Array.from({ length: lastYear - firstYear + 1 }, (_, i) => firstYear + i) : []
  return years.map(year => ({ year, months: Array.from({ length: 12 }, (_, i) => {
    const period = `${year}-${String(i + 1).padStart(2, '0')}`
    const month = data.journey.find(item => item.period === period)
    return { period, totalMs: !first || !last || period < first || period > last ? null : month?.totalMs ?? 0, artist: month?.definingArtist ?? null }
  }) }))
}
export function featurePoints(eras: Story['featureEvolution'], axis: string) {
  if (!eras.length) return []
  const first = Math.min(...eras.map(era => era.year)), last = Math.max(...eras.map(era => era.year))
  return Array.from({ length: last - first + 1 }, (_, i) => first + i).map(year => {
    const found = eras.find(era => era.year === year)?.axes.find(item => item.name === axis)
    return { year, value: found && found.value !== null && found.coveragePercent >= 50 ? found.value : null, coverage: found?.coveragePercent ?? 0 }
  })
}
export function strongestFeatureShift(points: ReturnType<typeof featurePoints>): { from: number; to: number; change: number } | null {
  let best: { from: number; to: number; change: number } | null = null
  points.forEach((point, i) => {
    const prior = points[i - 1]
    if (!prior || point.year !== prior.year + 1 || point.value === null || prior.value === null) return
    const change = point.value - prior.value
    if (Math.abs(change) >= 5 && (!best || Math.abs(change) > Math.abs(best.change))) best = { from: prior.year, to: point.year, change }
  })
  return best
}
