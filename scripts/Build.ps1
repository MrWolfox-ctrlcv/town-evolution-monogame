param([ValidateSet('Desktop','Android','All')][string]$Platform='Desktop')
. "$PSScriptRoot\Environment.ps1"
$availableLetter = @('R','S','U','V','W','X','Y','Z') | Where-Object { -not (Test-Path ($_ + ':\')) } | Select-Object -First 1
if (-not $availableLetter) { throw 'No free drive letter for the temporary ASCII content-build alias.' }
$buildAlias = $availableLetter + ':'
& subst.exe $buildAlias $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Could not create temporary content-build alias.' }
try {
    & "$PSScriptRoot\Prepare-Ui.ps1"
    & "$PSScriptRoot\Build-Font.ps1"
    & node (Join-Path $projectRoot 'scripts\convert-gltf.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'Asset conversion failed.' }
    $buildRoot = $buildAlias + '\'
    if ($Platform -in @('Desktop','All')) {
        & $dotnetExe build (Join-Path $buildRoot 'src\Town.Desktop\Town.Desktop.csproj') --no-restore -m:1 -p:UseSharedCompilation=false -v:minimal
        if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
    }
    if ($Platform -in @('Android','All')) {
        & $dotnetExe build (Join-Path $buildRoot 'src\Town.Android\Town.Android.csproj') --no-restore -m:1 -p:UseSharedCompilation=false "-p:AndroidSdkDirectory=$buildRoot.tools\android-sdk" '-p:AndroidSdkBuildToolsVersion=35.0.0' '-p:JavaSdkDirectory=C:\Program Files\Java\jdk-21' -v:minimal
        if ($LASTEXITCODE -ne 0) { throw 'Android build failed.' }
    }
} finally { & subst.exe $buildAlias /D }
