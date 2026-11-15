param([switch]$CheckOnly,[switch]$Capture)
. "$PSScriptRoot\Environment.ps1"
$assembly = Join-Path $projectRoot 'src\Town.Desktop\bin\Debug\net10.0\Town.Desktop.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw 'Build the Windows player first (BuildWindows.cmd). This launcher never downloads or installs anything.' }
if ($CheckOnly) { & $dotnetExe --version; exit $LASTEXITCODE }
$reviewDirectory = Join-Path $projectRoot 'review\npc-demo'
New-Item -ItemType Directory -Path $reviewDirectory -Force | Out-Null
Write-Output 'NPC demo island: click a resident to select, hold Shift and click the ground to send them there. The panel on the right spawns another resident or cancels the task.'
$arguments = @($assembly,'--npc-demo','--review',$reviewDirectory)
if ($Capture) { $arguments += '--exit-after-capture' }
& $dotnetExe @arguments
exit $LASTEXITCODE
