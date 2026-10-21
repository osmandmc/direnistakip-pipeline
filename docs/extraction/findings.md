# AI extraction — first evaluation (2026-10-21)

Question: can an LLM read a news article and pre-fill the case and protest form the way the researchers do, and which
fields can it be trusted with?

The prompt that came out of this evaluation is [prompt.md](prompt.md) (v2). Placeholders: `{{LISTS}}` (the list values
from the production database), `{{SOURCE}}`, `{{DATE}}`, `{{TITLE}}`, `{{TEXT}}`.

## Method

- **Evaluation set from production (read-only).** Articles linked to exactly one case, spread over months, plus articles
  the researchers dismissed. Expected values are what the researchers recorded: the case fields and the protest closest to
  the article's date.
  - Dev set (36 articles): used to write prompt v1; its answers were seen.
  - Blind test set (35 articles): 20 linked Evrensel 2023–2024, 6 linked Kızılbayrak 2020–2021, 10 dismissed Evrensel
    2024 (one linked article failed to download). Answers were kept in a separate folder and not opened until all
    outputs were written.
  - Evrensel text for 2023–2024 is not stored, so it was fetched and parsed with the evrensel-2 parser.
- **Extraction** with prompt v1 by Claude (Opus) in a Claude Code session, one JSON answer per article; list values were
  checked against the production lists before scoring.
- **Scoring** field by field: accuracy for single values, precision and recall for lists. Names are compared after
  Turkish-insensitive folding, dropping legal words (A.Ş., Ltd …) and allowing containment.
- Article texts are not in this repository (Evrensel's content stays out of a public repo); neither are the evaluation
  scripts yet.

## Results (blind test set, prompt v1)

Adjusted figures leave out three articles that look linked to the wrong case (see *Data quality*), allow spelling
variants of company names, and allow two days on dates.

| Field | Raw | Adjusted | Verdict |
|---|---|---|---|
| Relevant or not | 33/35 (94%) | | Pre-fill |
| Target type (company / organization) | 25/25 | | Pre-fill |
| Taşeron present | 24/25 | | Pre-fill |
| Company | 11/25 | 15/22 | Pre-fill after the v2 rules |
| Protest start date | 16/25 | 19/22 within 2 days | Pre-fill |
| Category | 21/25 (84%) | | Pre-fill after the v2 rule |
| Protest types | precision 72%, recall 81% | | Pre-fill |
| Employment types | precision 84%, recall 78% | | Pre-fill |
| Organizations | precision 67%, recall 60% | higher (abbreviations like "BMİS") | Pre-fill after the v2 rules |
| Reasons | precision 51%, recall 58% | | Pre-fill after the v2 rules |
| Places | precision 71%, recall 60% | | Pre-fill after the v2 rule |
| Cities | precision 85%, recall 46% | | Recall is low because one case has 25 cities |
| Union authority | 12/20 (60%) | | Do not pre-fill (needs a definition) |
| Develops right | 18/25 (72%) | | Do not pre-fill (needs a definition) |
| Result | 9/25 (36%) | | Do not pre-fill |
| Workplace size | 3/20 (15%) | | Do not pre-fill |
| Gender | 1/25 (4%) | | Do not pre-fill |
| Participant range | 0/25 | | Do not pre-fill (participant count is kept when the text gives it) |

## Failure analysis

### Not in the article
Result, gender, participant range and workplace size are filled by the researchers from estimates, photos, later news
or general knowledge. The result is the case's final outcome and is often "Sıfır Kazanım" when the article says nothing
about it. The model correctly left these empty; prompt v2 no longer asks for them.

### Relevance
Both mistakes were articles the researchers dismissed although they are labour news: arrests after May Day, and a
follow-up on the Borusan dismissals. They were dismissed because they belong to an existing case or were left out on
purpose, which the prompt cannot know. **The app needs a "probably belongs to case X" suggestion**; relevance alone is
not enough.

### Team conventions the prompt did not describe (fixed in v2)
- **Category:** a protest at one workplace or institution is İşletme/Kurum even when a union branch runs it (for
  example a KESK branch at its own hospital). Genel is for multi-workplace, city- or country-wide actions.
- **Company:** the workplace's short name as written in the article ("Yedikule Göğüs Hastalıkları Hastanesi"); event
  labels ("Örgüt (yer): Konu") only for Genel cases.
- **Reasons:** one or two at most. A strike or TİS is just "TİS" (not also "Düşük ücret"). Sürgün is
  "KHK ile isten atma, açığa alma, sürgün", not "Tayin".
- **Organizations:** the union with its confederation, without adding the confederation again on its own. Supporting
  organizations are not listed; an unorganized worker supported by a union is "Kurumsuz".
- **Place:** a protest in front of the protesters' own institution (an il müdürlüğü, SGK) is "İşyeri önü".
- **Location:** the researchers always record a district, "Merkez" when the article names none.

### Dates
Most misses were one day off (the article published the day after the action), or the case had many protests
(up to 11) and the closest one was a different action. v2 asks for "bugün/dün" to be resolved against the article date.

### Inconsistent answers (needs a decision)
- **Develops right:** a TİS case is marked false, while a miner fired for joining a union is marked true.
- **Union authority:** SES is "Memur sendikasi" in some cases and "Sendika yetkisi yok, üyeligi var" in others.

Until the team writes down how these two fields are decided, AI should not pre-fill them.

## Data quality

Three of the 25 linked test articles seem linked to the wrong case (to be checked in the app):

| News id | Article | Linked case |
|---|---|---|
| Evrensel 29279 | Özak Tekstil, Urfa OSB | Deniz-Sen strike, İstanbul Beşiktaş |
| Evrensel 30581 | Pilenpak Ambalaj, Manisa OSB | A TÜRK-İŞ TİS strike |
| Evrensel 30582 | BES İzmir, SGK İl Müdürlüğü | A protest in Diyarbakır |

## Evrensel category as a prefilter

In the dev set, 15 of 20 linked Evrensel articles are İŞÇİ-SENDİKA and 5 are EGE SAYFALARI; 8 of 10 dismissed articles
are also İŞÇİ-SENDİKA. Filtering on the category removes little noise and drops linked articles from other sections.
**Use it as a ranking signal, not a filter.**

## Recommendations

1. **Pre-fill:** relevance, category, company, taşeron, organizations, reasons, employment types, protest types,
   places, locations and dates. Show them as "AI önerisi" until a researcher confirms them.
2. **Leave to the researcher:** result, gender, sizes, develops right and union authority.
3. **"AI ile doldur" (copy-paste option):** the app shows prompt v2 with the article and lists filled in and a copy
   button; the user runs it in their own AI chat and pastes the JSON reply into a text box. The app validates the JSON
   and every list value, then fills the form. An API model can later produce the same JSON automatically.
4. **Case matching:** suggest existing cases for an article (same company, organization, place, recent dates) before a
   new case is created.
5. **Next evaluation:** prompt v2 was written after the test answers were seen, so it must be scored on a fresh set of
   about 30 articles (new sample seed, dev and test ids excluded).
