# Build VAULTS OF ZOR and its level editor.
#   .\build.ps1          gfx.inc -> zout\zor.cmd (engine) -> ZorEdit.exe -> ZOR.CMD + ZOR.LEV
#   .\build.ps1 -Run     ...then run ZOR.CMD in trs80gp (boots TRSDOS)
# Needs Python 3 and zmac (tools\zmac, on PATH or $env:ZMAC). ZorEdit is compiled with the
# C# compiler that ships with the .NET Framework 4 in Windows.
param([switch]$Run)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

python gen.py
if ($LASTEXITCODE -ne 0) { throw "gen.py failed" }

$zmac = if ($env:ZMAC) { $env:ZMAC } elseif (Test-Path 'tools\zmac\zmac.exe') { 'tools\zmac\zmac.exe' } else { 'zmac.exe' }
& $zmac zor.asm
if ($LASTEXITCODE -ne 0) { throw "zmac failed" }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
& $csc /nologo /target:winexe /out:ZorEdit.exe /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    "/resource:zout\zor.cmd,engine.cmd" editor\Program.cs editor\Pack.cs editor\MainForm.cs
if ($LASTEXITCODE -ne 0) { throw "csc failed" }

# Default game files from the default level pack
$p = Start-Process .\ZorEdit.exe -ArgumentList '/cmd', 'levels\default.zlp', 'ZOR.CMD' -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0) { throw "ZorEdit /cmd failed" }
$p = Start-Process .\ZorEdit.exe -ArgumentList '/lev', 'levels\default.zlp', 'ZOR.LEV' -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0) { throw "ZorEdit /lev failed" }

if ($Run) {
    $emu = if ($env:TRS80GP) { $env:TRS80GP } elseif (Test-Path 'tools\trs80gp\windows\trs80gp.exe') { 'tools\trs80gp\windows\trs80gp.exe' } else { 'trs80gp.exe' }
    & $emu -m1 ZOR.CMD
}
