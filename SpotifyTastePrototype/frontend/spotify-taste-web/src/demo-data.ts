import type { BehaviorAnalytics, Diagnostics, Discover, ModelValidation, Overview, RankEvolution, Recommendation, ScoreResult, TimelinePoint, TopAlbum, TopArtist, TopTrack } from './api'
import type { Story } from './story-api'

// Deliberately synthetic portfolio data. Names, identifiers and aggregates do not
// come from the owner's Spotify export; only the response shapes mirror the API.
const artistNames = ['Neon Harbor', 'Paper Satellites', 'Mira Vale', 'Northbound Static', 'Juniper Room', 'Low Tide Cinema', 'Glass Atlas', 'Afterlight', 'Soft Geometry', 'Night Orchard', 'Echo District', 'Small Hours']
const trackNames = ['Signals at Dawn', 'Borrowed Light', 'Parallel Lines', 'Slow Comet', 'Every Open Window', 'City in Reverse', 'Halfway Home', 'Blue Frequency', 'Paper Moons', 'Quiet Engines', 'Sunday Static', 'Last Train North']
const albumNames = ['Weather Systems', 'Rooms in Motion', 'A Map of Maybe', 'Night Transit', 'The Long Way Round', 'Soft Focus', 'Signal Gardens', 'Open Late']
const years = [2022, 2023, 2024, 2025, 2026]
const yearMinutes = [18_420, 22_780, 27_340, 34_810, 25_960]

export const demoTimeline: TimelinePoint[] = years.flatMap((year, yi) => Array.from({ length: 12 }, (_, month) => ({
  period: `${year}-${String(month + 1).padStart(2, '0')}`,
  listeningMinutes: Math.round(yearMinutes[yi] / 12 * (.7 + ((month * 7 + yi * 3) % 8) / 10)),
  uniqueArtists: 120 + yi * 28 + (month * 11) % 55,
  newArtists: Math.max(8, 42 - yi * 5 + (month * 3) % 13),
  newTracks: Math.max(24, 132 - yi * 14 + (month * 17) % 48),
}))).filter(point => point.period <= '2026-08')

export const demoYearly: TimelinePoint[] = years.map((year, index) => ({
  period: String(year), listeningMinutes: yearMinutes[index], uniqueArtists: 640 + index * 155,
  newArtists: 510 - index * 62, newTracks: 1_480 - index * 145,
}))

const topArtists: TopArtist[] = artistNames.map((artist, index) => ({
  artistId: index + 1, artist, listeningMinutes: 5_840 - index * 325, qualifiedPlays: 1_430 - index * 91,
  rawPlays: 1_610 - index * 96, uniqueTracks: 36 - index, skipRate: Number((8 + index * .9).toFixed(1)),
  firstListenUtc: `${2022 + index % 3}-0${index % 8 + 1}-12T10:00:00Z`, lastListenUtc: '2026-08-24T18:32:00Z',
}))

const topTracks: TopTrack[] = trackNames.map((track, index) => ({
  trackId: index + 1, track, artist: artistNames[index], album: albumNames[index % albumNames.length],
  rawEvents: 286 - index * 13, qualifiedPlays: 248 - index * 12, listeningMinutes: 1_090 - index * 55,
  averageListenSeconds: 228 - index * 3, skipRate: Number((4 + index * .8).toFixed(1)),
  firstListenUtc: `${2022 + index % 3}-04-12T10:00:00Z`, lastListenUtc: '2026-08-26T21:10:00Z', saved: index < 8,
}))

const topAlbums: TopAlbum[] = albumNames.map((album, index) => ({
  albumId: index + 1, album, artist: artistNames[index], listeningMinutes: 3_420 - index * 286,
  qualifiedPlays: 790 - index * 58, uniqueTracks: 14 - index % 5,
}))

const rangeScale: Record<string, number> = { lifetime: 1, last365: .27, last90: .075, last30: .026, 'year:2026': .205, 'year:2025': .275, 'year:2024': .216, 'year:2023': .18, 'year:2022': .145, 'year:2021': 0 }
const scaleFor = (range: string) => rangeScale[range] ?? 1
const scaled = (value: number, factor: number) => Math.round(value * factor)

export function overviewFor(range: string): Overview {
  const factor = scaleFor(range)
  return {
    totalListeningMinutes: scaled(129_310, factor), totalListeningHours: scaled(2_155, factor), totalListeningDays: scaled(1_018, Math.min(1, factor * 1.7)),
    listeningEvents: scaled(46_280, factor), qualifiedPlays: scaled(34_940, factor), uniqueTracks: scaled(8_640, Math.sqrt(factor)),
    uniqueArtists: scaled(2_180, Math.sqrt(factor)), uniqueAlbums: scaled(5_420, Math.sqrt(factor)), savedLibraryAvailable: true,
    savedTracks: 612, earliestListenUtc: factor ? '2022-01-03T08:15:00Z' : null, latestListenUtc: factor ? '2026-08-28T21:42:00Z' : null,
  }
}

export function behaviorFor(range: string): BehaviorAnalytics {
  const factor = scaleFor(range)
  return { listeningEvents: scaled(46_280, factor), skipRate: 11.8, shortListenRate: 9.4, repeatRate: 70.4, averageListenPercentage: 81.2, completionRateEstimate: 67.6, eventsWithKnownDuration: scaled(39_740, factor) }
}

export const tracksFor = (range: string, limit: number) => topTracks.slice(0, limit).map((row, i) => ({ ...row, listeningMinutes: scaled(row.listeningMinutes, Math.max(.08, scaleFor(range))), rawEvents: scaled(row.rawEvents, Math.max(.08, scaleFor(range))), qualifiedPlays: scaled(row.qualifiedPlays, Math.max(.08, scaleFor(range))), lastListenUtc: `2026-08-${String(28 - i).padStart(2, '0')}T18:00:00Z` }))
export const artistsFor = (range: string, limit: number) => topArtists.slice(0, limit).map((row, i) => ({ ...row, listeningMinutes: scaled(row.listeningMinutes, Math.max(.08, scaleFor(range))), qualifiedPlays: scaled(row.qualifiedPlays, Math.max(.08, scaleFor(range))), rawPlays: scaled(row.rawPlays, Math.max(.08, scaleFor(range))), artistId: range === 'last90' && i < 3 ? [3, 1, 6][i] : row.artistId, artist: range === 'last90' && i < 3 ? [artistNames[2], artistNames[0], artistNames[5]][i] : row.artist }))
export const albumsFor = (range: string, limit: number) => topAlbums.slice(0, limit).map(row => ({ ...row, listeningMinutes: scaled(row.listeningMinutes, Math.max(.08, scaleFor(range))), qualifiedPlays: scaled(row.qualifiedPlays, Math.max(.08, scaleFor(range))) }))

export function evolutionFor(grain: string): RankEvolution[] {
  const periods = grain === 'month' ? demoTimeline.slice(-8).map(x => x.period) : grain === 'quarter' ? ['2025 Q1', '2025 Q2', '2025 Q3', '2025 Q4', '2026 Q1', '2026 Q2'] : years.map(String)
  return periods.flatMap((period, pi) => artistNames.slice(0, 6).map((artist, ai) => ({
    period, artistId: ai + 1, artist, rank: (ai + pi * (ai % 2 ? 2 : 1)) % 6 + 1,
    listeningMinutes: 980 - ai * 90 + pi * 35, qualifiedPlays: 220 - ai * 14 + pi * 5, artworkUrl: null,
  })))
}

export const demoDiagnostics: Diagnostics = {
  databaseProvider: 'Static demo snapshot', databaseName: 'No runtime database', spotifyDataFiles: ['Sanitized synthetic portfolio dataset'],
  historyRowsImported: 46_280, savedLibraryAvailable: true, savedTrackCount: 612, uniqueTracks: 8_640, uniqueArtists: 2_180,
  uniqueAlbums: 5_420, tracksWithSpotifyIds: 9_050, tracksMappedToReccoBeats: 7_820, tracksWithFeatures: 7_814,
  tracksMissingFeatures: 1_236, reccoBeatsNotFound: 1_236, preferenceModelVersion: 'preference-v1.1-saved-recordings',
  candidateProvider: 'Prepared candidate snapshot', recommendationAlgorithmVersion: 'recommendation-v4-feedback',
}

export const demoValidation: ModelValidation = { trackCount: 9_050, positiveExamples: 1_954, negativeExamples: 1_954, meanPositiveScore: 5.23, meanNegativeScore: 5.08, pairwiseAccuracy: .592, modelVersion: 'preference-v1.1-saved-recordings', validationMethod: 'Prepared recording-held-out demonstration' }

export const demoScore: ScoreResult = {
  spotifyTrackId: 'demo-track', track: 'Signals at Dawn (demo example)', artists: ['Neon Harbor'], predictedLikeScore: 8.4, confidence: 'High',
  components: { featureMatch: 8.7, artistTasteMatch: 8.1, recentTasteMatch: 8.9, lifetimeTasteMatch: 8.3, historicalBehaviorMatch: 5, savedTrackMatch: 7.8, explorationAdjustment: .1 },
  explanations: ['Strong audio-feature match with high-weight positive neighbors.', 'Recent listening is especially close to this candidate.', 'The prepared example has no direct listening history, so behavior is not blended in.'],
  knownRecording: false, historicalEvents: 0,
}

const recommendations = trackNames.slice(0, 10).map((track, index) => ({
  rank: index + 1, track, artist: artistNames[index], artworkUrl: null, predictedLikeScore: 8.9 - index * .24,
  confidence: index < 5 ? 'High' : index < 8 ? 'Medium' : 'Exploratory', why: index < 5 ? 'Strong feature and lifetime-taste match, with support from recent listening.' : index < 8 ? 'Close to recent taste while adding some novelty.' : 'A wider feature-space pick included to keep discovery diverse.',
  historicalStatus: 'Not present in prepared listening history', spotifyUrl: null, band: index < 5 ? 'HighConfidence' : index < 8 ? 'Discovery' : 'Wildcard', candidateId: index + 1, album: albumNames[index % albumNames.length], feedback: null,
}))
export const demoDiscover: Discover = { savedLibraryAvailable: true, notSavedMessage: null, neverHeard: recommendations, notSaved: recommendations.slice().reverse().map((x, i) => ({ ...x, rank: i + 1, candidateId: 101 + i, historicalStatus: 'Heard in demo history · not in prepared saved snapshot' })) }

const soundAxes = (offset = 0) => [
  { name: 'Energy', value: 67 + offset, unit: '/100', coveragePercent: 86, personalPercentile: 61 },
  { name: 'Danceability', value: 59 + offset, unit: '/100', coveragePercent: 86, personalPercentile: 54 },
  { name: 'Valence', value: 48 + offset, unit: '/100', coveragePercent: 85, personalPercentile: 46 },
  { name: 'Acousticness', value: 31 - offset, unit: '/100', coveragePercent: 85, personalPercentile: 42 },
  { name: 'Tempo', value: 121 + offset, unit: 'BPM', coveragePercent: 86, personalPercentile: 58 },
]

export function storyFor(selectedYear: number | null): Story {
  const factor = selectedYear ? scaleFor(`year:${selectedYear}`) : 1
  const rankings = trackNames.slice(0, 5).map((name, i) => ({ id: i + 1, name, artist: artistNames[i], album: albumNames[i], artworkUrl: null, totalMs: scaled((1_090 - i * 85) * 60_000, factor), events: scaled(286 - i * 18, factor), qualifiedPlays: scaled(248 - i * 16, factor), firstListenUtc: `${2022 + i % 2}-03-12T09:00:00Z`, peakMonth: 'October', previousRank: i < 4 ? i + 2 : null, spotifyUrl: null }))
  const artistRankings = artistNames.slice(0, 5).map((name, i) => ({ ...rankings[i], id: 100 + i, name, artist: null, album: null }))
  const albumRankings = albumNames.slice(0, 5).map((name, i) => ({ ...rankings[i], id: 200 + i, name, artist: artistNames[i], album: null }))
  return {
    year: selectedYear, availableYears: years, summary: { totalMs: scaled(7_758_600_000, factor), events: scaled(46_280, factor), qualifiedPlays: scaled(34_940, factor), activeDays: scaled(1_018, Math.min(1, factor * 1.7)), recordings: scaled(8_640, Math.sqrt(factor)), artists: scaled(2_180, Math.sqrt(factor)), albums: scaled(5_420, Math.sqrt(factor)), firstListenUtc: selectedYear ? `${selectedYear}-01-02T08:00:00Z` : '2022-01-03T08:15:00Z', lastListenUtc: selectedYear === 2026 || !selectedYear ? '2026-08-28T21:42:00Z' : `${selectedYear}-12-29T20:10:00Z` },
    topArtists: artistRankings, topSongs: rankings, topAlbums: albumRankings,
    journey: demoTimeline.filter(x => !selectedYear || x.period.startsWith(String(selectedYear))).map((x, i) => ({ period: x.period, totalMs: x.listeningMinutes * 60_000, definingArtist: artistNames[i % artistNames.length] })),
    newRecordings: scaled(5_280, Math.sqrt(factor)), newArtists: scaled(1_620, Math.sqrt(factor)),
    behavior: { skipRate: 12, repeatRate: 70, completionRateEstimate: 68, eventsWithKnownDuration: scaled(39_740, factor) },
    soundProfile: soundAxes(), featureEvolution: years.map((year, i) => ({ year, axes: soundAxes(i - 2) })),
    comparison: selectedYear && selectedYear > 2022 ? { year: selectedYear - 1, totalMs: scaled(7_758_600_000, scaleFor(`year:${selectedYear - 1}`)), changePercent: selectedYear === 2026 ? -25 : 14, note: selectedYear === 2026 ? 'The demo year ends in August, so it is partial.' : 'Listening grew in this prepared example.' } : null,
    facts: [{ title: 'A long-running favorite', detail: 'Neon Harbor appears in every full demo year and remains near the top.' }, { title: 'A newer chapter', detail: 'Mira Vale rose quickly in the most recent 90-day snapshot.' }],
    featureInsights: ['Energy rises gradually across the prepared annual profiles.', 'Acousticness moves in the opposite direction while coverage stays above the reporting threshold.'],
  }
}

type DemoEvaluation = {
  id: number; targetCount: number
  items: { id: number; position: number; recommendation: Recommendation; rating: number | null; alreadyKnown: boolean }[]
  metrics: { presented: number; rated: number; alreadyKnown: number; accepted: number; rejected: number; precisionAt10: number | null; averageRating: number | null; acceptanceRate: number | null; rejectionRate: number | null; bands: { band: string; rated: number; accepted: number; acceptanceRate: number | null }[] }
}

export const demoEvaluation: DemoEvaluation = {
  id: 1, targetCount: 50,
  items: recommendations.slice(0, 10).map((recommendation, i) => ({ id: i + 1, position: i + 1, recommendation, rating: i < 4 ? [5, 4, 3, 2][i] : null, alreadyKnown: false })),
  metrics: { presented: 10, rated: 4, alreadyKnown: 0, accepted: 2, rejected: 1, precisionAt10: null, averageRating: 3.5, acceptanceRate: .5, rejectionRate: .25, bands: [{ band: 'HighConfidence', rated: 4, accepted: 2, acceptanceRate: .5 }] },
}

type DemoFeedback = { candidateId: number; kind: string; rating?: number; evaluationItemId?: number }

export function applyDemoFeedback(input: DemoFeedback) {
  for (const item of [...demoDiscover.neverHeard, ...demoDiscover.notSaved]) {
    if (item.candidateId === input.candidateId) item.feedback = input.kind
  }
  if (input.evaluationItemId) {
    const evaluation = demoEvaluation
    const item = evaluation.items.find(candidate => candidate.id === input.evaluationItemId)
    if (item) {
      item.alreadyKnown = input.kind === 'AlreadyKnow'
      item.rating = input.kind === 'Rating' ? input.rating ?? null : null
    }
    const eligible = evaluation.items.filter(candidate => !candidate.alreadyKnown && candidate.rating !== null)
    const ratings = eligible.map(candidate => candidate.rating as number)
    const accepted = ratings.filter(rating => rating >= 4).length
    const rejected = ratings.filter(rating => rating <= 2).length
    evaluation.metrics = {
      ...evaluation.metrics, rated: ratings.length, alreadyKnown: evaluation.items.filter(candidate => candidate.alreadyKnown).length,
      accepted, rejected, averageRating: ratings.length ? ratings.reduce((sum, rating) => sum + rating, 0) / ratings.length : null,
      acceptanceRate: ratings.length ? accepted / ratings.length : null, rejectionRate: ratings.length ? rejected / ratings.length : null,
      precisionAt10: eligible.length === 10 ? accepted / 10 : null,
      bands: ['HighConfidence', 'Discovery', 'Wildcard'].map(band => {
        const bandItems = eligible.filter(candidate => candidate.recommendation.band === band)
        const bandAccepted = bandItems.filter(candidate => (candidate.rating ?? 0) >= 4).length
        return { band, rated: bandItems.length, accepted: bandAccepted, acceptanceRate: bandItems.length ? bandAccepted / bandItems.length : null }
      }).filter(band => band.rated),
    }
  }
  return { saved: true, demoOnly: true }
}
