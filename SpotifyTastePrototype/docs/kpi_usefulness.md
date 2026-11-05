# Why these measures are in the application

This is a personal listening application, so its primary measures answer listening-history questions rather than claiming business performance.

| Question | Useful information | Limit |
|---|---|---|
| How much and when did I listen? | Recorded listening time, event/qualified-play counts, active dates, calendar trends and explicit period coverage | Imported records describe the available history; active dates are not equivalent full days of listening |
| Which music defines a selected period? | Recording-aware song, artist and album rankings, new-to-history counts and rank movement | First in an export does not prove first heard in life; equivalent releases must not inflate recording counts |
| How repetitive or complete were those events? | Repeat-event share, short/explicit skips and completion with known-duration coverage | Aggregate behavior is not an explanation of intent; skipped playback is not proof of dislike |
| Which candidates resemble my observed preferences? | Available score components and a relative heuristic compatibility ranking in API-backed modes | Neither the score nor signal coverage is a calibrated probability or measured predictive accuracy |
| Is the recommendation model supported by evidence? | Held-out behavioral concordance and actual human evaluation counts, when available | Behavioral groups are not human labels; tied evidence or absent independent neighbors must not produce invented validation |

The synthetic edition uses one deterministic fictional event fixture. Overview, range filters, rankings, calendar totals, behavior and stories are calculated from the same events. It has no fitted audio-feature model, provider candidate pool or human ratings; model scores, validation and recommendations therefore remain unavailable. This preserves an honest analytics demonstration without fabricating success.

Final checks: 149 backend tests, 23 frontend tests, TypeScript compilation and the demo production build passed on 2026-11-03. Browser visual validation is separate.
