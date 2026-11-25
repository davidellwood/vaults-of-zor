# Rebuild the engine and the format 5 test pack, then play keys and capture.
# usage: t5.ps1 -Out name.png -Keys "ENTER UP LOOK/" [-Wait 10] [-NoBuild]
param([string]$Out = "t5.png", [string]$Keys = "ENTER", [int]$Wait = 10, [int]$Hold = 8, [int]$Gap = 16, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
if (-not $env:TRS80GP) {
    foreach ($c in @((Join-Path $root 'tools\trs80gp\windows\trs80gp.exe'), 'C:\Daves Stuff\TRS80GP\trs80gp.exe')) { if (Test-Path $c) { $env:TRS80GP = $c; break } }
}
if (-not $NoBuild) {
    Push-Location $root
    try {
        $zmac = if ($env:ZMAC) { $env:ZMAC } elseif (Test-Path 'tools\zmac\zmac.exe') { 'tools\zmac\zmac.exe' } else { 'zmac.exe' }
        & $zmac zor.asm
        if ($LASTEXITCODE -ne 0) { throw "zmac failed" }
        python -I test\v5pack.py zout\zor.cmd test\T5.CMD test\T5.LEV
        if ($LASTEXITCODE -ne 0) { throw "v5pack failed" }
    } finally { Pop-Location }
}
& (Join-Path $PSScriptRoot 'play.ps1') -Cmd (Join-Path $PSScriptRoot 'T5.CMD') -Out $Out -Keys $Keys -Wait $Wait -Hold $Hold -Gap $Gap
