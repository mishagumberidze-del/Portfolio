import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ArrowLeft, ArrowRight, Pause, Play } from 'lucide-react'
import { api } from './api'
import { rankMovement, rankTimeline } from './story-math'
import MovingArtistRace from './MovingArtistRace'

export default function ArtistTimeline({ year = null, compact = false }: { year?: number | null; compact?: boolean }) {
  const [grain, setGrain] = useState(year ? 'month' : 'year'), [chosen, setChosen] = useState<number | null>(null)
  const [periodIndex, setPeriodIndex] = useState<number | null>(null), [playing, setPlaying] = useState(false)
  const [search, setSearch] = useState('')
  const [speed, setSpeed] = useState(1800)
  const query = useQuery({ queryKey: ['artist-timeline', grain], queryFn: () => api.evolution(grain), staleTime: 120_000 })
  const rows = (query.data ?? []).filter(row => !year || row.period.slice(0, 4) === String(year)), periods = [...new Set(rows.map(row => row.period))].sort()
  const index = Math.max(0, Math.min(periodIndex ?? periods.length - 1, periods.length - 1))
  const period = periods[index]
  const artists = [...new Map(rows.map(row => [row.artistId, row.artist])).entries()].sort((a, b) => a[1].localeCompare(b[1]))
  const selectedId = artists.some(([id]) => id === chosen) ? chosen! : rows.find(row => row.period === periods.at(-1) && row.rank === 1)?.artistId
  const timeline = selectedId === undefined ? [] : rankTimeline(rows, selectedId)
  const current = timeline[index]?.entry, previous = timeline[index - 1]?.entry
  const change = rankMovement(current?.rank ?? null, previous?.rank ?? null)
  const selectedName = artists.find(([id]) => id === selectedId)?.[1] ?? 'Choose an artist'
  useEffect(() => {
    const stop = () => { if (document.hidden) setPlaying(false) }
    document.addEventListener('visibilitychange', stop)
    return () => document.removeEventListener('visibilitychange', stop)
  }, [])
  useEffect(() => {
    if (!playing || !periods.length) return
    const timer = setTimeout(() => { if (index >= periods.length - 1) setPlaying(false); else setPeriodIndex(index + 1) }, speed)
    return () => clearTimeout(timer)
  }, [playing, index, periods.length, speed])
  const selectPeriod = (value: number) => { setPlaying(false); setPeriodIndex(value) }
  const graphWidth = Math.max(550, periods.length * 48)
  const x = (i: number) => 40 + i * (graphWidth - 80) / Math.max(1, periods.length - 1)
  const y = (rank: number) => 24 + (rank - 1) / 19 * 150
  const segments: string[] = []; let segment: string[] = []
  timeline.forEach((point, i) => { if (point.entry) segment.push(`${x(i)},${y(point.entry.rank)}`); else if (segment.length) { segments.push(segment.join(' ')); segment = [] } })
  if (segment.length) segments.push(segment.join(' '))
  return <section className={`home-panel artist-timeline ${compact ? 'compact-race' : ''}`}>
    <div className="section-intro"><div><p className="eyebrow">THE CHANGING GUARD</p>{!compact && <h2>Who ruled each era?</h2>}</div><label>Group by <select value={grain} onChange={e => { setGrain(e.target.value); setPeriodIndex(null); setPlaying(false) }}><option value="year">Year</option><option value="quarter">Quarter</option><option value="month">Month</option></select></label></div>
    {query.isPending ? <p role="status">Finding your chart history…</p> : query.error ? <div role="alert"><p>{query.error.message}</p><button onClick={() => query.refetch()}>Try again</button></div> : !rows.length ? <p>No artist rankings in the imported history.</p> : <>
      <div className="race-speed"><label>Animation pace <select value={speed} onChange={e => setSpeed(Number(e.target.value))}><option value={1000}>Fast</option><option value={1800}>Normal</option><option value={3000}>Slow</option></select></label><span>{year ? `${year} · ${grain} snapshots` : `All imported years · ${grain} snapshots`}</span></div>
      <div className="rank-player"><button aria-label="Previous period" disabled={index === 0} onClick={() => selectPeriod(index - 1)}><ArrowLeft size={18}/></button><strong aria-live="polite">{period}</strong><button aria-label="Next period" disabled={index === periods.length - 1} onClick={() => selectPeriod(index + 1)}><ArrowRight size={18}/></button><button onClick={() => { if (!playing && index === periods.length - 1) setPeriodIndex(0); setPlaying(p => !p) }} aria-label={playing ? 'Pause artist timeline' : 'Play artist timeline'}>{playing ? <Pause size={18}/> : <Play size={18}/>}</button></div>
      <label className="month-scrubber"><span className="sr-only">Artist ranking period</span><input type="range" min={0} max={periods.length - 1} value={index} aria-valuetext={period} onChange={e => selectPeriod(Number(e.target.value))}/></label>
      <MovingArtistRace rows={rows} period={period} chosen={selectedId} choose={id => { setChosen(id); setPlaying(false); setSearch('') }}/>
      <p className="race-art-note">Badges use cached associated album covers where available; otherwise artist initials. They are not official artist logos.</p>
      {!compact && <><div className="artist-selector"><label>Find an artist<input type="search" placeholder="Search charted artists" value={search} onChange={e => setSearch(e.target.value)}/></label><label>Follow their rank<select value={selectedId ?? ''} onChange={e => { setChosen(Number(e.target.value)); setPlaying(false) }}>{artists.filter(([id, name]) => id === selectedId || name.toLocaleLowerCase().includes(search.toLocaleLowerCase())).map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label></div>
      <div className="rank-spotlight" aria-live="polite"><div><strong>{selectedName}</strong><span>{current ? `#${current.rank} in ${period}` : `Outside the recorded top 20 in ${period}`}</span></div><p>{change === null ? 'No comparable rank in the previous displayed period.' : change === 0 ? 'Same rank as the previous displayed period.' : `${Math.abs(change)} place${Math.abs(change) === 1 ? '' : 's'} ${change > 0 ? 'up' : 'down'} from ${periods[index - 1]}.`}</p></div>
      <div className="rank-chart"><svg viewBox={`0 0 ${graphWidth} 225`} style={{ minWidth: graphWidth }} role="img" aria-label={`${selectedName}'s recorded top-20 rank over time. Rank 1 at top; gaps mean no recorded top-20 entry.`}>{[1, 10, 20].map(rank => <g key={rank}><line x1={30} x2={graphWidth - 20} y1={y(rank)} y2={y(rank)} stroke="#ffffff18"/><text x={4} y={y(rank) + 4} fill="#a9b6af" fontSize={12}>#{rank}</text></g>)}{segments.map((points, i) => <polyline key={i} points={points} fill="none" stroke="#1ed760" strokeWidth={3}/>)}{timeline.map((point, i) => <g key={point.period}>{point.entry && <circle cx={x(i)} cy={y(point.entry.rank)} r={i === index ? 7 : 4} fill={i === index ? '#fff' : '#1ed760'}/>}<text x={x(i)} y={205} textAnchor="middle" fontSize={12} fill={i === index ? '#fff' : '#9cab9f'}>{point.period}</text></g>)}</svg></div>
      <details className="rank-data"><summary>Read the rank history as a table</summary><div className="era-table"><table><thead><tr><th>Period</th><th>Rank</th><th>Minutes</th></tr></thead><tbody>{timeline.map(point => <tr key={point.period}><th>{point.period}</th><td>{point.entry ? `#${point.entry.rank}` : 'Outside recorded top 20'}</td><td>{point.entry?.listeningMinutes.toLocaleString('en-US') ?? 'Unknown'}</td></tr>)}</tbody></table></div></details>
      </>}
      <p className="source-caption">{year ? `${year} imported history.` : 'Lifetime imported history.'} Rankings use listening time, then qualified plays. Collaborators receive artist credit. Only top-20 entries are returned: gaps do not mean zero listening. Partial periods are not annualized.</p>
    </>}
  </section>
}
