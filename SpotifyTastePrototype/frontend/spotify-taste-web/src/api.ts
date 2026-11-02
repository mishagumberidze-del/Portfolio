export type Overview = { totalListeningMinutes: number; totalListeningHours: number; totalListeningDays: number; listeningEvents: number; qualifiedPlays: number; uniqueTracks: number; uniqueArtists: number; uniqueAlbums: number; savedLibraryAvailable: boolean; savedTracks: number | null; earliestListenUtc: string | null; latestListenUtc: string | null }
export type TopTrack = { trackId: number; track: string; artist: string; album: string | null; rawEvents: number; qualifiedPlays: number; listeningMinutes: number; averageListenSeconds: number; skipRate: number; firstListenUtc: string; lastListenUtc: string; saved: boolean | null }
export type TopArtist = { artistId: number; artist: string; listeningMinutes: number; qualifiedPlays: number; rawPlays: number; uniqueTracks: number; skipRate: number; firstListenUtc: string; lastListenUtc: string }
export type TopAlbum = { albumId: number; album: string; artist: string; listeningMinutes: number; qualifiedPlays: number; uniqueTracks: number }
export type TimelinePoint = { period: string; listeningMinutes: number; uniqueArtists: number; newArtists: number; newTracks: number }
export type BehaviorAnalytics = { listeningEvents: number; skipRate: number; shortListenRate: number; repeatRate: number; averageListenPercentage: number | null; completionRateEstimate: number | null; eventsWithKnownDuration: number }
export type RankEvolution = { period: string; artistId: number; artist: string; rank: number; listeningMinutes: number; qualifiedPlays: number; artworkUrl?: string | null }
export type Diagnostics = { databaseProvider: string; databaseName: string; spotifyDataFiles: string[]; historyRowsImported: number; savedLibraryAvailable: boolean; savedTrackCount: number; uniqueTracks: number; uniqueArtists: number; uniqueAlbums: number; tracksWithSpotifyIds: number; tracksMappedToReccoBeats: number; tracksWithFeatures: number; tracksMissingFeatures: number; reccoBeatsNotFound: number; preferenceModelVersion: string; candidateProvider: string; recommendationAlgorithmVersion: string }
export type ModelValidation = { trackCount: number; positiveExamples: number; negativeExamples: number; meanPositiveScore: number; meanNegativeScore: number; pairwiseAccuracy: number; modelVersion: string; validationMethod: string }
export type ScoreResult = { spotifyTrackId: string; track: string; artists: string[]; predictedLikeScore: number; confidence: string; components: { featureMatch: number; artistTasteMatch: number; recentTasteMatch: number; lifetimeTasteMatch: number; historicalBehaviorMatch: number; savedTrackMatch: number; explorationAdjustment: number }; explanations: string[]; knownRecording: boolean; historicalEvents: number }
export type Recommendation = { rank: number; track: string; artist: string; artworkUrl: string | null; predictedLikeScore: number; confidence: string; why: string; historicalStatus: string; spotifyUrl: string | null; band: string; candidateId: number; album: string | null; feedback: string | null }
export type Discover = { savedLibraryAvailable: boolean; notSavedMessage: string | null; notSaved: Recommendation[]; neverHeard: Recommendation[] }

export const demoMode = import.meta.env.VITE_APP_MODE === 'demo'
export const snapshotMode = import.meta.env.VITE_APP_MODE === 'personal'
export const readOnlyMode = import.meta.env.VITE_APP_MODE !== 'maintenance' && !demoMode

async function demoRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const data = await import('./demo-data')
  const url = new URL(path, window.location.origin)
  const range = url.searchParams.get('year') ? `year:${url.searchParams.get('year')}` : url.searchParams.get('range') ?? 'lifetime'
  const limit = Number(url.searchParams.get('limit') ?? 20)
  const route = url.pathname
  let result: unknown
  if (route === '/api/analytics/overview') result = data.overviewFor(range)
  else if (route === '/api/analytics/behavior') result = data.behaviorFor(range)
  else if (route === '/api/analytics/top-tracks') result = data.tracksFor(range, limit)
  else if (route === '/api/analytics/top-artists') result = data.artistsFor(range, limit)
  else if (route === '/api/analytics/top-albums') result = data.albumsFor(range, limit)
  else if (route === '/api/analytics/timeline') result = data.demoTimeline
  else if (route === '/api/analytics/timeline/yearly') result = data.demoYearly
  else if (route === '/api/analytics/artist-evolution') result = data.evolutionFor(url.searchParams.get('grain') ?? 'year')
  else if (route === '/api/diagnostics') result = data.demoDiagnostics
  else if (route === '/api/model/rebuild') result = data.demoValidation
  else if (route === '/api/scorer') result = data.demoScore
  else if (route === '/api/story') result = data.storyFor(url.searchParams.get('year') ? Number(url.searchParams.get('year')) : null)
  else if (route === '/api/discover' || route === '/api/discover/generate') result = data.demoDiscover
  else if (route === '/api/evaluation' || route === '/api/evaluation/start' || route === '/api/evaluation/next') result = data.demoEvaluation
  else if (route === '/api/feedback' && init?.method === 'POST') result = data.applyDemoFeedback(JSON.parse(String(init.body ?? '{}')))
  else throw new Error(`No prepared demo response exists for ${route}.`)
  await Promise.resolve()
  return structuredClone(result) as T
}

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  if (demoMode) return demoRequest<T>(path, init)
  if (snapshotMode) {
    const { loadSnapshot, snapshotResponse } = await import('./snapshot')
    return structuredClone(snapshotResponse(await loadSnapshot(), path, init?.method)) as T
  }
  if (readOnlyMode && init?.method && init.method.toUpperCase() !== 'GET') throw new Error('Read-only edition: stored data stays fixed. No new scores, imports or regeneration.')
  const response = await fetch(path, { ...init, headers: { 'Content-Type': 'application/json', ...init?.headers } })
  if (!response.ok) {
    if (response.status === 401 && document.querySelector('meta[name="spotify-taste-access"]')) window.location.assign('/share/login')
    const body = await response.text()
    const conciseMessage = body.split(/\r?\n/, 1)[0]
    throw new Error(conciseMessage || `${response.status} ${response.statusText}`)
  }
  return response.json() as Promise<T>
}

function analyticsUrl(path: string, range: string, extra: Record<string, string | number> = {}) {
  const params = new URLSearchParams()
  const year = /^year:(\d{4})$/.exec(range)?.[1]
  params.set('range', year ? 'year' : range)
  if (year) params.set('year', year)
  for (const [key, value] of Object.entries(extra)) params.set(key, String(value))
  return `${path}?${params.toString()}`
}

export const api = {
  overview: (range: string) => request<Overview>(analyticsUrl('/api/analytics/overview', range)),
  behavior: (range: string) => request<BehaviorAnalytics>(analyticsUrl('/api/analytics/behavior', range)),
  topTracks: (range: string, limit = 20) => request<TopTrack[]>(analyticsUrl('/api/analytics/top-tracks', range, { limit })),
  topArtists: (range: string, limit = 20) => request<TopArtist[]>(analyticsUrl('/api/analytics/top-artists', range, { limit })),
  topAlbums: (range: string, limit = 20) => request<TopAlbum[]>(analyticsUrl('/api/analytics/top-albums', range, { limit })),
  timeline: () => request<TimelinePoint[]>('/api/analytics/timeline'),
  timelineYearly: () => request<TimelinePoint[]>('/api/analytics/timeline/yearly'),
  evolution: (grain: string) => request<RankEvolution[]>(`/api/analytics/artist-evolution?grain=${encodeURIComponent(grain)}`),
  diagnostics: () => request<Diagnostics>('/api/diagnostics'),
  rebuildModel: () => request<ModelValidation>('/api/model/rebuild', { method: 'POST' }),
  score: (spotifyUrlOrUri: string) => request<ScoreResult>('/api/scorer', { method: 'POST', body: JSON.stringify({ spotifyUrlOrUri }) }),
  discover: () => request<Discover>('/api/discover/generate?count=10', { method: 'POST' }),
}
