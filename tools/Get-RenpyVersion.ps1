<#
.SYNOPSIS
    Detect the Ren'Py engine version a shipped game was built with.

.DESCRIPTION
    Checks, in order of reliability:
      1. renpy/vc_version.py  (version_tuple = (8, 2, 3, ...))
      2. log.txt              (first line: "Ren'Py 8.2.3.24061702")
      3. renpy/__init__.py    (version_tuple fallback)
      4. lib/ folder names    (python runtime hint: py3 => 7.4+/8.x, py2 => <= 7.3)

.PARAMETER Path
    Path to the extracted game folder (the one containing the .exe and the game/
    subfolder), or directly to the game .exe.

.EXAMPLE
    .\Get-RenpyVersion.ps1 -Path "C:\...\games\SomeVN"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Path)) { throw "Path not found: $Path" }

$item = Get-Item $Path
$root = if ($item.PSIsContainer) { $item.FullName } else { $item.DirectoryName }

Write-Host "Scanning: $root" -ForegroundColor Cyan

$findings = [ordered]@{}

# --- 1. renpy/vc_version.py ---
$vc = Join-Path $root 'renpy\vc_version.py'
if (Test-Path $vc) {
    $txt = Get-Content $vc -Raw
    if ($txt -match 'version_tuple\s*=\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)') {
        $findings['vc_version.py'] = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
    }
    if ($txt -match "official\s*=\s*(True|False)") {
        $findings['official build'] = $Matches[1]
    }
}

# --- 2. log.txt ---
foreach ($log in @('log.txt', 'game\saves\log.txt', 'errors.txt', 'traceback.txt')) {
    $lp = Join-Path $root $log
    if (Test-Path $lp) {
        $line = Select-String -Path $lp -Pattern "Ren'?Py\s+([0-9]+\.[0-9]+(\.[0-9]+)?)" | Select-Object -First 1
        if ($line -and $line.Matches[0].Groups[1].Value) {
            $findings["$log"] = $line.Matches[0].Groups[1].Value
            break
        }
    }
}

# --- 3. renpy/__init__.py ---
$init = Join-Path $root 'renpy\__init__.py'
if (Test-Path $init) {
    $txt = Get-Content $init -Raw
    if ($txt -match 'version_tuple\s*=\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)') {
        $findings['renpy/__init__.py'] = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
    }
}

# --- 4. lib/ folder hint ---
$lib = Join-Path $root 'lib'
if (Test-Path $lib) {
    $dirs = Get-ChildItem $lib -Directory | Select-Object -ExpandProperty Name
    $findings['lib/ runtimes'] = ($dirs -join ', ')
    if ($dirs -match 'py3') { $findings['python hint'] = 'py3 => Ren''Py 7.4+ or 8.x' }
    elseif ($dirs -match 'py2') { $findings['python hint'] = 'py2 => Ren''Py 7.3 or older' }
}

# --- assets present ---
$gameDir = Join-Path $root 'game'
if (Test-Path $gameDir) {
    $rpa  = @(Get-ChildItem $gameDir -Filter *.rpa -ErrorAction SilentlyContinue)
    $rpyc = @(Get-ChildItem $gameDir -Filter *.rpyc -Recurse -ErrorAction SilentlyContinue)
    $rpy  = @(Get-ChildItem $gameDir -Filter *.rpy  -Recurse -ErrorAction SilentlyContinue)
    $findings['assets'] = "$($rpa.Count) .rpa archive(s), $($rpyc.Count) .rpyc, $($rpy.Count) .rpy source"
}

Write-Host ""
if ($findings.Count -eq 0) {
    Write-Warning "No Ren'Py markers found. Is this an extracted Ren'Py game folder?"
    exit 1
}

$findings.GetEnumerator() | ForEach-Object {
    "{0,-22} {1}" -f $_.Key, $_.Value
}

Write-Host ""
$best = $findings['vc_version.py'], $findings['log.txt'], $findings['renpy/__init__.py'] |
        Where-Object { $_ } | Select-Object -First 1
if ($best) {
    Write-Host "Best guess: Ren'Py $best" -ForegroundColor Green
    Write-Host "SDK: https://www.renpy.org/dl/$best/" -ForegroundColor Green
} else {
    Write-Host "Version not pinned from files above — run the game and read log.txt." -ForegroundColor Yellow
}
