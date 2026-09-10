# WhatTheyOwn

A public, searchable record of the **declared shareholdings** of UK parliamentarians — both the House of Commons and the House of Lords — showing current declarations and the history of declarations no longer held.

**Status:** pre-implementation. API research complete; ingestion not yet built.

---

## What this is, precisely

Not "what MPs own". The UK has no equivalent of the US STOCK Act — there is no transaction reporting, no amounts, no share counts, no prices. What exists is a register of interests above a threshold, and this site publishes that register in a form you can search by person and by company.

The distinction matters enough that it shapes the whole project, so it is worth being blunt about the limits:

- **Commons** — a shareholding is registrable if it exceeds 15% of issued share capital, or is valued over £70,000.
- **Lords** — registrable if it amounts to a controlling interest, or is valued over £100,000, with separate provisions for private equity and corporate debt.
- **Collective investment vehicles** — unit trusts, investment trusts, index funds — are generally **not** registrable. Someone with millions in trackers has a blank page.
- **Blind trusts** are entirely exempt.
- No valuations are published. Members are instructed not to state amounts or percentages.

A member with no entries has not declared anything registrable. That is not the same as owning nothing, and the site says so on every profile.

## Coverage

|  | Records | Live | History reaches |
|---|---|---|---|
| **Lords** (Categories 2(a)–(d)) | 5,422 | 2,136 | 2010 |
| **Commons** (Category 7) | 193 | 182 | March 2024 |

The Lords carries roughly 28× the Commons shareholding data, and is where declared holdings in listed companies actually appear — around 80% of Commons entries are at the 15% control threshold, meaning an MP's own company rather than a portfolio.

Northern Ireland postcodes are excluded from postcode lookup for licensing reasons; NI members are otherwise fully covered.

---

## How it works

```
Parliament Interests API (Commons) ─┐
Lords Register  (Members API)       ─┼─► .NET 8 ingestor ──► R2 (raw archive)
Companies House · Wikidata          ─┘   (GitHub Actions, 6h)      │
                                                                    ▼
                                                        D1 (append-only event log)
                                                                    │
                                                          Astro static build
                                                                    │
                                              Cloudflare Workers Static Assets
```

Two things drive the design.

**The archive is the product.** Parliament's registers are pruned — entries are removed once they cease, and the Commons API only reaches back to March 2024. Anything created and deleted between two ingestion runs is gone permanently. So the ingestor writes every raw API response to R2, untouched, before parsing anything.

**Nothing runs at request time.** No database queries, no API calls, no server rendering. Every page is computed during the build and served as a static file.

---

## Repository

| Path | |
|---|---|
| `ARCHITECTURE.md` | Domain rules, verified API behaviour, schema. **Read before writing code.** |
| `CLAUDE.md` | Agent operating manual — hard rules, conventions, commands |
| `DECISIONS.md` | Rejected alternatives and deferred ideas |
| `fixtures/` | Committed API responses — evidence and test data |
| `src/Ingestor/` | .NET 8 console app, runs on a 6-hourly cron |
| `src/ShardBuilder/` | ONS postcode data → static shards, quarterly |
| `src/Web/` | Astro + Svelte, static output |
| `tests/` | xUnit |

## Getting started

```bash
dotnet test                                       # runs against fixtures/, no network
dotnet run --project src/Ingestor -- --dry-run    # fetch and diff, write nothing
```

Full command list in `ARCHITECTURE.md` §15.

Secrets required for a live run: `CF_API_TOKEN`, `CF_ACCOUNT_ID`, `CH_API_KEY`.

---

## Licence and attribution

**Code** is MIT licensed — see `LICENSE`.

**Data is not.** It carries its own terms and cannot be relicensed:

- Register and member data — **Open Parliament Licence**
- Companies House data — **Open Government Licence**
- Postcode data — **OS OpenData**, with Ordnance Survey, Royal Mail and National Statistics Crown copyright conditions

This applies to `fixtures/` as much as to the live site. Full statements are on the `/attributions` page.

## Corrections

If a declaration is shown inaccurately, the correction process is on the site's `/about` page. Corrections are recorded as new events with their own source and timestamp — the original observation is retained rather than overwritten, so the record of what was published and when stays intact.

## Contributing

The API quirks that took the longest to find are documented in `ARCHITECTURE.md` §3 — Lords pagination being 1-indexed despite the docs, the 20-item cap on both APIs, the Commons CSV export being a ZIP. If a response shape differs from what §3 describes, please open an issue rather than adapting the parser silently; it means either the API changed or the request is wrong.
