<#
.SYNOPSIS
    Baut eine neue Version der Filament-Verwaltung als eigenständige EXE für ein GitHub-Release.

.DESCRIPTION
    1. Prüft, dass alles committet ist und GitHub keinen neueren Stand hat
    2. Setzt <Version> in FilamentVerwaltung.vbproj, committet das als "Version x.y.z" und pusht
       (damit der Release-Tag auf GitHub auf genau diesen Code zeigt)
    3. Baut eine einzelne, eigenständige EXE (enthält .NET, läuft ohne Installation)
    4. Legt sie unter artefakte\<Version>\FilamentVerwaltung.exe ab und zeigt die nächsten Schritte

    Danach auf GitHub ein Release mit dem Tag v<Version> anlegen und die EXE anhängen.
    Die installierten Programme finden das Update beim nächsten Start.

.PARAMETER OhneGit
    Nur bauen, ohne Prüfung, Commit und Push (z. B. für einen Testbuild).

.EXAMPLE
    .\release.ps1 -Version 1.1.0
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [switch]$OhneGit
)

$ErrorActionPreference = 'Stop'
$wurzel = $PSScriptRoot
$projekt = Join-Path $wurzel 'FilamentVerwaltung\FilamentVerwaltung.vbproj'
$serverProjekt = Join-Path $wurzel 'FilamentVerwaltung.Server\FilamentVerwaltung.Server.vbproj'
$ziel = Join-Path $wurzel "artefakte\$Version"

function GitAusfuehren {
    # git schreibt Fortschritt nach stderr – das ist kein Fehler, entscheidend ist der Exit-Code
    $alt = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $ausgabe = & git.exe -C $wurzel @args 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE; $ErrorActionPreference = $alt
    if ($code -ne 0) { throw "git $($args -join ' ') ist fehlgeschlagen:`n$($ausgabe -join "`n")" }
    return $ausgabe
}

# --- Git-Stand prüfen
if (-not $OhneGit) {
    $offen = GitAusfuehren status --porcelain
    if ($offen) {
        throw "Es gibt nicht committete Änderungen. Bitte zuerst committen (oder mit -OhneGit nur bauen):`n$($offen -join "`n")"
    }
    $zweig = (GitAusfuehren rev-parse --abbrev-ref HEAD | Select-Object -First 1).Trim()
    GitAusfuehren fetch --quiet --tags origin | Out-Null
    $remote = (GitAusfuehren rev-parse "origin/$zweig" | Select-Object -First 1).Trim()
    $basis = (GitAusfuehren merge-base HEAD "origin/$zweig" | Select-Object -First 1).Trim()
    if ($basis -ne $remote) {
        throw "Auf GitHub gibt es Commits, die lokal fehlen. Bitte zuerst pullen."
    }
    if (GitAusfuehren tag --list "v$Version") {
        throw "Den Tag v$Version gibt es schon. Bitte eine höhere Versionsnummer wählen."
    }
}

# --- Version in App und Server setzen (der Server meldet sie unter /api/info; GitHub baut das Image nach dem Push)
foreach ($datei in $projekt, $serverProjekt) {
    $inhalt = [System.IO.File]::ReadAllText($datei)
    $neu = [regex]::Replace($inhalt, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
    if ($neu -ne $inhalt) {
        [System.IO.File]::WriteAllText($datei, $neu, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "Version in $([System.IO.Path]::GetFileName($datei)) auf $Version gesetzt."
    }
}

# --- Committen und pushen, damit der Release-Tag auf diesen Stand zeigt
if (-not $OhneGit) {
    if (GitAusfuehren status --porcelain) {
        GitAusfuehren add -- $projekt $serverProjekt | Out-Null
        GitAusfuehren commit --quiet -m "Version $Version" | Out-Null
        Write-Host "Commit ""Version $Version"" angelegt."
    }
    GitAusfuehren push --quiet origin $zweig | Out-Null
    Write-Host "Auf GitHub gepusht ($zweig)."
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
if ($OhneGit) { Write-Host "  1. Aenderungen committen und pushen (mit -OhneGit gebaut)" } else { Write-Host "  1. Code ist bereits committet und gepusht" }
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
