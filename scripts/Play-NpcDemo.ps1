param([switch]$CheckOnly,[switch]$Capture,[switch]$Town,[string]$IslandSnapshot,[switch]$Review)
. "$PSScriptRoot\Environment.ps1"
$playerCandidates = @('src\Town.Desktop\bin\Debug\net10.0\Town.Desktop.dll','src\Town.Desktop\bin\npc-playtest\Town.Desktop.dll')
$assembly = $playerCandidates | ForEach-Object {
    Get-Item -LiteralPath (Join-Path $projectRoot $_) -ErrorAction SilentlyContinue
} | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $assembly) { throw 'Build the Windows player first (BuildWindows.cmd). This launcher never downloads or installs anything.' }
if ($CheckOnly) { & $dotnetExe --version; exit $LASTEXITCODE }
$reviewDirectory = Join-Path $projectRoot $(if ($Town) { 'review\npc-town-v2' } else { 'review\npc-demo' })
Write-Output 'Left-click a resident to select. Left-click ground or press middle mouse to deselect. Right-click to assign work.'
$arguments = @($assembly,$(if ($Town) { '--npc-town' } else { '--npc-demo' }),
    '--save-dir',(Join-Path $reviewDirectory 'isolated-save'))
if ($Capture -or $Review) { $arguments += @('--review',$reviewDirectory) }
if ($Town -and $IslandSnapshot) { $arguments += @('--island-snapshot',$IslandSnapshot) }
if ($Capture) { $arguments += '--exit-after-capture' }
& $dotnetExe @arguments
exit $LASTEXITCODE
