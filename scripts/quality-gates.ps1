<#
.SYNOPSIS
  Runs every quality gate from the master prompt (rule 5) on Windows. Mirrors scripts/quality-gates.sh used by CI.
  format -> build (warnings as errors) -> tests + coverage >= MinLineCoverage -> vulnerable packages
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\quality-gates.ps1
#>
param([double]$MinLineCoverage = 85)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$out = 'artifacts/coverage'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

function Invoke-Gate([string]$Name, [scriptblock]$Command) {
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Gate failed: $Name" }
}

Invoke-Gate '1/5 restore' { dotnet restore }
Invoke-Gate '2/5 format (verify no changes)' { dotnet format --verify-no-changes --no-restore }
Invoke-Gate '3/5 build (Release, warnings as errors)' { dotnet build --no-restore -c Release }
Invoke-Gate '4/5 test + coverage' { dotnet test --no-build -c Release --collect:"XPlat Code Coverage" --results-directory $out }

$report = Get-ChildItem $out -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
[xml]$xml = Get-Content $report.FullName
$line = [double]$xml.coverage.'line-rate' * 100
$branch = [double]$xml.coverage.'branch-rate' * 100
Write-Host ("Line coverage: {0:N1}%  Branch coverage: {1:N1}%  (gate: line >= {2}%)" -f $line, $branch, $MinLineCoverage)
if ($line -lt $MinLineCoverage) { throw "Line coverage below $MinLineCoverage%" }

Write-Host '=== 5/5 vulnerable packages ===' -ForegroundColor Cyan
$vuln = dotnet list package --vulnerable --include-transitive 2>&1 | Out-String
Write-Host $vuln
if ($vuln -match 'has the following vulnerable packages') { throw 'Vulnerable packages found' }

Write-Host 'All quality gates passed.' -ForegroundColor Green
