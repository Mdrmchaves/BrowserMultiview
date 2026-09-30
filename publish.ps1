<#
.SYNOPSIS
    Builds BrowserMultiview as a single .exe and installs it for the current user.

.DESCRIPTION
    Publishes a Release single-file build and copies only the .exe to the output folder
    (default: %LOCALAPPDATA%\Programs\BrowserMultiview), a stable place to point a shortcut at.
    Refuses to run while any BrowserMultiview instance is open: the running .exe can't be replaced,
    and every instance shares the same sessions in %LOCALAPPDATA%\BrowserMultiview.

.PARAMETER Output
    Folder that receives BrowserMultiview.exe.

.PARAMETER SelfContained
    Bundle the .NET runtime so the .exe runs on PCs without .NET 10 installed (much larger file).

.PARAMETER Run
    Start the app when done.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\publish.ps1 -Run
#>
param(
    [string]$Output = (Join-Path $env:LOCALAPPDATA "Programs\BrowserMultiview"),
    [switch]$SelfContained,
    [switch]$Run
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\BrowserMultiview\BrowserMultiview.csproj"
$staging = Join-Path ([IO.Path]::GetTempPath()) "BrowserMultiview-publish"

$running = @(Get-Process BrowserMultiview -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "O BrowserMultiview está aberto ($($running.Count) instância(s)). Feche-o e rode de novo." -ForegroundColor Yellow
    exit 1
}

if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }

$selfContainedArg = if ($SelfContained) { "true" } else { "false" }
Write-Host "Publicando (Release, win-x64, self-contained=$selfContainedArg)..."
dotnet publish $project -c Release -r win-x64 `
    --self-contained $selfContainedArg `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -o $staging
if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet publish falhou (código $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$exe = Join-Path $Output "BrowserMultiview.exe"
Copy-Item (Join-Path $staging "BrowserMultiview.exe") $exe -Force
Remove-Item -Recurse -Force $staging

$sizeMb = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Pronto: $exe ($sizeMb MB)" -ForegroundColor Green

if ($Run) {
    Start-Process $exe
}
