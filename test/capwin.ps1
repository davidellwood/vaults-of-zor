# Start a program, wait, capture its main window to a PNG, close it.
param([string]$Exe, [string[]]$ArgList = @(), [string]$Out = "win.png", [int]$Wait = 4, [switch]$Keep)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class W2 { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
 public struct RECT { public int L, T, R, B; } }
"@
$p = Start-Process -FilePath $Exe -ArgumentList $ArgList -PassThru
Start-Sleep -Seconds $Wait
$p.Refresh()
$h = $p.MainWindowHandle
$r = New-Object W2+RECT
[W2]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.R - $r.L; $ht = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc(); [W2]::PrintWindow($h, $dc, 2) | Out-Null; $g.ReleaseHdc($dc)
$bmp.Save((Join-Path $PSScriptRoot $Out), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
if (-not $Keep) { Stop-Process -Id $p.Id -Force }
"saved $Out ${w}x${ht}"
