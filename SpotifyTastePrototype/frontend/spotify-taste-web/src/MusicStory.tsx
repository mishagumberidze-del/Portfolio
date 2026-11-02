import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ArrowDownRight, ArrowLeft, ArrowRight, Disc3, Play, Pause, List, Sparkles, X } from 'lucide-react'
import { getStory, type Ranking, type SoundAxis, type Story } from './story-api'
import { demoMode } from './api'
import './story.css'
import ArtistTimeline from './ArtistTimeline'
import { ListeningHeatmap, SoundJourney } from './ListeningInsights'
import { ArtistGuess, CountUp, EraCompare, MonthExplorer, RecapDownload } from './StoryExtras'
import { safeSpotifyUrl, stepChapter } from './story-math'

const number = (n: number) => new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 }).format(n)
const hours = (ms: number) => number(ms / 3_600_000)
const date = (s: string | null) => s ? s.slice(0, 10) : 'No history'

export default function MusicStory({ explore, discover }: { explore: () => void; discover: () => void }) {
  const [year, setYear] = useState<number | null>(null)
  const [playing, setPlaying] = useState(false)
  const query = useQuery({ queryKey: ['story', year], queryFn: () => getStory(year), staleTime: 120_000 })
  if (query.isPending) return <div className="story-loading" role="status"><Disc3 className="spin"/>Finding the chapters in your listening history…</div>
  if (query.error || !query.data) return <div className="notice" role="alert">{query.error?.message ?? 'Story unavailable'} <button onClick={() => query.refetch()}>Try again</button></div>
  const d = query.data
  return <section className="music-home">
    <div className="story-toolbar"><span className="eyebrow">YOUR MUSIC. YOUR TIMELINE.</span><label>Chapter <select value={year ?? 'lifetime'} onChange={e => setYear(e.target.value === 'lifetime' ? null : Number(e.target.value))}><option value="lifetime">All-time story</option>{d.availableYears.map(y => <option key={y} value={y}>{y}</option>)}</select></label></div>
    <div className="music-hero">
      <div className="hero-orbits" aria-hidden="true"><i/><i/><i/><div><Disc3 strokeWidth={.7}/></div></div>
      <div className="music-hero-copy"><p className="eyebrow">{year ?? 'EVERY ERA'} · A PRIVATE LISTENING STORY</p><h2>Your music.<br/><em>Still unfolding.</em></h2><p>{d.summary.events ? `${hours(d.summary.totalMs)} hours. ${number(d.summary.recordings)} recordings. A soundtrack that could only be yours.` : 'This chapter has no imported listening yet.'}</p>
        <div className="hero-actions"><button className="primary-button" disabled={!d.summary.events} onClick={() => setPlaying(true)}><Play size={17} fill="currentColor"/> Play my story</button><button className="text-button" onClick={explore}>Explore the numbers <ArrowDownRight size={18}/></button></div>
        <span className="source-caption">{date(d.summary.firstListenUtc)} — {date(d.summary.lastListenUtc)} · imported history · UTC</span>
      </div>
    </div>
    {!!d.summary.events && <>
      <div className="story-stat-grid"><SmallStat value={hours(d.summary.totalMs)} label="hours in your headphones"/><SmallStat value={number(d.summary.artists)} label="artists in your orbit"/><SmallStat value={number(d.newRecordings)} label="recordings first seen in this chapter"/></div>
      <section className="home-panel"><p className="eyebrow">SCRUB THROUGH YOUR SOUNDTRACK</p><h2>One month. A whole era.</h2><MonthExplorer key={year ?? 'all'} journey={d.journey}/></section>
      <EraCompare years={d.availableYears}/>
      <ArtistTimeline key={year ?? 'all-eras'} year={year}/>
      <ListeningHeatmap key={`heat-${year ?? 'all'}`} data={d}/>
      <SoundJourney data={d}/>
      <RecapDownload data={d}/>
      <div className="section-intro"><div><p className="eyebrow">THE HEAVY ROTATION</p><h2>These stayed with you.</h2></div><span className="muted">Ranked by listening time</span></div>
      <div className="home-favorites">{[['Your defining artist', d.topArtists[0]], ['The song on repeat', d.topSongs[0]], ['An album to return to', d.topAlbums[0]]].map(([label, item], i) => typeof item !== 'string' && item && <article className={`favorite-tile tone-${i}`} key={String(label)}><Artwork item={item}/><p className="eyebrow">{String(label)}</p><h3>{item.name}</h3><p>{item.artist ?? 'Your #1 artist'} · {number(item.totalMs / 60_000)} min</p></article>)}</div>
      <div className="home-duo"><section className="home-panel"><p className="eyebrow">YOUR TOP FIVE</p><h2>A familiar refrain.</h2><RankList items={d.topSongs}/></section><section className="home-panel profile-panel"><p className="eyebrow">SOUND PROFILE</p><h2>The shape of your sound.</h2><SoundProfile axes={d.soundProfile}/></section></div>
      <div className="discovery-bridge"><Sparkles size={36}/><div><p className="eyebrow">THE NEXT CHAPTER</p><h2>There’s more out there.</h2><p>Find unheard recordings, then tell your recommendation lab what actually connects.</p></div><button className="primary-button" onClick={discover}>Find my next favorite <ArrowRight size={17}/></button></div>
      <div className="home-duo"><section className="home-panel"><p className="eyebrow">YOUR PEOPLE</p><h2>Five defining voices.</h2><RankList items={d.topArtists}/></section><section className="home-panel"><p className="eyebrow">BETWEEN THE NUMBERS</p>{d.facts.slice(0, 2).map(f => <div className="personal-fact" key={f.title}><h3>{f.title}</h3><p>{f.detail}</p></div>)}<button className="text-button" onClick={() => setPlaying(true)}>See the full story <ArrowRight size={16}/></button></section></div>
    </>}
    <p className="source-caption">Independent personal project, not an official Spotify product. Saved status is separate from listening behavior.</p>
    {playing && <StoryPlayer data={d} close={() => setPlaying(false)}/>}
  </section>
}

export function Artwork({ item }: { item: Pick<Ranking, 'name' | 'artworkUrl'> }) {
  return <div className="story-art">{item.artworkUrl ? <img src={item.artworkUrl} alt={`Album artwork associated with ${item.name}`} loading="lazy" referrerPolicy="no-referrer" onError={e => { e.currentTarget.style.display = 'none' }}/>: null}<Disc3 aria-hidden="true"/></div>
}
function SmallStat({ value, label }: { value: string; label: string }) { return <div><strong>{value}</strong><span>{label}</span></div> }
function RankList({ items }: { items: Ranking[] }) { return <ol className="story-ranking">{items.map((r, i) => <li key={r.id}><b>{String(i + 1).padStart(2, '0')}</b><div><strong>{safeSpotifyUrl(r.spotifyUrl) ? <a className="story-listen-link" href={r.spotifyUrl!} target="_blank" rel="noreferrer" aria-label={`Open ${r.name} on Spotify`}>{r.name} <ArrowRight size={16}/></a> : r.name}</strong><span>{r.artist ?? `${number(r.qualifiedPlays)} qualified plays`}</span></div><small>{number(r.totalMs / 60_000)} min</small></li>)}</ol> }
function SoundProfile({ axes }: { axes: SoundAxis[] }) { return <><div className="sound-axes">{axes.map(a => <div key={a.name}><div><span>{a.name}</span><b>{a.value ?? '—'} <small>{a.unit}</small></b></div>{a.unit !== 'BPM' && <div className="sound-meter"><i style={{ width: `${a.value ?? 0}%` }}/></div>}<small>{a.coveragePercent}% of listening time covered{a.personalPercentile !== null ? ` · above ${a.personalPercentile}% of your lifetime listening-weighted values` : ''}</small></div>)}</div><p className="source-caption">Listening-time-weighted audio features. The bars describe your music, not your mood or personality. Valence is a provider’s musical-brightness estimate.</p></> }

type Card = { label: string; title: string; accent?: string; body: ReactNode }
export function StoryCard({ card, period }: { card: Card; period: string }) { return <article className={`story-card ${card.accent ?? ''}`}><div className="story-card-top"><span>MY MUSIC STORY</span><span>{period}</span></div><div className="story-card-content"><p className="eyebrow">{card.label}</p><h2>{card.title}</h2>{card.body}</div><footer>{demoMode ? 'PREPARED PORTFOLIO STORY' : 'YOUR PRIVATE SOUNDTRACK'} <span>Spotify Taste · {demoMode ? 'static demo' : 'independent & local'}</span></footer></article> }

function cardsFor(d: Story): Card[] {
  const s = d.summary; const period = d.year ? String(d.year) : 'all these years'
  const cards: Card[] = [{ label: 'PRESS REPLAY', title: `Here’s what ${period} sounded like.`, accent: 'lime-card', body: <><div className="giant-number">{<CountUp value={s.totalMs / 3_600_000}/>}<small>hours of music</small></div><p>{number(s.recordings)} recordings, {number(s.artists)} artists, and every little phase in between.</p></> },
    { label: 'TIME WELL LISTENED', title: 'You made room for music.', body: <><div className="giant-number">{<CountUp value={s.totalMs / 86_400_000} decimals={1}/>}<small>equivalent full days</small></div><p>{number(s.totalMs / 60_000)} minutes · {number(s.activeDays)} active calendar dates</p>{d.comparison && <p className="card-note">{d.comparison.changePercent !== null ? `${Math.abs(d.comparison.changePercent)}% ${d.comparison.changePercent >= 0 ? 'more' : 'less'} listening time than ${d.comparison.year}. ` : ''}{d.comparison.note}</p>}</> }]
  const spotlight = (label: string, item: Ranking | undefined, song = false) => { if (item) cards.push({ label, title: item.name, accent: 'violet-card', body: <div className="spotlight"><Artwork item={item}/><div><p>{item.artist}</p>{safeSpotifyUrl(item.spotifyUrl) && <a className="story-listen-link" href={item.spotifyUrl!} target="_blank" rel="noreferrer">Open in Spotify <ArrowRight size={18}/></a>}<div className="giant-number medium">{number(item.totalMs / 60_000)}<small>minutes · {number(item.events)} events</small></div><p>{number(item.qualifiedPlays)} qualified plays{item.previousRank ? ` · #${item.previousRank} in the previous year` : ''}</p>{song && <p className="card-note">Recording-level total across equivalent releases. First imported listen {date(item.firstListenUtc)}. Peak month: {item.peakMonth}.</p>}</div></div> }) }
  spotlight('YOUR #1 ARTIST', d.topArtists[0]); spotlight('YOUR #1 RECORDING', d.topSongs[0], true); spotlight('YOUR #1 ALBUM', d.topAlbums[0])
  cards.push({ label: 'TOP FIVE ARTISTS', title: 'Your inner circle.', body: <RankList items={d.topArtists}/> }, { label: 'TOP FIVE RECORDINGS', title: 'You came back for more.', accent: 'peach-card', body: <RankList items={d.topSongs}/> },
    { label: 'MONTH BY MONTH', title: 'Every chapter had a sound.', body: <MonthExplorer journey={d.journey}/> },
    { label: 'FIRST ENCOUNTERS', title: 'Your world got a little bigger.', accent: 'lime-card', body: <><div className="giant-number">{number(d.newRecordings)}<small>recordings first seen</small></div><p>And {number(d.newArtists)} artists first appearing in this chapter of your imported history.</p><p className="card-note">“New” means absent from earlier imported history, not necessarily the first time you heard them in your life.</p></> },
    { label: 'THE ONES THAT STAY', title: 'Some sounds follow you.', body: <>{d.facts.map(f => <div className="personal-fact" key={f.title}><h3>{f.title}</h3><p>{f.detail}</p></div>)}</> },
    { label: 'LISTENING HABITS', title: 'The rhythm behind the plays.', body: <><div className="story-stat-grid"><SmallStat value={`${d.behavior.repeatRate}%`} label="events after a recording’s first play in this period"/><SmallStat value={`${d.behavior.skipRate}%`} label="explicit skips or listens under 10 seconds"/><SmallStat value={d.behavior.completionRateEstimate === null ? '—' : `${d.behavior.completionRateEstimate}%`} label="known-duration plays reaching at least 90%"/></div><p className="card-note">Completion coverage: {number(d.behavior.eventsWithKnownDuration)} of {number(s.events)} events. Aggregate behavior, not judgments about individual skips.</p></> },
    { label: 'YOUR SOUND PROFILE', title: 'A signature, not a stereotype.', accent: 'violet-card', body: <SoundProfile axes={d.soundProfile}/> },
    { label: 'TASTE IN MOTION', title: 'Your sound has chapters.', body: <><div className="era-table"><table><caption className="sr-only">Listening-time-weighted features by year</caption><thead><tr><th>Year</th><th>Energy /100</th><th>Dance /100</th><th>Tempo BPM</th></tr></thead><tbody>{d.featureEvolution.map(e => <tr key={e.year}><th>{e.year}</th>{['Energy', 'Danceability', 'Tempo'].map(n => <td key={n}>{e.axes.find(a => a.name === n)?.value ?? '—'}</td>)}</tr>)}</tbody></table></div>{d.featureInsights.slice(0, 2).map(i => <p className="card-note" key={i}>{i}</p>)}<p className="card-note">Full imported calendar-year profiles; edge years may be partial. Insights require ≥50% feature-time coverage in both years and a shift of ≥5 points or BPM.</p></> },
    { label: 'KEEP THE STORY GOING', title: 'This soundtrack is yours.', accent: 'peach-card', body: <><div className="giant-number medium">{number(s.recordings)}<small>recordings worth a chapter</small></div><p>{d.topArtists[0]?.name} led the way. The next favorite is still out there.</p><p className="card-note">Save your recap as a portrait image. No automatic posts, no private access link included.</p></> })
  if (d.topArtists.length > 1) cards.splice(2, 0, { label: 'THE BIG REVEAL', title: 'You had a favorite. Who was it?', accent: 'peach-card', body: <ArtistGuess items={d.topArtists}/> })
  cards.splice(7, 0, { label: 'ARTISTS IN MOTION', title: 'Your favorites changed places.', body: <ArtistTimeline year={d.year} compact/> })
  cards.splice(cards.length - 1, 0, { label: 'SOUND WAVES', title: 'Every era has its own signature.', accent: 'violet-card', body: <SoundJourney data={d}/> })
  return cards
}

function StoryPlayer({ data, close }: { data: Story; close: () => void }) {
  const [index, setIndex] = useState(0)
  const [autoplay, setAutoplay] = useState(() => !matchMedia('(prefers-reduced-motion: reduce)').matches)
  const [holding, setHolding] = useState(false)
  const [hidden, setHidden] = useState(document.hidden)
  const [chapters, setChapters] = useState(false)
  const [duration, setDuration] = useState(12)
  const dialog = useRef<HTMLDialogElement>(null)
  const progress = useRef<HTMLDivElement>(null)
  const elapsed = useRef(0)
  const touchStart = useRef<{ x: number; y: number } | null>(null)
  const cards = useMemo(() => cardsFor(data), [data])
  const go = (target: number) => { elapsed.current = 0; progress.current?.style.setProperty('--elapsed', '0%'); setIndex(stepChapter(target, 0, cards.length)) }
  const move = (delta: number) => go(index + delta)
  const interactiveCard = cards[index].label === 'THE BIG REVEAL' || cards[index].label === 'MONTH BY MONTH' || cards[index].label === 'ARTISTS IN MOTION' || cards[index].label === 'SOUND WAVES'
  const running = autoplay && !holding && !hidden && !chapters && !interactiveCard

  useEffect(() => {
    const el = dialog.current; const previousFocus = document.activeElement as HTMLElement | null
    el?.showModal(); const prior = document.body.style.overflow; document.body.style.overflow = 'hidden'
    const visibility = () => setHidden(document.hidden)
    const motion = matchMedia('(prefers-reduced-motion: reduce)')
    const motionChange = () => { if (motion.matches) setAutoplay(false) }
    document.addEventListener('visibilitychange', visibility); motion.addEventListener('change', motionChange)
    return () => { el?.close(); document.body.style.overflow = prior; previousFocus?.focus(); document.removeEventListener('visibilitychange', visibility); motion.removeEventListener('change', motionChange) }
  }, [])
  useEffect(() => {
    if (!running) return
    let frame = 0, prior = performance.now()
    const tick = (now: number) => {
      elapsed.current += Math.min(100, now - prior); prior = now
      const fraction = Math.min(1, elapsed.current / (duration * 1000))
      progress.current?.style.setProperty('--elapsed', `${fraction * 100}%`)
      if (fraction >= 1) {
        if (index === cards.length - 1) setAutoplay(false)
        else { elapsed.current = 0; progress.current?.style.setProperty('--elapsed', '0%'); setIndex(i => stepChapter(i, 1, cards.length)) }
      } else frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [running, duration, index, cards.length])

  return <dialog ref={dialog} className="story-dialog" aria-label="Your music story" onCancel={close} onKeyDown={e => {
    if (['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON', 'A'].includes((e.target as HTMLElement).tagName)) return
    if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') { e.preventDefault(); move(e.key === 'ArrowRight' ? 1 : -1) }
    if (e.code === 'Space') { e.preventDefault(); setAutoplay(p => !p) }
  }}>
    <div className="story-controls">
      <span aria-live="polite">{index + 1} / {cards.length} · {cards[index].label}</span>
      <div className="player-actions">
        <button onClick={() => setAutoplay(p => !p)} aria-label={autoplay ? 'Pause story playback' : 'Play story automatically'} aria-pressed={autoplay}>{autoplay ? <Pause size={18}/> : <Play size={18}/>}</button>
        <label className="story-speed"><span className="sr-only">Seconds per chapter</span><select aria-label="Seconds per chapter" value={duration} onChange={e => setDuration(Number(e.target.value))}><option value={8}>8s</option><option value={12}>12s</option><option value={20}>20s</option></select></label>
        <button onClick={() => setChapters(p => !p)} aria-label="Choose a story chapter" aria-expanded={chapters}><List size={18}/></button>
        <button onClick={close} aria-label="Close story"><X/></button>
      </div>
    </div>
    <div ref={progress} className="story-progress" aria-label="Story chapters">{cards.map((c, i) => <button key={c.label} aria-label={`Go to ${c.label}`} aria-current={i === index ? 'step' : undefined} className={i < index ? 'complete' : i === index ? 'current' : ''} onClick={() => go(i)}><i/></button>)}</div>
    {chapters && <nav className="chapter-picker" aria-label="Choose a story chapter">{cards.map((c, i) => <button key={c.label} aria-current={i === index ? 'step' : undefined} onClick={() => { go(i); setChapters(false) }}><span>{String(i + 1).padStart(2, '0')}</span>{c.label}</button>)}</nav>}
    <p className="player-hint">{interactiveCard ? 'Take your time—this chapter waits for you. Tap Next when ready.' : running ? 'Playing your story · hold the card to pause' : 'Paused · move at your own pace'}</p>
    <div className="story-stage" key={index} tabIndex={0} aria-label="Story card; use left and right arrow keys to navigate"
      onPointerDown={e => { if (!(e.target as HTMLElement).closest('button, a, input, select')) { e.currentTarget.setPointerCapture(e.pointerId); setHolding(true) } }}
      onPointerUp={() => setHolding(false)} onPointerCancel={() => setHolding(false)} onLostPointerCapture={() => setHolding(false)}
      onFocus={e => { if ((e.target as HTMLElement).closest('button, a, input, select')) setAutoplay(false) }}
      onTouchStart={e => { touchStart.current = { x: e.changedTouches[0].screenX, y: e.changedTouches[0].screenY } }}
      onTouchEnd={e => { const start = touchStart.current; if (!start) return; const dx = e.changedTouches[0].screenX - start.x, dy = e.changedTouches[0].screenY - start.y; if (!(e.target as HTMLElement).closest('input, button, select, a, summary, .month-bars, .sound-plot, .heatmap-scroll, .era-table, .rank-chart') && Math.abs(dx) > 70 && Math.abs(dx) > Math.abs(dy) * 1.5) move(dx < 0 ? 1 : -1); touchStart.current = null }}>
      <StoryCard card={cards[index]} period={data.year ? String(data.year) : 'ALL TIME'}/>
    </div>
    <div className="story-controls story-bottom"><button onClick={() => move(-1)} disabled={index === 0}><ArrowLeft size={18}/> Previous</button><span>Swipe · arrow keys · space to pause</span>{index < cards.length - 1 ? <button onClick={() => move(1)}>Next <ArrowRight size={18}/></button> : <button onClick={close}>Done <CheckEnd/></button>}</div>
    <div className="player-recap" onFocus={() => setAutoplay(false)}><RecapDownload data={data}/></div>
  </dialog>
}
function CheckEnd() { return <Sparkles size={18}/> }
