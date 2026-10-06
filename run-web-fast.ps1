param (
    [switch]$NoOpen,
    [switch]$Publish,
    [int]$Port = 5000,
    [string]$Configuration = "Debug",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AppArgs
)

# =========================================================
#   FileFlow Studio - WebAssembly Fast Launcher (NoBuild)
# =========================================================

$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }
$webLauncher = Join-Path $scriptDir "run-web.ps1"

$params = @{ NoBuild = $true }
if ($NoOpen) { $params["NoOpen"] = $true }
if ($Publish) { $params["Publish"] = $true }
if ($Port) { $params["Port"] = $Port }
if ($Configuration) { $params["Configuration"] = $Configuration }
if ($AppArgs) { $params["AppArgs"] = $AppArgs }

& $webLauncher @params
exit $LASTEXITCODE
