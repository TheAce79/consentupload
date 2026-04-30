# 1. Point to the ROOT workspace
Set-Location -Path "$env:UserProfile\OneDrive\Phis"

# 2. Define the OUTPUT destination at the top level
$oneDrivePath = "$env:UserProfile\OneDrive\Phis\Publish-Output"

if (!(Test-Path $oneDrivePath)) { 
    New-Item -ItemType Directory -Path $oneDrivePath -Force 
}

# 3. Publish the UI
dotnet publish "consentupload\ConsentSync\OrchestratorUi\OrchestratorUi.csproj" `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  --output $oneDrivePath --nologo

# 4. Copy the settings from the core project folder
Copy-Item "consentupload\ConsentSync\ConsentSyncCore\appsettings.json" `
  "$oneDrivePath\appsettings.json" -Force


# 5 — FORCE "Testing: Enabled" to false for Production Release
Write-Host "🔧 Patching appsettings.json to disable Testing Mode..." -ForegroundColor Yellow

$settingsPath =  "$env:UserProfile\OneDrive\Phis\Publish-Output\appsettings.json" 
$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json

# Force the specific flag to false
$settings.Phase3.Testing.Enabled = $false

# Convert back to JSON and save (with UTF8 to preserve your French accents!)
$settings | ConvertTo-Json -Depth 20 | Out-File $settingsPath -Encoding utf8

Write-Host "✅ UI published and Testing Mode forced to FALSE" -ForegroundColor Green


Write-Host "`n✅ Build complete! Folder is here: $oneDrivePath" -ForegroundColor Cyan


# ... (Previous code for Publish and Copy-Item remains the same)

# 6. Define the Zip path
$zipFileName = "ConsentSync_Release_$(Get-Date -Format 'yyyyMMdd').zip"
$zipPath = Join-Path -Path (Split-Path $oneDrivePath -Parent) -ChildPath $zipFileName

# 7. Remove old zip if it exists to avoid appending
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# 8. Zip the contents of the Publish-Output folder
Write-Host "🗜️ Zipping files..." -ForegroundColor Yellow
Compress-Archive -Path "$oneDrivePath\*" -DestinationPath $zipPath -Force

Write-Host "`n✅ Build and Zip complete!" -ForegroundColor Cyan
Write-Host "📦 Zip File: $zipPath" -ForegroundColor Green