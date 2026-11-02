import type { Overview, BehaviorAnalytics, TopTrack, TopArtist, TopAlbum, TimelinePoint, RankEvolution, Diagnostics, Discover } from './api'
import type { Story } from './story-api'
export type CatalogTrack = {
  id: number; spotifyTrackId: string | null; name: string; album: string | null; artists: string[]; durationMs: number | null; isrc: string | null;
  saved: boolean; savedAtUtc: string | null;
  history: { events: number; totalMs: number; firstListenUtc: string; lastListenUtc: string } | null;
  features: ({ provider: string; fetchedAtUtc: string } & Record<string, string | number | null>)[] | null;
  preference: ({ modelVersion: string; calculatedAtUtc: string } & Record<string, string | number>) | null;
}
export type PersonalSnapshot = {
  manifest: { schemaVersion: number; capturedAtUtc: string; events: number; totalMs: number; savedTracks: number; catalogTracks: number; featureRows: number; years: number[]; sourceFirstUtc: string; sourceLastUtc: string; factIdentitySha256: string };
  ranges: Record<string, { overview: Overview; behavior: BehaviorAnalytics; tracks: TopTrack[]; artists: TopArtist[]; albums: TopAlbum[] }>;
  stories: Record<string, Story>; evolution: Record<string, RankEvolution[]>;
  timeline: TimelinePoint[]; yearly: TimelinePoint[]; diagnostics: Diagnostics; discover: Discover; evaluation: unknown; catalog: CatalogTrack[];
}
let loaded: Promise<PersonalSnapshot> | null = null
export function loadSnapshot() {
  loaded ??= fetch('/private-snapshot/data.json', { cache: 'no-store' }).then(async response => {
    if (!response.ok || !response.headers.get('content-type')?.includes('json')) throw new Error('Your personal snapshot has not been installed. No demo data will be substituted.')
    const data = await response.json() as PersonalSnapshot
    if (data.manifest?.schemaVersion !== 1 || data.stories?.lifetime?.summary.events !== data.manifest.events) throw new Error('Personal snapshot validation failed.')
    return data
  }).catch(error => { loaded = null; throw error })
  return loaded
}
// Frozen responses; interactions never generate records or silently fall back to an API.
export function snapshotResponse(data: PersonalSnapshot, path: string, method = 'GET'): unknown {
  if (method.toUpperCase() !== 'GET') throw new Error('Read-only edition: imports, new scores, feedback and regeneration are disabled.')
  const url = new URL(path, 'http://snapshot.local')
  const range = url.searchParams.get('year') ? `year:${url.searchParams.get('year')}` : url.searchParams.get('range') ?? 'lifetime'
  const limit = Math.max(1, Math.min(500, Number(url.searchParams.get('limit') ?? 50)))
  const selected = data.ranges[range]
  const route = url.pathname
  if (route === '/api/analytics/overview' && selected) return selected.overview
  if (route === '/api/analytics/behavior' && selected) return selected.behavior
  if (route === '/api/analytics/top-tracks' && selected) return selected.tracks.slice(0, limit)
  if (route === '/api/analytics/top-artists' && selected) return selected.artists.slice(0, limit)
  if (route === '/api/analytics/top-albums' && selected) return selected.albums.slice(0, limit)
  if (route === '/api/analytics/timeline') return data.timeline
  if (route === '/api/analytics/timeline/yearly') return data.yearly
  if (route === '/api/analytics/artist-evolution' && data.evolution[url.searchParams.get('grain') ?? 'year']) return data.evolution[url.searchParams.get('grain') ?? 'year']
  if (route === '/api/story' && data.stories[url.searchParams.get('year') ?? 'lifetime']) return data.stories[url.searchParams.get('year') ?? 'lifetime']
  if (route === '/api/diagnostics') return data.diagnostics
  if (route === '/api/discover') return data.discover
  if (route === '/api/evaluation') return data.evaluation
  throw new Error('This selection is not present in your fixed snapshot. No substitute data was generated.')
}
