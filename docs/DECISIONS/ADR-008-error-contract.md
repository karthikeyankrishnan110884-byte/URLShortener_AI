# ADR-008: Domain exceptions → RFC 7807 via an MVC exception filter

**Status:** Accepted · **Date:** 2026-10-10 · **Sign-off:** approved by Karthik, 2026-10-15 (public API contract)

## Decision
Services throw typed `DomainException`s; `DomainExceptionFilter` maps them to `application/problem+json` with one table
(400/404/409/410/422/503). Every problem carries `traceId` (W3C) and `requestId` (echo of `X-Request-ID`). Rate-limit rejections
are also problem+json with `Retry-After`. Unknown exceptions fall through to the global handler: generic 500, no internals.

## Why a filter, not `IExceptionHandler`
The first version used the global handler, which logs every handled exception at Error level, so normal 404/409/410 outcomes polluted
error logs. Revised during T2 (see AI_LOG).
