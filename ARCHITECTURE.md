# WhatTheyOwn — Architecture

**Status:** design, pre-implementation. Phase 0 API probing complete (2026-09-09) — see `fixtures/`.
**Last updated:** 2026-09-09
**Domain:** whattheyown.com
**Licence posture:** non-commercial for now, must stay commercially viable.

---

## 1. What this is

A public, searchable record of **declared shareholdings** of UK parliamentarians — House of Commons and House of Lords — showing current declarations and the history of declarations no longer held.

Search by member name, or by postcode for Commons members. Each member page separates public from private companies, and current from historical declarations.

Later, possibly other countries. Naming, schema and routing assume the UK is stage one of several.

**Out of scope for v1:** categories other than shareholdings; commercial features; non-UK legislatures.

---

## 2. Domain knowledge

Read this before writing code. The naive model — "the API tells you what politicians own" — is wrong in ways that affect the schema, the copy and the legal position.

### 2.1 No UK equivalent of the STOCK Act

There is no transaction reporting. You get a registered interest with a registration date and, eventually, a cessation or deletion. No amounts, no share counts, no prices, no buy/sell events.

Never describe the site as showing what someone owns. It shows what they have **declared** under the registration rules.

### 2.2 The two Houses have different thresholds

**Commons — Category 7: Shareholdings.** Registrable if either:
- greater than 15% of issued share capital (measured on the preceding 5 April), or
- 15% or less of issued share capital but valued at more than **£70,000**.

**Lords — Category 2: Shareholdings etc.** Registrable if:
- (a) amounting to a controlling interest, or
- (b) not amounting to a controlling interest but exceeding **£100,000** in value; also
- (c) private equity investments over £100,000 or more than 10% of the fund, and
- (d) corporate debt securities over £100,000.

**There is no percentage test in the Lords.** The £100,000 figure was doubled from £50,000 by a Code change approved in September 2023.

Worked example: Lord Lee of Trafford declares roughly thirty plc holdings, all under Category 2(b) — because he is a large private investor and each position clears £100,000, not because peers declare everything.

The Lords is where the shareholding data is richest, despite being the chamber most tools ignore. Do not build Commons-first.

### 2.3 What an empty page means

A member with no declarations is not a member who owns nothing. Excluded from registration:

- Anything below the relevant threshold.
- **Collective investment vehicles** — unit trusts, investment trusts, ICVCs — are generally not registrable. Someone with £5m in trackers shows a blank page. Sector-specific vehicles are the exception.
- **Blind trusts**, entirely exempt.
- Companies existing only to own the freehold of a personal residence.

**There are four distinct empty states, and they mean different things.** *Verified against live API 2026-09-09.*

| State | Signal | Meaning |
|---|---|---|
| `no_registrable_interests` | Category `11` ("Nil"), text `"No registrable interests"` | Member has affirmatively declared nothing registrable. |
| `exempt` | Category `11`, text `"On leave of absence; exempt from registration"` | Not required to register at all. |
| `awaiting_information` | Category `11`, text `"Awaiting information"` | Registrar is waiting on the member. |
| `no_filing` | `interestCategories: []` | No entry at all — typically a very new member. |

Examples seen live: Baroness Adams of Craigielea and Lord Anderson of Swansea carry `no_registrable_interests`; Baroness Amos has historic `exempt` and `awaiting_information` entries (both now deleted); Baroness Aglionby, who joined 2026-09-01, has an empty `interestCategories` array.

Never collapse these into "no holdings". This must be rendered on every profile, including empty ones. See `disclosure_context.state`.

### 2.4 Voluntary over-declaration happens

Members may register below-threshold interests after taking the Registrar's advice, and the Commons Miscellaneous category has no threshold plus a catch-all purpose test. Presence in the register does not prove the threshold was met.

### 2.5 The register is pruned

Interests are removed. Cessation is recorded, then eventually the entry goes.

**The archive is the product.** Anything created and deleted between two ingestor runs is lost permanently. There is no backfill for it.

### 2.6 Northern Ireland

18 constituencies. Sinn Féin MPs do not take their seats but are still MPs with registrable interests — their data belongs on the site like anyone else's. The NI restriction affects postcode lookup only (§7).

### 2.7 The Lords category scheme change of 5 April 2025

*Verified across all 815 members, 2026-09-09.*

On 5 April 2025 the Lords register moved to a new category scheme. 3,856 interests were deleted at exactly `2025-04-05T00:00:00` — a bulk operation, not human activity (the next-busiest deletion date in the whole dataset is 2022-01-17 with 196).

**Shareholdings were not affected.** All 3,856 fall in categories retired without a counterpart: `16`, `10`, `3`, `13`, `15`, `14` — non-financial interests and PSC. Shareholding categories `4`, `12` and `31` have **zero** records with this timestamp; their deletions are spread naturally across 2010–2025 and are genuine cessations.

**Consequence for v1: none.** Keep the `migrated` event type and the timestamp guard anyway — it becomes live the moment scope widens beyond shareholdings, and a guard that never fires costs nothing.

### 2.8 Lords shareholding data is in two disjoint sets

*Verified 2026-09-09.*

| Set | Categories | Records | Live |
|---|---|---|---|
| Current scheme | `1002`–`1005` | 2,558 | 2,136 |
| Retired scheme | `4`, `12`, `31` | 2,864 | **0** |

The old categories contain **no live records at all**. Every one is a ceased holding with a real deletion date. So the two sets are **additive, not overlapping** — there is no old-to-new record linking to do. Ingest both, treat old-scheme records as history, treat new-scheme records as current state.

Total: roughly **5,400 Lords shareholding records spanning 2010 to the present**, of which 2,136 are current.

### 2.9 Scale: the Lords is the product

| | Shareholding records |
|---|---|
| Commons (Category 7) | **193** |
| Lords (Categories 2(a)–(d), current scheme) | **2,558** |
| Lords (retired scheme, history) | **2,864** |

The Lords carries roughly **28× the Commons shareholding data**. Any design decision that treats the Commons as the primary case is wrong. Build Lords-first.

### 2.10 The two Houses hold different *kinds* of data

*Verified across all 193 Commons records, 2026-09-09.*

Commons Category 7 splits by threshold as:

| Threshold | Records |
|---|---|
| (i) over 15% of issued share capital | **155** (80%) |
| (ii) other shareholdings valued over £70,000 | **38** (20%) |

And **only 3 of 193 `OrganisationName` values contain "plc"** (case-insensitive).

So the typical Commons declaration is not a share portfolio — it is **an MP's own company**. Personal service companies, dormant Ltds, family businesses. `Office of Andy Burnham Ltd`, described in the register as *"A private limited company with myself as the sole director for office purposes"*, is the pattern rather than the exception.

The Lords, by contrast, is full of listed equity and private equity funds.

**Design consequence:** the two Houses answer different questions. Commons entries tell you *what an MP runs*; Lords entries tell you *what a peer holds*. The public-companies tab will be near-empty for the Commons. Present them honestly rather than merging into one dataset behind a tab, and do not let a sparse Commons page read as "this MP has no financial interests".

---

## 3. Data sources

**The two Houses come from two different APIs with different shapes.** This is not one client with a parameter. *All verified against live API 2026-09-09; saved responses in `fixtures/`.*

### 3.1 Commons — Interests API

`interests-api.parliament.uk/api/v1` · Open Parliament Licence

**Commons-only.** A `Type` parameter exists and defaults to `Commons`; passing `Type=Lords` is silently ignored and returns the Commons payload. All 12 categories come back `"type":"Commons"`.

- **Shareholdings is `id: 8`, `number: "7"`.** `parentCategoryIds: []` — flat, no sub-categories. Only Employment subdivides (ids 1, 2 under parent 12).
- **`id` is never equal to `number`** and the ordering differs (Employment is `id: 12`, `number: "1"`). Key off `id` everywhere.
- `totalResults: 193` for the whole category.
- **`Take` caps at 20.** Confirmed: `Take=100` returns `"take": 20` with 20 items. Read the `take` field back rather than trusting your request.
- `Skip` works correctly. 10 pages covers the category; 193 distinct IDs confirmed.
- **`ExpandChildInterests=True`** defaults to `False`. **Zero** Category 7 records currently have a `parentInterestId`, so this is precautionary — pass it anyway, it costs nothing.
- Default sort is `PublishingDateDescending`.
- **There is no `includeDeleted`.** Both `includeDeleted=true` and `IncludeDeleted=true` are silently ignored and still return 193. **The Commons API gives current state only.**

Records are **fully structured**. No text parsing required:

| Field | Type | Notes |
|---|---|---|
| `ShareholdingThreshold` | String | `"(i) Shareholdings: over 15% of issued share capital"` (155 records) or `"(ii) Other shareholdings, valued at more than £70,000"` (38) |
| `RegistrableDate` | DateOnly | Acquired, or reached registrable value |
| `EndDate` | DateOnly | Structured cessation date. **Populated on only 11 of 193 records.** |
| `OrganisationName` | String | Clean company name — no extraction needed |
| `OrganisationDescription` | String | Nature of business |
| `HeldOnBehalfOf` | String | Spouse, partner, dependent child |
| `ManagedBy` | String | Trust or delegated management |

Top level also carries `id`, `summary`, `parentInterestId`, `registrationDate`, `publishedDate`, `updatedDates[]`, `rectified`, `rectifiedDetails`, and an embedded `member` object (MNIS id, name, house, `memberFrom`, party).

#### Registers endpoint

`totalResults: 50`, published roughly fortnightly. IDs are **not sequential** (820, 812, 804, 803, 801, … 511) and are probably shared with Lords registers — order by `publishedDate`, never by id.

**Depth: the oldest register is id `511`, published 2024-03-18.** Roughly the current Parliament plus a few months before the July 2024 general election. Nothing earlier is available from this API.

Each register exposes:

| Link | What it actually is |
|---|---|
| `csv` → `/api/v1/Interests/csv?registerId={id}` | **A ZIP archive**, despite the route name. See below. |
| `registerDocument` → `/api/v1/Registers/{id}/document` | Document form |
| `registerUpdatesDocument` → `/api/v1/Registers/{id}/document?type=Updated` | **A PDF** (`%PDF-1.7`, served as `application/octet-stream`). Not machine-readable. Ignore it. |

#### ⭐ The CSV export is the preferred Commons ingest path

`/api/v1/Interests/csv?registerId={id}` returns a **ZIP** (magic bytes `PK`) containing one CSV per category:

```
PublishedInterest-Category_1.csv     PublishedInterest-Category_6.csv
PublishedInterest-Category_1.1.csv   PublishedInterest-Category_7.csv   ← shareholdings, ~56KB
PublishedInterest-Category_1.2.csv   PublishedInterest-Category_8.csv
PublishedInterest-Category_2.csv     PublishedInterest-Category_9.csv
PublishedInterest-Category_3.csv     PublishedInterest-Category_10.csv
PublishedInterest-Category_4.csv
PublishedInterest-Category_5.csv
```

`PublishedInterest-Category_7.csv` header — **the dynamic `fields` array already flattened into named columns**:

```
ID, Summary, Parent Interest ID, Registered, Published, Updated_1, Category,
MNIS ID, Member, ShareholdingThreshold, RegistrableDate, EndDate,
OrganisationName, OrganisationDescription, HeldOnBehalfOf, ManagedBy, Link, Updated_2
```

Advantages over the JSON API: one request per register instead of ten, no 20-item cap, no field-bag mapping, and **each zip is a complete snapshot of the register at its publication date**.

Note `updatedDates[]` is flattened to exactly two columns (`Updated_1`, `Updated_2`) — check whether any row populates both before assuming a cap of two.

**This makes Commons history reconstructible.** Pull all ~50 register zips (~50 requests, one-off), diff consecutive Category 7 snapshots, and you recover creations, amendments and removals fortnight by fortnight back to March 2024 — far better than the 11 `EndDate` values the live API exposes. See §7 Backfill.

### 3.2 Lords — Members API register endpoint

`members-api.parliament.uk/api/LordsInterests/Register` · Open Parliament Licence

**Parameters are `searchTerm`, `page`, `includeDeleted`. There is no `skip` or `take`** — those exist on other Members API endpoints and are silently ignored here, returning page 1 every time.

**⚠️ `page` is 1-indexed, despite the API documenting "default 0".** Page 0 and page 1 return identical payloads. A naive `for (page = 0; page < 41; page++)` loop fetches page 1 twice and **silently drops the final 15 peers**. Loop 1 to 41 inclusive.

- **One document per peer**, not per interest. `totalResults: 815`.
- **Page size fixed at 20**, not adjustable. 815 members = 41 pages.
- **No category filter.** Pull all 815 and filter client-side.
- **`includeDeleted=true` works** and returns deleted interests with `deletedWhen` populated, back to 2010. See §7.

**Assert coverage every run.** Distinct member IDs must equal `totalResults`. A pagination regression that silently serves page 1 forever is the failure mode most likely to produce a plausible-looking site built on 2% of the data.

**Shareholding categories:**

| ID | Name | Records | Live |
|---|---|---|---|
| `1002` | Category 2: Shareholdings etc. **(a)** — controlling interest | | |
| `1003` | Category 2: Shareholdings etc. **(b)** — over £100,000 | | |
| `1004` | Category 2: Shareholdings etc. **(c)** — private equity | | |
| `1005` | Category 2: Shareholdings etc. **(d)** — corporate debt | 66 | |
| | *all four combined* | **2,558** | **2,136** |
| `4`, `12`, `31` | retired scheme (a)/(b)/(c) — history only | **2,864** | **0** |
| `11` | Nil (see §2.3) | | |

**Full category list, current scheme:** `1001` Remunerated employment etc., `1002`–`1005` Shareholdings (a)–(d), `1006` Land and property, `1007` Sponsorship, `1008` Overseas visits, `1009` Gifts/benefits/hospitality, `1010` Miscellaneous financial interests. Ten categories.

**Retired scheme:** `1` Directorships, `2` Remunerated employment, `3` PSC, `4`/`12`/`31` Shareholdings (a)/(b)/(c), `5` Land and property, `6` Sponsorship, `7` Overseas visits, `8` Gifts, `9` Miscellaneous, `10`/`13`/`14`/`15`/`16` Non-financial (a)–(e). `11` Nil is unchanged and used by both.

Note `1005` (corporate debt) has **no counterpart** in the old scheme — it is new. Categories `3`, `10`, `13`–`16` were retired with no counterpart (§2.7).

- **The interest is a single free-text string.** `"Hampden & Co plc (banking)"`, `"Equinor ASA (energy company engaged in oil and gas exploration and production activities)"`. All parsing burden is here — the inverse of the Commons.
- **Cessation is embedded in the text**, not a field: `"(interest ceased 3 March 2026)"`. There is no `EndDate`. **Only 77 of 2,136 live records (~3.6%) contain this**, so the regex matters mainly for historic records, not current state.
- **Category blocks repeat rather than group.** Lord Agnew has eight separate `{"id":1003,...}` blocks each holding one interest. Flatten; never assume one block per category.
- Per-interest fields: `id`, `interest`, `createdWhen`, `lastAmendedWhen`, `deletedWhen`, `isCorrection`, `childInterests[]`.
- Member object is richer than the Commons one: `nameListAs`, `nameDisplayAs`, `nameFullTitle`, `nameAddressAs`, `latestParty` (with colours), `gender`, `latestHouseMembership` (start date, end date, end reason, status), `thumbnailUrl`.

### 3.3 Other sources

| Source | Base URL | Licence | Notes |
|---|---|---|---|
| Members API (general) | `members-api.parliament.uk` | Open Parliament Licence | Member profiles, both Houses. Photos at `/api/Members/{id}/Thumbnail`. |
| Companies House | `api.company-information.service.gov.uk` | OGL | Free. Rate limited **600 requests / 5 minutes**. Name-based search only. No websites, no logos. |
| Wikidata | `query.wikidata.org` (SPARQL) | CC0 for statements | Joins on the Companies House ID property. Yields official website, logo image (→ Wikimedia Commons), industry. Near-total for LSE-listed, poor for small private Ltds. Verify property IDs before building. |
| Logo.dev | `img.logo.dev` | Free tier 500K req/month, attribution required | Fallback logo source keyed on domain. Successor to the Clearbit Logo API, which shut down 8 December 2025. Brandfetch equivalent. |
| ONS NSPL | ONS Open Geography Portal | OGL + OS OpenData (GB); restricted (NI) | Postcode → constituency. Quarterly. Use the 2024 boundary field. |
| Archived registers | `publications.parliament.uk` | Open Parliament Licence | Static HTML. **Commons backfill only** — the Lords is covered by `includeDeleted`. |
| mySociety register data | `pages.mysociety.org/parl_register_interests` | — | Per-category CSV/Parquet, including a Category 7 table. Useful as a completeness cross-check. |

### 3.4 Encoding

Console output on Windows renders UTF-8 as CP1252 (`┬ú70,000`, `2024ÔÇô25`, `memberÔÇÖs`). Almost certainly a terminal artefact rather than the API. Set `Encoding.UTF8` explicitly on all `HttpClient` reads and assert on a `£` in a fixture test rather than trusting the console.

---

## 4. Architecture

```
Interests API (Commons, structured) ─┐
Lords Register  (Lords, free text)   ─┼─► .NET 10 console app ──► R2  raw/{timestamp}/*.json
Members API  (profiles, both Houses) ─┤   (GitHub Actions, 6h)      (immutable archive)
Companies House                      ─┘
                                      │
                                      ├──► D1  append-only event log
                                      │
                                      └──► data/*.json  (build payload)
                                                 │
ONS NSPL ──► shard builder ──► public/pc/*.json  │
             (Actions, quarterly)                │
                                                 ▼
                                    Astro static build (same Actions job)
                                                 │
                                          wrangler deploy
                                                 │
                                    Cloudflare Workers Static Assets
```

**Nothing runs at request time.** No Worker script, no `main` in `wrangler.toml`, no bindings, no database access from the edge. Every byte served is computed before anyone asks for it.

Revisit this only if the site gains user watchlists, email alerts, sub-minute publishing, or a page count high enough that rebuilds hurt. Keep page assembly behind a single `getMemberPage(slug)` function so the storage layer can be swapped in an afternoon.

---

## 5. Repository layout

Single monorepo, public on GitHub.

```
/ARCHITECTURE.md          this file — domain rules, verified API behaviour, schema
/CLAUDE.md                agent operating manual, auto-loaded by Claude Code
/DECISIONS.md             rejected alternatives and deferred ideas
/src/Ingestor/            .NET 10 console app (generic host)
/src/ShardBuilder/        .NET 10 console app (ONS NSPL → postcode shards)
/src/Web/                 Astro + Svelte, static output
/tests/Ingestor.Tests/    xUnit
/migrations/              D1 SQL migrations
/data/                    build payload, generated (gitignored)
/fixtures/                committed API responses — read-only, see §16
/.github/workflows/
  ingest-and-publish.yml
  refresh-postcodes.yml
```

---

## 6. Data model (D1)

```sql
members (
  id                INTEGER PRIMARY KEY,   -- MNIS ID, the real key
  house             TEXT NOT NULL,         -- 'commons' | 'lords'
  name              TEXT NOT NULL,
  party             TEXT,
  constituency      TEXT,                  -- NULL for peers
  slug              TEXT NOT NULL UNIQUE,  -- generated once, never recomputed
  photo_url         TEXT,
  is_current        INTEGER NOT NULL,
  first_seen        TEXT NOT NULL,
  last_confirmed    TEXT NOT NULL
)

member_slugs (         -- old slugs → 301; names and titles change
  slug              TEXT PRIMARY KEY,
  member_id         INTEGER NOT NULL,
  retired_at        TEXT
)

declaration_events (   -- APPEND ONLY. Never UPDATE, never DELETE.
  id                 INTEGER PRIMARY KEY,
  member_id          INTEGER NOT NULL,
  interest_id        TEXT NOT NULL,        -- Parliament's ID
  house              TEXT NOT NULL,        -- 'commons' | 'lords'
  event_type         TEXT NOT NULL,        -- observed|created|amended|ceased|removed|corrected|migrated
  observed_at        TEXT NOT NULL,        -- when WE saw it
  raw_text           TEXT NOT NULL,        -- NEVER overwrite
  raw_json           TEXT NOT NULL,        -- full API record as fetched

  -- category
  category_id        INTEGER NOT NULL,     -- API's numeric id: 8 (Commons) | 1002-1005 (Lords)
  sub_category       TEXT,                 -- '2a' | '2b' | '2c' | '2d' — Lords only
  threshold_text     TEXT,                 -- Commons ShareholdingThreshold verbatim

  -- dates
  created_when       TEXT,                 -- Lords createdWhen / Commons registrationDate
  last_amended_when  TEXT,
  deleted_when       TEXT,                 -- Lords only
  registrable_date   TEXT,                 -- Commons RegistrableDate
  end_date           TEXT,                 -- Commons EndDate (structured)
  ceased_on          TEXT,                 -- Lords: parsed from raw_text; Commons: = end_date
  published_date     TEXT,                 -- Commons
  source_register_id TEXT,                 -- Commons

  -- Commons-only structured fields
  held_on_behalf_of  TEXT,
  managed_by         TEXT,
  organisation_desc  TEXT,

  -- relationships
  parent_interest_id TEXT,
  is_duplicate_of    INTEGER,              -- FK to the surviving row after a correction
  migrated_from_id   TEXT,                 -- pre-2025-04-05 Lords interest_id, where matched

  -- resolution
  company_id         INTEGER,              -- FK, nullable until resolved
  match_confidence   REAL
)

companies (
  id                INTEGER PRIMARY KEY,
  canonical_name    TEXT NOT NULL,
  slug              TEXT NOT NULL UNIQUE,
  ch_number         TEXT,
  ticker            TEXT,
  wikidata_id       TEXT,
  company_type      TEXT,                  -- PLC | Ltd | LLP | ...
  is_public         INTEGER,               -- drives the public/private tab split
  status            TEXT,

  -- descriptive
  website           TEXT,                  -- displayed as an outbound link
  description       TEXT,                  -- one line, see §7 enrichment
  description_source TEXT,                 -- register|wikidata|sic|manual
  sic_codes         TEXT,
  sector            TEXT,                  -- internal taxonomy, mapped from SIC
  sector_source     TEXT,                  -- sic|wikidata|manual

  -- logo
  logo_key          TEXT,                  -- R2 object key, NULL = render monogram
  logo_source       TEXT,                  -- wikidata|logodev|favicon|manual
  logo_attribution  TEXT,                  -- Commons author + licence where applicable
  logo_checked      TEXT,                  -- last attempt, success or failure

  -- enrichment bookkeeping
  ch_last_checked   TEXT,
  enrich_last_run   TEXT
)

company_aliases (
  alias_text        TEXT PRIMARY KEY,
  company_id        INTEGER NOT NULL,
  source            TEXT,                  -- 'auto' | 'manual'
  confidence        REAL
)

resolution_queue (
  raw_text          TEXT PRIMARY KEY,
  first_seen        TEXT NOT NULL,
  candidate_ids     TEXT,
  status            TEXT                   -- pending | resolved | unmatchable
)

disclosure_context (   -- rendered on every profile, including empty ones
  member_id         INTEGER PRIMARY KEY,
  house             TEXT NOT NULL,
  state             TEXT NOT NULL,   -- has_declarations | no_registrable_interests
                                     -- | exempt | awaiting_information | no_filing  (§2.3)
  state_source_text TEXT,            -- the category-11 text verbatim, where applicable
  threshold_note    TEXT NOT NULL,
  last_confirmed    TEXT NOT NULL
)

ingestion_runs (
  id                INTEGER PRIMARY KEY,
  started_at        TEXT NOT NULL,
  register_id       TEXT,
  content_hash      TEXT NOT NULL,
  changed           INTEGER NOT NULL,
  r2_key            TEXT NOT NULL
)
```

### Rules

1. `declaration_events` is append-only. Current state is a **projection** over the log, never a separate source of truth. This is what lets the site answer "what did they used to own."
2. Event grain is the interest record, not the member.
3. `raw_text` is never overwritten, however confident the company match.
4. Members are never deleted. Leaving Parliament sets `is_current = false`. A former member's holdings while in office are among the most interesting data on the site.
5. Slugs are generated once and stored, never recomputed from a name. Retired slugs go in `member_slugs` and 301.
6. **Commons and Lords populate different column subsets.** Commons fills `threshold_text`, `registrable_date`, `end_date`, `held_on_behalf_of`, `managed_by`, `organisation_desc`, `published_date`, `source_register_id`. Lords fills `sub_category`, `deleted_when`, and a `ceased_on` parsed out of `raw_text`. Nulls here are structural, not missing data.
7. **A deletion at exactly `2025-04-05T00:00:00` is `migrated`, never `ceased`** (§2.7).

---

## 7. Ingestion

### Cadence

Every 6 hours via GitHub Actions schedule, plus manual dispatch. Poll more often than feels necessary — the interval sets the resolution of the history, and gaps are unrecoverable.

### Per-run sequence

1. **Fetch the full set.**
   - Commons: either `/Interests?CategoryId=8&ExpandChildInterests=True` (`Skip`/`Take` at 20, 10 requests) or the latest register's CSV zip (1 request, flat columns — see §3.1). Prefer the zip.
   - Lords: `/LordsInterests/Register?page={n}&includeDeleted=true` for **n = 1 to 41 inclusive** (1-indexed — see §3.2). 815 members, 41 requests. Filter to categories `1002`–`1005` (current) and `4`, `12`, `31` (history) after fetching.
   - Members list, both Houses.

   **Assert distinct member count equals `totalResults` before proceeding.** Abort the run on mismatch rather than writing a partial dataset.

   Never fetch incrementally. Even with `includeDeleted`, a hard removal is an *absence*, visible only by diffing the complete current ID set against the last known set.
2. **Write raw responses to R2 verbatim**, keyed `raw/{iso-timestamp}/`, before parsing anything. Never edited, never deleted. When the field mapping turns out wrong later, re-derive from here — Parliament will no longer serve the original.
3. **Hash the normalised set.** Unchanged → record the run and exit. This is most runs.
4. **Changed** → diff against last known state, append events, write `data/*.json`, signal `changed=true` to the workflow via `GITHUB_OUTPUT`.

### CLI flags

- `--dry-run` — fetch and diff, write nothing
- `--export-only` — read D1, write `data/*.json`, no Parliament fetch (for template changes)
- `--member {id}` — single member, for debugging
- `--backfill {path}` — load archived registers

### Parsing — Commons

No text parsing. Two equivalent paths:

- **CSV zip (preferred):** flat named columns, already mapped. See §3.1.
- **JSON API:** read the typed `fields` array directly.

Either way, map `OrganisationName` straight to company resolution and `EndDate` straight to `ceased_on`.

Log unrecognised field names or CSV columns as warnings rather than dropping them — the field set is a data payload, not a versioned contract.

### Parsing — Lords

All the parsing burden is here. The interest is one free-text string.

**Company and description:** `Name (description)` — e.g. `Equinor ASA (energy company engaged in oil and gas exploration and production activities)`. Note some entries have no parenthetical at all (`Abingdon Software `, with a trailing space), some have nested or multiple parentheses, and some carry a rename inside them (`Visionable Limited (formerly IOCOM UK Ltd) (visual business platform)`).

**Cessation:** `\(interest ceased ([^)]+)\)`. **Only 77 of 2,136 live records (~3.6%) carry this**, so it matters mainly for the 2,864 historic records, not current state. Variants seen live:

```
(interest ceased 3 March 2026)
(interest ceased 31 December 2015)
(interest ceased 30 November 2021 - notified 4 February 2022)
(interest ceased 1 January 2022 - notified 3 March 2022)
(interest ceased 2013)                        ← year only
```

Handle the `- notified` suffix and the year-only form. Store the parsed date in `ceased_on` and always keep `raw_text` intact.

**Percentages sometimes appear** in 2(a) text: `100 per cent ownership with partner of Leadership in Mind Ltd`, `50 per cent holding in TomahawkPro Ltd`, `30 per cent holding in Three Sixty Action Ltd`. Worth extracting where present, but it is not a reliable field.

### Deduplication

Corrections produce near-identical rows. Observed live:

- Lord Agnew — `Intermediate Capital Group plc (financial)` at ids 30972 and 30973, the second deleted three minutes after creation.
- Lord Anderson of Ipswich — identical Cyprus legal-advice text at ids 39339 and 47352.
- Lord Allen of Kensington — `Chair, British Horseracing Authority` at 46347 and 46846.

Dedupe on normalised `raw_text` + `member_id` + `category_id`. Record the loser in `is_duplicate_of` rather than discarding it. `isCorrection` exists on the Lords record but was `false` on every observed duplicate, so do not rely on it.

### Migration handling

Per §2.7, the 5 April 2025 scheme change did **not** touch any shareholding category, so this is a guard rather than a live concern for v1:

```
if (deletedWhen == 2025-04-05T00:00:00 exactly)
    → event_type = 'migrated'
    → NEVER emit 'ceased'
```

No old-to-new record linking is required. Per §2.8 the retired and current shareholding categories are disjoint — old-scheme records are all deleted, new-scheme records are the current state.

Add a regression test asserting no shareholding cessation is ever dated 5 April 2025 via this path. It should pass trivially today; it exists to catch a future scope widening.

### Company resolution

The hardest part. Declarations arrive as free text, inconsistent, carrying commentary, sometimes misspelled:

```
Standard Life plc (formerly Phoenix Group Holdings plc)
Christie Group plc (business & leisure industry services)
Highland Bookshop Ltd
```

- Fuzzy match against `companies` and `company_aliases`.
- Above threshold → link automatically, record `match_confidence`.
- Below → `resolution_queue` for manual review.
- Every manual decision writes an alias row, so the system improves rather than re-guessing.
- Companies House enrichment runs as a separate, slower job (600 req/5min) touching only unresolved names. Cache permanently.
- **Display `raw_text` alongside the resolved company, and mark low-confidence matches visibly.** A wrong match — same name, different entity — is the most likely route to publishing something false about a named person.

### Company enrichment

A **separate, slower job** from the 6-hourly ingest. Runs against companies where `enrich_last_run` is null or older than the re-check interval. Never blocks a declarations update — if enrichment fails, the site still publishes with monograms and no descriptions.

Rate limits set the pace: Companies House allows 600 requests per 5 minutes, and the logo free tiers are capped monthly. Enrichment touches a few hundred companies total and each field is re-checked at most yearly, so this is cheap once the initial pass is done.

**Resolution order.** Each field falls through the tiers until something returns. Record which tier won, so a single tier can be re-run later without disturbing the others.

**1. Website** — Wikidata official website property, joined via the Companies House ID. Falls back to the Companies House registered office record where useful, then manual. Displayed on the company page as an outbound link. Use `rel="noopener nofollow"`.

**2. Description** — one line, sourced in this order:
   - **The register itself.** Members are required to briefly indicate the nature of the business, so declarations arrive as `Christie Group plc (business & leisure industry services)` or `Anpario plc (natural feed additives)`. This is free, already ingested, under the Open Parliament Licence, and often better than anything else available. Parse the bracketed text from `raw_text`.
   - Wikidata description (CC0).
   - A generated line from the primary SIC code.
   - Manual.

**3. Sector** — an internal taxonomy, not a licensed one. ICB and GICS are commercial products and must not be used. Map from Companies House SIC codes to a small set of sector labels maintained in the repo; fall back to the Wikidata industry property. Keep the mapping table in version control so it can be corrected and re-derived.

**4. Logo** — the hard step is the domain, not the logo, since every provider is keyed on one. Resolve `website` first.
   - **Wikidata logo image → Wikimedia Commons.** Best quality, properly licensed, near-total coverage for LSE-listed companies. Capture the author and licence into `logo_attribution`.
   - **Logo.dev**, keyed on the resolved domain. Called once from the ingestor, so the API key stays in GitHub secrets and never reaches the browser.
   - **Favicon** — `apple-touch-icon` or `og:image` from the company's own site. Low quality, but better than nothing for small companies with a web presence.
   - Otherwise leave `logo_key` null.

**Store the file, never hotlink.** Fetch once, normalise to ~200px WebP, write to R2, serve from our own domain. Hotlinking would reintroduce a runtime third-party dependency and spend free-tier quota on page views rather than on the few hundred companies that exist.

**Null logo renders a monogram**, not a broken image: initials plus a colour derived deterministically from the company name, so a given company always renders the same placeholder.

### Expected coverage

Declared companies split into LSE-listed plcs, where enrichment is near-total, and small private companies (`Highland Bookshop Ltd`, `Tout a L'Heure Limited (dormant company)`), where no website or logo exists anywhere. Expect roughly 30–40% logo coverage overall, concentrated entirely in the public-companies tab. Descriptions do better, because the register supplies them.

Scale to plan for: ~5,400 Lords records plus 193 Commons, resolving to some smaller number of distinct companies after deduplication. Sizing the enrichment jobs and the manual resolution queue against that figure, not against the raw record count.

### Backfill

**Lords: nothing to do.** `includeDeleted=true` returns deleted interests back to **2010** as part of the normal 6-hourly fetch. *Verified 2026-09-09: 2,864 historic shareholding records under the retired categories, all deleted, none live.*

**Commons: pull the register zips.** A one-off job, roughly 50 requests:

1. `GET /api/v1/Registers` — all 50, ordered by `publishedDate`.
2. For each, `GET /api/v1/Interests/csv?registerId={id}` → ZIP → extract `PublishedInterest-Category_7.csv`.
3. Write every zip to R2 unmodified.
4. Diff consecutive snapshots by interest `ID` to emit `created`, `amended` and `removed` events back to **2024-03-18**.

This is the only route to Commons history — the live API has no `includeDeleted` and only 11 of 193 records carry an `EndDate`. It is cheap enough to belong in Phase 2 rather than a later phase.

**Pre-March-2024 Commons:** would require scraping `publications.parliament.uk`. Given the Commons is 193 records against the Lords' 5,400, and given §2.10 (Commons declarations are mostly MPs' own companies rather than portfolios), this is low value. Defer indefinitely; state the coverage window on the site instead.

*(The question of looking through investment vehicles to their underlying holdings is deferred — see `DECISIONS.md`.)*

---

## 8. Frontend

Astro with `output: 'static'`, Svelte islands for search and tab filtering. No adapter — there is no server-rendering to compile. `wrangler.toml` has an `[assets]` directory and no `main`, so no Worker script exists.

The build reads `data/*.json` from disk, written by the ingestor. No network calls during the build, no Cloudflare credentials in the build step, and builds are reproducible from any commit.

- **Routes:** `/{house}/{slug}` for members, `/company/{slug}` for companies, generated via `getStaticPaths`.
- **Loader must be memoised** at module scope — `getStaticPaths` runs independently in every route file.
- **Search:** a static `search-index.json` of slug, house, name, party, constituency. ~1,450 entries, roughly 50KB gzipped. Fetched once, filtered in the browser.
- **Postcode lookup (GB only):** shards in `public/pc/{outcode}.json`, ~2,900 files. Most outward codes sit in a single constituency and compress to one key; split codes carry a slug dictionary plus an incode index. Browser fetches one ~2KB shard and does a property lookup. Peers have no constituency — say so in the UI rather than leaving a dead end.
- **File count budget.** *Verified 2026-09-09.* Workers Static Assets allows **20,000 files per Worker version on the free plan, 100,000 on paid**, with a 25 MiB cap per individual file. The paid limit rose from 20,000 in September 2025 and **requires Wrangler 4.34.0 or higher** — older versions still enforce 20,000.

  Projected: ~1,465 member pages + ~2,700 postcode shards + ~2,000–3,000 company pages + ~50 index/asset files ≈ **6,200–7,200**, roughly a third of the free-tier limit. Logos live in R2, not in assets, so the 25 MiB cap is never in play — the largest asset is the ~50KB search index.

  Astro emits one `index.html` per route directory, so file count equals page count. Verify after the first real build:

  ```
  find dist -type f | wc -l
  find dist -type f -size +25M -print
  ```

  The binding constraint is build time, not file count: build time grows with page count while the file limit stays fixed.

---

## 9. CI/CD

Two workflows.

**`ingest-and-publish.yml`** — cron every 6 hours plus manual dispatch. Runs the ingestor; if it reports `changed=true`, installs Node, builds the Astro site and runs `wrangler deploy`. Ingest logs and build logs sit in the same run.

**`refresh-postcodes.yml`** — quarterly, on its own schedule. Different cadence and different failure mode; a broken ONS download must not block a declarations update.

Notes:

- Wrangler hashes assets and uploads only changed files. Two amended entries push two HTML files, not 1,450.
- Deploys are atomic; the current site serves until the new version is fully uploaded. Rollback is available from the dashboard.
- GitHub Actions is unmetered for public repos.
- **A general election churns a large fraction of the Commons overnight.** Expect one very large diff and a full rebuild. The ingestor must handle hundreds of member changes in a single run.

---

## 10. Licensing and legal

The project may become commercial later. Every decision below assumes that.

### Attribution page (`/attributions`)

- **Open Parliament Licence** — register and member data. Permits commercial re-use with attribution.
- **OGL** — Companies House data.
- **OS OpenData** plus Crown copyright statements for postcode data:
  - Contains OS data © Crown copyright and database right [YEAR]
  - Contains Royal Mail data © Royal Mail copyright and database right [YEAR]
  - Contains National Statistics data © Crown copyright and database right [YEAR]
  - Contains NRS data © Crown copyright and database right [YEAR]

- **Wikimedia Commons** — per-file author and licence, stored in `companies.logo_attribution` and rendered where required.
- **Logo.dev** — attribution required on the free tier.

Pull current wording from each source directly; the year rolls over annually.

### Logos are trademarks

Using a logo to identify the company it belongs to is referential use and is what every financial site does. Constraints: keep them small, factual and unaltered, never imply endorsement, and drop any logo on request.

Be aware of the context — a company that dislikes appearing beside "this member declares shares here" has an easier complaint about use of its mark than about use of the register. `logo_source` and `logo_key` are nullable precisely so a single logo can be removed without touching anything else.

Sector classification must use an internally maintained taxonomy. **ICB and GICS are licensed commercial products and must not be used.**

### Northern Ireland postcodes — EXCLUDED

BT postcodes are present in ONSPD/NSPL but licensed separately. ONS issues only a **Northern Ireland End User Licence for internal business use only**; anything else needs a licence direct from **Land and Property Services (LPS)**. "Internal business use" is narrower than "non-commercial" — a public website serving BT shards is neither.

Routing via postcodes.io does not change this. The licence attaches to the data, not the delivery route.

**Decision:** omit BT from the shards. Show *"Postcode lookup isn't available for Northern Ireland — search by name."* NI member pages exist and are fully searchable; only that entry route is missing.

To add later: licence the Central Postcode Directory from LPS, or email ONS to confirm scope. A deliberate purchase, not a retrofit.

### If/when commercial

- ICO data protection fee (~£52–78/yr for a small organisation — verify current figure).
- Privacy notice; lawful basis for publishing personal data about named individuals.
- **A correction route that does not rewrite history.** If a member disputes an entry, append a `corrected` event with its own source and timestamp, display the correction, keep the original observation. Never build an edit path that overwrites.
- Worth a couple of hours with a lawyer before launch rather than after.

---

## 11. Build order

Each phase has an acceptance criterion. Do not advance until it passes.

### Phase 1 — repo and cloud resources

Repo created public; `ARCHITECTURE.md`, `CLAUDE.md`, `DECISIONS.md` and `fixtures/` committed; R2 bucket and D1 database created; `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `CF_ACCOUNT_ID`, `CF_API_TOKEN`, `CH_API_KEY` in repo secrets.

**Done when:** `wrangler d1 execute whattheyown --command="SELECT 1"` succeeds and `wrangler r2 bucket list` shows the bucket.

### Phase 2 — ingestor v0.1, archive only

No parsing, no D1, no diffing. Both clients, coverage assertion, raw responses to R2, 6-hourly cron.

**Done when:** a manually triggered workflow run writes a `raw/{iso-timestamp}/` prefix containing 41 Lords pages and 10 Commons pages, the coverage assertion passes (815 distinct members), and the cron is live on `main`.

**Then leave it running.** Everything after this is rebuildable from the archive; the gap before it is not.

### Phase 3 — D1 and the event log

Schema, both parsers, migration guard, deduplication, hashing, diffing, `--export-only`, replay of the accumulated archive, `disclosure_context`.

**Done when:** `dotnet test` passes every case in §16, a full run populates `declaration_events`, and a second run with unchanged data emits `changed=false` and writes nothing.

### Phase 4 — Commons register-zip backfill

~50 register ZIPs pulled, archived to R2, diffed by snapshot back to 2024-03-18.

**Done when:** Commons events exist with `observed_at` reflecting register publication dates, not today.

### Phase 5 — company resolution and enrichment

Companies and aliases built, fuzzy matching plus resolution queue, queue worked manually once, Companies House and Wikidata enrichment, logos to R2, SIC-to-sector mapping.

**Done when:** every `declaration_event` has either a `company_id` or a `resolution_queue` row, and no company row was created without its `raw_text` preserved.

### Phase 6 — frontend

Astro static, member and company pages, disclosure context everywhere, current/historical and public/private splits, search index, deploy.

**Done when:** the site builds under 20,000 files, every member page renders including empty ones, and low-confidence company matches are visibly marked.

### Phase 7 — postcodes

NSPL download, shards excluding BT, quarterly workflow, client-side lookup.

**Done when:** a GB postcode resolves to the right 2024-boundary MP, and a BT postcode shows the NI message rather than failing.

### Phase 8 — pre-launch

`/attributions`, `/about`, correction process and contact route, full copy review against §13, twenty members hand-checked against the live register (include Lord Agnew and Lord Lee).

---

## 12. Open questions

Phase 0 API probing completed 2026-09-09. Findings are recorded in §2.7–2.10 and §3; raw responses in `fixtures/`.

- [ ] Does any Commons CSV row populate both `Updated_1` and `Updated_2`? Determines whether the flattening caps at two.
- [ ] Confidence threshold for auto-linking companies — tune empirically against the ~5,400 Lords records (Phase 4).
- [ ] Current Wikidata property IDs for Companies House ID, official website, logo image, industry (Phase 4).
- [ ] Actual Wikidata join rate against the real company set — decides whether Logo.dev is needed at all (Phase 4).
- [ ] SIC-to-sector mapping, drafted against the codes that actually appear (Phase 4).

---

## 13. Copy conventions

- **"Declared shareholdings"**, never "holdings", "stocks" or "owns". UK usage is *shares*; "stocks" is American; "holdings" and "owns" overpromise given the site publishes declared above-threshold interests.
- A member is described as having **declared an interest**, never as owning something.
- An empty page says which of the four states applies (§2.3), never "no financial interests".
- Commons and Lords are described distinctly (§2.10) — an MP's own company is not the same claim as a peer's listed holding.
- International expansion goes in the path — `/uk/...` — not a rebrand.

Domain rationale and rejected names: see `DECISIONS.md`.

---

## 14. Code conventions

**Runtime:** C#, .NET 10. Nullable reference types enabled. `TreatWarningsAsErrors` on.

**Host:** `Microsoft.Extensions.Hosting` generic host with DI.

- Clients registered as typed `HttpClient`s via `IHttpClientFactory`.
- Polly resilience pipeline on each: 3 retry attempts, exponential backoff, 30s timeout.
- Config from `appsettings.json` plus environment variables, env taking precedence.
- `ILogger<T>` injected; console provider only (output goes to the Actions log).

**JSON:** `System.Text.Json` exclusively. `PropertyNameCaseInsensitive = true`, options held as a static singleton. No Newtonsoft.

**Encoding:** UTF-8 explicit on every read. Console output is not evidence — Windows terminals render UTF-8 as CP1252, making `£` appear as `┬ú`. Assert on a literal `£` in tests.

**Interfaces:** every API client and the archive writer gets one (`ILordsRegisterClient`, `ICommonsInterestsClient`, `IArchiveWriter`) so tests substitute fixtures.

**Tests:** xUnit. Hand-written fakes reading from `fixtures/`. No mocking framework.

**Data naming:** `commons` / `lords` lowercase. Category IDs are integers. Never conflate an API `id` with its `number` (§3.1).

---

## 15. Commands

```bash
dotnet run --project src/Ingestor                      # full run
dotnet run --project src/Ingestor -- --dry-run         # fetch + diff, write nothing
dotnet run --project src/Ingestor -- --member 4689     # single member (Lord Agnew)
dotnet run --project src/Ingestor -- --export-only     # D1 → data/*.json, no fetch
dotnet run --project src/Ingestor -- --backfill-commons

dotnet test

wrangler d1 execute whattheyown --file=migrations/0001_init.sql
wrangler d1 execute whattheyown --command="SELECT COUNT(*) FROM declaration_events"
wrangler r2 object list whattheyown-raw --prefix raw/

cd src/Web && npm run build && npx wrangler deploy
find dist -type f | wc -l                              # must stay under 20,000
```

---

## 16. Fixtures

Committed API responses under `fixtures/`. **Read-only** — they are the evidence behind every "verified" claim in §2–3 and the input to the test suite.

| Path | What it is | What it proves / tests |
|---|---|---|
| `lords-full/page-0001.json` … `page-0041.json` | Full Lords register, `includeDeleted=true`, 41 pages | 815 members, category scheme, deletion history to 2010. Pagination test: page 1 ≠ page 0. |
| `commons-full/page-0000.json` … `page-0180.json` | All 193 Commons Category 7 records, 10 pages | Typed `fields` array, threshold split, 11 populated `EndDate`s |
| `categories-lords.json` | `/Categories?Type=Lords` | Proves `Type` is ignored; Interests API is Commons-only |
| `registers.json` | `/Registers?Take=5` | Register cadence, non-sequential IDs, link shapes |
| `commons-820/PublishedInterest-Category_7.csv` | Extracted from the register ZIP | Flat column layout for the CSV ingest path (§3.1) |
| `commons-updates.zip` | `registerUpdatesDocument`, misnamed | Is a PDF (`%PDF-1.7`) — proves it is not machine-readable |

**Required test cases** drawn from these:

- Lords page 1 and page 0 return identical payloads → 0-indexed loop is a bug
- Distinct member IDs = 815 = `totalResults`
- A `2025-04-05T00:00:00` deletion never emits `ceased`
- Each cessation-text variant parses: plain, `- notified`, year-only
- An entry with no parenthetical (`Abingdon Software `, trailing space) parses without throwing
- Duplicate corrections (ids 30972/30973) resolve to one row with `is_duplicate_of` set
- A `£` survives ingestion intact
- All four `disclosure_context` states are reachable
