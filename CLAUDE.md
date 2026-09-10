# WhatTheyOwn — agent instructions

A public record of **declared shareholdings** of UK parliamentarians, both Houses. Static site, no runtime data access.

**Read `ARCHITECTURE.md` before writing code.** It carries domain rules and verified API behaviour that are not guessable. `DECISIONS.md` records why rejected options were rejected — check it before proposing an alternative approach.

---

## Current status

**Phase 1 — repo setup.** Phase 0 (API probing) is complete; findings are in `ARCHITECTURE.md` §2–3, evidence in `fixtures/`.

Next: Phase 2, the archive-only ingestor. Nothing else matters until that is running on a cron.

---

## Never do these

Each one produces output that looks correct and is wrong.

1. **Never `UPDATE` or `DELETE` rows in `declaration_events`.** It is append-only. Current state is a projection over it.
2. **Never overwrite `raw_text`**, however confident a company match is.
3. **Never recompute a slug from a name.** Generate once, store, reuse. Retired slugs go in `member_slugs` and 301.
4. **Never fetch Lords pages 0-indexed.** `page` is 1-indexed despite the docs saying 0. Loop 1→41. A 0-indexed loop silently drops the last 15 peers.
5. **Never continue a run when distinct member count ≠ `totalResults`.** Abort. A partial dataset written to R2 is worse than no run.
6. **Never write to `fixtures/`.** It is committed evidence for the claims in §3, not scratch space.
7. **Never invent a company match to avoid a null.** Unresolved goes to `resolution_queue`.
8. **Never write copy saying a member "owns" anything.** They *declared* an interest. See §2.1.
9. **Never delete a member who left Parliament.** Set `is_current = false`.
10. **Never fetch incrementally from either API.** A removal is an absence; only a full-set diff reveals it.
11. **Never treat a Lords deletion at exactly `2025-04-05T00:00:00` as a cessation.** See §2.7.
12. **Never add browser storage (`localStorage`/`sessionStorage`) to the frontend.** Not supported.

---

## Conventions

**Language and runtime:** C#, .NET 8. Nullable reference types enabled. `TreatWarningsAsErrors` on.

**Host:** `Microsoft.Extensions.Hosting` generic host with DI. Register clients as typed `HttpClient`s via `IHttpClientFactory` with a Polly retry pipeline (3 attempts, exponential backoff). Config binds from `appsettings.json` plus environment variables, env taking precedence.

**JSON:** `System.Text.Json` only. No Newtonsoft. `JsonSerializerOptions` with `PropertyNameCaseInsensitive = true` held as a static singleton.

**Encoding:** UTF-8 explicitly on all reads. Never trust console output — Windows terminals render UTF-8 as CP1252 and make `£` look broken when it isn't. Assert on a literal `£` in tests.

**Tests:** xUnit. Test doubles are hand-written fakes implementing the client interfaces and reading from `fixtures/`. No mocking framework.

**Interfaces:** every API client gets one (`ILordsRegisterClient`, `ICommonsInterestsClient`, `IArchiveWriter`) so tests can substitute fixtures.

**Frontend:** Astro `output: 'static'`, Svelte islands only for search and tab filtering. No adapter, no `main` in `wrangler.toml`.

**Naming:** `commons` / `lords` lowercase in data. Category IDs are integers, never strings. Never conflate an API `id` with its `number`.

---

## Layout

```
/src/Ingestor/            .NET 8 console app (generic host)
/src/ShardBuilder/        .NET 8 console app (ONS NSPL → postcode shards)
/src/Web/                 Astro + Svelte
/tests/Ingestor.Tests/    xUnit
/fixtures/                committed API responses — read-only
/data/                    build payload, generated, gitignored
/.github/workflows/
```

---

## Commands

```bash
# Ingestor
dotnet run --project src/Ingestor                      # full run
dotnet run --project src/Ingestor -- --dry-run         # fetch + diff, write nothing
dotnet run --project src/Ingestor -- --member 4689     # single member (Lord Agnew)
dotnet run --project src/Ingestor -- --export-only     # D1 → data/*.json, no fetch

# Tests
dotnet test

# D1
wrangler d1 execute whattheyown --file=migrations/0001_init.sql
wrangler d1 execute whattheyown --command="SELECT COUNT(*) FROM declaration_events"

# Frontend
cd src/Web && npm run build && npx wrangler deploy
find dist -type f | wc -l                              # must stay under 20,000
```

---

## Secrets

`CF_API_TOKEN`, `CF_ACCOUNT_ID`, `CH_API_KEY`. GitHub repo secrets in CI, user secrets locally. Never commit them, never log them.

---

## Working style

- Scope work to one phase step at a time. `ARCHITECTURE.md` §11 lists them with acceptance criteria.
- When an API response shape differs from §3, **stop and report** rather than adapting silently. §3 is verified against live responses; a mismatch means either the API changed or the request is wrong, and both need a human.
- Log unrecognised field names as warnings. Never drop them silently.
- Prefer adding a fixture and a test over manual verification.
