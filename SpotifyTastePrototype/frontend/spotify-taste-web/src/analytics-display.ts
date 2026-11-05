import type { ScoreResult, TimelinePoint } from './api'

// Month aggregates cannot be prorated into exact rolling-day totals.
export function calendarContext(points: TimelinePoint[], range: string, asOf: string) {
  const year = /^year:(\d{4})$/.exec(range)?.[1]
  if (year) return { points: points.filter(p => p.period.startsWith(`${year}-`)), rolling: false }
  const days = /^last(30|90|365)$/.exec(range)?.[1]
  if (!days) return { points, rolling: false }
  const end = new Date(asOf)
  if (!Number.isFinite(end.getTime())) return { points: [], rolling: true }
  const start = new Date(end)
  start.setUTCDate(start.getUTCDate() - Number(days))
  return { points: points.filter(p => p.period >= start.toISOString().slice(0, 7) && p.period <= end.toISOString().slice(0, 7)), rolling: true }
}

export function scoreSignalRows(score: ScoreResult) {
  const labels: [keyof ScoreResult['components'], string][] = [
    ['featureMatch', 'Feature match'], ['artistTasteMatch', 'Artist affinity'],
    ['recentTasteMatch', 'Recent taste match'], ['lifetimeTasteMatch', 'Lifetime taste match'],
    ['historicalBehaviorMatch', 'Observed behavior'], ['savedTrackMatch', 'Saved-track match'],
  ]
  return labels.filter(([key]) => score.availableSignals?.includes(key))
    .map(([key, label]) => ({ label, value: score.components[key] }))
}
