#!/usr/bin/env bash
# Runs every quality gate from the master prompt (rule 5). Used by CI; mirrors scripts/quality-gates.ps1.
#   format → build (warnings as errors) → tests + coverage ≥ MIN_COVERAGE → vulnerable packages
set -euo pipefail
cd "$(dirname "$0")/.."
MIN_COVERAGE=${MIN_COVERAGE:-85}
OUT=artifacts/coverage
rm -rf "$OUT"

echo "=== 1/5 restore ==="
dotnet restore

echo "=== 2/5 format (dotnet format --verify-no-changes) ==="
dotnet format --verify-no-changes --no-restore

echo "=== 3/5 build (Release, nullable on, TreatWarningsAsErrors) ==="
dotnet build --no-restore -c Release

echo "=== 4/5 test + coverage ==="
dotnet test --no-build -c Release --collect:"XPlat Code Coverage" --results-directory "$OUT" --logger "trx;LogFileName=results.trx"
REPORT=$(find "$OUT" -name coverage.cobertura.xml | head -1)
LINE=$(grep -o 'line-rate="[0-9.]*"' "$REPORT" | head -1 | grep -o '[0-9.]*')
BRANCH=$(grep -o 'branch-rate="[0-9.]*"' "$REPORT" | head -1 | grep -o '[0-9.]*')
LINE_PCT=$(awk "BEGIN { printf \"%.1f\", $LINE * 100 }")
BRANCH_PCT=$(awk "BEGIN { printf \"%.1f\", $BRANCH * 100 }")
echo "Line coverage: ${LINE_PCT}%  Branch coverage: ${BRANCH_PCT}%  (gate: line >= ${MIN_COVERAGE}%)"
awk "BEGIN { exit !($LINE * 100 >= $MIN_COVERAGE) }" || { echo "FAIL: line coverage below ${MIN_COVERAGE}%"; exit 1; }

echo "=== 5/5 vulnerable packages (needs nuget.org) ==="
VULN=$(dotnet list package --vulnerable --include-transitive 2>&1)
echo "$VULN"
if echo "$VULN" | grep -q "has the following vulnerable packages"; then echo "FAIL: vulnerable packages found"; exit 1; fi

echo "All quality gates passed."
