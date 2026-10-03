<#
.SYNOPSIS
    Sincroniza en la caché NuGet POR DEFECTO los paquetes Uno.* que el DevServer necesita y que sólo
    están en la caché configurada (NUGET_PACKAGES).

.DESCRIPTION
    El Uno DevServer —el puente MCP `uno-app`, que el IDE lanza con
    `dotnet dnx -y uno.devserver --mcp-app`— resuelve el SDK y sus paquetes desde
    `$env:USERPROFILE\.nuget\packages`, IGNORANDO `NUGET_PACKAGES`.

    En una máquina con la caché NuGet redirigida a otra unidad (aquí `NUGET_PACKAGES=D:\packages\NuGet\cache`)
    eso deja el puente `Unhealthy` con errores fatales de paquete ausente
    (`SdkNotInCache`, `DevServerPackageNotCached`, `AddInPackageNotCached`) aunque `dotnet build` funcione
    perfectamente: son dos cachés distintas y el DevServer sólo conoce la de por defecto.

    Este script copia los paquetes `uno.*` que falten, sin borrar ni mover nada de la caché de origen
    (la copia es aditiva e idempotente). Es seguro volver a ejecutarlo: si ya está todo, no hace nada.

    Diagnóstico (medido):
      - `uno_health` → `SdkNotInCache` con `unoSdkPath: null`, aunque `dotnet nuget locals global-packages`
        apunte a la caché configurada y `uno.sdk/<versión>` exista allí.
      - Tras copiar `uno.sdk`, el puente resuelve `unoSdkPath` EN LA CACHÉ POR DEFECTO y pasa a pedir
        `uno.winui.devserver` y el add-in `uno.settings.devserver`: confirma que lee la caché por defecto.

.PARAMETER Target
    Caché NuGet de destino (la que el DevServer consulta). Por defecto, `$env:USERPROFILE\.nuget\packages`.

.PARAMETER Source
    Caché de origen. Por defecto se deduce de `dotnet nuget locals global-packages` y, si no, de
    `$env:NUGET_PACKAGES`.

.EXAMPLE
    .\sync-uno-devserver-cache.ps1
    .\sync-uno-devserver-cache.ps1 -Target 'C:\Users\me\.nuget\packages'
#>
param(
    [string]$Target = (Join-Path $env:USERPROFILE '.nuget\packages'),
    [string]$Source = ''
)

$ErrorActionPreference = 'Stop'

function Resolve-SourceCache {
    param([string]$Explicit)

    if ($Explicit) { return $Explicit }

    # La caché que usa `dotnet`: es la que puede salir de NUGET_PACKAGES.
    try {
        $line = (& dotnet nuget locals global-packages --list 2>$null | Out-String)
        if ($line -match 'global-packages:\s*(.+?)\s*$') {
            return $Matches[1].Trim()
        }
    }
    catch {
        # Sin `dotnet` en PATH se usa la variable de entorno como último recurso.
    }

    if ($env:NUGET_PACKAGES) { return $env:NUGET_PACKAGES }

    return ''
}

$Source = Resolve-SourceCache -Explicit $Source

if (-not $Source) {
    Write-Host '========================================='
    Write-Host '  Sync caché Uno DevServer               '
    Write-Host '========================================='
    Write-Host 'No se pudo deducir la caché de origen (ni dotnet ni NUGET_PACKAGES). Nada que hacer.'
    exit 0
}

$sourcePath = (Resolve-Path -LiteralPath $Source -ErrorAction SilentlyContinue)
$targetPath = $Target

Write-Host '========================================='
Write-Host '  Sync caché Uno DevServer               '
Write-Host '========================================='
Write-Host "Origen : $Source"
Write-Host "Destino: $targetPath"
Write-Host ''

if (-not $sourcePath) {
    Write-Host "La caché de origen no existe: $Source"
    exit 0
}

if ([string]::Equals($sourcePath.Path, $targetPath, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host 'Origen y destino son la MISMA carpeta: no hay nada que sincronizar.'
    exit 0
}

if (-not (Test-Path -LiteralPath $targetPath)) {
    New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
}

$copied = 0
$skipped = 0

# Los paquetes del ecosistema Uno que el DevServer resuelve por su cuenta: el SDK (global.json), el
# propio DevServer y los add-ins (`Uno.Settings.DevServer`, etc.). El patrón `uno.*` los cubre todos.
foreach ($packageDir in Get-ChildItem -LiteralPath $sourcePath.Path -Directory -Filter 'uno.*') {
    foreach ($versionDir in Get-ChildItem -LiteralPath $packageDir.FullName -Directory) {
        $destination = Join-Path (Join-Path $targetPath $packageDir.Name) $versionDir.Name

        if (Test-Path -LiteralPath $destination) {
            $skipped++
            continue
        }

        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $versionDir.FullName -Destination $destination -Recurse -Force
        $copied++
        Write-Host "  copiado: $($packageDir.Name)/$($versionDir.Name)"
    }
}

Write-Host ''
Write-Host "Paquetes copiados: $copied  (ya presentes: $skipped)"
Write-Host 'Reinicia el DevServer (uno_app_select_solution con forceRestart) para que lo vea.'
