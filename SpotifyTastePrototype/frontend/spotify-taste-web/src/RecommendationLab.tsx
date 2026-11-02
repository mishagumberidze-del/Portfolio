import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowUpRight, Compass, Disc3, LoaderCircle } from 'lucide-react'
import { api, demoMode, readOnlyMode, request, type Discover, type Recommendation } from './api'
import './lab.css'
import { sharedAccess } from './shared-access'

type Feedback = { candidateId: number; kind: string; rating?: number; evaluationItemId?: number }
type EvaluationEntry = { id: number; position: number; recommendation: Recommendation; rating: number | null; alreadyKnown: boolean }
type Metrics = { presented: number; rated: number; alreadyKnown: number; accepted: number; rejected: number; precisionAt10: number | null; averageRating: number | null; acceptanceRate: number | null; rejectionRate: number | null; bands: { band: string; rated: number; accepted: number; acceptanceRate: number | null }[] }
type Evaluation = { id: number; targetCount: number; items: EvaluationEntry[]; metrics: Metrics }
const label = (s: string) => s.replace(/([a-z])([A-Z])/g, '$1 $2')
const percentage = (n: number | null) => n === null ? '—' : `${(100 * n).toFixed(0)}%`
const submitFeedback = (f: Feedback) => request('/api/feedback', { method: 'POST', body: JSON.stringify(f) })

export function FeedbackControls({ item, evaluation }: { item: Recommendation; evaluation?: EvaluationEntry }) {
  const cache = useQueryClient()
  const [last, setLast] = useState<string | null>(null)
  const save = useMutation({ mutationFn: submitFeedback, onSuccess: async (_, input) => { setLast(input.kind === 'Rating' ? `${input.rating}/5` : label(input.kind)); await Promise.all([cache.invalidateQueries({ queryKey: ['discover-latest'] }), cache.invalidateQueries({ queryKey: ['evaluation'] })]) } })
  const send = (kind: string, rating?: number) => save.mutate({ candidateId: item.candidateId, kind, rating, evaluationItemId: evaluation?.id })
  return <div className="feedback-controls"><div className="feedback-buttons" role="group" aria-label={`Feedback for ${item.track}`}>
    {evaluation ? <>{[1, 2, 3, 4, 5].map(n => <button key={n} disabled={save.isPending} aria-label={`Rate ${item.track} ${n} out of 5`} aria-pressed={!evaluation.alreadyKnown && evaluation.rating === n} onClick={() => send('Rating', n)}>{n}<span> /5</span></button>)}<button disabled={save.isPending} aria-pressed={evaluation.alreadyKnown} onClick={() => send('AlreadyKnow')}>Already know it</button></> : [['Love', 'Love it'], ['Like', 'Like it'], ['NotForMe', 'Not for me'], ['AlreadyKnow', 'Already know it'], ['NotInterested', 'Not interested']].map(([kind, name]) => <button key={kind} disabled={save.isPending} aria-pressed={item.feedback === kind} onClick={() => send(kind)}>{name}</button>)}
    </div><div className="feedback-status" aria-live="polite">{save.isPending ? (demoMode ? 'Saving in this demo session…' : 'Saving locally…') : save.error ? <span role="alert">{save.error.message}</span> : last ? `${demoMode ? 'Demo response' : 'Saved locally'}: ${last}` : evaluation?.alreadyKnown ? 'Excluded from unheard evaluation' : item.feedback ? `Your feedback: ${label(item.feedback)}` : 'Your feedback stays separate from Spotify saved status.'}</div>
  </div>
}

export function RecommendationCard({ item, evaluation }: { item: Recommendation; evaluation?: EvaluationEntry }) { return <article className="discovery-card">
  <div className="discovery-art">{item.artworkUrl && <img src={item.artworkUrl} alt="" loading="lazy" referrerPolicy="no-referrer" onError={e => { e.currentTarget.style.display = 'none' }}/>}<Disc3 size={65} strokeWidth={.7} aria-hidden="true"/><span className="lab-rank">{String(evaluation?.position ?? item.rank).padStart(2, '0')}</span><span className="lab-band">{label(item.band)}</span></div>
  <div className="discovery-body"><div className="discovery-heading"><div><h3>{item.track}</h3><p>{item.artist}</p><small>{item.album ?? 'Album metadata unavailable'}</small></div><div className="compatibility"><strong>{item.predictedLikeScore.toFixed(1)}</strong><span>/10 compatibility</span></div></div>
    <p className="lab-explanation">{item.why}</p><div className="lab-track-meta"><span>{evaluation?.alreadyKnown ? 'Already known' : item.historicalStatus} · {item.confidence} confidence</span>{item.spotifyUrl && <a href={item.spotifyUrl} target="_blank" rel="noreferrer">Listen on Spotify <ArrowUpRight size={14}/></a>}</div>
    {!readOnlyMode && !sharedAccess && <FeedbackControls item={item} evaluation={evaluation}/>}
  </div></article> }

export default function DiscoveryLab() {
  const cache = useQueryClient(); const latest = useQuery({ queryKey: ['discover-latest'], queryFn: () => request<Discover>('/api/discover') })
  const generate = useMutation({ mutationFn: api.discover, onSuccess: d => cache.setQueryData(['discover-latest'], d) })
  const d = latest.data
return <section className="page-stack"><div className="lab-hero"><p className="eyebrow">A NEW CORNER OF YOUR SOUND</p><h2>Your next favorite<br/>is still out there.</h2><p>{demoMode ? 'Prepared candidates ranked with the same score fields and diversity bands as the local application.' : 'Existing candidates ranked by your stored personal model. Never Heard and Not Saved remain separate lists.'}</p><button className="primary-button" onClick={() => generate.mutate()} disabled={readOnlyMode || sharedAccess || generate.isPending}>{generate.isPending ? <LoaderCircle size={18} className="spin"/> : <Compass size={18}/>} {demoMode ? 'Replay prepared mix' : readOnlyMode ? 'Saved mix · no regeneration' : generate.isPending ? 'Finding and scoring candidates…' : 'Generate a fresh mix'}</button></div>
    {(latest.error || generate.error) && <div className="notice" role="alert">{(latest.error ?? generate.error)?.message}</div>}
    {latest.isPending && <p role="status">Loading your last saved recommendation run…</p>}
    {d && <><div className="notice"><p>{demoMode ? 'Not Saved uses the prepared saved-track snapshot.' : d.savedLibraryAvailable ? 'Not Saved uses your real Spotify saved-library snapshot.' : `${d.notSavedMessage} Not Saved remains disabled.`} {demoMode ? 'Shown recordings are not repeated in new mixes; different releases of the same recording count too.' : 'Existing results and scores are preserved, including their original explanations.'}</p></div>
      <h2>Top songs you’ve never heard</h2><p className="source-caption">“Never heard” means absent from imported Spotify history, not proof you have never heard it elsewhere. Relative compatibility is not a probability. The 5 / 3 / 2 band mix is a target, subject to eligible candidates.</p>
      {!d.neverHeard.length && <p className="empty-list">No eligible recommendations in the latest run. Generate a fresh mix; coverage can limit the pool.</p>}<div className="discovery-grid">{d.neverHeard.map(item => <RecommendationCard key={item.candidateId} item={item}/>)}</div>
      {d.savedLibraryAvailable && <><h2>Songs you may like but haven’t saved</h2><div className="discovery-grid">{d.notSaved.map(item => <RecommendationCard key={item.candidateId} item={item}/>)}</div></>}
    </>}
  </section>
}

export function EvaluationLab() {
  const cache = useQueryClient(); const [target, setTarget] = useState(50)
  const [batchMessage, setBatchMessage] = useState<string | null>(null)
  const query = useQuery({ queryKey: ['evaluation'], queryFn: () => request<Evaluation | null>('/api/evaluation') })
  const action = useMutation({ mutationFn: (path: string) => request<Evaluation>(path, { method: 'POST' }), onMutate: () => setBatchMessage(null), onSuccess: (d, path) => {
    const before = cache.getQueryData<Evaluation | null>(['evaluation'])
    if (path.endsWith('/next')) { const added = d.items.length - (before?.items.length ?? 0); setBatchMessage(added ? `Added ${added} eligible tracks. Provider coverage and repeat suppression can produce a smaller batch.` : 'No new eligible tracks were available in this provider pool. Your existing evaluation and ratings are unchanged. Try a fresh pool later; a second candidate provider may be needed.') }
    cache.setQueryData(['evaluation'], d); cache.invalidateQueries({ queryKey: ['discover-latest'] })
  } })
  const d = query.data; const metrics = d?.metrics; const unfinished = d?.items.some(i => !i.alreadyKnown && i.rating === null)
  const complete = !!d && d.metrics.rated >= d.targetCount
  if (readOnlyMode) return <section className="page-stack"><div className="panel"><h2>Your recorded evaluation</h2><p>Read-only: no ratings or example tracks are invented, and no new evaluation batch is generated.</p>{query.isPending ? <p role="status">Reading saved evaluation…</p> : query.error ? <p role="alert">{query.error.message}</p> : !d ? <p>No evaluation session has been recorded yet.</p> : <p>{d.metrics.rated} genuine ratings out of {d.targetCount} planned. Acceptance: {percentage(d.metrics.acceptanceRate)}. Precision@10: {percentage(d.metrics.precisionAt10)}.</p>}</div>{d && <div className="discovery-grid">{d.items.map(entry => <RecommendationCard key={entry.id} item={entry.recommendation} evaluation={entry}/>)}</div>}</section>
  return <section className="page-stack"><div className="lab-hero evaluation-hero"><p className="eyebrow">THE HUMAN PART OF THE MODEL</p><h2>Good predictions?<br/>You decide.</h2><p>{demoMode ? 'Rate the prepared cohort from 1–5. Demo responses last for this page session and never retrain a model.' : 'Listen first, then rate 1–5. We’ll collect 50–100 unheard recordings in batches of ten. Labels stay local; no automatic retraining.'}</p>{!d ? <div className="hero-actions"><label>Evaluation size <select value={target} onChange={e => setTarget(Number(e.target.value))}><option value={50}>50 tracks</option><option value={100}>100 tracks</option></select></label><button className="primary-button" disabled={query.isPending || query.isError || action.isPending} onClick={() => action.mutate(`/api/evaluation/start?target=${target}`)}>Start evaluation</button></div> : <><p>{metrics!.rated} / {d.targetCount} rated · {metrics!.alreadyKnown} known tracks excluded</p><button className="primary-button" disabled={action.isPending || unfinished || complete} onClick={() => action.mutate('/api/evaluation/next')}>{action.isPending ? 'Preparing next batch…' : complete ? 'Evaluation complete' : unfinished ? 'Rate this batch to continue' : 'Get next unheard batch'}</button></>}</div>
    {(query.error || action.error) && <div className="notice" role="alert">{(query.error ?? action.error)?.message}</div>}
    {batchMessage && <div className="notice" role="status"><p>{batchMessage}</p></div>}
    {metrics && <><div className="evaluation-metrics">{[['Precision@10', percentage(metrics.precisionAt10)], ['Average rating', metrics.averageRating === null ? '—' : `${metrics.averageRating.toFixed(2)} / 5`], ['Acceptance (4–5)', percentage(metrics.acceptanceRate)], ['Rejection (1–2)', percentage(metrics.rejectionRate)]].map(([name, value]) => <div key={name}><span>{name}</span><strong>{value}</strong></div>)}</div><p className="source-caption">Acceptance = ratings 4–5 / rated eligible tracks; rejection = ratings 1–2 / rated eligible tracks; 3 is neutral. Current denominator: {metrics.rated}. Precision@10 stays blank until the first ten eligible cohort positions are all rated (known tracks excluded). This evaluates the presented diversified mix, not an unbiased population sample.</p><div className="band-metrics">{metrics.bands.map(b => <div key={b.band}><strong>{label(b.band)}</strong><span>{percentage(b.acceptanceRate)} accepted · {b.accepted} / {b.rated} rated</span></div>)}</div><div className="notice"><p>1 = not for me · 2 = weak fit · 3 = neutral · 4 = like · 5 = love. “Already know it” excludes the track from unheard metrics and allows a replacement. Scores are visible, so this first evaluation is not blinded.</p></div>
      {complete && <div className="notice"><p>Your human-label dataset is ready to review. No scoring weights have been changed. Compare acceptance by band before deciding whether to recalibrate.</p></div>}
      <div className="discovery-grid">{[...d!.items].sort((a, b) => Number(a.rating !== null || a.alreadyKnown) - Number(b.rating !== null || b.alreadyKnown) || a.position - b.position).map(entry => <RecommendationCard key={entry.id} item={entry.recommendation} evaluation={entry}/>)}</div>
    </>}
  </section>
}
