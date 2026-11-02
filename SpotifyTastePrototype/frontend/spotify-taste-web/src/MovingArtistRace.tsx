import { useMemo, useState } from 'react'
import type { RankEvolution } from './api'
import { artistInitials, raceCast } from './story-motion'

function ArtistBadge({ row }: { row: RankEvolution }) {
  const [failed, setFailed] = useState(false)
  const url = row.artworkUrl && /^https:\/\//.test(row.artworkUrl) ? row.artworkUrl : null
  return <span className="race-avatar" title={url && !failed ? `Associated album artwork for ${row.artist}, not an artist portrait` : row.artist}>
    <span aria-hidden="true">{artistInitials(row.artist)}</span>{url && !failed && <img src={url} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(true)}/>}
  </span>
}
export default function MovingArtistRace({ rows, period, chosen, choose }: { rows: RankEvolution[]; period: string; chosen?: number; choose: (id: number) => void }) {
  const cast = useMemo(() => raceCast(rows), [rows])
  const current = rows.filter(row => row.period === period)
  const max = Math.max(1, ...current.map(row => row.listeningMinutes))
  return <div className="moving-race" role="group" aria-label={`Top six artists in ${period}; badges move when rankings change`}>
    <div className="race-watermark" aria-hidden="true">{period}</div>
    {cast.map(actor => {
      const entry = current.find(row => row.artistId === actor.artistId)
      const visible = !!entry && entry.rank <= 6
      const slot = visible ? entry.rank - 1 : 6
      return <button key={actor.artistId} className="moving-race-row" tabIndex={visible ? 0 : -1} disabled={!visible} aria-hidden={!visible} aria-pressed={chosen === actor.artistId}
        aria-label={visible ? `Rank ${entry.rank}, ${actor.artist}, ${Math.round(entry.listeningMinutes).toLocaleString('en-US')} minutes. Follow this artist.` : undefined}
        onClick={() => choose(actor.artistId)} style={{ transform: `translateY(${slot * 68}px)`, opacity: visible ? 1 : 0, zIndex: visible ? 20 - slot : 0 }}>
        <span className="race-number">{visible ? `#${entry.rank}` : ''}</span><ArtistBadge row={actor}/>
        <span className="race-body"><strong>{actor.artist}</strong><span className="race-bar"><i style={{ width: `${visible ? entry.listeningMinutes / max * 100 : 0}%` }}/></span></span>
        <span className="race-value">{visible ? Math.round(entry.listeningMinutes).toLocaleString('en-US') : '—'}<small>min</small></span>
      </button>
    })}
  </div>
}
