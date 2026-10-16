# ADR-005: Cache-aside `CachedLinkRepository` decorator, TTL + invalidation

**Status:** Accepted · **Date:** 2026-10-15

## Decision
`CachedLinkRepository : ILinkRepository` wraps the real store and is composed in the DI root, so **no service changed**.
* Hit → memory. Miss → **one load per key at a time** (in-flight `Lazy<Task>` map: request coalescing prevents stampedes on hot keys).
* TTL `Cache:LinkTtlSeconds` (60 s), and an entry never outlives the link's own `expiresAt`.
* `UpdateAsync` (deactivate) evicts the entry. Unknown codes are negatively cached for `NotFoundTtlSeconds` (5 s) and cleared on add.
* `IMemoryCache` with `SizeLimit` bounds memory. It ships in the ASP.NET Core shared framework: no new package.

## Correctness notes
* Expiry is enforced by the service on every request from the entity's `ExpiresAt`; the cache can never make an expired link redirect.
* Multi-node: invalidation is local, so another node can serve a deactivated link for up to the TTL. Accepted for the prototype;
  production uses Redis pub/sub or a short TTL for deactivation-sensitive tenants.
* Known edge: a load that started before an alias was created can re-insert a negative entry; bounded by the 5 s negative TTL.

## Evidence
`CachedLinkRepositoryTests` (hits, TTL, link-expiry cap, invalidation, negative cache, 50-way stampede → 1 load) and the load test
(p95 8.4 → 3.7 ms with a 2 ms store).
