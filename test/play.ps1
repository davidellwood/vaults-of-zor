# Drive the game through the Model I keyboard matrix and capture the window.
# usage: play.ps1 -Out x.png -Keys "ENTER UP UP RIGHT TAKE/ ..." -Wait 15
#   tokens: ENTER UP DOWN LEFT RIGHT SPACE CLEAR, single characters,
#   or a word followed by '/' which types the word then ENTER.
param([string]$Out = "play.png", [string]$Keys = "", [int]$Wait = 15, [int]$Gap = 12, [int]$Hold = 4, [string]$Cmd = "", [string[]]$Pre = @(), [string]$Ready = "PRESS ENTER", [int]$Burst = 0, [int]$Start = 10, [int]$Every = 200)
$rows = @{}
$lines = @("@ABCDEFG", "HIJKLMNO", "PQRSTUVW", "XYZ", "01234567", "89:;,-./")
for ($r = 0; $r -lt $lines.Count; $r++) {
    for ($b = 0; $b -lt $lines[$r].Length; $b++) { $rows[[string]$lines[$r][$b]] = @($r, (1 -shl $b)) }
}
$named = @{ ENTER = @(6, 1); CLEAR = @(6, 2); BREAK = @(6, 4); UP = @(6, 8); DOWN = @(6, 16); LEFT = @(6, 32); RIGHT = @(6, 64); SPACE = @(6, 128) }
$argv = @('-iw', $Ready, '-id', '20')
function Press($rm) { $script:argv += @('-ik', "$($rm[0])", ('{0:x}' -f $rm[1]), '-id', "$Hold", '-ik', "$($rm[0])", '0', '-id', "$Gap") }
foreach ($t in ($Keys -split ' ')) {
    if ($t -eq '') { continue }
    if ($t -eq 'WAIT') { $script:argv += @('-id', '420'); continue }   # let speech finish
    if ($named.ContainsKey($t)) { Press $named[$t]; continue }
    $word = $t; $enter = $false
    if ($t.EndsWith('/') -and $t.Length -gt 1) { $word = $t.Substring(0, $t.Length - 1); $enter = $true }
    foreach ($c in $word.ToCharArray()) { if ($c -eq '_') { Press $named['SPACE'] } else { Press $rows[[string]$c] } }
    if ($enter) { Press $named['ENTER'] }
}
if ($Burst -gt 0) {
    if ($Cmd -eq "") { $Cmd = Join-Path $PSScriptRoot "..\ZOR.CMD" }
    & (Join-Path $PSScriptRoot 'burst.ps1') -Cmd $Cmd -Extra $argv -Start $Start -Count $Burst -Every $Every -Prefix ([IO.Path]::GetFileNameWithoutExtension($Out))
} else {
    & (Join-Path $PSScriptRoot 'shot.ps1') -Out $Out -Wait $Wait -Extra $argv -Cmd $Cmd -Pre $Pre
}
