Office PC (no admin rights, .NET not installed)
You need a self-contained publish — the .NET runtime gets bundled into the output. Your .csproj already has this configured:


<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<PublishSingleFile>true</PublishSingleFile>

Run this once on your local PC to produce the deployable output:

dotnet publish OrchestratorUi/OrchestratorUi.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output C:\PHIS\Deploy
	
	
	
	
Copy the entire C:\PHIS\Deploy folder to the office PC. No admin rights, no .NET installation needed — just run OrchestratorUi.exe.