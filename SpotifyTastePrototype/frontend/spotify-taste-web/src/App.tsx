import { Fragment, lazy, Suspense, useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Activity, BarChart3, BookOpen, Compass, Database, Disc3, Headphones, History, Radio, Sparkles } from 'lucide-react'
import { api, demoMode, snapshotMode, readOnlyMode } from './api'
import './App.css'
import MusicStory from './MusicStory'
import { sharedAccess } from './shared-access'
import DiscoveryLab, { EvaluationLab } from './RecommendationLab'
const ExplorePages = lazy(() => import('./ExplorePages'))
const ProjectPage = lazy(() => import('./ProjectPage'))
const SnapshotLibrary = lazy(() => import('./SnapshotLibrary'))

type View = 'story' | 'evaluation' | 'overview' | 'history' | 'taste' | 'scorer' | 'discover' | 'diagnostics' | 'project' | 'library'

const views: { id: View; label: string; shortLabel: string; icon: typeof BarChart3 }[] = [
  { id: 'story', label: 'My Music Story', shortLabel: 'Story', icon: Disc3 },
  { id: 'overview', label: 'Overview', shortLabel: 'Explore', icon: BarChart3 },
  { id: 'history', label: 'History', shortLabel: 'History', icon: History },
  { id: 'taste', label: 'Taste', shortLabel: 'Taste', icon: Sparkles },
  { id: 'scorer', label: readOnlyMode ? 'Song Inspector' : 'Song Scorer', shortLabel: 'Song', icon: Radio },
  { id: 'library', label: 'Saved Songs', shortLabel: 'Saved', icon: Headphones },
  { id: 'discover', label: 'Discover', shortLabel: 'Discover', icon: Compass },
  { id: 'evaluation', label: 'Evaluation', shortLabel: 'Rate', icon: Headphones },
  { id: 'diagnostics', label: 'Diagnostics', shortLabel: 'System', icon: Activity },
  { id: 'project', label: 'Project', shortLabel: 'Project', icon: BookOpen },
]

const viewFromPath = (): View => {
  const id = window.location.pathname.replace(/^\/+|\/+$/g, '') || 'story'
  return views.some(view => view.id === id) ? id as View : 'story'
}

export default function App() {
  const [view, setViewState] = useState<View>(() => !demoMode && !sharedAccess && new URLSearchParams(window.location.search).has('spotify') ? 'diagnostics' : viewFromPath())
  const setView = (next: View) => { history.pushState({}, '', next === 'story' ? '/' : `/${next}`); setViewState(next); window.scrollTo({ top: 0, behavior: 'smooth' }) }
  useEffect(() => {
    const syncPath = () => setViewState(viewFromPath())
    window.addEventListener('popstate', syncPath)
    return () => window.removeEventListener('popstate', syncPath)
  }, [])
  const diagnostics = useQuery({
    queryKey: ['diagnostics'],
    queryFn: api.diagnostics,
    refetchInterval: false,
  })
  const connectionFailed = diagnostics.isError || diagnostics.isRefetchError
  const connectionLabel = connectionFailed
    ? snapshotMode ? 'Snapshot unavailable' : 'Local API unavailable'
    : diagnostics.data
      ? demoMode ? 'Static demo · no backend' : snapshotMode ? 'Real data · fixed snapshot' : `${diagnostics.data.databaseName} · read-only`
      : demoMode ? 'Loading demo snapshot' : snapshotMode ? 'Loading listening snapshot' : 'Checking local API'

  return <div className="app-shell">
    <a className="skip-link" href="#main-content">Skip to content</a>
    <aside className="sidebar">
      <div className="brand">
        <div className="brand-mark"><Disc3 size={22} aria-hidden="true" /></div>
        <div><strong>Spotify Taste</strong><span>{snapshotMode ? 'A listening-data portfolio' : 'Your private soundtrack'}</span></div>
      </div>
      <nav aria-label="Main navigation">
        {views.filter(item => (!sharedAccess || !['evaluation', 'diagnostics'].includes(item.id)) && (snapshotMode || item.id !== 'library') && (!demoMode || item.id !== 'diagnostics')).map(({ id, label, shortLabel, icon: Icon }) =>
          <Fragment key={id}>{id === 'overview' && <span className="nav-group">EXPLORE / ANALYTICS</span>}{id === 'scorer' && <span className="nav-group">RECOMMENDATION LAB</span>}{id === 'diagnostics' && <span className="nav-group">ADVANCED</span>}<button

            type="button"
            className={view === id ? 'active' : ''}
            aria-label={label}
            aria-current={view === id ? 'page' : undefined}
            onClick={() => setView(id)}
          >
            <Icon size={20} aria-hidden="true" />
            <span className="nav-label nav-label-full">{label}</span>
            <span className="nav-label nav-label-compact">{shortLabel}</span>
          </button></Fragment>)}
      </nav>
      <div className="privacy-note">
        <Database size={15} aria-hidden="true" />
        <div><strong>{demoMode ? 'Public demo' : snapshotMode ? 'Personal snapshot' : 'Real data · read-only'}</strong><span>{demoMode ? 'Prepared data · no account or backend.' : 'No imports, generated mixes or model changes. Your controls change only the view.'}</span></div>
      </div>
    </aside>
    <main id="main-content">
      <header className="topbar">
        <div><p className="eyebrow">{snapshotMode ? 'PERSONAL LISTENING · PUBLIC PORTFOLIO' : 'PRIVATE LISTENING LAB'}</p><h1>{views.find(item => item.id === view)?.label}</h1></div>
        <div className={`status-pill ${connectionFailed ? 'offline' : diagnostics.data ? 'online' : 'checking'}`} role="status" aria-live="polite">
          <span className="status-dot" aria-hidden="true" /> {connectionLabel}
        </div>
      </header>
      {demoMode && <div className="notice demo-notice"><p><strong>Portfolio demo.</strong> You’re exploring prepared synthetic data. Scoring, discovery and ratings are illustrative and stay in this browser session.</p></div>}
      {readOnlyMode && <div className="notice data-source-note"><p><strong>{snapshotMode ? 'Real listening history, shared by its owner.' : 'Your real Spotify data.'}</strong> {snapshotMode ? 'A fixed snapshot with interactive filters, charts and stories. This is the owner’s taste, not your Spotify account.' : 'Read-only SQL views.'} No sample songs or regenerated results.</p></div>}
      {sharedAccess && <div className="notice"><p>You’re exploring the owner’s taste, not your own Spotify account. Imports, ratings and model changes are local-only.</p><form method="post" action="/share/logout"><button className="secondary-button" type="submit">Lock shared view</button></form></div>}
      {view === 'story' && <MusicStory explore={() => setView('overview')} discover={() => setView('discover')} />}
      {view === 'evaluation' && <EvaluationLab />}
      {['overview', 'history', 'taste', 'scorer', 'diagnostics'].includes(view) && <Suspense fallback={<p role="status">Opening your listening lab…</p>}><ExplorePages view={view}/></Suspense>}
      {view === 'discover' && <DiscoveryLab />}
      {view === 'project' && <Suspense fallback={<p role="status">Opening project notes…</p>}><ProjectPage /></Suspense>}
      {view === 'library' && snapshotMode && <Suspense fallback={<p role="status">Opening your saved library…</p>}><SnapshotLibrary/></Suspense>}
    </main>
  </div>
}
