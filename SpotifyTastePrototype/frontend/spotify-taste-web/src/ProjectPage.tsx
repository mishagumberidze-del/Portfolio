import { ArrowRight, Database, Layers3, ShieldCheck, Sparkles } from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { snapshotMode } from './api'
import { loadSnapshot } from './snapshot'
import { formatCount } from './number-format'

const tables = [
  ['ListeningEvent', 'Immutable play facts: UTC time, duration played, skip and playback signals.'],
  ['Track / Artist / Album', 'Normalized music identity while preserving exact provider track records.'],
  ['UserSavedTrack', 'A real Liked Songs snapshot; never inferred from play count.'],
  ['TrackExternalId / TrackFeature', 'Cached provider mappings and audio features used by analysis.'],
  ['TrackPreference', 'Versioned positive, negative, recency, repeat, completion and saved evidence.'],
  ['RecommendationRun / Candidate', 'The ranked pool, score components, band and explanation for each run.'],
  ['RecommendationFeedback / Evaluation', 'Explicit local labels and resumable human evaluation results.'],
  ['ImportBatch / ImportError', 'Auditable source counts, duplicates, rejected rows and import status.'],
]

export default function ProjectPage() {
  const snapshot = useQuery({ queryKey: ['personal-snapshot'], queryFn: loadSnapshot, enabled: snapshotMode, staleTime: Infinity })
  const provenance = snapshot.data?.manifest
  return <section className="project-page page-stack">
    <div className="project-hero"><p className="eyebrow">PROJECT NOTES</p><h2>Your listening history,<br/>made explainable.</h2><p>Spotify Taste brings your imported listening history, real saved songs and cached audio features together. The read-only edition preserves stored data while filters, comparisons and stories remain interactive.</p></div>

    <div className="project-grid">
      {provenance && <article className="project-card"><Database/><h3>Real data, with permission</h3><p>The owner approved this public snapshot of {formatCount(provenance.events)} listening events, {formatCount(provenance.savedTracks)} saved songs and {formatCount(provenance.featureRows)} cached audio-feature records. Listening coverage: {provenance.sourceFirstUtc.slice(0, 10)} to {provenance.sourceLastUtc.slice(0, 10)} UTC. Snapshot captured {provenance.capturedAtUtc.slice(0, 10)} UTC.</p><p>Credentials and raw Spotify exports are excluded. Published summaries can be downloaded by visitors. Period filters use the snapshot date, so the numbers do not drift as time passes.</p></article>}
      <article className="project-card"><Sparkles/><h3>How to explore</h3><p>Start with My Music Story, then filter Overview by period. History shows change over time; Taste compares recent and lifetime favorites. Discover displays existing recommendations, not a newly generated mix.</p></article>
      <article className="project-card"><ShieldCheck/><h3>What the data means</h3><p>Listening events come from your Spotify export. Saved songs come from the actual Spotify library snapshot, never inferred from play counts. Audio features come from the local ReccoBeats cache. Missing information stays unknown. No synthetic songs or scaled sample statistics are substituted.</p></article>
    </div>

    <article className="project-section"><h3>Main calculations</h3><div className="project-columns"><p><strong>Analytics.</strong> Listening time, qualified plays, skips, repeats, estimated completion, first-seen discoveries, top recordings and artist-rank movement are calculated from event facts. Audio profiles are listening-time weighted and show coverage gaps.</p><p><strong>Recording identity.</strong> Equivalent album, deluxe, single and compilation entries can be grouped through guarded ID, ISRC, exact title/artist and duration checks. Live, remix, remaster, acoustic and similar versions stay separate.</p></div></article>

    <article className="project-section"><h3>Preference scoring</h3><p>The 0–10 result is relative compatibility, not a probability. For unheard music, available signals are reweighted across audio-feature neighbors (30%), recent 90-day taste (22%), lifetime taste (18%), artist affinity (12%) and real saved-track evidence (8%). Missing signals are omitted. A bounded exploration adjustment can widen discovery. For known recordings, time-decayed direct behavior is blended gradually, so sustained repeats/completion or skips matter more than one event.</p></article>

    <article className="project-section"><h3>Discovery and evaluation</h3><p>A provider supplies a candidate pool, but the local model ranks it. Previously heard or saved recording equivalents are excluded for the relevant list, duplicate recordings are removed, and only one track per artist is retained. The Top 10 targets five high-confidence picks, three discovery picks and two wildcards. Feedback remains separate from Spotify saved state; 1–5 evaluation ratings report acceptance, rejection and Precision@10 without automatic retraining.</p></article>

    <article className="project-section"><div className="section-heading"><div><Database/><h3>SQL at a glance</h3></div><span>SQL Server · EF Core</span></div><div className="table-map">{tables.map(([name, purpose]) => <div key={name}><strong>{name}</strong><span>{purpose}</span></div>)}</div></article>

    <article className="project-section"><h3>Import, cleaning and reconciliation</h3><p>The importer reads a folder or ZIP without changing it, keeps music rows with Spotify track URIs, ignores podcast/video data, validates required values and deduplicates exact source events with a SHA-256 fact hash. Names are normalized for lookup without fuzzy artist merging. IP and country are never stored. Batch totals and errors are recorded, and source-versus-SQL counts can be reconciled after an idempotent re-import.</p></article>

    <article className="project-section"><div className="section-heading"><div><Layers3/><h3>Architecture</h3></div></div><div className="flow" aria-label="Application data flow"><span>Spotify export</span><ArrowRight/><span>Validated import</span><ArrowRight/><span>SQL Server + feature cache</span><ArrowRight/><span>.NET analytics and scoring API</span><ArrowRight/><span>React dashboard</span></div></article>

    <div className="project-grid"><article className="project-card"><h3>Tech stack</h3><p>React, TypeScript, Vite, TanStack Query and Recharts present the data. ASP.NET Core, EF Core and SQL Server maintain the original normalized database. A personal snapshot can preserve read-only responses for use without the API.</p></article><article className="project-card"><h3>Fixed data, interactive presentation</h3><p>Animation changes positions and presentation, not play counts or scores. The read-only edition disables imports, OAuth, feedback writes, feature fetching, new scoring and model or recommendation regeneration. The full maintenance tools remain in the source for explicitly requested future work.</p></article></div>
  </section>
}
