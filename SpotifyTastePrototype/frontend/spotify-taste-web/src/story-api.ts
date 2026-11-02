import { request } from './api'
export type Ranking = { id: number; name: string; artist: string | null; album: string | null; artworkUrl: string | null; totalMs: number; events: number; qualifiedPlays: number; firstListenUtc: string; peakMonth: string; previousRank: number | null; spotifyUrl?: string | null }
export type SoundAxis = { name: string; value: number | null; unit: string; coveragePercent: number; personalPercentile: number | null }
export type Story = {
  year: number | null; availableYears: number[];
  summary: { totalMs: number; events: number; qualifiedPlays: number; activeDays: number; recordings: number; artists: number; albums: number; firstListenUtc: string | null; lastListenUtc: string | null };
  topArtists: Ranking[]; topSongs: Ranking[]; topAlbums: Ranking[];
  journey: { period: string; totalMs: number; definingArtist: string | null }[];
  newRecordings: number; newArtists: number;
  behavior: { skipRate: number; repeatRate: number; completionRateEstimate: number | null; eventsWithKnownDuration: number };
  soundProfile: SoundAxis[]; featureEvolution: { year: number; axes: SoundAxis[] }[];
  comparison: { year: number; totalMs: number; changePercent: number | null; note: string } | null;
  facts: { title: string; detail: string }[]; featureInsights: string[];
}
export const getStory = (year: number | null) => request<Story>(`/api/story${year ? `?year=${year}` : ''}`)
