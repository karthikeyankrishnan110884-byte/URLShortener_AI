#!/usr/bin/env bash
# Before/after redirect load test. Same simulated 2 ms storage latency for every run, so differences come from
# the brownfield changes (cache-aside + async analytics), not from storage speed.
#   ./scripts/load-test.sh [duration-seconds] [concurrency]
set -euo pipefail
cd "$(dirname "$0")/.."
DURATION=${1:-15}
CONCURRENCY=${2:-64}
PORT=5199
dotnet build -c Release -v q >/dev/null

run() { # label analyticsMode cacheEnabled latencyMs
  ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS="http://localhost:$PORT" \
  Shortener__BaseUrl="http://localhost:$PORT" Logging__LogLevel__Default=Warning \
  RateLimiting__RedirectPermitLimit=0 RateLimiting__CreatePermitLimit=100000 \
  Analytics__Mode="$2" Cache__Enabled="$3" Storage__SimulatedLatencyMs="$4" \
    dotnet src/UrlShortener.Api/bin/Release/net8.0/UrlShortener.Api.dll --contentRoot "$PWD/src/UrlShortener.Api/bin/Release/net8.0" >/dev/null 2>&1 &
  local pid=$!
  for _ in $(seq 1 50); do curl -sf "http://localhost:$PORT/healthz" >/dev/null && break; sleep 0.2; done
  dotnet tests/UrlShortener.LoadTests/bin/Release/net8.0/UrlShortener.LoadTests.dll \
    --base-url "http://localhost:$PORT" --duration "$DURATION" --concurrency "$CONCURRENCY" --label "$1" | grep '^| ' | tail -1 || true
  kill "$pid"; wait "$pid" 2>/dev/null || true
}

echo "| label | requests | rps | p50 ms | p95 ms | p99 ms | max ms | non-302 | clicks recorded / redirects |"
echo "|---|---|---|---|---|---|---|---|---|"
run "before: inline analytics, no cache (2 ms store)" Inline false 2
run "async analytics only (2 ms store)"               Async  false 2
run "after: async analytics + cache (2 ms store)"     Async  true  2
run "after, in-memory store (0 ms)"                   Async  true  0
