param (
    [switch]$NoBuild,
    [switch]$NoOpen,
    [switch]$Publish,
    [int]$Port = 5000,
    [string]$Configuration = "Debug",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AppArgs
)

# =========================================================
#   FileFlow Studio - WebAssembly (Browser WASM) Launcher
# =========================================================
# Compila y lanza la versión WebAssembly (net10.0-browserwasm)
# en el navegador automáticamente.
#
# Uso:
#   .\run-web.ps1                       # Compila y lanza en el navegador
#   .\run-web.ps1 -NoBuild              # Lanza sin compilar
#   .\run-web.ps1 -Configuration Release # Lanza compilación optimizada
#   .\run-web.ps1 -Port 5050            # Usa un puerto específico
#   .\run-web.ps1 -NoOpen               # No abre el navegador automáticamente
#   .\run-web.ps1 -Publish              # Publica y sirve el paquete estático

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }

$projectDir = Join-Path $scriptDir "FileFlow.App.Uno"
$projectPath = Join-Path $projectDir "FileFlow.App.Uno.csproj"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  FileFlow Studio - WebAssembly (WASM)   " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$url = "http://localhost:$Port"

if ($Publish) {
    $publishDir = Join-Path $scriptDir "publish\FileFlowStudio-web"
    if (-not $NoBuild) {
        Write-Host "`nPublicando la versión WebAssembly ($Configuration)..." -ForegroundColor Yellow
        & dotnet publish $projectPath -c $Configuration -p:FileFlowTarget=wasm -o $publishDir --nologo -v:m
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERROR] Falló la publicación WebAssembly." -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }

    Write-Host "`nServidor web listo para alojar: $publishDir" -ForegroundColor Green
    Write-Host "URL: $url" -ForegroundColor Cyan

    if (-not $NoOpen) {
        Start-Process $url
    }

    if (Get-Command "dotnet-serve" -ErrorAction SilentlyContinue) {
        & dotnet-serve -d $publishDir -p $Port
    } elseif (Get-Command "npx" -ErrorAction SilentlyContinue) {
        & npx -y serve $publishDir -l $Port
    } elseif (Get-Command "python" -ErrorAction SilentlyContinue) {
        & python -m http.server $Port --directory $publishDir
    } else {
        Write-Host "Iniciando servidor HTTP integrado de PowerShell..." -ForegroundColor Yellow
        $listener = [System.Net.HttpListener]::new()
        $listener.Prefixes.Add("http://localhost:$Port/")
        $listener.Start()
        Write-Host "Servidor escuchando en $url. Presiona Ctrl+C para detener." -ForegroundColor Green
        try {
            while ($listener.IsListening) {
                $context = $listener.GetContext()
                $requestPath = $context.Request.Url.LocalPath.TrimStart('/')
                if ([string]::IsNullOrEmpty($requestPath)) { $requestPath = "index.html" }
                $filePath = Join-Path $publishDir $requestPath

                if (Test-Path $filePath -PathType Leaf) {
                    $ext = [System.IO.Path]::GetExtension($filePath).ToLowerInvariant()
                    $contentType = switch ($ext) {
                        ".html" { "text/html; charset=utf-8" }
                        ".js"   { "application/javascript" }
                        ".wasm" { "application/wasm" }
                        ".json" { "application/json" }
                        ".css"  { "text/css" }
                        ".png"  { "image/png" }
                        ".ico"  { "image/x-icon" }
                        default { "application/octet-stream" }
                    }
                    $context.Response.ContentType = $contentType
                    $bytes = [System.IO.File]::ReadAllBytes($filePath)
                    $context.Response.ContentLength64 = $bytes.Length
                    $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
                } else {
                    $context.Response.StatusCode = 404
                }
                $context.Response.Close()
            }
        } finally {
            $listener.Stop()
        }
    }
} else {
    if (-not $NoBuild) {
        Write-Host "`nCompilando la versión WebAssembly ($Configuration)..." -ForegroundColor Yellow
        & dotnet build $projectPath -c $Configuration -p:FileFlowTarget=wasm --nologo -v:m
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERROR] Falló la compilación de WebAssembly." -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }

    Write-Host "`nIniciando FileFlow Studio Web..." -ForegroundColor Green
    Write-Host "URL: $url" -ForegroundColor Cyan

    if (-not $NoOpen) {
        Start-Job -ScriptBlock {
            param($targetUrl)
            Start-Sleep -Seconds 2
            Start-Process $targetUrl
        } -ArgumentList $url | Out-Null
    }

    $runArgs = @(
        "run",
        "--project", $projectPath,
        "-c", $Configuration,
        "-p:FileFlowTarget=wasm",
        "--launch-profile", "WebAssembly",
        "--no-build"
    )
    if ($AppArgs) {
        $runArgs += "--"
        $runArgs += $AppArgs
    }

    & dotnet @runArgs
    exit $LASTEXITCODE
}
