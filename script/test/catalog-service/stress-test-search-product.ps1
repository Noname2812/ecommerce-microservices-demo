<#
.SYNOPSIS
  Lightweight throughput test for Catalog GET /products?q=... (no k6 required).
  Requires PowerShell 7+ (ForEach-Object -Parallel).

.PARAMETER BaseUrl
  Catalog API root, e.g. http://localhost:5025

.PARAMETER Query
  Search string (seed data includes "Seed Product N" -> try "Seed").

.PARAMETER Concurrency
  ThrottleLimit for parallel workers.

.PARAMETER TotalRequests
  Total GET requests to send.

.EXAMPLE
  .\stress-test-search-product.ps1 -BaseUrl http://localhost:5025 -Query Seed -Concurrency 50 -TotalRequests 5000
#>
param(
    [string]$BaseUrl = "http://localhost:5025",
    [string]$Query = "Seed",
    [int]$Concurrency = 30,
    [int]$TotalRequests = 2000
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Error "PowerShell 7+ is required (uses ForEach-Object -Parallel)."
}

$root = $BaseUrl.TrimEnd("/")
$q = [System.Uri]::EscapeDataString($Query)
$uri = "$root/api/v1/catalog/products?q=$q&page=1&pageSize=20&sort=relevance"

Write-Host "URI: $uri"
Write-Host "Concurrency=$Concurrency TotalRequests=$TotalRequests"

$sw = [System.Diagnostics.Stopwatch]::StartNew()

$results = 1..$TotalRequests | ForEach-Object -Parallel {
    try {
        $r = Invoke-WebRequest -Uri $using:uri -Method Get -TimeoutSec 60 -SkipHttpErrorCheck
        if ($r.StatusCode -ne 200) { 1 } else { 0 }
    }
    catch {
        1
    }
} -ThrottleLimit $Concurrency

$sw.Stop()
$errors = ($results | Measure-Object -Sum).Sum
$elapsed = [Math]::Max($sw.Elapsed.TotalSeconds, 0.001)
$rps = [Math]::Round($TotalRequests / $elapsed, 2)
Write-Host "Done in $($sw.Elapsed.TotalSeconds.ToString('F2')) s -> ~$rps req/s (http errors+exceptions=$errors)"
