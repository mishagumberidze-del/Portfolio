import { useState } from 'react'
import type { Story } from './story-api'
import { featurePoints, monthlyGrid, strongestFeatureShift } from './story-motion'
import { monthLabel } from './story-math'

export function ListeningHeatmap({ data }: { data: Story }) {
  const grid = monthlyGrid(data), cells = grid.flatMap(row => row.months)
  const peak = Math.max(1, ...cells.map(cell => cell.totalMs ?? 0))
  const [chosen, setChosen] = useState<string | null>(null)
  const selected = cells.find(cell => cell.period === chosen) ?? [...cells].sort((a, b) => (b.totalMs ?? -1) - (a.totalMs ?? -1))[0]
  const nonzero = cells.filter(cell => (cell.totalMs ?? 0) > 0)
  const total = nonzero.reduce((sum, cell) => sum + (cell.totalMs ?? 0), 0)
  const bestThree = [...nonzero].sort((a, b) => b.totalMs! - a.totalMs!).slice(0, 3)
  const share = total > 0 ? bestThree.reduce((sum, cell) => sum + cell.totalMs!, 0) / total * 100 : null
  return <section className="home-panel listening-heatmap"><p className="eyebrow">YOUR MUSIC CALENDAR</p><h2>Some months hit different.</h2>
    <div className="heatmap-scroll"><div className="heatmap-grid" role="group" aria-label="Monthly listening heatmap"><span/>{Array.from({ length: 12 }, (_, i) => <span className="heatmap-month" key={i}>{new Intl.DateTimeFormat(undefined, { month: 'short', timeZone: 'UTC' }).format(new Date(Date.UTC(2020, i, 1)))}</span>)}
      {grid.map(row => <div className="heatmap-year" key={row.year}><strong>{row.year}</strong>{row.months.map(cell => <button key={cell.period} disabled={cell.totalMs === null} aria-pressed={cell.period === selected?.period} aria-label={`${monthLabel(cell.period)}: ${cell.totalMs === null ? 'outside imported coverage' : `${Math.round(cell.totalMs / 60000).toLocaleString('en-US')} minutes`}`} title={monthLabel(cell.period)} onClick={() => setChosen(cell.period)} style={{ backgroundColor: cell.totalMs === null ? 'transparent' : `rgba(30,215,96,${.09 + cell.totalMs / peak * .86})` }}/>)}</div>)}
    </div></div>
    {selected && <div className="heatmap-detail" aria-live="polite"><div><span>{monthLabel(selected.period)}</span><strong>{selected.totalMs === null ? 'No imported coverage' : `${Math.round(selected.totalMs / 60000).toLocaleString('en-US')} minutes`}</strong><p>{selected.artist ?? 'No defining artist'}</p></div><p>{share !== null ? `Your ${bestThree.length} busiest month${bestThree.length === 1 ? '' : 's'} account for ${share.toFixed(1)}% of listening time in this selection.` : 'No listening time available.'}</p></div>}
    <p className="source-caption">Brighter = more listening time. Outlined blanks lie outside the imported date range; dark filled cells have zero time in covered months. Partial edge months are not scaled up. UTC.</p>
  </section>
}

export function SoundJourney({ data }: { data: Story }) {
  const [axis, setAxis] = useState('Energy')
  const points = featurePoints(data.featureEvolution, axis), shift = strongestFeatureShift(points)
  const maximum = axis === 'Tempo' ? Math.max(100, Math.ceil(Math.max(...points.map(point => point.value ?? 0)) / 20) * 20) : 100
  const x = (i: number) => 45 + i * 550 / Math.max(1, points.length - 1), y = (value: number) => 200 - value / maximum * 160
  const segments: string[] = []; let segment: string[] = []
  points.forEach((point, i) => { if (point.value !== null) segment.push(`${x(i)},${y(point.value)}`); else if (segment.length) { segments.push(segment.join(' ')); segment = [] } }); if (segment.length) segments.push(segment.join(' '))
  return <section className="home-panel sound-journey"><p className="eyebrow">THE SOUND EVOLVES</p><h2>Your taste, in motion.</h2>
    <div className="feature-tabs" role="group" aria-label="Select an audio feature">{['Energy', 'Danceability', 'Valence', 'Acousticness', 'Tempo'].map(name => <button key={name} aria-pressed={axis === name} onClick={() => setAxis(name)}>{name}</button>)}</div>
    <div className="sound-plot"><svg viewBox="0 0 640 250" role="img" aria-label={`${axis} by year; gaps indicate less than 50% feature coverage`}>
      {[0, maximum / 2, maximum].map(tick => <g key={tick}><line x1={35} x2={620} y1={y(tick)} y2={y(tick)} stroke="#ffffff22"/><text x={0} y={y(tick) + 4} fill="#afbbba" fontSize={13}>{tick}</text></g>)}
      {segments.map((line, i) => <polyline key={`${axis}-${i}`} points={line} fill="none" stroke="#bba0ff" strokeWidth={4}/>)}
      {points.map((point, i) => <g key={point.year}>{point.value !== null && <><circle cx={x(i)} cy={y(point.value)} r={7} fill="#bba0ff"/><text x={x(i)} y={y(point.value) - 17} textAnchor="middle" fill="#fff" fontSize={14}>{point.value}</text></>}<text x={x(i)} y={233} textAnchor="middle" fill="#afbbba" fontSize={13}>{point.year}</text></g>)}
    </svg></div>
    <p className="sound-insight" aria-live="polite">{shift ? `Largest supported adjacent-year shift: ${axis.toLowerCase()} moved ${Math.abs(shift.change).toFixed(1)} ${axis === 'Tempo' ? 'BPM' : 'points'} ${shift.change > 0 ? 'up' : 'down'}, ${shift.from} → ${shift.to}.` : 'No adjacent-year shift of at least 5 points/BPM with sufficient coverage in both years.'}</p>
    <details><summary>Feature values and coverage</summary><div className="era-table"><table><thead><tr><th>Year</th><th>{axis} {axis === 'Tempo' ? '(BPM)' : '(/100)'}</th><th>Time covered</th></tr></thead><tbody>{points.map(point => <tr key={point.year}><th>{point.year}</th><td>{point.value ?? 'Insufficient coverage'}</td><td>{point.coverage}%</td></tr>)}</tbody></table></div></details>
    <p className="source-caption">Full imported calendar years, weighted by listening time. Only ≥50%-covered values appear; missing values are not zero. This describes music characteristics, not your emotions. Edge years may be partial.</p>
  </section>
}
