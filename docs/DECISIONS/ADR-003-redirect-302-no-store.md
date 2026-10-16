# ADR-003: 302 Found + `Cache-Control: no-store` for redirects

**Status:** Accepted · **Date:** 2026-10-10

## Context
301 is cached by browsers and proxies, often indefinitely.

## Decision
302 with `no-store`. Every visit reaches the service, so clicks are counted and deactivation, expiry and denylisting take effect immediately.
`HEAD /{code}` returns the same 302 **without** counting a click (link checkers and unfurlers).

## Consequences
One extra request per repeat visit. Mitigated by the cache-aside decorator (ADR-005); a CDN edge redirect is the production step.
