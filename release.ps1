<#
.SYNOPSIS
    Baut eine neue Version der Filament-Verwaltung als eigenständige EXE für ein GitHub-Release.

.DESCRIPTION
    1. Setzt <Version> in FilamentVerwaltung.vbproj
    2. Baut eine einzelne, eigenständige EXE (enthält .NET, läuft ohne Installation)
    3. Legt sie unter artefakte\<Version>\FilamentVerwaltung.exe ab und zeigt die nächsten Schritte

    Danach auf GitHub ein Release mit dem Tag v<Version> anlegen und die EXE anhängen.
    Die installierten Programme finden das Update beim nächsten Start.

.EXAMPLE
    .\release.ps1 -Version 1.1.0
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$wurzel = $PSScriptRoot
$projekt = Join-Path $wurzel 'FilamentVerwaltung\FilamentVerwaltung.vbproj'
$ziel = Join-Path $wurzel "artefakte\$Version"

# --- Version im Projekt setzen
$inhalt = [System.IO.File]::ReadAllText($projekt)
$neu = [regex]::Replace($inhalt, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
if ($neu -ne $inhalt) {
    [System.IO.File]::WriteAllText($projekt, $neu, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Version im Projekt auf $Version gesetzt."
}

# --- Eigenständige Einzel-EXE bauen
if (Test-Path $ziel) { Remove-Item $ziel -Recurse -Force }
Write-Host "Baue Version $Version ..."
dotnet publish $projekt -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $ziel
if ($LASTEXITCODE -ne 0) { throw "dotnet publish ist fehlgeschlagen." }

$exe = Join-Path $ziel 'FilamentVerwaltung.exe'
if (-not (Test-Path $exe)) { throw "Die EXE wurde nicht erzeugt: $exe" }
$groesse = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash

Write-Host ""
Write-Host "Fertig: $exe ($groesse MB)" -ForegroundColor Green
Write-Host "SHA-256: $hash"
Write-Host ""
Write-Host "Naechste Schritte:" -ForegroundColor Cyan
Write-Host "  1. Aenderungen committen und pushen"
Write-Host "  2. Auf GitHub: Releases -> 'Draft a new release'"
Write-Host "     Tag:   v$Version"
Write-Host "     Titel: Filament-Verwaltung $Version"
Write-Host "     Text:  Was ist neu (wird im Update-Dialog angezeigt)"
Write-Host "     Datei: $exe  (Name muss FilamentVerwaltung.exe bleiben)"
Write-Host "  3. 'Publish release' - installierte Programme melden das Update beim naechsten Start."
if (Get-Command gh -ErrorAction SilentlyContinue) {
    Write-Host ""
    Write-Host "Oder mit der GitHub-CLI:" -ForegroundColor Cyan
    Write-Host "  gh release create v$Version `"$exe`" --title `"Filament-Verwaltung $Version`" --notes `"...`""
}
