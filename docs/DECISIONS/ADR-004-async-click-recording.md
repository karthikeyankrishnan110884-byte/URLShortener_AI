# ADR-004: Click analytics off the redirect path (bounded Channel + BackgroundService, batched writes)

**Status:** Accepted · **Date:** 2026-10-15 · **Sign-off:** approved by Karthik, 2026-10-15 (data-flow change)

## Context
Brownfield report: *"analytics writes are blocking the redirect path."* With a 2 ms store, inline recording gave p95 14.9 ms at 5.2 k rps.

## Decision
* `IClickRecorder` on the hot path. Default `ChannelClickQueue`: a **bounded** `Channel<ClickEvent>` (`TryWrite`, never blocks).
* `ClickEventProcessor : BackgroundService` drains everything available up to `MaxBatchSize` and writes **one batch**:
  one counter update per distinct link + one bulk insert (`IClickIngestor.IngestBatchAsync`).
* `Analytics:Mode=Inline` keeps the synchronous path for comparison and deterministic tests (same API contract, proven by `StartupTests`).

## Why batching (found, not assumed)
The first async version wrote one event per round-trip. The load test showed the single consumer managing ~250 events/s against
~14 k redirects/s: the queue filled and **226,919 of 230,987 events were dropped**. Batching fixed it: 0 lost at 30 k+ rps.

## Failure semantics (explicit trade-off: availability over analytics completeness)
| Situation | Behaviour |
|---|---|
| Happy path | Every event recorded (load test asserts recorded == redirects) |
| Queue full (store down/slow) | Event dropped, `dropped` counter incremented, `/readyz` reports Degraded at 90 % |
| Batch write throws | Batch counted as `failed`, logged, processor continues |
| Graceful shutdown | Queue drained before exit (logged `drainedOnStop`) |
| Process crash | Up to `QueueCapacity` queued events lost |

Production: durable queue (Kafka/SQS/Event Hubs) or outbox, retry with backoff, dead-letter.

## Consequences
`clickCount` / `lastAccessedAt` / stats are **eventually consistent** (milliseconds). Integration tests poll (`Eventually`) instead of sleeping.
