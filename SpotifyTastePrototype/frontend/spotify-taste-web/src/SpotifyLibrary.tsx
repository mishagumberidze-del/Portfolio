import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Heart, LoaderCircle } from 'lucide-react'
import { api, request } from './api'

type Status = { configured: boolean; connected: boolean; redirectUri: string; savedLibraryAvailable: boolean; savedTrackCount: number;
  savedTracksWithFeatures: number; savedTracksNotFound: number; savedTracksPending: number; preferenceModelVersion: string; savedBoostedHistoricalEntries: number; lastModelRebuildAtUtc: string | null;
  lastImport: { recordsFound: number; recordsImported: number; completedAtUtc: string | null; status: string } | null }
type ImportResult = { spotifyTotal: number; savedTracks: number; added: number; updated: number; removed: number; newTracks: number }
const post = <T,>(path: string) => request<T>(`/api/spotify/${path}`, { method: 'POST', headers: { 'X-Spotify-Local': '1' } })

export default function SpotifyLibrary() {
  const cache = useQueryClient()
  const [callback] = useState(() => new URLSearchParams(window.location.search).get('spotify'))
  const status = useQuery({ queryKey: ['spotify-connection'], queryFn: () => request<Status>('/api/spotify/status'), refetchOnWindowFocus: true })
  const connect = useMutation({ mutationFn: () => post<{ authorizationUrl: string }>('connect'), onSuccess: result => window.location.assign(result.authorizationUrl) })
  const refresh = () => { void cache.invalidateQueries() }
  const imported = useMutation({ mutationFn: () => post<ImportResult>('import-liked'), onSettled: refresh })
  const end = useMutation({ mutationFn: () => post('end-session'), onSuccess: refresh })
  const rebuild = useMutation({ mutationFn: api.rebuildModel, onSuccess: refresh })
  const busy = connect.isPending || imported.isPending || end.isPending || rebuild.isPending
  const error = status.error ?? connect.error ?? imported.error ?? end.error ?? rebuild.error
  const startConnect = () => {
    if (window.location.hostname !== '127.0.0.1') { window.location.assign('http://127.0.0.1:5173/?spotify=setup'); return }
    connect.mutate()
  }
  return <article className="panel spotify-library" aria-labelledby="spotify-library-title" aria-busy={busy}>
    <div className="panel-heading"><div><p className="eyebrow">OPTIONAL · READ-ONLY SPOTIFY CONNECTION</p><h3 id="spotify-library-title"><Heart size={20} aria-hidden="true" /> Spotify Liked Songs</h3></div>
      <span className="library-status">{status.data?.connected ? 'Connected for this session' : 'Not connected'}</span></div>
    <p>Import your real saved songs into local SQL. No playback or library-edit permission is requested. Sign in to the same Spotify account as your downloaded history.</p>
    <p>Each successful import replaces the saved-state snapshot only after every page validates. Your previous snapshot is backed up locally; listening history is never changed.</p>
    {callback === 'failed' && !status.data?.connected && <p role="alert">Spotify connection did not finish. Check the Redirect URI below, then reconnect and approve access in the same browser.</p>}
    <div className="library-actions">
      <button className="primary-button" type="button" disabled={busy || !status.data?.configured} onClick={startConnect}>{connect.isPending ? <LoaderCircle className="spin" size={16}/> : <Heart size={16}/>} {status.data?.connected ? 'Reconnect Spotify' : 'Connect Spotify'}</button>
      <button className="primary-button" type="button" disabled={busy || !status.data?.connected} onClick={() => imported.mutate()}>{imported.isPending ? 'Importing all pages…' : 'Import Liked Songs'}</button>
      {status.data?.connected && <button type="button" disabled={busy} onClick={() => end.mutate()}>Forget session tokens</button>}
    </div>
    <div aria-live="polite">
      {status.isPending && <p>Checking Spotify connection…</p>}
      {status.data && <p>{status.data.savedLibraryAvailable ? `${status.data.savedTrackCount.toLocaleString()} real saved tracks stored in SQL.` : 'Saved/liked status is still unknown. Connecting alone does not import songs.'}</p>}
      {status.data?.savedLibraryAvailable && <p>Audio features: {status.data.savedTracksWithFeatures} / {status.data.savedTrackCount} saved songs cached · {status.data.savedTracksNotFound} unavailable from ReccoBeats · {status.data.savedTracksPending} pending. Missing features do not erase a song’s saved status.</p>}
      {status.data?.lastModelRebuildAtUtc && <p>Model: {status.data.preferenceModelVersion} · {status.data.savedBoostedHistoricalEntries.toLocaleString()} historical release entries receive a saved-song boost, including equivalent album/deluxe releases. This is not an additional saved-song count.</p>}
      {status.data?.lastImport && <p>Latest API import: {status.data.lastImport.status} · {status.data.lastImport.recordsImported.toLocaleString()} imported / {status.data.lastImport.recordsFound.toLocaleString()} reported.</p>}
      {imported.isPending && <p>Downloading and validating your library. Please leave Liked Songs unchanged while this runs.</p>}
      {imported.data && <p>Verified {imported.data.savedTracks.toLocaleString()} / {imported.data.spotifyTotal.toLocaleString()} saved tracks. Added {imported.data.added}; refreshed {imported.data.updated}; removed {imported.data.removed} stale saved-state entries; created {imported.data.newTracks} track records. No listening events were added.</p>}
      {error && <p role="alert">{error instanceof Error ? error.message : 'Spotify operation did not complete.'}</p>}
    </div>
    {status.data?.savedLibraryAvailable && <div className="library-actions"><button type="button" disabled={busy} onClick={() => rebuild.mutate()}>{rebuild.isPending ? 'Rebuilding and validating…' : 'Rebuild taste model with saved state'}</button>{rebuild.data && <span>Rebuilt {rebuild.data.trackCount.toLocaleString()} historical track preferences. Holdout pairwise accuracy: {(rebuild.data.pairwiseAccuracy * 100).toFixed(1)}%.</span>}</div>}
    <details><summary>Connection setup & privacy</summary>
      <p>Add this exact Redirect URI in your Spotify developer app settings:</p><code>{status.data?.redirectUri ?? 'http://127.0.0.1:5143/api/spotify/callback'}</code>
      <p>PKCE uses a Client ID, not a client secret. Access/refresh tokens stay in API process memory only. Restarting the API requires reconnecting; imported SQL data remains. “Forget session tokens” stops API access but intentionally keeps your local import.</p>
      <p>Development-mode apps require a Premium app owner and an allowed Spotify user. A 403 indicates an access restriction, not necessarily a rate limit.</p>
      <p>Spotify API terms restrict profiling and ML/AI use. Review those terms before expanding how API-derived data is used. This local tool is not endorsed by Spotify.</p>
      <a href="https://developer.spotify.com/dashboard" target="_blank" rel="noreferrer">Spotify developer dashboard</a> · <a href="https://developer.spotify.com/policy" target="_blank" rel="noreferrer">Spotify developer policy</a>
    </details>
  </article>
}
