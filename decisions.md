# direnistakip: AI-assisted case entry — Decision log

Started 2026-10-17. One entry per decision. Don't change an accepted entry's substance; add a new entry that supersedes it.

**Context.** direnistakip is the case entry app for a labour research group. A former process scraped news sites, judged relevance and exported a CSV for upload; it no longer runs, and researchers enter cases by hand. Goal: a pipeline that collects articles and pre-fills the case form for human review. Sources: Evrensel, Sendika.org, Kızılbayrak, BirGün. Built and maintained by one developer.

## Index

Diagrams, roadmap and cost options: [architecture.md](architecture.md).

**Boundaries and ownership**

| ID | Decision | Status |
|---|---|---|
| D-001 | Pipeline is a standalone service; the app is a consumer | Accepted |
| D-009 | Review happens in the existing app, reading from the pipeline API | Accepted |
| D-010 | The app's database decides what is handled | Accepted |
| D-011 | Outcome notification to the pipeline is simple and repeatable | Accepted |
| D-016 | No on-demand LLM calls from the app | Accepted |

**Article identity and data**

| ID | Decision | Status |
|---|---|---|
| D-002 | Article identity = hash of canonical URL; entry points are adapters | Accepted |
| D-003 | Stage outputs stored separately, immutable, versioned | Accepted |
| D-020 | Cosmos partition key `/id` | Proposed |
| D-024 | URL canonicalisation rules with a test table | Proposed |

**AI: relevance, extraction, validation**

| ID | Decision | Status |
|---|---|---|
| D-004 | No fine-tuning; data is for evaluation and prompt work | Accepted |
| D-005 | Extraction schema comes from the form; evidence-backed fields | Accepted |
| D-006 | Draft has resistance and protest parts; company resolved by the app | Accepted |
| D-007 | Relevance is a separate stage before extraction | Accepted |
| D-008 | Validation: deterministic checks first, no raw confidence numbers | Accepted |
| D-012 | Duplicate events handled in the app, not by pipeline grouping | Accepted |
| D-018 | Colleague's Python prototype: reuse assets, decide on code later | Accepted |

**Platform and hosting**

| ID | Decision | Status |
|---|---|---|
| D-013 | Hosting split: app on SmarterASP, pipeline in Azure | Accepted |
| D-014 | Azure Functions with queues; Cosmos DB free tier | Provisional |
| D-019 | Runtime: .NET isolated Azure Functions | Proposed |
| D-021 | Azure Storage Queues | Proposed |
| D-025 | API auth: API key to start | Proposed |
| D-026 | Region: Sweden Central | Proposed |

**Delivery and operations**

| ID | Decision | Status |
|---|---|---|
| D-015 | Monitoring from a ledger with rollups | Accepted |
| D-017 | Build order | Accepted |
| D-022 | Infrastructure as code with Bicep | Proposed |
| D-023 | Contracts v0 as JSON Schema | Proposed |
| D-027 | Solution structure: libraries vs deployable units | Proposed |
| D-028 | CI/CD: GitHub Actions with OIDC | Proposed |

---

## D-001 — Pipeline is a standalone service; the app is a consumer
**Decision.** One owner per fact. Pipeline: articles (raw, parsed, relevance, extraction, status) and drafts. App: cases, resistances, protests, article-to-case links, reviews. Neither writes what the other owns.
**Consequences.** No sync conflicts; manual entry works if the pipeline is down.
**Rejected.** Shared article table; article content copied into the app.

## D-002 — Article identity = hash of canonical URL; entry points are adapters
**Decision.** `ArticleId` = hash of the canonicalised URL (one shared function). Every entry point (CSV, collector, manual) emits the same `ArticleSubmitted` message. The app's `News` table is a thin projection keyed by `ArticleId` (URL, title, publisher, date; no text).
**Consequences.** Retiring the upload = removing an adapter. Existing `News` rows must be backfilled and de-duplicated first.

## D-003 — Stage outputs stored separately, immutable, versioned
**Decision.** Raw HTML (compressed, Blob), parsed content, relevance and draft are separate records, each tagged with parser/prompt/model/schema version. Never overwritten; re-runs create new records.
**Consequences.** Reprocess without refetching; evidence offsets stay valid against their text version.

## D-004 — No fine-tuning; data is for evaluation and prompt work
**Decision.** Historical and review data used only for evaluation, prompt tuning and few-shot examples. Negatives only for evaluating relevance. Test articles kept separate from tuning examples.
**Consequences.** Behaviour lives in prompts and schema; models are swappable.

## D-005 — Extraction schema comes from the form; evidence-backed fields
**Decision.** Generate the schema from the form's definitions (enums, canonical lists, required fields). Each field carries a supporting quote, verified in code against the article text. Blank rather than guess.
**Consequences.** Unverified quotes are the strongest review signal.

## D-006 — Draft has resistance and protest parts; company resolved by the app
**Decision.** Draft = `resistance` (used only when creating one) + `protest` (always). `dateFrom`/`dateTo` resolved against publication date; unresolvable dates flagged. Company returned as written; the app resolves it and each confirmed match adds an alias.
**Consequences.** Matching improves without model changes. Participant-count conventions must be defined in the schema.

## D-007 — Relevance is a separate stage before extraction
**Decision.** Relevance (relevant / not relevant / uncertain + reason) sits between parsing and extraction, stored separately. Extraction may return zero events. Until the collector exists, relevance can stay a human step.
**Consequences.** Measured and tuned independently; favour recall.

## D-008 — Validation: deterministic checks first, no raw confidence numbers
**Decision.** (1) Deterministic checks (schema, enums, quote found, date plausibility, canonical lists); (2) optional LLM judge that only raises flags; (3) numeric confidence only once calibrated. Show reasons, not percentages.
**Consequences.** Flags order the review queue. Nothing skips human review.

## D-009 — Review happens in the existing app, reading from the pipeline API
**Decision.** A new app screen lists articles from the pipeline API (server-side, paged) and opens drafts with flags. The researcher validates and saves into the app's database. Admin app deferred.
**Consequences.** The pipeline API is the stable contract and must not be openly accessible (see D-025).

## D-010 — The app's database decides what is handled
**Decision.** `ArticleReview` in the app, unique on `ArticleId` (or `ArticleId` + event index if multi-event), inserted in the same transaction as the case, `News` row and link. `DraftId` stored as lineage only. Dismissed = handled.
**Consequences.** A second researcher sees "already handled".

## D-011 — Outcome notification to the pipeline is simple and repeatable
**Decision.** After saving, the app calls `POST /articles/{id}/outcome` (`digested` + resistance ID, or `dismissed` + reason). Idempotent. Optional `OutcomeSentAt` + retry job. Failure delays metrics, never the workflow.

## D-012 — Duplicate events handled in the app, not by pipeline grouping
**Decision.** On open and on save, the app checks company and recent resistances: (1) none → new resistance + protest; (2) exists, no protest that date → add protest, maybe extend end date; (3) protest exists → link article only. Researcher always confirms.
**Consequences.** Rare concurrent duplicates caught by a "possible duplicates" list.
**Rejected.** Pipeline-side clustering for now.

## D-013 — Hosting split: app on SmarterASP, pipeline in Azure
**Decision.** App and MSSQL stay on SmarterASP (Basic .NET). Collection, parsing, extraction and evaluation run in Azure. Core logic in a plain class library so the host can be swapped.
**Consequences.** Check SmarterASP renewal price. Verify early that sources don't block Azure IPs.

## D-014 — Azure Functions with queues; Cosmos DB free tier (provisional)
**Decision.** Timer-triggered discovery; queue-triggered stages with limited concurrency and a poison queue. Cosmos DB free tier (provisioned). Raw HTML in Blob. Persistence behind an interface with regular export.
**Consequences.** Budget alert. One free-tier account per subscription. Partition key superseded by D-020.
**Rejected.** Hangfire (needs always-on host); Data Factory (bulk movement).

## D-015 — Monitoring from a ledger with rollups
**Decision.** One ledger record per article with timestamp and outcome per stage plus failure reasons. Daily rollups by source, origin and stage; the app reads rollups only. App Insights alerts: source silence, queue depth, poison queue, LLM errors and spend. No full article text in logs.

## D-016 — No on-demand LLM calls from the app
**Decision.** Extraction runs ahead of time in the pipeline; the app only reads drafts. File contract kept as transition fallback.
**Consequences.** The list shows only articles with a draft ready.

## D-017 — Build order
**Decision.** (1) Foundations: repo, IaC, budget alerts, contracts. (2) Walking skeleton: CSV adapter → fetch → parse → ledger and funnel, no AI. (3) Extraction and pre-fill. (4) Relevance stage. (5) Collector per source; retire the upload.
**Consequences.** Each phase leaves a working system.

## D-018 — Colleague's Python prototype: reuse assets, decide on code later
**Decision.** Reuse his prompt, field definitions, failure findings and Excel comparison (test set). Port or wrap decided after code review. One source of truth for schema and prompt in this repo. He owns prompt quality; this project owns the platform.

## D-019 — Runtime: .NET isolated Azure Functions (proposed)
**Decision.** Pipeline in .NET isolated-worker Functions, matching the app and the single maintainer's stack. Prompts and schema kept as JSON/YAML so prototype work carries over (D-018).
**Note.** Targets `net9.0` (set once in `Directory.Build.props`); move to `net10.0` (LTS) before .NET 9 support ends.
**Rejected.** Python Functions.

## D-020 — Cosmos partition key `/id` (proposed)
**Decision.** Ledger partitioned on `/id` (= `ArticleId`); versioned stage records (several per article, D-003) on `/articleId`. Supersedes `/sourceId` in D-014.
**Context.** Outcome calls and draft reads have only the `ArticleId`; `/sourceId` would force cross-partition queries. Volume is too low for hot partitions to matter.

## D-021 — Azure Storage Queues (proposed)
**Decision.** One queue per stage, built-in poison queues.
**Rejected for now.** Service Bus (unneeded features and cost).

## D-022 — Infrastructure as code with Bicep (proposed)
**Decision.** Bicep in `/infra`, deployed from CI.
**Rejected.** Terraform (Azure-only project).

## D-023 — Contracts v0 as JSON Schema (proposed)
**Decision.** `ArticleSubmitted`, ledger record, draft envelope (versions + evidence spans) and API surface defined as JSON Schema in `/core` before code.

## D-024 — URL canonicalisation rules with a test table (proposed)
**Decision.** Lowercase host, strip tracking params and fragments, normalise per-source AMP/mobile variants. Rules covered by a test table; changes require a new entry (identity depends on them).

## D-025 — API auth: API key to start (proposed)
**Decision.** API key in a header, stored in Key Vault. The API is internet-reachable (shared hosting cannot join a VNet) but authenticated. Add IP restriction if the app's outbound IP proves stable; Entra app token later if needed.

## D-026 — Region: Sweden Central (proposed)
**Decision.** All pipeline resources in Sweden Central (EU).
**Context.** West Europe rejected the new pay-as-you-go subscription ("region not accepting new customers"). Any EU region with Flex Consumption that accepts the subscription is acceptable; North Europe is the fallback. Set in `infra/main.bicepparam` and the bootstrap default.

## D-027 — Solution structure: libraries vs deployable units (proposed)
**Decision.** Few deployable units; logic in libraries so hosts stay swappable (D-013).

| Project | Kind | Deployed as |
|---|---|---|
| `Pipeline.Core` | Library: domain, canonicalisation, stage logic, validation, interfaces. No Azure references | — |
| `Pipeline.Contracts` | Library: messages, ledger, draft, API DTOs; JSON Schema generated from it (D-023) | NuGet package for the app |
| `Pipeline.Infrastructure` | Library: Cosmos, Blob, Queue, LLM client, fetchers | — |
| `Pipeline.Functions` | Host: queue/timer triggers + HTTP API | Function App |
| `Pipeline.Eval` | Console: runs test sets against prompts/models, writes reports | Not deployed; run locally or on demand in CI |
| `*.Tests` | Unit (Core) and integration (Infrastructure, Azurite/emulator) | — |
| `/infra` | Bicep | Azure resources |
| `/prompts`, `/schema` | Versioned assets (D-018) | Shipped with the Function App |

App-side changes (review screen, `ArticleReview`, outcome call) live in the app's repo and deploy with the app.
**Start** with one Function App. **Split** the API into its own Function App if isolation is needed (different scaling, the API not holding the LLM key); Core makes this cheap.
**Check.** The app's target framework (if .NET Framework, `Contracts` must target `netstandard2.0`).

## D-028 — CI/CD: GitHub Actions with OIDC (proposed)
**Decision.** GitHub Actions (repo is on GitHub). Azure login via OIDC: a user-assigned managed identity with federated credentials, created once by `infra/bootstrap` (Bicep, subscription scope); no stored secrets, no Entra app registration. It holds Contributor on the resource group and an RBAC Administrator role constrained to the roles `main.bicep` assigns.
- `ci.yml` — on PR: build, test, `bicep build` + `what-if`.
- `infra.yml` — on main, path `/infra/**`: deploy Bicep (environment approval).
- `functions.yml` — on main, path `src/**`, `prompts/**`, `schema/**`: publish and deploy the Function App.
- `contracts.yml` — on tag: publish `Pipeline.Contracts` to GitHub Packages.
- `eval.yml` — manual: run `Pipeline.Eval`, upload report as artifact.

Environments: `prod` only to start; add `dev` when changes need a safe place to land.
**Rejected.** Azure DevOps (second platform to maintain).

---

## Open decisions (deferred)

1. **Form fields:** free text vs enums/canonical lists. Blocks the draft schema.
2. **Old relevance logic:** how it was decided; whether researchers keep assembling the list.
3. **News links:** resistance only, or individual protests too.
4. **New-resistance rule:** when a protest starts a new resistance.
5. **Multi-event articles:** one article → several cases? Affects `ArticleReview` key and draft shape.
6. **Metrics store:** Cosmos rollups or relational.
7. **LLM provider, model and monthly cap** (see cost options in architecture.md).
8. **Source terms:** robots.txt, terms of use, attribution. Before the first scheduled fetch.
9. **Privacy:** fields never stored (e.g. rank-and-file names). Before the first scheduled fetch; immutable records make deletion costly.
10. **Operations:** runbook and IaC in the repo.
