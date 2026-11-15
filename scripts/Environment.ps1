$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$env:DOTNET_ROOT = Join-Path $projectRoot '.tools\dotnet'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.tools\nuget-http-cache'
$env:NUGET_SCRATCH = Join-Path $projectRoot '.tools\nuget-scratch'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:MSBUILDDISABLENODEREUSE = '1'
# Process scope only. Do not use the user's proxy or oulucloud nodes for downloads.
foreach ($proxyName in @('HTTP_PROXY','HTTPS_PROXY','ALL_PROXY')) {
    [Environment]::SetEnvironmentVariable($proxyName, $null, 'Process')
}
$env:NO_PROXY = '*'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$dotnetExe = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
Set-Location -LiteralPath $projectRoot
