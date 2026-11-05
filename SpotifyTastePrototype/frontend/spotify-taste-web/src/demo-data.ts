import type { BehaviorAnalytics, Diagnostics, Discover, ModelValidation, Overview, RankEvolution, TimelinePoint, TopAlbum, TopArtist, TopTrack } from './api'
import type { Ranking, SoundAxis, Story } from './story-api'

// Fictional event fixture, never personal data. Every displayed count, duration,
// rank and rate is aggregated from these events; no independent scaling factors.
const artistNames = ['Neon Harbor', 'Paper Satellites', 'Mira Vale', 'Northbound Static', 'Juniper Room', 'Low Tide Cinema', 'Glass Atlas', 'Afterlight', 'Soft Geometry', 'Night Orchard', 'Echo District', 'Small Hours']
const trackNames = ['Signals at Dawn', 'Borrowed Light', 'Parallel Lines', 'Slow Comet', 'Every Open Window', 'City in Reverse', 'Halfway Home', 'Blue Frequency', 'Paper Moons', 'Quiet Engines', 'Sunday Static', 'Last Train North']
const albumNames = ['Weather Systems', 'Rooms in Motion', 'A Map of Maybe', 'Night Transit', 'The Long Way Round', 'Soft Focus', 'Signal Gardens', 'Open Late', 'Small Worlds', 'After the Rain', 'Late Letters', 'Morning Lines']
export const DEMO_AS_OF = '2026-08-29T00:00:00Z' // Exclusive end; last complete fixture date is August 28.
export const demoCatalog = Array.from({ length: 24 }, (_, i) => ({
  id: i + 1, name: trackNames[i % 12] + (i >= 12 ? ' II' : ''), artistId: Math.floor(i / 2) + 1,
  albumId: Math.floor(i / 2) + 1, durationMs: i % 7 === 0 ? null : (160 + i % 8 * 20) * 1000, saved: i % 4 === 0,
}))
type Event = { trackId: number; at: string; ms: number; skipped: boolean }
export const demoEvents: Event[] = []
const dayMs = 86_400_000
for (let day = 0, at = Date.UTC(2022, 0, 3); at < Date.parse(DEMO_AS_OF); day++, at += dayMs) {
  if (day % 4 === 0) continue
  const era = new Date(at).getUTCFullYear() - 2022
  const availableTracks = Math.min(24, 8 + era * 4)
  for (let slot = 0; slot < 3 + day % 5; slot++) {
    const track = demoCatalog[(day * 7 + slot * 3 + era) % availableTracks]
    const duration = track.durationMs ?? 220_000
    const ms = [5000, 20000, 60000, Math.round(duration * .9), duration][(day + slot) % 5]
    demoEvents.push({ trackId: track.id, at: new Date(at + (8 + slot * 2) * 3_600_000).toISOString(), ms, skipped: (day + slot) % 11 === 0 })
  }
}
const trackOf = (e: Event) => demoCatalog[e.trackId - 1]
const sumMs = (rows: Event[]) => rows.reduce((n, e) => n + e.ms, 0)
const unique = <T,>(values: T[]) => [...new Set(values)]
const round = (n: number) => Math.round(n * 10) / 10
const percent = (n: number, d: number) => d ? round(100 * n / d) : 0
const qualified = (e: Event) => e.ms >= 30_000 || (trackOf(e).durationMs !== null && e.ms >= trackOf(e).durationMs! * .5)
const skipped = (e: Event) => e.skipped || e.ms < 10_000
const first = (rows: Event[]) => rows.length ? rows[0].at : null
const last = (rows: Event[]) => rows.length ? rows[rows.length - 1].at : null
function groups<K>(rows: Event[], key: (e: Event) => K) {
  const result = new Map<K, Event[]>()
  for (const row of rows) { const k = key(row); const values = result.get(k) ?? []; values.push(row); result.set(k, values) }
  return result
}
const ranked = (rows: Event[], key: (e: Event) => number) => [...groups(rows, key)].sort((a, b) => sumMs(b[1]) - sumMs(a[1]) || b[1].filter(qualified).length - a[1].filter(qualified).length || a[0] - b[0])
const limitRows = <T,>(rows: T[], limit: number) => rows.slice(0, Math.max(1, Math.min(500, Math.trunc(limit) || 20)))
export function eventsFor(range: string): Event[] {
  if (range === 'lifetime') return demoEvents
  const year = /^year:(\d{4})$/.exec(range)?.[1]
  if (year) return demoEvents.filter(e => e.at.startsWith(year + '-'))
  const days = /^last(30|90|365)$/.exec(range)?.[1]
  if (!days) return []
  const start = Date.parse(DEMO_AS_OF) - Number(days) * dayMs
  return demoEvents.filter(e => Date.parse(e.at) >= start && Date.parse(e.at) < Date.parse(DEMO_AS_OF))
}
export function overviewFor(range: string): Overview {
  const rows = eventsFor(range), ms = sumMs(rows)
  return {
    totalListeningMinutes: Math.floor(ms / 60_000), totalListeningHours: round(ms / 3_600_000), totalListeningDays: unique(rows.map(e => e.at.slice(0, 10))).length,
    listeningEvents: rows.length, qualifiedPlays: rows.filter(qualified).length, uniqueTracks: unique(rows.map(e => e.trackId)).length,
    uniqueArtists: unique(rows.map(e => trackOf(e).artistId)).length, uniqueAlbums: unique(rows.map(e => trackOf(e).albumId)).length,
    savedLibraryAvailable: true, savedTracks: demoCatalog.filter(t => t.saved).length, earliestListenUtc: first(rows), latestListenUtc: last(rows),
  }
}
function behavior(rows: Event[]): BehaviorAnalytics {
  const known = rows.filter(e => trackOf(e).durationMs !== null)
  return { listeningEvents: rows.length, skipRate: percent(rows.filter(skipped).length, rows.length), shortListenRate: percent(rows.filter(e => e.ms < 10_000).length, rows.length),
    repeatRate: percent(rows.length - unique(rows.map(e => e.trackId)).length, rows.length), eventsWithKnownDuration: known.length,
    averageListenPercentage: known.length ? round(known.reduce((n, e) => n + Math.min(125, 100 * e.ms / trackOf(e).durationMs!), 0) / known.length) : null,
    completionRateEstimate: known.length ? percent(known.filter(e => e.ms >= trackOf(e).durationMs! * .9).length, known.length) : null }
}
export const behaviorFor = (range: string) => behavior(eventsFor(range))
export function tracksFor(range: string, limit: number): TopTrack[] {
  return limitRows(ranked(eventsFor(range), e => e.trackId).map(([id, rows]) => {
    const track = demoCatalog[id - 1]
    return { trackId: id, track: track.name, artist: artistNames[track.artistId - 1], album: albumNames[track.albumId - 1],
      rawEvents: rows.length, qualifiedPlays: rows.filter(qualified).length, listeningMinutes: round(sumMs(rows) / 60_000), averageListenSeconds: round(sumMs(rows) / rows.length / 1000),
      skipRate: percent(rows.filter(skipped).length, rows.length), firstListenUtc: first(rows)!, lastListenUtc: last(rows)!, saved: track.saved }
  }), limit)
}
export function artistsFor(range: string, limit: number): TopArtist[] {
  return limitRows(ranked(eventsFor(range), e => trackOf(e).artistId).map(([id, rows]) => ({ artistId: id, artist: artistNames[id - 1], listeningMinutes: round(sumMs(rows) / 60_000),
    qualifiedPlays: rows.filter(qualified).length, rawPlays: rows.length, uniqueTracks: unique(rows.map(e => e.trackId)).length,
    skipRate: percent(rows.filter(skipped).length, rows.length), firstListenUtc: first(rows)!, lastListenUtc: last(rows)! })), limit)
}
export function albumsFor(range: string, limit: number): TopAlbum[] {
  return limitRows(ranked(eventsFor(range), e => trackOf(e).albumId).map(([id, rows]) => ({ albumId: id, album: albumNames[id - 1], artist: artistNames[id - 1],
    listeningMinutes: round(sumMs(rows) / 60_000), qualifiedPlays: rows.filter(qualified).length, uniqueTracks: unique(rows.map(e => e.trackId)).length })), limit)
}
const firstTrack = new Map([...groups(demoEvents, e => e.trackId)].map(([id, rows]) => [id, rows[0].at]))
const firstArtist = new Map([...groups(demoEvents, e => trackOf(e).artistId)].map(([id, rows]) => [id, rows[0].at]))
function timeline(size: number): TimelinePoint[] {
  return [...groups(demoEvents, e => e.at.slice(0, size))].map(([period, rows]) => ({ period, listeningMinutes: round(sumMs(rows) / 60_000),
    uniqueArtists: unique(rows.map(e => trackOf(e).artistId)).length,
    newArtists: unique(rows.map(e => trackOf(e).artistId)).filter(id => firstArtist.get(id)!.startsWith(period)).length,
    newTracks: unique(rows.map(e => e.trackId)).filter(id => firstTrack.get(id)!.startsWith(period)).length }))
}
export const demoTimeline = timeline(7)
export const demoYearly = timeline(4)
export function evolutionFor(grain: string): RankEvolution[] {
  const period = (e: Event) => grain === 'year' ? e.at.slice(0, 4) : grain === 'quarter' ? `${e.at.slice(0, 4)}-Q${Math.ceil(Number(e.at.slice(5, 7)) / 3)}` : e.at.slice(0, 7)
  return [...groups(demoEvents, period)].flatMap(([period, rows]) => ranked(rows, e => trackOf(e).artistId).map(([id, entries], i) => ({ period, artistId: id, artist: artistNames[id - 1],
    rank: i + 1, listeningMinutes: round(sumMs(entries) / 60_000), qualifiedPlays: entries.filter(qualified).length, artworkUrl: null })))
}
const unavailableAxes = (): SoundAxis[] => ['Energy', 'Danceability', 'Acousticness', 'Instrumentalness', 'Valence', 'Tempo'].map(name => ({ name, value: null, unit: name === 'Tempo' ? 'BPM' : '/100', coveragePercent: 0, personalPercentile: null }))
function storyRankings(rows: Event[], kind: 'song' | 'artist' | 'album', previous: Event[]): Ranking[] {
  const key = (e: Event) => kind === 'song' ? e.trackId : kind === 'artist' ? trackOf(e).artistId : trackOf(e).albumId
  const prior = ranked(previous, key).map(([id]) => id)
  return ranked(rows, key).slice(0, 5).map(([id, entries]) => {
    const track = trackOf(entries[0]); const previousIndex = prior.indexOf(id)
    const peakMonth = [...groups(entries, e => e.at.slice(0, 7))].sort((a, b) => sumMs(b[1]) - sumMs(a[1]) || a[0].localeCompare(b[0]))[0][0]
    return { id, name: kind === 'song' ? track.name : kind === 'artist' ? artistNames[id - 1] : albumNames[id - 1], artist: kind === 'artist' ? null : artistNames[track.artistId - 1],
      album: kind === 'song' ? albumNames[track.albumId - 1] : null, artworkUrl: null, totalMs: sumMs(entries), events: entries.length,
      qualifiedPlays: entries.filter(qualified).length, firstListenUtc: kind === 'song' ? firstTrack.get(id)! : first(entries)!, peakMonth,
      previousRank: previousIndex < 0 ? null : previousIndex + 1, spotifyUrl: null }
  })
}
export function storyFor(selectedYear: number | null): Story {
  const rows = eventsFor(selectedYear === null ? 'lifetime' : `year:${selectedYear}`)
  const previous = selectedYear === null ? [] : eventsFor(`year:${selectedYear - 1}`)
  const previousMs = sumMs(previous), totalMs = sumMs(rows), stats = overviewFor(selectedYear === null ? 'lifetime' : `year:${selectedYear}`)
  const months = [...groups(rows, e => e.at.slice(0, 7))].map(([period, entries]) => ({ period, totalMs: sumMs(entries), definingArtist: artistNames[ranked(entries, e => trackOf(e).artistId)[0][0] - 1] }))
  const journey = selectedYear === null ? months : Array.from({ length: 12 }, (_, i) => {
    const period = `${selectedYear}-${String(i + 1).padStart(2, '0')}`
    return months.find(m => m.period === period) ?? { period, totalMs: 0, definingArtist: null }
  })
  const busiest = months.slice().sort((a, b) => b.totalMs - a.totalMs || a.period.localeCompare(b.period))[0]
  return { year: selectedYear, availableYears: demoYearly.map(p => Number(p.period)).reverse(),
    summary: { totalMs, events: rows.length, qualifiedPlays: stats.qualifiedPlays, activeDays: stats.totalListeningDays, recordings: stats.uniqueTracks, artists: stats.uniqueArtists, albums: stats.uniqueAlbums, firstListenUtc: first(rows), lastListenUtc: last(rows) },
    topArtists: storyRankings(rows, 'artist', previous), topSongs: storyRankings(rows, 'song', previous), topAlbums: storyRankings(rows, 'album', previous), journey,
    newRecordings: unique(rows.map(e => e.trackId)).filter(id => selectedYear === null || firstTrack.get(id)!.startsWith(String(selectedYear))).length,
    newArtists: unique(rows.map(e => trackOf(e).artistId)).filter(id => selectedYear === null || firstArtist.get(id)!.startsWith(String(selectedYear))).length,
    behavior: behavior(rows), soundProfile: unavailableAxes(), featureEvolution: demoYearly.map(p => ({ year: Number(p.period), axes: unavailableAxes() })), featureInsights: [],
    comparison: previous.length && selectedYear !== null ? { year: selectedYear - 1, totalMs: previousMs, changePercent: previousMs ? round(100 * (totalMs - previousMs) / previousMs) : null, note: 'Fictional calendar-year event totals, not annualized. 2022 begins January 3 and 2026 ends August 28.' } : null,
    facts: busiest ? [{ title: 'Busiest fixture month', detail: `${busiest.period} has the most listening in this selection: ${round(busiest.totalMs / 3_600_000)} hours. Its leading artist is ${busiest.definingArtist}.` }] : [] }
}
export const demoDiagnostics: Diagnostics = {
  databaseProvider: 'Fictional event fixture', databaseName: 'No runtime database', spotifyDataFiles: ['Deterministic fictional events through 2026-08-28'],
  historyRowsImported: demoEvents.length, savedLibraryAvailable: true, savedTrackCount: demoCatalog.filter(t => t.saved).length,
  uniqueTracks: firstTrack.size, uniqueArtists: firstArtist.size, uniqueAlbums: unique(demoCatalog.map(t => t.albumId)).length,
  tracksWithSpotifyIds: 0, tracksMappedToReccoBeats: 0, tracksWithFeatures: 0, tracksMissingFeatures: demoCatalog.length,
  reccoBeatsNotFound: 0, preferenceModelVersion: 'Not fitted in synthetic demo', candidateProvider: 'Unavailable in synthetic demo', recommendationAlgorithmVersion: 'Not run in synthetic demo',
}
// The fixture contains no measured audio features, fitted model or human ratings.
// Returning unavailable evidence avoids inventing predictive accuracy or taste scores.
export const demoValidation: ModelValidation = { trackCount: 0, positiveExamples: 0, negativeExamples: 0, meanPositiveScore: null, meanNegativeScore: null, pairwiseAccuracy: null, modelVersion: 'Not fitted', validationMethod: 'Unavailable: no model was fitted to the fictional fixture' }
export const demoDiscover: Discover = { savedLibraryAvailable: true, notSavedMessage: 'Recommendations require a fitted model and a candidate provider; neither runs in this analytics demo.', neverHeard: [], notSaved: [] }
export const demoEvaluation = null
export function applyDemoFeedback() { throw new Error('No recommendation evaluation has been run in the analytics demo.') }
