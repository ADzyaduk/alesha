# Local tooling scripts

Fast (uses `--no-restore`):
- `powershell -ExecutionPolicy Bypass -File .\scripts\build-local.ps1`
- `powershell -ExecutionPolicy Bypass -File .\scripts\publish-local.ps1`

With explicit restore:
- `powershell -ExecutionPolicy Bypass -File .\scripts\build-local.ps1 -Restore`
- `powershell -ExecutionPolicy Bypass -File .\scripts\publish-local.ps1 -Restore`

Custom output:
- `powershell -ExecutionPolicy Bypass -File .\scripts\publish-local.ps1 -Output "C:\path\to\out31"`

Scripts use local:
- `.dotnet_home`
- `.nuget\packages`
- `NuGet.Config`


Default publish output now: C:\pj\alesha\out-latest (single stable folder, overwritten each publish).
You can disable cleanup with: -CleanOutput $false.
