# ADR-006: In-memory storage behind async repository interfaces

**Status:** Accepted · **Date:** 2026-10-10, revised 2026-10-15

## Decision
"Simple mode": `InMemoryLinkRepository` and `InMemoryClickRepository` behind `ILinkRepository` / `IClickRepository`.
**Revised:** the interfaces became async (`ValueTask`, `CancellationToken`) and links and clicks were split, so a SQL/Redis
implementation can be dropped in without changing the contract, and each store can scale independently.

## Mapping to a real database
| In memory | SQL |
|---|---|
| `TryAdd` | `INSERT` with `UNIQUE(code)`; duplicate-key → `false` |
| `UpdateAsync` | `UPDATE links SET is_active = 0, deactivated_at = @t WHERE code = @c` |
| click aggregates | `click_daily(code, day, count)` + dimension tables, updated by the batch writer |
| bounded recent events | partitioned `clicks` table with retention |

## Consequences
Data is lost on restart (documented in RISKS). `Storage:SimulatedLatencyMs` (dev only) emulates a network store for performance work.
