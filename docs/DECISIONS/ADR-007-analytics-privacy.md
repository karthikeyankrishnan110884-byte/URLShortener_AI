# ADR-007: Analytics privacy: no IP addresses, referrer host only, bounded values

**Status:** Accepted · **Date:** 2026-10-10, extended 2026-10-15

## Decision
* No client IP is stored, raw or hashed (a hash of a 32-bit IPv4 space is reversible by brute force).
* Referrer: **host only** (paths and queries often contain personal data or tokens). Only http/https referrers count.
* User agent: truncated to 256 chars; aggregated into a coarse device class (bot/mobile/tablet/desktop/unknown).
* UTM tags: lower-cased, ≤ 64 chars, restricted charset, else dropped (no free-text injection into dashboards).
* Per-dimension cardinality capped (`MaxDistinctValuesPerDimension`, overflow → `(other)`); raw events capped per link.

## Consequences
No unique-visitor or geo metrics. If marketing needs them: a salted, daily-rotated HMAC of IP+UA (unique visitors) and country-level
GeoIP at ingest, discarding the IP. That needs a privacy review first (see C-ambiguous.md, open question 3).
