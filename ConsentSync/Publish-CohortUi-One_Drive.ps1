# 1. Point to the ROOT workspace
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'CohortUi\CohortUi.csproj'
$sourceSettingsPath = Join-Path $PSScriptRoot 'ConsentSyncCore\appsettings.json'
if (!(Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "CohortUi project not found: $projectPath"
}
Write-Host "Building CohortUi from: $projectPath" -ForegroundColor Cyan

# 2. Define the OUTPUT destination for CohortUi
$oneDrivePath = "$env:UserProfile\OneDrive\Phis\Publish-Output_Cohort"

if (!(Test-Path $oneDrivePath)) { 
    New-Item -ItemType Directory -Path $oneDrivePath -Force 
}

# 3. Publish the CohortUi project
dotnet publish $projectPath `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  --output $oneDrivePath --nologo

if ($LASTEXITCODE -ne 0) {
    throw "CohortUi publish failed (exit code $LASTEXITCODE). No release ZIP was created."
}

# 4. Copy the settings from the core project folder
Copy-Item $sourceSettingsPath `
  "$oneDrivePath\appsettings.json" -Force

# 5. Patch appsettings.json for Production Release
Write-Host "🔧 Patching appsettings.json for CohortUi Release..." -ForegroundColor Yellow

$settingsPath = "$oneDrivePath\appsettings.json" 
$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json

# Force safety/dev flags for production release
$settings.DevMode = $false
if ($null -ne $settings.Phase3 -and $null -ne $settings.Phase3.Testing) {
    $settings.Phase3.Testing.Enabled = $false
}

# Convert back to JSON and save (UTF-8 preserved for French accents)
$settings | ConvertTo-Json -Depth 20 | Out-File $settingsPath -Encoding utf8

Write-Host "✅ CohortUi published and Production settings applied." -ForegroundColor Green
Write-Host "`n✅ Build complete! Folder is here: $oneDrivePath" -ForegroundColor Cyan

# 6. Define the Zip path for CohortUi release
$zipFileName = "CohortUi_Release_$(Get-Date -Format 'yyyyMMdd').zip"
$zipPath = Join-Path -Path (Split-Path $oneDrivePath -Parent) -ChildPath $zipFileName

# 7. Remove old zip if it exists
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# 8. Zip the contents of the Publish-Output_Cohort folder
Write-Host "🗜️ Zipping CohortUi files..." -ForegroundColor Yellow
Compress-Archive -Path "$oneDrivePath\*" -DestinationPath $zipPath -Force

Write-Host "`n✅ CohortUi Build and Zip complete!" -ForegroundColor Cyan
Write-Host "📦 Zip File: $zipPath" -ForegroundColor Green
