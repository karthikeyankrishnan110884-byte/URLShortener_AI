<#
.SYNOPSIS
  Before/after redirect load test on Windows (mirrors scripts/load-test.sh).
  Every run uses the same simulated storage latency, so differences come from the brownfield changes.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\load-test.ps1 -Duration 15 -Concurrency 64
#>
param([int]$Duration = 15, [int]$Concurrency = 64, [int]$Port = 5199)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet build -c Release -v q | Out-Null

function Invoke-Run([string]$Label, [string]$Mode, [string]$Cache, [int]$LatencyMs) {
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $env:ASPNETCORE_URLS = "http://localhost:$Port"
    $env:Shortener__BaseUrl = "http://localhost:$Port"
    $env:Logging__LogLevel__Default = 'Warning'
    $env:RateLimiting__RedirectPermitLimit = '0'
    $env:RateLimiting__CreatePermitLimit = '100000'
    $env:Analytics__Mode = $Mode
    $env:Cache__Enabled = $Cache
    $env:Storage__SimulatedLatencyMs = "$LatencyMs"
    $api = Start-Process dotnet -ArgumentList 'src/UrlShortener.Api/bin/Release/net8.0/UrlShortener.Api.dll','--contentRoot',"$PWD/src/UrlShortener.Api/bin/Release/net8.0" -PassThru -WindowStyle Hidden
    try {
        for ($i = 0; $i -lt 50; $i++) {
            try { Invoke-WebRequest "http://localhost:$Port/healthz" -UseBasicParsing | Out-Null; break } catch { Start-Sleep -Milliseconds 200 }
        }
        dotnet tests/UrlShortener.LoadTests/bin/Release/net8.0/UrlShortener.LoadTests.dll `
            --base-url "http://localhost:$Port" --duration $Duration --concurrency $Concurrency --label $Label |
            Select-String '^\| ' | Select-Object -Last 1 | ForEach-Object { $_.Line }
    }
    finally {
        Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue
    }
}

'| label | requests | rps | p50 ms | p95 ms | p99 ms | max ms | non-302 | clicks recorded / redirects |'
'|---|---|---|---|---|---|---|---|---|'
Invoke-Run 'before: inline analytics, no cache (2 ms store)' Inline false 2
Invoke-Run 'async analytics only (2 ms store)'               Async  false 2
Invoke-Run 'after: async analytics + cache (2 ms store)'     Async  true  2
Invoke-Run 'after, in-memory store (0 ms)'                   Async  true  0
