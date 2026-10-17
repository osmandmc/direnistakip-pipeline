# Tasks

Implemented one at a time, each after approval. **You** = needs your action or access; otherwise Claude implements and you review.

Status: ☐ open · ◐ in progress · ☑ done

## Phase 1 — Foundations (finish)

| # | Task | Deliverable | Done when | Who |
|---|---|---|---|---|
| ☑ T1 | Commit foundations | Branch + PR with current code, docs, workflows | CI green on the PR | Claude, you merge |
| ☑ T2 | Azure bootstrap | `infra/bootstrap/` (Bicep): resource group, managed identity with OIDC federated credentials, role assignments; README setup steps; workflow guards | Template reviewed | Claude |
| ☑ T3 | Run bootstrap + first deploy | Infra and Function App deployed | `/api/health` returns ok; budget alert exists | **You** run, Claude fixes |

## Phase 2 — Walking skeleton (no AI)

| # | Task | Deliverable | Done when | Who |
|---|---|---|---|---|
| ☐ T4 | Fetch spike | Temporary function fetching ~20 articles per source from Azure; findings note | Blocking / JS rendering / URL patterns known per source; `SourceCatalog` and canonicalisation tests updated | Claude, you trigger |
| ☐ T5 | Storage layer | `Pipeline.Infrastructure`: ledger store, record store, raw HTML store, queue sender behind Core interfaces | Integration tests pass against local emulators | Claude |
| ☐ T6 | SubmitArticles | `POST /articles` (batch of URLs + optional title/date) → ledger + `article-submitted` | Resubmitting the same URL is a no-op | Claude |
| ☐ T7 | Fetch | Queue function: polite HTTP client, per-source rate limit, gzip HTML to blob | Spike articles fetched end to end in Azure | Claude |
| ☐ T8 | Parse | Per-source parsers (title, date, body), parser version on record | Parse tests pass on saved spike HTML for all 4 sources | Claude |
| ☐ T9 | Failures + rollups | RecordFailure on poison queues; daily Rollups timer | Failed article shows reason in ledger; rollup counts match ledger | Claude |
| ☐ T10 | Read API | `GET /articles`, `GET /metrics` with function keys | App can list articles and funnel counts | Claude |
| ☐ T11 | Contracts package | `contracts.yml`, package published | App can reference it (framework checked) | Claude, **you** confirm app framework |
| ☐ T12 | App: `News` backfill | Script / migration adding `ArticleId`, duplicate report | Duplicates resolved | **You** (app repo access), Claude drafts |
| ☐ T13 | App: CSV → pipeline + funnel screen | Upload sends to `POST /articles`; dashboard reads `/metrics` | A real CSV flows through to "Parsed" and shows in the funnel | **You** (app repo), Claude drafts |

## Phase 3 — Extraction and pre-fill (planned at the end of phase 2)

Inputs needed first: form field inventory (open #1), colleague's prototype review (D-018), LLM provider choice (open #7), privacy rules (open #9).

Outline: draft contract → Eval tool on the test set → Extract → Validate → draft/outcome API → app review screen, `ArticleReview`, case matching, outcome sender.

## Phases 4–5

Relevance stage; Discover per source and CSV retirement. Planned later.
