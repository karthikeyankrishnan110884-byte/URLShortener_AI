# ADR-010: Idempotent create via `Idempotency-Key`

**Status:** Accepted · **Date:** 2026-10-15 · **Sign-off:** approved by Karthik, 2026-10-15 (additive API change)

## Context
NFR: idempotency. `POST` is not idempotent, and a client retrying after a timeout would create a second link.

## Decision
Optional `Idempotency-Key` header (≤ 64 chars). The store records key → (SHA-256 fingerprint of the normalised body, code), TTL 24 h.
| Situation | Response |
|---|---|
| First request | 201, link created |
| Same key + same body | 201, **same** link, `Idempotent-Replayed: true` |
| Same key + different body | 422 |
| Same key while the first is in flight | 409 (retry later) |
| First request failed | Key released, so the retry runs normally |

Without the header, behaviour is unchanged (every POST creates a new link, by decision in REQUIREMENTS).
Production: Redis `SET NX PX` or a SQL table keyed by (client, key).
