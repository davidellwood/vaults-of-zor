# Run trs80gp with scripted input and capture a burst of window shots.
# usage: burst.ps1 -Cmd game.cmd -Extra @(...) -Start 10 -Count 8 -Every 500 -Prefix b
param([string]$Cmd, [string[]]$Extra = @(), [int]$Start = 10, [int]$Count = 8, [int]$Every = 500, [string]$Prefix = "b")
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class W3 { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
 public struct RECT { public int L, T, R, B; } }
"@
$E = if ($env:TRS80GP) { $env:TRS80GP } else { Join-Path $PSScriptRoot "..\tools\trs80gp\windows\trs80gp.exe" }
$argv = @('-m1', '-itime', '0', $Cmd) + $Extra
$argv = $argv | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
$p = Start-Process -FilePath $E -ArgumentList $argv -PassThru
Start-Sleep -Seconds $Start
$p.Refresh(); $h = $p.MainWindowHandle
for ($i = 0; $i -lt $Count; $i++) {
    $r = New-Object W3+RECT; [W3]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); [W3]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc)
    # keep just the 3D view area
    $crop = $bmp.Clone((New-Object System.Drawing.Rectangle 8, 50, 392, 610), $bmp.PixelFormat)
    $crop.Save((Join-Path $PSScriptRoot ("{0}{1}.png" -f $Prefix, $i)), [System.Drawing.Imaging.ImageFormat]::Png)
    $crop.Dispose(); $g.Dispose(); $bmp.Dispose()
    Start-Sleep -Milliseconds $Every
}
Stop-Process -Id $p.Id -Force
"captured $Count"
