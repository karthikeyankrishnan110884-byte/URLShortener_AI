# ADR-002: Uniqueness enforced by atomic insert, not check-then-insert

**Status:** Accepted · **Date:** 2026-10-10 · **Sign-off:** approved by Karthik, 2026-10-15 (data integrity, contract change)

## Context
Brownfield report: *"two different URLs occasionally get the same code."* The classic cause is
`if (!Exists(code)) Add(code)`: two requests both see "free" and both insert.

## Decision
The repository contract is `TryAddAsync(link) → bool`, which **must be atomic** (`ConcurrentDictionary.TryAdd` in memory;
a `UNIQUE` index + catching the duplicate-key error in SQL). The service retries generation on `false`, and maps an alias conflict to 409.

## Evidence
* `InMemoryLinkRepositoryTests.TryAdd_UnderConcurrentWritesOfSameCode_ExactlyOneWins` (200 parallel inserts → exactly one success).
* `UrlShortenerServiceTests.Create_ConcurrentRequestsWithScriptedCollisions_AllGetDistinctCodes` (50 concurrent creates forced onto one candidate code).
* `Create_WhenGeneratedCodeCollides_RetriesAndSucceeds` / `..._FailsAfterConfiguredAttempts` (deterministic, scripted generator).

## Rejected
A lock around generate+insert: correct on one node, wrong on many, and it serialises all writes.
