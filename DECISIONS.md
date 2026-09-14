# Decisions

Why things are the way they are, and why the obvious alternatives were rejected. Check here before proposing a different approach — it has probably been considered.

---

## Architecture

### Static build, not Cloudflare KV

**Rejected.** KV was in an earlier design and reconsidered twice.

It adds a service, a namespace, a credential and a write quota without removing any work — you still need the ingestor, the diff, the payload assembly and the trigger. And a KV read is itself a network call, so pages end up slower than files already sitting on the CDN.

At ~650–1,500 keys the usual objection (long-tail cold reads) is minor, so this is "doesn't earn its place" rather than "broken".

Free tier is roughly 1,000 writes/day shared across writes, deletes and lists, and bulk REST writes bill per key-value pair — a full republish would exceed it several times over.

**Revisit when:** user watchlists, email alerts, sub-minute publishing, or page counts high enough that rebuilds hurt. Keep page assembly behind a single `getMemberPage(slug)` so swapping the implementation is an afternoon.

### GitHub Actions cron, not AWS Lambda

**Rejected**, though nothing is wrong with Lambda and the free tier covers this easily.

- Most of the work is parsing inconsistent free text. `dotnet run -- --dry-run --member 4689` with a breakpoint beats packaging and deploying.
- Lambda's 15-minute ceiling. The Commons register-zip backfill and Companies House enrichment at 600 req/5min will exceed it. A GitHub Actions job runs up to 6 hours.
- One fewer cloud account, credential set, bill and place to look when something breaks. Ingest and build logs sit in the same run.

### D1, not DynamoDB

**Rejected.** An earlier design scanned the whole table into memory and did everything in LINQ — i.e. used a distributed NoSQL store as a file.

The queries here are relational (member → companies, company → members), and D1 is a SQLite file you can download, diff and back up.

### Workers Static Assets, not Pages

Cloudflare's docs tell you not to use Workers Sites for new projects and point at Static Assets; new investment goes to Workers rather than Pages. Pages isn't deprecated and still gets fixes, but Workers is the recommended target for a new project and leaves a clean path to a dynamic route later without a second deployment.

### Generic host, not plain `Program.cs`

Plain wiring was the initial recommendation — five lines of manual construction, no container, for a process that starts, does one job and exits. Chosen against in favour of the host for `IHttpClientFactory` with Polly, config layering, and an easier path if the ingestor ever moves off Actions.

### Retry and timeout only, no circuit breaker

**Rejected:** `AddStandardResilienceHandler`, the recommended default.

It bundles five strategies — rate limiter, total timeout, retry, circuit breaker, per-attempt timeout — and the breaker is wrong for this workload. A Lords run is 41 sequential requests that must *all* land: §7 aborts the run outright when distinct member count ≠ `totalResults`. A breaker that opens partway through converts a transient wobble into an aborted run, which is the same outcome as having fetched nothing, and it does so based on request history rather than on the request in front of it. Breakers earn their place by protecting a struggling dependency from a caller hammering it concurrently. This caller is one request at a time, 4 times a day.

The bundled options also validate against each other, so the knobs are not independent. Setting a 30s per-attempt budget alone throws at client-construction time:

```
OptionsValidationException: The sampling duration of circuit breaker strategy needs to be at
least double of an attempt timeout strategy's timeout interval, in order to be effective.
Sampling Duration: 30s, Attempt Timeout: 30s
```

— which means inventing a sampling window and a total-request timeout to satisfy a breaker that was not wanted.

`HostConfiguration.AddIngestorHttpClients` therefore builds the pipeline explicitly: retry outermost (3 attempts, exponential, jittered), 30s timeout innermost so every attempt gets its own budget, nothing else. `HttpClient.Timeout` is set to `InfiniteTimeSpan` so its 100s default cannot fire mid-sequence and surface as a bare `TaskCanceledException` naming no attempt.

**Revisit when:** something here makes genuinely concurrent calls. Companies House enrichment at 600 req/5min (Phase 5) is the first candidate, and it should get its own pipeline rather than widening this one — its failure modes and its rate limit are not the Parliament APIs'.

### Parliament base URLs must end in a slash

Validated at startup by `ParliamentOptionsValidator`, which looks pedantic and is not.

`HttpClient` resolves a relative request path via `new Uri(baseAddress, requestUri)`, and RFC 3986 resolution replaces the last segment of the base when that segment is not followed by a slash. Verified:

```
base https://members-api.parliament.uk/api   + LordsInterests/Register
  -> https://members-api.parliament.uk/LordsInterests/Register        ← "/api" gone
base https://members-api.parliament.uk/api/  + LordsInterests/Register
  -> https://members-api.parliament.uk/api/LordsInterests/Register
```

Both truncated forms return **404**, so this is not silent data corruption — but it is a failure that arrives mid-run, attributed to the endpoint rather than to the setting, and 404 is not transient so the retry pipeline will not mask it either. Catching it at startup costs one string comparison and names the setting. The Commons base is worse for being subtler: `.../api/v1` loses only `v1`, leaving a URL that still looks right.

The same rule cuts the other way and the validator cannot see it: a **leading** slash on the relative path discards the base path even when the base is correct. Request paths are written without one.

```
base https://members-api.parliament.uk/api/  + /LordsInterests/Register
  -> https://members-api.parliament.uk/LordsInterests/Register        ← "/api" gone again
```

### Monorepo, not separate repos

The mixed toolchain (Rider for .NET, VS Code for Astro) is not a real constraint — each IDE opens its own subtree.

What binds them: the CI job is one job, with the ingestor's output on disk when the build runs; the data contract is a file shape written by C# and read by TypeScript; and nothing deploys independently. A data change and a template change both produce exactly one artifact.

**If split anyway:** publish the payload as a release artifact rather than letting the frontend query D1, and version the payload's types in a shared package. Fold `ShardBuilder` in rather than making a third repo.

---

## Data scope

### Shareholdings only, both Houses

An earlier design covered all 12 Commons categories. Narrowed to shareholdings for v1.

Both Houses from the start, not Commons-first: the Lords carries ~28× the shareholding data and is where the listed equity lives (§2.9).

### Northern Ireland postcodes excluded

BT postcodes are present in ONSPD/NSPL but licensed separately. ONS issues only a **Northern Ireland End User Licence for internal business use only**; anything else needs a licence from Land and Property Services. "Internal business use" is narrower than "non-commercial" — a public website serving BT shards is neither.

Routing via postcodes.io does not change this. The licence attaches to the data, not the delivery route.

The project may become commercial, so the non-commercial reading is not available either.

**To add later:** licence the Central Postcode Directory from LPS, as a deliberate purchase rather than a retrofit.

### ICB and GICS not used

Licensed commercial products. Sector classification uses an internally maintained SIC-derived taxonomy, kept in version control so it can be corrected and re-derived.

---

## Deferred

### Looking through investment vehicles to what they hold

**Not in v1.**

Many declarations name investment vehicles rather than operating companies — `Ovington Investments Ltd (diversified investments)`, `Winterton Capital Limited (investment company)`, `Warburg Pincus No X (international private equity fund)`. The obvious question is what those hold.

**Mostly it is not obtainable:**

- Small companies file micro-entity or filleted accounts — a balance sheet, no P&L, no investment schedule. That is the design of the small-company regime, not an oversight.
- Companies House has no "what does this company own" endpoint. PSC points upward at controllers, Officers sideways at people. Nothing points downward at holdings.
- Overseas funds have no UK disclosure obligation at all.

**Three partial routes that do work:**

- **PSC bulk data.** Companies House publishes a full PSC snapshot as a downloadable bulk file. Indexed by corporate PSC, it finds companies controlled by a declared company — one level down the group. Verify current format and cadence before relying on it.
- **TR-1 major shareholding notifications.** Crossing 3% of a UK listed company triggers a notification published via RNS. Only catches large positions, but those are the interesting ones.
- **Full accounts.** Companies above the small threshold file notes on subsidiary and associate undertakings with names and percentages, as iXBRL. Applies to few targets, but rich where it does.

**Why it is deferred rather than scheduled:** this shifts the product from "here is what they declared" to "here is what we inferred they indirectly own". Error rates rise sharply — a wrong PSC match or a stale filing produces a false claim about a named person's finances. It also weakens the GDPR position materially: compiling a profile beyond the public register is a much harder legitimate-interests argument than republishing the register, particularly once commercial.

**If built:** keep it strictly separate — a clearly labelled "corporate structure" section sourced to Companies House with filing dates shown, never blended into declared holdings. After launch, not in v1.

### Pre-March-2024 Commons history

Would require scraping `publications.parliament.uk`. The Commons is 193 records against the Lords' 5,400, and most Commons declarations are MPs' own companies rather than portfolios (§2.10). Low value. State the coverage window on the site instead.

---

## Naming

Domain is `whattheyown.com`, already owned.

Descriptive alternatives (`PoliticianStockHoldings.com`, `ParliamentStocks`) were considered and rejected: the exact-match-domain SEO argument stopped working with Google's 2012 EMD update, they are awkward to say aloud, "stocks" is American usage where UK sources say *shares*, and "holdings" overpromises given the site publishes declared above-threshold interests rather than portfolios. `ParliamentStocks` also breaks the international path.

Descriptive domains may 301 in. The brand stays.

International expansion goes in the path — `/uk/...` — not a rebrand.

**Prior art:** Ticker has matched declared interests to LSE-listed companies (334 active declarations, 109 members, 197 issuers), organised by company. By member is the differentiator.
