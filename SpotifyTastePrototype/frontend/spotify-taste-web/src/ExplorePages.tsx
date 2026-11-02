import { formatCount, formatMetric, formatChartValue } from './number-format'
import { useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Activity, BarChart3, Clock3, Database, Disc3, Headphones, ListMusic, LoaderCircle, Music2, Radio, Search, Sparkles, TrendingDown, TrendingUp, TriangleAlert } from 'lucide-react'
import { Area, AreaChart, Bar, BarChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api, demoMode, snapshotMode, readOnlyMode, type ScoreResult, type TopAlbum, type TopArtist, type TopTrack, type TimelinePoint } from './api'
import SnapshotLibrary from './SnapshotLibrary'
import { loadSnapshot } from './snapshot'
import SpotifyLibrary from './SpotifyLibrary'
import { sharedAccess } from './shared-access'

const rangeOptions = [
  { value: 'lifetime', label: 'Lifetime' },
  { value: 'last365', label: 'Last 365 days' },
  { value: 'last90', label: 'Last 90 days' },
  { value: 'last30', label: 'Last 30 days' },
  ...[2026, 2025, 2024, 2023, 2022, 2021].map(year => ({ value: `year:${year}`, label: String(year) })),
]

const tooltipStyle = { background: '#282828', border: 'none', borderRadius: 8, color: '#fff', fontSize: 12, boxShadow: '0 8px 24px rgba(0,0,0,.5)' }


export default function ExplorePages({ view }: { view: string }) {
  return <>{view === 'overview' && <OverviewPage/>}{view === 'history' && <HistoryPage/>}{view === 'taste' && <TastePage/>}{view === 'scorer' && (snapshotMode ? <SnapshotLibrary inspector/> : readOnlyMode ? <section className="panel page-stack"><h2>Stored results, not regenerated scores.</h2><p>Existing predicted scores remain visible in Discover. New song scoring is disabled in this read-only edition because it can fetch data and write to SQL.</p><p>The fixed personal snapshot will add searchable cached features and behavioral evidence. No placeholder score is shown.</p></section> : <ScorerPage/>)}{view === 'diagnostics' && <DiagnosticsPage/>}</>
}

function OverviewPage() {
  const [range, setRange] = useState('lifetime')
  const trackLimit = 12
  const artistLimit = 8
  const albumLimit = 8
  const overview = useQuery({ queryKey: ['overview', range], queryFn: () => api.overview(range) })
  const behavior = useQuery({ queryKey: ['behavior', range], queryFn: () => api.behavior(range) })
  const tracks = useQuery({ queryKey: ['tracks', range, trackLimit], queryFn: () => api.topTracks(range, trackLimit) })
  const artists = useQuery({ queryKey: ['artists', range, artistLimit], queryFn: () => api.topArtists(range, artistLimit) })
  const albums = useQuery({ queryKey: ['albums', range, albumLimit], queryFn: () => api.topAlbums(range, albumLimit) })
  const timeline = useQuery({ queryKey: ['timeline', 'monthly'], queryFn: api.timeline })
  const snapshot = useQuery({ queryKey: ['personal-snapshot'], queryFn: loadSnapshot, enabled: snapshotMode, staleTime: Infinity })
  const queries = [overview, behavior, tracks, artists, albums, timeline]
  const firstError = queries.find(query => query.error)?.error
  const chartData = useMemo(() => filterTimelineForRange(timeline.data ?? [], range, snapshot.data?.manifest.capturedAtUtc), [timeline.data, range, snapshot.data])

  if (queries.some(query => query.isPending)) return <Loading label="Reading your listening history" />
  if (firstError || !overview.data || !behavior.data) return <ErrorState error={firstError} />

  const value = overview.data
  const behaviorValue = behavior.data
  const kpis: { label: string; metric: string; Icon: typeof BarChart3 }[] = [
    { label: 'Listening minutes', metric: formatNumber(value.totalListeningMinutes), Icon: Clock3 },
    { label: 'Listening hours', metric: formatNumber(value.totalListeningHours), Icon: Clock3 },
    { label: 'Active listening dates', metric: formatNumber(value.totalListeningDays), Icon: Headphones },
    { label: 'Listening events', metric: formatNumber(value.listeningEvents), Icon: ListMusic },
    { label: 'Qualified plays', metric: formatNumber(value.qualifiedPlays), Icon: Activity },
    { label: 'Unique recordings', metric: formatNumber(value.uniqueTracks), Icon: Music2 },
    { label: 'Unique artists', metric: formatNumber(value.uniqueArtists), Icon: Sparkles },
    { label: 'Unique albums', metric: formatNumber(value.uniqueAlbums), Icon: Disc3 },
    { label: 'Saved tracks', metric: value.savedLibraryAvailable ? formatNumber(value.savedTracks ?? 0) : 'Unknown', Icon: Database },
  ]
  const behaviorMetrics = [
    ['Skip rate', formatPercent(behaviorValue.skipRate)],
    ['Short-listen rate', formatPercent(behaviorValue.shortListenRate)],
    ['Repeat rate', formatPercent(behaviorValue.repeatRate)],
    ['Average listened', formatPercent(behaviorValue.averageListenPercentage)],
    ['Completion estimate', formatPercent(behaviorValue.completionRateEstimate)],
    ['Known-duration events', formatNumber(behaviorValue.eventsWithKnownDuration)],
  ]

  return <section className="page-stack" aria-label="Listening overview">
    <div className="toolbar">
      <p>Listening from <strong>{formatDate(value.earliestListenUtc)}</strong> to <strong>{formatDate(value.latestListenUtc)}</strong> · UTC</p>
      <RangeSelect value={range} onChange={setRange} />
    </div>
    {value.listeningEvents === 0
      ? <EmptyState message="No listening events exist in this date range." />
      : <>
        <div className="kpi-grid">
          {kpis.map(({ label, metric, Icon }) =>
            <article className="kpi-card" key={label}>
              <div className="icon-box"><Icon size={19} aria-hidden="true" /></div>
              <span>{label}</span><strong>{metric}</strong>
            </article>)}
        </div>
        <div className="dashboard-grid">
          <Panel title="Listening rhythm" subtitle={`Minutes by month · ${rangeLabel(range)}`} className="chart-panel wide">
            {chartData.length === 0
              ? <EmptyState message="No monthly listening data exists in this range." compact />
              : <div className="chart-wrap" role="img" aria-label={`Monthly listening minutes for ${rangeLabel(range)}`}>
                <ResponsiveContainer width="100%" height="100%">
                  <AreaChart data={chartData} accessibilityLayer>
                    <defs><linearGradient id="listenFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor="#1ed760" stopOpacity={0.26}/><stop offset="100%" stopColor="#1ed760" stopOpacity={0}/></linearGradient></defs>
                    <CartesianGrid stroke="#2a2a2a" vertical={false}/><XAxis dataKey="period" tick={{ fill: '#a7a7a7', fontSize: 11 }} minTickGap={38}/><YAxis tick={{ fill: '#a7a7a7', fontSize: 11 }} width={72} tickFormatter={formatChartValue}/><Tooltip contentStyle={tooltipStyle} formatter={formatChartValue}/><Area type="monotone" dataKey="listeningMinutes" name="Listening minutes" stroke="#1ed760" strokeWidth={2.5} fill="url(#listenFill)" />
                  </AreaChart>
                </ResponsiveContainer>
              </div>}
          </Panel>
          <Panel title="Top artists" subtitle="By listening time"><ArtistBars artists={artists.data ?? []} /></Panel>
        </div>
        <Panel title="Listening behavior" subtitle={`Aggregate signals · ${formatNumber(behaviorValue.listeningEvents)} events`}>
          <div className="behavior-grid">
            {behaviorMetrics.map(([label, metric]) => <Metric key={label} label={label} value={metric} />)}
          </div>
        </Panel>
        <div className="dashboard-grid">
          <Panel title="Top tracks" subtitle="Behavior, not just play count" className="wide"><TrackTable tracks={tracks.data ?? []} /></Panel>
          <Panel title="Top albums" subtitle="Listening time and qualified plays"><AlbumTable albums={albums.data ?? []} /></Panel>
        </div>
      </>}
  </section>
}

function HistoryPage() {
  const [grain, setGrain] = useState('year')
  const yearly = useQuery({ queryKey: ['timeline', 'yearly'], queryFn: api.timelineYearly })
  const evolution = useQuery({ queryKey: ['evolution', grain], queryFn: () => api.evolution(grain) })
  const firstError = yearly.error ?? evolution.error

  if (yearly.isPending || evolution.isPending) return <Loading label="Building your listening timeline" />
  if (firstError) return <ErrorState error={firstError} />

  const rows = yearly.data ?? []
  return <section className="page-stack" aria-label="Listening history">
    <div className="section-intro">
      <div><h2>Your taste, through time</h2><p>New discoveries and the artists that defined each period.</p></div>
      <select value={grain} onChange={event => setGrain(event.target.value)} aria-label="Evolution period">
        <option value="year">By year</option><option value="quarter">By quarter</option><option value="month">By month</option>
      </select>
    </div>
    {rows.length === 0
      ? <EmptyState message="No yearly listening history is available." />
      : <>
        <div className="dashboard-grid">
          <Panel title="Listening minutes" subtitle="Annual total" className="wide">
            <div className="chart-wrap" role="img" aria-label="Annual listening minutes">
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={rows} accessibilityLayer><CartesianGrid stroke="#2a2a2a" vertical={false}/><XAxis dataKey="period" tick={{ fill: '#a7a7a7' }}/><YAxis tick={{ fill: '#a7a7a7' }} width={72} tickFormatter={formatChartValue}/><Tooltip contentStyle={tooltipStyle} formatter={formatChartValue}/><Bar dataKey="listeningMinutes" name="Listening minutes" fill="#1ed760" radius={[5, 5, 0, 0]}/></BarChart>
              </ResponsiveContainer>
            </div>
          </Panel>
          <Panel title="Track discovery" subtitle="New recordings per year">
            <div className="chart-wrap" role="img" aria-label="New recordings discovered each year">
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={rows} accessibilityLayer><CartesianGrid stroke="#2a2a2a" vertical={false}/><XAxis dataKey="period" tick={{ fill: '#a7a7a7' }}/><YAxis tick={{ fill: '#a7a7a7' }} width={72} tickFormatter={formatChartValue}/><Tooltip contentStyle={tooltipStyle} formatter={formatChartValue}/><Line dataKey="newTracks" name="New recordings" stroke="#fff" strokeWidth={2.5} dot={{ fill: '#fff', stroke: '#181818', strokeWidth: 2 }}/></LineChart>
              </ResponsiveContainer>
            </div>
          </Panel>
        </div>
        <Panel title="Artist landscape" subtitle="Unique artists and new discoveries by year">
          <div className="chart-key" aria-hidden="true"><span className="green-key">Unique artists</span><span className="purple-key">New artists</span></div>
          <div className="chart-wrap" role="img" aria-label="Unique and newly discovered artists by year">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={rows} accessibilityLayer><CartesianGrid stroke="#2a2a2a" vertical={false}/><XAxis dataKey="period" tick={{ fill: '#a7a7a7' }}/><YAxis tick={{ fill: '#a7a7a7' }} width={72} tickFormatter={formatChartValue}/><Tooltip contentStyle={tooltipStyle} formatter={formatChartValue}/><Line dataKey="uniqueArtists" name="Unique artists" stroke="#1ed760" strokeWidth={2.5}/><Line dataKey="newArtists" name="New artists" stroke="#fff" strokeWidth={2.5} strokeDasharray="5 5"/></LineChart>
            </ResponsiveContainer>
          </div>
        </Panel>
      </>}
    <Panel title="Favorite artist evolution" subtitle={`Top artists ${grain === 'year' ? 'each year' : `each ${grain}`}`}>
      <EvolutionTable rows={evolution.data ?? []} />
    </Panel>
  </section>
}

function TastePage() {
  const lifetimeLimit = 500
  const recentLimit = 30
  const lifetime = useQuery({ queryKey: ['artists', 'lifetime', lifetimeLimit], queryFn: () => api.topArtists('lifetime', lifetimeLimit) })
  const recent = useQuery({ queryKey: ['artists', 'last90', recentLimit], queryFn: () => api.topArtists('last90', recentLimit) })
  const rebuild = useMutation({ mutationFn: api.rebuildModel })
  const firstError = lifetime.error ?? recent.error
  const comparisons = (recent.data ?? []).slice(0, 12).map((artist, index) => {
    const lifetimeIndex = (lifetime.data ?? []).findIndex(item => item.artistId === artist.artistId)
    return {
      ...artist,
      recentRank: index + 1,
      lifetimeRank: lifetimeIndex < 0 ? null : lifetimeIndex + 1,
      delta: lifetimeIndex < 0 ? null : lifetimeIndex - index,
    }
  })

  if (lifetime.isPending || recent.isPending) return <Loading label="Comparing recent and lifetime taste" />
  if (firstError) return <ErrorState error={firstError} />

  return <section className="page-stack" aria-label="Taste model">
    <div className="model-hero">
      <div><p className="eyebrow">EXPLAINABLE MODEL · V1</p><h2>Behavior first. Features second.</h2><p>Repeats, completion, recency and careful skip penalties shape positive and negative taste neighborhoods. Only real imported saved-state evidence contributes a saved-song signal.</p></div>
      <button type="button" className="primary-button" onClick={() => rebuild.mutate()} disabled={readOnlyMode || sharedAccess || rebuild.isPending} title={readOnlyMode ? 'Stored model remains unchanged.' : undefined}>
        {rebuild.isPending ? <LoaderCircle className="spin" size={17} aria-hidden="true"/> : <Sparkles size={17} aria-hidden="true"/>} {demoMode ? 'Replay validation' : readOnlyMode ? 'Stored model · read-only' : 'Rebuild & validate'}
      </button>
    </div>
    <div aria-live="polite">
      {rebuild.isPending && <InlineStatus label="Rebuilding and validating the preference model" />}
      {rebuild.error && <ErrorState error={rebuild.error} compact />}
      {rebuild.data && <div className="validation-strip">
        <Metric label="Positive mean" value={`${rebuild.data.meanPositiveScore.toFixed(2)}/10`} />
        <Metric label="Negative mean" value={`${rebuild.data.meanNegativeScore.toFixed(2)}/10`} />
        <Metric label="Pairwise accuracy" value={`${(rebuild.data.pairwiseAccuracy * 100).toFixed(1)}%`} />
        <Metric label="Tracks modeled" value={formatNumber(rebuild.data.trackCount)} />
      </div>}
    </div>
    <Panel title="Recent vs historical taste" subtitle="Last 90 days compared with the lifetime top 500">
      {comparisons.length === 0
        ? <EmptyState message="No listening activity exists in the last 90 days." compact />
        : <div className="table-scroll"><table className="taste-table">
          <caption className="sr-only">Recent artist ranks compared with lifetime ranks</caption>
          <thead><tr><th>Artist</th><th>Lifetime rank</th><th>90-day rank</th><th>Trend</th><th>90-day minutes</th></tr></thead>
          <tbody>{comparisons.map(row => <tr key={row.artistId}>
            <td className="primary-cell">{row.artist}</td>
            <td>{row.lifetimeRank === null ? 'Outside top 500' : `#${row.lifetimeRank}`}</td>
            <td>#{row.recentRank}</td>
            <td>{row.delta === null
              ? <span className="trend">Not ranked</span>
              : row.delta > 0
                ? <span className="trend up"><TrendingUp size={14} aria-hidden="true"/> Up</span>
                : row.delta < 0
                  ? <span className="trend down"><TrendingDown size={14} aria-hidden="true"/> Down</span>
                  : <span className="trend">Stable</span>}</td>
            <td>{formatNumber(row.listeningMinutes)}</td>
          </tr>)}</tbody>
        </table></div>}
    </Panel>
  </section>
}

function ScorerPage() {
  const [value, setValue] = useState('')
  const score = useMutation({ mutationFn: api.score })
  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (value.trim()) score.mutate(value.trim())
  }

  return <section className="scorer-layout" aria-busy={score.isPending}>
    <div className="scorer-copy">
      <p className="eyebrow">NO SPOTIFY LOGIN REQUIRED</p><h2>Would this track fit your taste?</h2>
      <p id="scorer-help">{demoMode ? 'Paste any Spotify-shaped song link to run the prepared scoring example. No request leaves this page.' : 'Paste a Spotify song link or URI. ReccoBeats supplies the audio features; your local history supplies the judgment.'}</p>
      <form onSubmit={submit}>
        <div className="url-input">
          <Search size={20} aria-hidden="true"/>
          <input value={value} onChange={event => setValue(event.target.value)} placeholder="https://open.spotify.com/track/…" aria-label="Spotify song link" aria-describedby="scorer-help" autoComplete="off" required/>
          <button disabled={score.isPending}>{score.isPending ? <LoaderCircle className="spin" size={18} aria-hidden="true"/> : 'Score track'}</button>
        </div>
      </form>
      <div aria-live="polite">
        {score.isPending && <InlineStatus label="Looking up and scoring this track" />}
        {score.error && <ErrorState error={score.error} compact />}
      </div>
    </div>
    <div aria-live="polite">
      {score.data
        ? <ScoreCard score={score.data}/>
        : <div className="empty-score"><Radio size={38} aria-hidden="true"/><strong>Your score will appear here</strong><span>Relative compatibility from 0 to 10, with the actual components.</span></div>}
    </div>
  </section>
}

function DiagnosticsPage() {
  const result = useQuery({ queryKey: ['diagnostics'], queryFn: api.diagnostics })
  if (result.isPending) return <Loading label="Checking local system" />
  if (result.error || !result.data) return <ErrorState error={result.error}/>
  const d = result.data
  const rows = [
    ['Database provider', d.databaseProvider], ['Database name', d.databaseName], ['History rows', formatNumber(d.historyRowsImported)],
    ['Saved library', d.savedLibraryAvailable ? 'Yes' : 'No — status unknown'], ['Saved tracks', d.savedLibraryAvailable ? formatNumber(d.savedTrackCount) : 'Unknown'],
    ['Unique recordings', formatNumber(d.uniqueTracks)], ['Unique artists', formatNumber(d.uniqueArtists)], ['Unique albums', formatNumber(d.uniqueAlbums)],
    ['Tracks with Spotify IDs', formatNumber(d.tracksWithSpotifyIds)], ['Mapped to ReccoBeats', formatNumber(d.tracksMappedToReccoBeats)],
    ['Tracks with features', formatNumber(d.tracksWithFeatures)], ['Tracks missing features', formatNumber(d.tracksMissingFeatures)],
    ['ReccoBeats NotFound', formatNumber(d.reccoBeatsNotFound)], ['Preference model', d.preferenceModelVersion],
    ['Candidate provider', d.candidateProvider], ['Recommendation algorithm', d.recommendationAlgorithmVersion],
  ]

  return <section className="page-stack" aria-label="System diagnostics">
    {!readOnlyMode && !demoMode && <SpotifyLibrary />}
    <div className="diagnostic-hero"><div className="pulse"><Activity size={25} aria-hidden="true"/></div><div><h2>Local system is ready</h2><p>Private history stays in SQL Server. Provider enrichment is cached and resumable.</p></div></div>
    <div className="diagnostic-grid">{rows.map(([label, metric]) => <div key={label}><span>{label}</span><strong>{metric}</strong></div>)}</div>
    <Panel title="Spotify inputs detected" subtitle="Original files remain untouched">
      {d.spotifyDataFiles.length === 0
        ? <EmptyState message="No Spotify input files are recorded." compact />
        : <ul className="file-list">{d.spotifyDataFiles.map(file => <li key={file}><Database size={15} aria-hidden="true"/>{file}</li>)}</ul>}
    </Panel>
  </section>
}

function ScoreCard({ score }: { score: ScoreResult }) {
  const entries: [string, number][] = [
    ['Feature match', score.components.featureMatch],
    ['Artist / taste match', score.components.artistTasteMatch],
    ['Recent taste match', score.components.recentTasteMatch],
    ['Lifetime taste match', score.components.lifetimeTasteMatch],
    ['Historical behavior', score.components.historicalBehaviorMatch],
    ['Saved-track match', score.components.savedTrackMatch],
  ]
  return <article className="score-card">
    <div className="score-heading"><div><span className="eyebrow">PREDICTED LIKE SCORE</span><h3>{score.track}</h3><p>{score.artists.join(', ')}</p></div><div className="score-orb" aria-label={`${score.predictedLikeScore.toFixed(1)} out of 10`}><strong>{score.predictedLikeScore.toFixed(1)}</strong><span>/ 10</span></div></div>
    <p className="score-history-notice">{score.knownRecording ? `Known recording · ${formatCount(score.historicalEvents)} imported events across equivalent releases. Direct listening behavior contributes to this score.` : 'Unheard candidate · predicted from your broader listening patterns, without direct listening evidence.'} Relative compatibility, not a probability.</p>
    <div className="score-bars">{entries.map(([label, metric]) => <div key={label}><span>{label}</span><div aria-hidden="true"><i style={{ width: `${Math.max(0, Math.min(100, metric * 10))}%` }}/></div><b>{metric.toFixed(1)}</b></div>)}</div>
    <div className="score-adjustment"><span>Exploration adjustment</span><strong>{formatSigned(score.components.explorationAdjustment)}</strong></div>
    <div className="explanation"><strong>{score.confidence} confidence</strong>{score.explanations.map(line => <p key={line}>{line}</p>)}</div>
  </article>
}

function TrackTable({ tracks }: { tracks: TopTrack[] }) {
  if (tracks.length === 0) return <EmptyState message="No tracks exist in this date range." compact />
  return <div className="table-scroll"><table className="track-table">
    <caption className="sr-only">Top tracks by listening behavior</caption>
    <thead><tr><th>#</th><th>Track</th><th>Events</th><th>Qualified</th><th>Minutes</th><th>Avg. listen</th><th>Skip rate</th><th>First listen</th><th>Last listen</th><th>Saved</th></tr></thead>
    <tbody>{tracks.map((track, index) => <tr key={track.trackId}>
      <td className="muted">{String(index + 1).padStart(2, '0')}</td>
      <td><div className="track-name"><strong>{track.track}</strong><span>{track.artist}{track.album ? ` · ${track.album}` : ''}</span></div></td>
      <td>{formatNumber(track.rawEvents)}</td><td>{formatNumber(track.qualifiedPlays)}</td><td>{formatNumber(track.listeningMinutes)}</td><td>{formatMetric(track.averageListenSeconds)}s</td>
      <td><span className={track.skipRate > 35 ? 'bad' : ''}>{track.skipRate}%</span></td>
      <td><time dateTime={ensureUtc(track.firstListenUtc)}>{formatDate(track.firstListenUtc)}</time></td>
      <td><time dateTime={ensureUtc(track.lastListenUtc)}>{formatDate(track.lastListenUtc)}</time></td>
      <td>{track.saved === null ? '—' : track.saved ? 'Yes' : 'No'}</td>
    </tr>)}</tbody>
  </table></div>
}

function AlbumTable({ albums }: { albums: TopAlbum[] }) {
  if (albums.length === 0) return <EmptyState message="No albums exist in this date range." compact />
  return <div className="table-scroll"><table className="album-table">
    <caption className="sr-only">Top albums by listening time</caption>
    <thead><tr><th>Album</th><th>Minutes</th><th>Qualified</th><th>Recordings</th></tr></thead>
    <tbody>{albums.map(album => <tr key={album.albumId}><td><div className="track-name"><strong>{album.album}</strong><span>{album.artist}</span></div></td><td>{formatNumber(album.listeningMinutes)}</td><td>{formatNumber(album.qualifiedPlays)}</td><td>{formatNumber(album.uniqueTracks)}</td></tr>)}</tbody>
  </table></div>
}

function ArtistBars({ artists }: { artists: TopArtist[] }) {
  if (artists.length === 0) return <EmptyState message="No artists exist in this date range." compact />
  const max = artists[0]?.listeningMinutes || 1
  return <div className="artist-bars">{artists.map((artist, index) => <div key={artist.artistId}><span className="bar-rank">{index + 1}</span><div><strong>{artist.artist}</strong><span style={{ width: `${artist.listeningMinutes / max * 100}%` }}/></div><b>{formatCount(artist.listeningMinutes)}m</b></div>)}</div>
}

function EvolutionTable({ rows }: { rows: { period: string; artistId: number; artist: string; rank: number; listeningMinutes: number; qualifiedPlays: number }[] }) {
  const periods = [...new Set(rows.map(item => item.period))].slice(-6).reverse()
  if (periods.length === 0) return <EmptyState message="No artist evolution data is available." compact />
  return <div className="evolution-grid">{periods.map(period => <div key={period}><strong>{period}</strong>{rows.filter(item => item.period === period).slice(0, 5).map(row => <span key={row.artistId}><i>{row.rank}</i>{row.artist}<b>{formatCount(row.listeningMinutes)}m</b></span>)}</div>)}</div>
}

function Panel({ title, subtitle, className = '', children }: { title: string; subtitle?: string; className?: string; children: ReactNode }) {
  return <article className={`panel ${className}`}><div className="panel-heading"><div><h3>{title}</h3>{subtitle && <span>{subtitle}</span>}</div></div>{children}</article>
}

function RangeSelect({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return <select value={value} onChange={event => onChange(event.target.value)} aria-label="Date range">{rangeOptions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}</select>
}

function Metric({ label, value }: { label: string; value: string }) {
  return <div><span>{label}</span><strong>{value}</strong></div>
}

function Loading({ label }: { label: string }) {
  return <div className="state" role="status" aria-live="polite"><LoaderCircle className="spin" aria-hidden="true"/><strong>{label}</strong></div>
}

function InlineStatus({ label }: { label: string }) {
  return <div className="inline-state" role="status"><LoaderCircle className="spin" size={16} aria-hidden="true"/><span>{label}</span></div>
}

function ErrorState({ error, compact = false }: { error: unknown; compact?: boolean }) {
  return <div className={`state error ${compact ? 'compact' : ''}`} role="alert"><TriangleAlert aria-hidden="true"/><strong>That didn’t work</strong><span>{error instanceof Error ? error.message.slice(0, 300) : 'The local API is unavailable.'}</span></div>
}

function EmptyState({ message, compact = false }: { message: string; compact?: boolean }) {
  return <div className={`empty-state ${compact ? 'compact' : ''}`} role="status">{message}</div>
}

function formatNumber(value: number) {
  return new Intl.NumberFormat('en-US').format(Math.round(value))
}

function formatPercent(value: number | null) {
  return value === null ? 'Unknown' : `${Number.isFinite(value) ? value.toFixed(1) : '0.0'}%`
}

function ensureUtc(value: string) {
  return /(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : `${value}Z`
}

function formatDate(value: string | null) {
  return value
    ? new Intl.DateTimeFormat(undefined, { year: 'numeric', month: 'short', day: 'numeric', timeZone: 'UTC' }).format(new Date(ensureUtc(value)))
    : '—'
}

function formatSigned(value: number) {
  return `${value > 0 ? '+' : ''}${value.toFixed(1)}`
}

function rangeLabel(range: string) {
  return rangeOptions.find(option => option.value === range)?.label ?? range
}

function filterTimelineForRange(points: TimelinePoint[], range: string, asOf?: string) {
  const year = /^year:(\d{4})$/.exec(range)?.[1]
  if (year) return points.filter(point => point.period.startsWith(year))
  if (range === 'lifetime') return points
  const days = range === 'last365' ? 365 : range === 'last90' ? 90 : range === 'last30' ? 30 : null
  if (!days) return points
  const cutoff = asOf ? new Date(asOf) : new Date()
  cutoff.setUTCDate(cutoff.getUTCDate() - days)
  const cutoffPeriod = cutoff.toISOString().slice(0, 7)
  return points.filter(point => point.period >= cutoffPeriod)
}
