# Scenario B: Brownfield (enhance the existing service)

## Incoming requirement
> "Redirects are slow under load and analytics writes are blocking the redirect path. Also, we've had a report that two
> different URLs occasionally get the same code."

## 1. Impact analysis (written and signed off before any code)
| Area | Impact |
|---|---|
| Modules | `UrlShortenerService` (create, resolve), `ILinkRepository` (contract), new `IClickRepository`, new `Services/Analytics/*`, `ServiceCollectionExtensions` (composition), `RedirectController` (UTM/HEAD), `LinksController` (stats via analytics service) |
| APIs | **No breaking change.** Additive fields (`isCustomAlias`, `deactivatedAt`, `lastAccessedAt`, `topSources`…), additive endpoints/headers. `clickCount` becomes eventually consistent (ms) |
| Data flows | Click write moves from request thread → bounded queue → batched background writer. Link reads go through a cache |
| Migration | None in simple mode. SQL: add `UNIQUE(code)` (verify no duplicates first: `SELECT code FROM links GROUP BY code HAVING COUNT(*) > 1`), add `click_daily` aggregate table |
| Backward-compatibility risks | Clients that read `clickCount` immediately after a redirect may see the old value; `Analytics:Mode=Inline` restores synchronous behaviour without a deploy |
| Operational risks | Queue overflow drops events (observable: `/readyz`, `dropped`); multi-node cache staleness ≤ TTL after deactivate |
| Rollback | Each step is its own change: revert the commit, or flip config (`Cache:Enabled=false`, `Analytics:Mode=Inline`) without a code rollback |

## 2. Execution

### Step 1: reproduce the collision bug with a failing test, then fix
* **Root cause pattern:** check-then-insert. `CheckThenInsertLinkRepository` (test double) reproduces it faithfully; a barrier makes the
  interleaving deterministic rather than timing luck.
* `CollisionBugReproductionTests.Reproduction_CheckThenInsert_HandsTheSameCodeToSeveralUrls`: 20 concurrent creates drawing the same
  candidate code → **all 20 are told "created SAME000"**, only one URL is stored. That is the reported bug.
* **Fix:** atomic `TryAddAsync` contract (SQL: unique index + catch duplicate key) with retry in the service.
  `Fix_AtomicTryAdd_HandsOutTheCodeExactlyOnce` → exactly 1 success. Justification: a unique constraint is the only guarantee that
  holds across processes; a lock would only hold on one node and serialise writes (ADR-002).

### Step 2: async, split repositories (enabling refactor, no behaviour change)
`ILinkRepository` / `IClickRepository` became `ValueTask`-based with `CancellationToken`, so a real store can be swapped in.
All pre-existing tests were ported mechanically (sync → `await`) and stayed green: the behaviour-preservation proof.

### Step 3: cache-aside decorator (no service changes)
`CachedLinkRepository` composed in DI: TTL (60 s, capped by link expiry), request coalescing (stampede protection), negative cache (5 s),
eviction on deactivate, size limit. See ADR-005. Integration test `Deactivate_IsVisibleImmediately_DespiteCache`.

### Step 4: analytics off the hot path
`ChannelClickQueue` (bounded, `TryWrite`, never blocks) + `ClickEventProcessor` (`BackgroundService`). See ADR-004.

**What happens on crash:** events still in the queue (≤ `Analytics:QueueCapacity`, default 50 k) are lost; events already written are not.
On graceful shutdown (SIGTERM, Ctrl+C) the processor drains the queue first and logs `drainedOnStop`. When the store is slow or down the
queue fills, new events are dropped and counted, and `/readyz` reports Degraded at 90 %. Redirects are never slowed. That is the
deliberate trade-off. Production: durable broker or outbox.

### Step 5: SRP refactor (behaviour unchanged, tests unchanged in intent)
Extracted `AliasPolicy` (alias rules), `ClickEventFactory` (untrusted input → bounded event), `LinkAnalyticsService` (read side),
`ClickIngestor` (write side). `UrlShortenerService` now only owns lifecycle rules.

### Step 6: load test, and the bug it found
Tool: `tests/UrlShortener.LoadTests` (dependency-free; k6 equivalent in `tests/load/redirect.k6.js`).
Every run: 64 concurrent clients, 20 hot links, 15 s + 3 s warm-up, Release build, **same simulated 2 ms storage round-trip**
(`Storage:SimulatedLatencyMs`) so differences come from the design, not from the in-memory store. After each run the tool sums
`totalClicks` across links and compares with successful redirects.

**First async attempt (one write per event):** the single consumer did ~250 events/s against ~14 k redirects/s:
```
| async analytics only (2 ms store) | 207297 | 13819 | 4.38 | 7.56 | 9.29 | 76.20 | 0 | 4068 / 230987 |
FAIL: errors=0 lostEvents=226919
```
Redirects got faster, but **98 % of click events were dropped**. Fix: batch writes (`IngestBatchAsync`): one counter update per distinct
link + one bulk insert per batch of ≤ 1,000 (task B4b).

**Final results** (`./scripts/load-test.sh 15 64`, 2-vCPU Linux container; load generator on the same host, so read the ratios, not the absolutes):

| label | requests | rps | p50 ms | p95 ms | p99 ms | max ms | non-302 | clicks recorded / redirects |
|---|---|---|---|---|---|---|---|---|
| before: inline analytics, no cache (2 ms store) | 77739 | 5182 | 12.27 | 14.94 | 17.03 | 33.67 | 0 | 88594 / 88594 |
| async analytics only (2 ms store) | 186119 | 12407 | 4.85 | 8.42 | 10.57 | 84.87 | 0 | 204650 / 204650 |
| **after: async analytics + cache (2 ms store)** | 459746 | **30649** | 1.84 | **3.74** | 5.65 | 77.03 | 0 | **482502 / 482502** |
| after, in-memory store (0 ms) | 505624 | 33708 | 1.69 | 3.16 | 5.38 | 98.29 | 0 | 530786 / 530786 |

**Outcome:** p95 **14.9 → 3.7 ms (−75 %)**, throughput **5.2 k → 30.6 k rps (5.9×)**, **zero events lost**, zero errors.
With the cache on, the 2 ms store costs almost nothing (3.7 vs 3.2 ms p95). Max latency spikes (~80-100 ms) are GC and thread-pool
warm-up on a 2-core box sharing the CPU with the load generator.

## 3. Validation
| Check | Evidence |
|---|---|
| Bug reproduced, then fixed | `CollisionBugReproductionTests` (deterministic, 10/10 runs) |
| No behaviour change from refactors | All pre-existing scenarios still pass in the 212-test suite |
| Cache correctness | `CachedLinkRepositoryTests` (6), `HardeningApiTests` (cache + deactivate, negative cache + later alias) |
| Pipeline correctness | `ClickPipelineTests` (drop-when-full never blocks, drain-on-stop, failing batch, batching, readiness) |
| No lost events in happy path | Load test assertion recorded == redirects; CI smoke test fails the build otherwise |
| Gates | format ✅ build 0 warnings ✅ 212 tests ✅ coverage 97.8 % ✅ |
