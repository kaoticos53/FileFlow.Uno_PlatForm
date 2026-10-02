param(
    [string]$Configuration = "Release",
    [string[]]$Targets = @("desktop", "wasm")
)

# =========================================================
#   FileFlow Studio - Matriz de compilación multiplataforma
# =========================================================
# Compila el host Uno para cada familia soportada usando la selección explícita
# `-p:FileFlowTarget=<plataforma>`:
#   windows  -> net10.0-windows10.0.19041.0 (WinUI 3)
#   desktop  -> net10.0-desktop              (Skia: Linux y macOS)
#   wasm     -> net10.0-browserwasm          (navegador)
#
# Uso:
#   .\build-matrix.ps1                     # desktop + wasm
#   .\build-matrix.ps1 -Targets windows,desktop,wasm

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }

$projectPath = Join-Path $scriptDir "FileFlow.App.Uno\FileFlow.App.Uno.csproj"

$all = [System.Collections.Generic.List[string]]::new()
foreach ($t in $Targets) { $all.Add($t) }

$failed = @()

foreach ($target in $all) {
    Write-Host "`n===== FileFlowTarget=$target ($Configuration) =====" -ForegroundColor Cyan

    & dotnet build $projectPath -c $Configuration -p:FileFlowTarget=$target --nologo -v:m
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [FALLO] FileFlowTarget=$target" -ForegroundColor Red
        $failed += $target
    } else {
        Write-Host "  [OK] FileFlowTarget=$target" -ForegroundColor Green
    }
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "Plataformas con fallo: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "Matriz de compilación superada: $($all -join ', ')" -ForegroundColor Green
