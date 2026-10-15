# Risks, trade-offs, failure scenarios

| Risk | Likelihood | Impact | Mitigation in prototype | Production follow-up |
|---|---|---|---|---|
| Data lost on restart (in-memory) | Certain | High | Documented. Interface isolates storage | SQL/Postgres with unique index on `Code` |
| Phishing / malware targets | Medium | High | Scheme allowlist only | Threat-intel / Safe Browsing check, report-abuse flow |
| SSRF via DNS rebinding (hostname → private IP) | Low | Medium | IP literals and local TLDs blocked | Resolve and re-check at create time. The service never fetches the target |
| No auth: anyone can deactivate any link | High | Medium | Out of scope, flagged | API keys / OAuth, owner on link |
| Code enumeration | Low | Medium | Crypto-random 7-char Base62 | Longer codes for sensitive links, 404 rate limiting |
| Create flood | Medium | Medium | Per-IP fixed window (429) | Distributed limiter (Redis), WAF |
| Rate limit keyed on IP behind proxy | Medium | Medium | Not addressed | `ForwardedHeaders` with known proxies |
| Unbounded click events in memory | Medium | Medium | UA truncated to 256 chars | Persist and roll up daily aggregates |
| Hot key / cache stampede | Low (prototype) | Medium | N/A | Cache-aside with request coalescing |
| Swagger exposed in all environments | Certain | Low | Prototype convenience | Development only |
| Expiry clock skew across nodes | Low | Low | Single node, `TimeProvider` | UTC everywhere, NTP |

**Trade-offs accepted:** 302 over 301 (accuracy over bandwidth). Exceptions for domain flow (clarity over micro-performance).
Synchronous click recording (simplicity. It's a memory write).
