import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ArrowDownRight, ArrowUpRight, Check, Download, Sparkles } from 'lucide-react'
import { getStory, type Ranking, type Story } from './story-api'
import { artistChoices, changePercent, monthLabel, strongestMonth } from './story-math'

const format = (value: number) => new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 }).format(value)

export function CountUp({ value, decimals = 0 }: { value: number; decimals?: number }) {
  // Keep the real total visible from the first frame; animate the card, not its facts.
  return <span>{new Intl.NumberFormat('en-US', { maximumFractionDigits: decimals, minimumFractionDigits: decimals }).format(value)}</span>
}

export function ArtistGuess({ items }: { items: Ranking[] }) {
  const [answer, setAnswer] = useState<number | null>(null)
  const winner = items[0]
  if (!winner) return <p>There isn’t enough listening in this chapter for a top artist.</p>
  return <div className="artist-guess"><p>Who took the most listening time in this chapter?</p><div className="guess-options">{artistChoices(items).map(item => <button key={item.id} disabled={answer !== null} className={answer !== null && item.id === winner.id ? 'guess-winner' : answer === item.id ? 'guess-picked' : ''} onClick={() => setAnswer(item.id)}>{item.name}{answer !== null && item.id === winner.id && <Check size={20}/>}</button>)}</div>
    <div className="guess-result" aria-live="polite">{answer === null ? 'Tap a name to reveal your #1.' : <><strong>{answer === winner.id ? 'You know your soundtrack.' : 'The numbers have a surprise.'}</strong><p>{winner.name} · {format(winner.totalMs / 60_000)} minutes · {format(winner.qualifiedPlays)} qualified plays.</p></>}</div>
    {answer !== null && <button className="text-button" onClick={() => setAnswer(null)}>Try the reveal again</button>}
  </div>
}

export function MonthExplorer({ journey }: { journey: Story['journey'] }) {
  const [selected, setSelected] = useState(() => strongestMonth(journey))
  const index = Math.min(selected, Math.max(0, journey.length - 1)), month = journey[index]
  const active = useRef<HTMLButtonElement>(null)
  const bars = useRef<HTMLDivElement>(null)
  const peak = Math.max(1, ...journey.map(item => item.totalMs))
  useEffect(() => { const row = bars.current, button = active.current; if (row && button) row.scrollLeft = button.offsetLeft - row.offsetLeft - row.clientWidth / 2 + button.clientWidth / 2 }, [index])
  if (!month) return <p>No monthly listening available.</p>
  return <div className="month-explorer">
    <div className="month-focus" aria-live="polite"><div><span>{monthLabel(month.period)}</span><strong>{month.definingArtist ?? 'A quiet chapter'}</strong></div><div><b>{format(month.totalMs / 60_000)}</b><span>minutes</span></div></div>
    <div ref={bars} className="month-bars" role="group" aria-label="Select a listening month">{journey.map((item, i) => <button key={item.period} ref={i === index ? active : undefined} aria-pressed={i === index} aria-label={`${monthLabel(item.period)}: ${format(item.totalMs / 60_000)} minutes, ${item.definingArtist ?? 'no artist'}`} onClick={() => setSelected(i)}><i style={{ height: `${Math.max(3, item.totalMs / peak * 130)}px` }}/><span>{item.period.slice(2)}</span></button>)}</div>
    <label className="month-scrubber">Move through your music <input type="range" min={0} max={journey.length - 1} value={index} onChange={e => setSelected(Number(e.target.value))} aria-valuetext={`${monthLabel(month.period)}, ${format(month.totalMs / 60_000)} minutes`}/></label>
    <p className="card-note">Tap a bar or drag the slider. Months and timestamps use UTC; missing listening is not filled in.</p>
  </div>
}

export function EraCompare({ years }: { years: number[] }) {
  const sorted = [...years].sort((a, b) => b - a)
  const [before, setBefore] = useState(sorted[1] ?? sorted[0])
  const [after, setAfter] = useState(sorted[0])
  const first = useQuery({ queryKey: ['story', before], queryFn: () => getStory(before), enabled: !!before, staleTime: 120_000 })
  const second = useQuery({ queryKey: ['story', after], queryFn: () => getStory(after), enabled: !!after, staleTime: 120_000 })
  if (sorted.length < 2) return <p>Import listening from two calendar years to compare eras.</p>
  const a = first.data, b = second.data
  const delta = a && b ? changePercent(a.summary.totalMs, b.summary.totalMs) : null
  return <section className="home-panel era-compare"><div className="section-intro"><div><p className="eyebrow">TIME MACHINE</p><h2>Same you. Different era.</h2></div><Sparkles aria-hidden="true"/></div>
    <div className="era-selectors">{[{ value: before, change: setBefore, label: 'From year' }, { value: after, change: setAfter, label: 'To year' }].map(item => <label key={item.label}>{item.label}<select value={item.value} onChange={e => item.change(Number(e.target.value))}>{sorted.map(year => <option key={year} value={year}>{year}</option>)}</select></label>)}</div>
    {(first.isPending || second.isPending) ? <p role="status">Rewinding your listening…</p> : first.error || second.error ? <div role="alert"><p>Could not load this comparison.</p><button onClick={() => { first.refetch(); second.refetch() }}>Try again</button></div> : a && b && <>
      <div className="era-matchup">{[a, b].map((item, i) => <div key={i}><span>{item.year}</span><strong>{item.topArtists[0]?.name ?? 'No listening'}</strong><p>{format(item.summary.totalMs / 3_600_000)} hours · {format(item.summary.artists)} artists</p><small>{item.summary.firstListenUtc?.slice(0, 10) ?? '—'} — {item.summary.lastListenUtc?.slice(0, 10) ?? '—'}</small></div>)}</div>
      <div className="era-change">{delta !== null ? <>{delta >= 0 ? <ArrowUpRight/> : <ArrowDownRight/>}<strong>{format(Math.abs(delta))}% {delta >= 0 ? 'more' : 'less'} listening</strong></> : <strong>No earlier listening to calculate a percentage.</strong>}</div>
      <div className="era-feature-shifts">{['Energy', 'Danceability', 'Valence'].map(name => { const x = a.soundProfile.find(axis => axis.name === name), y = b.soundProfile.find(axis => axis.name === name); const covered = x?.value != null && y?.value != null && x.coveragePercent >= 50 && y.coveragePercent >= 50; return <div key={name}><span>{name}</span><b>{covered ? `${x!.value} → ${y!.value}` : 'Insufficient coverage'}</b><small>{x?.coveragePercent ?? 0}% / {y?.coveragePercent ?? 0}% time covered</small></div> })}</div>
      <p className="source-caption">Calendar-year totals, not equal-length samples. Edge years can be partial; compare the actual date ranges above. Audio features describe the tracks, not your mood.</p>
    </>}
  </section>
}

// A fresh data visualization, drawn locally. No screenshot library or external upload.
export function RecapDownload({ data }: { data: Story }) {
  const [status, setStatus] = useState('')
  const [accent, setAccent] = useState('#1ed760')
  const [featured, setFeatured] = useState('artists')
  const ranking = featured === 'artists' ? data.topArtists : data.topSongs
  const download = async () => {
    try {
      setStatus('Preparing your recap…')
      const canvas = document.createElement('canvas'); canvas.width = 1080; canvas.height = 1920
      const ctx = canvas.getContext('2d'); if (!ctx) throw new Error('Canvas unavailable')
      ctx.fillStyle = '#131416'; ctx.fillRect(0, 0, 1080, 1920)
      ctx.fillStyle = accent; ctx.fillRect(0, 0, 1080, 14)
      const text = (value: string, x: number, y: number, size: number, color = '#fff', weight = 700) => { ctx.fillStyle = color; ctx.font = `${weight} ${size}px system-ui, sans-serif`; ctx.fillText(value, x, y) }
      const clipped = (value: string, maxWidth: number, size: number) => { ctx.font = `700 ${size}px system-ui, sans-serif`; let result = value; while (ctx.measureText(result).width > maxWidth && result.length > 1) result = result.slice(0, -1); return result === value ? result : result.slice(0, -1) + '…' }
      text('MY MUSIC STORY', 80, 120, 30, accent); text(data.year ? String(data.year) : 'ALL TIME', 80, 300, 130)
      text('This was my', 80, 425, 76); text('soundtrack.', 80, 520, 76, accent)
      text(format(data.summary.totalMs / 60_000), 80, 715, 120); text('MINUTES OF MUSIC', 85, 775, 28, '#babdc4')
      text(`${format(data.summary.artists)} artists  ·  ${format(data.summary.recordings)} recordings`, 80, 855, 30)
      text(featured === 'artists' ? 'MY TOP ARTISTS' : 'MY TOP RECORDINGS', 80, 970, 28, accent)
      ranking.slice(0, 5).forEach((item, i) => { text(`0${i + 1}`, 80, 1060 + i * 98, 32, '#7d828b'); text(clipped(item.name, 790, 42), 160, 1060 + i * 98, 42) })
      text('MY #1 RECORDING', 80, 1580, 28, accent)
      text(clipped(data.topSongs[0]?.name ?? 'No recording', 920, 48), 80, 1660, 48)
      text(clipped(data.topSongs[0]?.artist ?? '', 920, 30), 80, 1710, 30, '#babdc4')
      text(`${data.summary.firstListenUtc?.slice(0, 10) ?? '—'} — ${data.summary.lastListenUtc?.slice(0, 10) ?? '—'} · UTC`, 80, 1815, 26, '#babdc4', 400)
      text('Imported history · independent Spotify Taste project', 80, 1870, 24, '#babdc4', 400)
      const blob = await new Promise<Blob>((resolve, reject) => canvas.toBlob(b => b ? resolve(b) : reject(new Error('Image unavailable')), 'image/png'))
      const url = URL.createObjectURL(blob), link = document.createElement('a'); link.href = url; link.download = `my-music-story-${data.year ?? 'all-time'}-${featured}.png`; document.body.appendChild(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 30_000)
      setStatus('Recap downloaded. It contains listening stats—share it only where you want.')
    } catch { setStatus('Could not download on this browser. You can still take a screenshot.') }
  }
  return <div className="recap-download"><details className="recap-customizer"><summary>Customize my recap</summary><div className="recap-options"><label>Accent<select value={accent} onChange={e => setAccent(e.target.value)}><option value="#1ed760">Spotify green</option><option value="#bba0ff">Electric violet</option><option value="#ffb8dc">Pink</option></select></label><label>Feature<select value={featured} onChange={e => setFeatured(e.target.value)}><option value="artists">Top artists</option><option value="recordings">Top recordings</option></select></label></div><div className="recap-mini" style={{ borderColor: accent }}><span style={{ color: accent }}>MY MUSIC STORY · {data.year ?? 'ALL TIME'}</span><strong>{format(data.summary.totalMs / 60_000)} minutes</strong><ol>{ranking.slice(0, 5).map(item => <li key={item.id}>{item.name}</li>)}</ol><small>Text preview of the portrait recap</small></div></details><button className="text-button" onClick={download}><Download size={18}/> Download my recap</button><small role="status">{status || 'Portrait PNG · made on this device · nothing posted automatically'}</small></div>
}
