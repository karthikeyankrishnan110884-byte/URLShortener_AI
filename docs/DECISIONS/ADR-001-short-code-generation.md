# ADR-001: Short-code generation: crypto-random Base62, 7 characters

**Status:** Accepted · **Date:** 2026-10-10 · **Sign-off:** approved by Karthik, 2026-10-15 (security-relevant)

## Context
Codes must be short, unique and must not be guessable (otherwise anyone can enumerate everyone's links and their analytics).

## Options
| Option | Collision risk | Enumerable? | Scaling | Verdict |
|---|---|---|---|---|
| Counter → Base62 | None | **Yes**: next code is predictable | Needs a central sequence / ID service | Rejected |
| Hash of URL (truncated) | Birthday-bound, plus same URL ⇒ same code | Partly (hash of known URL) | Stateless | Rejected: conflicts with "new code per create" (REQUIREMENTS) |
| **Crypto-random Base62** | 62⁷ ≈ 3.5×10¹². At 10 M links, P(collision per create) ≈ 10⁷/3.5×10¹² ≈ 3×10⁻⁶ | No | Stateless, any node can generate | **Chosen** |

## Decision
`RandomNumberGenerator.GetString(Base62, 7)`. Collisions are handled by atomic insert + retry (ADR-002), so a collision costs one retry,
not a wrong link. `CodeLength` is configurable (5-16). Generated codes equal to reserved words (`swagger`, `healthz`) are skipped.

## Consequences
* With 5 attempts the probability of exhausting retries at 10 M links is ≈ (3×10⁻⁶)⁵: effectively zero; exhaustion maps to 503.
* At ~1 B links, raise `CodeLength` to 8 (2×10¹⁴) or move to a range-allocating ID service (FINAL_SUMMARY "Next").
