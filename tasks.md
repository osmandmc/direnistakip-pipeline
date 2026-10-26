# Tasks

Implemented one at a time, each after approval. **You** = needs your action or access; otherwise Claude implements and you review.

Status: ☐ open · ◐ in progress · ☑ done

## Phase 1 — Foundations (finish)

| # | Task | Deliverable | Done when | Who |
|---|---|---|---|---|
| ☑ T1 | Commit foundations | Branch + PR with current code, docs, workflows | CI green on the PR | Claude, you merge |
| ☑ T2 | Azure bootstrap | `infra/bootstrap/` (Bicep): resource group, managed identity with OIDC federated credentials, role assignments; README setup steps; workflow guards | Template reviewed | Claude |
| ☑ T3 | Run bootstrap + first deploy | Infra and Function App deployed | `/api/health` returns ok; budget alert exists | **You** run, Claude fixes |

## Phase 2a — Evrensel end to end (no AI)

One source all the way from link to parsed article, before adding the others. Entry is manual first.

| # | Task | Deliverable | Done when | Who |
|---|---|---|---|---|
| ☑ E1 | Close the fetch spike (was T4) | Findings note: Evrensel works from Azure; BirGün blocked by Cloudflare; Sendika and Kızılbayrak deferred | Note reviewed | Claude |
| ☑ E2 | Storage (was T5) | `Pipeline.Infrastructure`: ledger store, parsed-record store, raw HTML store (gzip), queue sender, behind Core interfaces | Tests pass against local emulators | Claude |
| ☑ E3 | SubmitArticles (was T6) | `POST /articles` (URLs + optional title/date) → ledger record + `article-submitted` message | Sending an Evrensel URL creates its ledger record; sending it again changes nothing | Claude, you try |
| ☑ E3b | SubmitArticles with content | Rows with `content` (Evrensel CSV: Url, Title, Content, Date) are saved as the parsed record (`provided:csv`, no raw HTML); ledger Submitted + Parsed; queued to `article-parsed`. Rows without content go to Fetch as before | A CSV-style row shows as Parsed with its text | Claude, you try |
| ☑ E4 | Fetch (was T7) | Queue function: downloads the page, saves raw HTML, updates the ledger; failures recorded with a reason. Restore the deploy smoke test (host from a GitHub variable) | Ledger shows `Fetched` and the file is in blob storage; a bad URL shows `Failed` + reason | Claude, you try |
| ☑ E5 | Parse Evrensel (was T8, one source) | Evrensel parser (title, date, body) with a version tag; tests on 5–6 trimmed samples | Parsed record in Cosmos for a submitted article | Claude, you try. **evrensel-2** (2026-10-21): second layout of explainer and service pages (“… ne zaman”, “… nasıl yapılır”; text in `[property=articleBody]`, whose HTML carries only the introduction) and text inside plain wrapper divs (a box pasted from Word); 4 of the first 100 collected articles had failed on these. FixtureTrimmer knows both |
| ☑ E6 | Read API (was T10, part) | `GET /articles` and `GET /articles/{id}` with stage status | One request shows the whole flow for an article | Claude, you try |
| ☑ E7 | Evrensel RSS timer (was phase 5) | **RSS is the main source** (no sitemap): timer `CollectFeeds` reads `rss/haber.xml` every 2 hours (at :05; the feed holds ~100 items ≈ 14 h, so a missed run loses nothing) and submits new links (origin Collector); duplicates are ignored. **Switched on 2026-10-21**; `POST /api/collect` (function key) runs it on demand and reports per feed (`feedCollectionEnabled = true` in `infra/main.bicepparam`; set it back to false and run the infra workflow to stop). A second way in already exists: researchers paste links in the app (app task R18) | Turned on only when you decide; a day without new Evrensel articles is visible in the metrics (B6) | Claude |

## Phase 2b — Rest of the walking skeleton

App integration without shared code: the JSON API is the contract, and the app keeps its own small request/response records. Existing `News` rows are not backfilled; a **cut-over month** separates old (local table) from new (pipeline inbox), so the same article never lives in both. The app reuses `News` (thin rows created on link/dismiss) and keeps its own source numbers with a mapping.

App-side tasks (B2, B3, the inbox and the redesigned screens) are tracked in the app repo's `tasks.md` (ResistanceSurvey).

| # | Task | Repo | Notes |
|---|---|---|---|
| ☑ B4 | Outcome endpoint | pipeline | `POST /api/articles/{id}/outcome` (linked / dismissed); `GET /articles` hides handled articles by default |
| ☑ B6 | Funnel metrics (was T9) | pipeline | `GET /metrics?days=14` (1–31): per source latest arrival and origin, received, failed, open inbox; per day received, read, failed, linked, dismissed (Turkey time, zero days included); the 20 latest failures. Counted on request from the ledger; rollups deferred (D-015 update). The app's dashboard is R10 in the app |
| ☑ B7 | Title search in the read API | pipeline | `GET /articles?q=`: titles compared in folded form (case, accents and Turkish letters ignored); a link finds its article in any state. For the app's article archive (app task R7) |
| ☑ B8 | Parse again | `POST /api/articles/reparse?source=evrensel` parses the stored pages of articles whose parsing failed (after a successful download) with the current parser; `&version=evrensel-1` instead re-reads articles read by that parser version. Articles researchers handled are left alone; no download (`IRawHtmlStore.FindLatestAsync`); at most 200 per call, `more` says whether to call again. Answers examined / parsed / still failed / no stored page |
| ☑ B9 | Relevance stage (AI) | Moved forward from phase 4 (D-007): too many unrelated feed articles. `Relevance` function on `article-parsed`: the start of the text (1,500 characters) with `docs/extraction/relevance-prompt.md` (`relevance-v1`) → relevant / not relevant / uncertain + reason; saved in the `relevance` container and in the ledger (`stages.Relevance`: `answer` = verdict, `reason`, `version` = prompt · exact model). Links pasted by researchers are skipped; articles researchers already handled are left alone; without a key nothing happens. Model: Gemini Flash-Lite (`gemini-flash-lite-latest`) on the free tier for testing (`GeminiModel` behind `ILanguageModel`; key = Key Vault secret `gemini-api-key`, model = `geminiModel` parameter); a rate limit sends the article back to the queue for later. `POST /api/articles/assess?source=evrensel` queues open articles without a verdict, spaced 9 s apart, at most 200 per call. Measured on the 71 evaluation articles (`tools/RelevanceEval`; article texts stay local): all 51 linked articles judged relevant, 15 of 20 dismissed judged not relevant (docs/extraction/findings.md). **You:** create the key in Google AI Studio; set it in user-secrets for the measurement and in Key Vault for Azure. App side: Gelen Kutusu ordering (app R21) |
| ☑ B10 | Article list by verdict | `GET /api/articles?relevance=notRelevant` lists what the Relevance stage judged not relevant; `relevance=other` everything else (relevant, uncertain, not assessed, assessment failed); `any` (default) as before. The verdict and reason already come with each article (`stages.Relevance.answer`, `.reason`). For the app's Gelen Kutusu (app R21): the AI's rejections apart, never hidden |
| ◐ B12 | Extract stage: AI drafts | Phase 3, first version. Articles judged relevant or uncertain go to `article-relevant`; the `Extract` function runs `docs/extraction/prompt.md` (v2, the prompt researchers can copy in the app) with the list values in `docs/extraction/lists.json` (**a snapshot of the app's lists**, until the app sends them when they change) and the whole text (up to 15,000 characters). The model's JSON is kept in the `drafts` container; the ledger gets `stages.Extracted`: answer `Draft` (a labour action, with a one-line summary “company · protest type · city / district · date” as reason) or `NoEvent`. Same rules as Relevance: handled articles left alone, no key → nothing, rate limit / busy / slow → back to the queue. Own model setting (`geminiExtractionModel`), Flash-Lite by default like Relevance: on the free tier the full Flash allows only 20 requests a day (it ran out after one draft in a test, 2026-10-24), Flash-Lite 500, enough for both stages; the paid tier would allow the full Flash. `GET /api/articles/{id}/draft`, `POST /api/articles/draft?source=` (queues open relevant/uncertain articles without a draft, 10 s apart, at most 100), `GET /api/articles?draft=ready|none`. The app checks a draft as it checks a pasted answer (app R11) |
| ☑ B13 | Prompt versions | `GET /api/prompts`: the relevance and extraction prompts the AI stages run, their versions and models, and how many open articles were judged by an older relevance prompt. `POST /api/articles/assess?source=…&again=true` queues those to be judged again with the current prompt (only on request; the message says so, so the stage does not skip them). Metrics per source: `openWithOlderVerdict`. Ledger queries: `RelevanceOlderThan`. For the app's “AI istemleri” page (R25) |
| ☑ B11 | The AI in the metrics | `GET /api/metrics` per source: the open inbox split into with a draft, judged not relevant and not assessed yet (assessment failed counts as not assessed; pasted links do not); per day: verdicts given (relevant, uncertain, not relevant) and drafts made; `ai`: whether a key is configured, and for articles researchers decided in the period what the AI had said against what they did (not relevant but linked = the AI rejected a case). CORS: the portal may call the functions (its “Test/Run”; the function key is still needed). For the app's Durum Paneli (R22) |
| ☑ B14 | Each day's arrivals by state | `GET /api/metrics`: every day row gets `now`, the articles received that day counted by where they stand now (linked, dismissed, with a draft, to review, rejected by the AI, failed to download or read, not read yet); the counts add up to the day's received. For the app's Durum Paneli (R26) |
| ☐ S1 | Other sources | pipeline | Sendika: run the spike from Azure, then a parser (Kızılbayrak: S2). BirGün: blocked until access is granted (**you / the group** contact BirGün) |
| ☑ S2 | Kızılbayrak from its RSS feed | pipeline | Feed `http://www.kizilbayrak.net/rss` (redirects to the current mirror, e.g. `kizilbayrak85.net`; HTTPS to the old host fails TLS). Its `<description>` holds the whole text: checked on 8 items (news, long reports, columns), every page paragraph is in the feed. So articles enter **already parsed from the feed** (`provided:collector`, paragraphs as the page parsers produce them): no page download, no page parser. Source enabled with `FeedUrl` and `FeedHasFullText`; collected by the same `CollectFeeds` timer, so it starts when feed collection is switched on (E7). Pasted Kızılbayrak links not in the feed go to Fetch and fail at Parse ("No parser"). Fetching the feed from Azure is first seen in the run log. **When switching on:** the app still imports Kızılbayrak Excel files locally (`Pipeline:Sources` lists Evrensel only); add Kızılbayrak there with a cut-over month so an article does not arrive both ways. Covers news and columns beyond labour (relevance later) |

Dropped: the contracts package (was T11) and the `News` backfill / identity endpoint (was T12, B1), both made unnecessary by the cut-over.

## Phase 3 — Extraction and pre-fill (planned at the end of phase 2)

Inputs needed first (can be gathered during phase 2): form field inventory (open #1), colleague's prototype review (D-018), LLM provider choice (open #7), privacy rules (open #9), **Evrensel's `ai-input=no` signal** (open #8).

Outline: draft contract → Eval tool on the test set → Extract → Validate → draft/outcome API → app review screen, `ArticleReview`, case matching, outcome sender.

## Phases 4–5

Relevance stage; Discover per source and CSV retirement. Planned later.
