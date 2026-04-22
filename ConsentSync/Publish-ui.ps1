# Set the working directory
Set-Location -Path "C:\PHIS\ConsentSync"

# 1 — Publish UI
dotnet publish "OrchestratorUi\OrchestratorUi.csproj" `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  --output "C:\PHIS\publish-ui" --nologo

# 2 — Copy appsettings.json next to the exe
# Ensure the path to appsettings is relative to the new Location
Copy-Item "ConsentSyncCore\appsettings.json" `
  "C:\PHIS\publish-ui\appsettings.json" -Force

Write-Host "`n✅ UI published to C:\PHIS\publish-ui" -ForegroundColor Green
