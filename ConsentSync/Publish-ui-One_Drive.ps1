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

Write-Host "`n✅ Build complete! Folder is here: $oneDrivePath" -ForegroundColor Cyan


# ... (Previous code for Publish and Copy-Item remains the same)

# 5. Define the Zip path
$zipFileName = "ConsentSync_Release_$(Get-Date -Format 'yyyyMMdd').zip"
$zipPath = Join-Path -Path (Split-Path $oneDrivePath -Parent) -ChildPath $zipFileName

# 6. Remove old zip if it exists to avoid appending
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# 7. Zip the contents of the Publish-Output folder
Write-Host "🗜️ Zipping files..." -ForegroundColor Yellow
Compress-Archive -Path "$oneDrivePath\*" -DestinationPath $zipPath -Force

Write-Host "`n✅ Build and Zip complete!" -ForegroundColor Cyan
Write-Host "📦 Zip File: $zipPath" -ForegroundColor Green