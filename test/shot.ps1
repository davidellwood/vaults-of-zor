# Run trs80gp with scripted input, then capture its window to a PNG.
# usage: shot.ps1 -Out name.png -Wait 8 -Extra @('-i','\r')
# Set $env:TRS80GP to the path of trs80gp.exe if it is not on PATH.
param([string]$Out = "shot.png", [int]$Wait = 8, [string[]]$Extra = @(), [string]$Cmd = "", [string[]]$Pre = @())
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class W { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
 public struct RECT { public int L, T, R, B; } }
"@
$E = if ($env:TRS80GP) { $env:TRS80GP } else { Join-Path $PSScriptRoot "..\tools\trs80gp\windows\trs80gp.exe" }
if ($Cmd -eq "") { $Cmd = Join-Path $PSScriptRoot "..\ZOR.CMD" }
$argv = @('-m1', '-turbo', '-itime', '0') + $Pre + @($Cmd) + $Extra
if ($Cmd -eq "none") { $argv = @('-m1', '-turbo', '-itime', '0') + $Pre + $Extra }
$argv = $argv | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
$p = Start-Process -FilePath $E -ArgumentList $argv -PassThru
Start-Sleep -Seconds $Wait
$p.Refresh()
$h = $p.MainWindowHandle
$r = New-Object W+RECT
[W]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[W]::PrintWindow($h, $dc, 2) | Out-Null
$g.ReleaseHdc($dc)
$bmp.Save((Join-Path $PSScriptRoot $Out), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Stop-Process -Id $p.Id -Force
"saved $Out ${w}x${ht}"
