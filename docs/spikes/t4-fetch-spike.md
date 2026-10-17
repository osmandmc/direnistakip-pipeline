# T4 — Fetch spike findings

Question: can the four sources be read from Azure, is the article in the HTML without JavaScript, and what do article URLs look like?

## Part 1 — Local check (2026-10-18, from a home connection)

| Source | robots.txt for general crawlers | Article discovery | URL pattern | Notes |
|---|---|---|---|---|
| Evrensel | Allowed (admin/system paths excluded) | `sitemap-news.xml` (~290 recent links) | `https://www.evrensel.net/haber/{id}/{slug}` | **Content-Signal: `ai-train=no, search=yes, ai-input=no`**. Blocks AI training crawlers (GPTBot, CCBot, anthropic-ai …) |
| BirGün | Allowed; `?amp` and `*.html` disallowed | `sitemap/haberler` (~500 links) | `https://www.birgun.net/haber/{slug}-{id}` | — |
| Sendika.org | Allowed; search and `?page=` disallowed | Homepage links (`/feed` and `/sitemap.xml` return HTML, not XML) | `https://sendika.org/{yyyy}/{mm}/{slug}-{id}` | Historically used numbered domains (sendika62.org …) |
| Kızılbayrak | Allowed; paginated listings disallowed | `/rss` (RSS 2.0, ~50 items with dates) | `https://kizilbayrak85.net/ana-sayfa/{section}/{sub}/{slug}` | `www.kizilbayrak.net` **redirects to `kizilbayrak85.net`**. HTTPS to the old host failed the TLS handshake; HTTP works and redirects |

### Consequences already applied
- **Identity (D-024):** numbered mirror domains (`kizilbayrak\d*.net`, `sendika\d*.org`) canonicalise to the main host, so an article keeps one `ArticleId` when the site changes domain. Tests added.
- **Fetch (T7):** always fetch the **original** URL (or the latest redirect target), never the canonical one. The canonical URL is for identity only (`https://kizilbayrak.net/...` may not even resolve).

### Needs a decision — Evrensel's AI signal
Evrensel publishes `ai-input=no`: it asks that its content not be used as input to AI models. Collecting and parsing is not affected (`search=yes`, general crawling allowed). **Extraction (phase 3) sends article text to an LLM, which is AI input.** Options: ask Evrensel for permission (a labour research use may well be welcome); exclude Evrensel from LLM extraction and pre-fill only metadata; or decide the signal does not apply to non-public research use. To be settled under open decision #8 before phase 3.

## Part 2 — From Azure (pending)

Run `POST /api/spike/fetch?source={evrensel|birgun|sendika|kizilbayrak}&count=20` once per source and record the `summary` block here.

| Source | Statuses | Challenged | Article in HTML (median text chars, `<article>`, published time) | Notes |
|---|---|---|---|---|
| Evrensel | | | | |
| BirGün | | | | |
| Sendika.org | | | | |
| Kızılbayrak | | | | |

Raw HTML is saved under `raw-html/spike/{runId}/{source}/` for the Parse task (T8).
