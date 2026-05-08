param(
    [string]$MigrationName = "InitialCreate",
    [switch]$SkipDelete
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$servicesRoot = Join-Path $repoRoot "src/Services"

if (-not (Test-Path $servicesRoot)) {
    throw "Cannot find services folder at: $servicesRoot"
}

Write-Host "==> Repo root: $repoRoot"
Write-Host "==> Services root: $servicesRoot"
Write-Host "==> Migration name: $MigrationName"
Write-Host ""

$serviceDirectories = Get-ChildItem -Path $servicesRoot -Directory | Sort-Object Name
$processedCount = 0
$skippedCount = 0

foreach ($serviceDirectory in $serviceDirectories) {
    $serviceName = $serviceDirectory.Name
    Write-Host ">>> Service: $serviceName"

    $persistenceProject = Get-ChildItem -Path $serviceDirectory.FullName -Filter "UrbanX.*.Persistence.csproj" -File -Recurse | Select-Object -First 1

    if (-not $persistenceProject) {
        Write-Host "    - Skip: no Persistence project found."
        $skippedCount++
        Write-Host ""
        continue
    }

    $persistenceDirectory = Split-Path -Parent $persistenceProject.FullName
    $migrationsDirectory = Join-Path $persistenceDirectory "Migrations"

    if (-not $SkipDelete) {
        if (Test-Path $migrationsDirectory) {
            Remove-Item -Path $migrationsDirectory -Recurse -Force
            Write-Host "    - Deleted: $migrationsDirectory"
        } else {
            Write-Host "    - Migrations folder not found, skip delete."
        }
    } else {
        Write-Host "    - Skip delete enabled."
    }

    Write-Host "    - Running: dotnet ef migrations add $MigrationName"
    & dotnet ef migrations add $MigrationName `
        --project $persistenceProject.FullName `
        --output-dir Migrations

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet ef failed for service: $serviceName"
    }

    $processedCount++
    Write-Host "    - Done"
    Write-Host ""
}

Write-Host "==> Completed"
Write-Host "    Processed services: $processedCount"
Write-Host "    Skipped services:   $skippedCount"
