. "$PSScriptRoot\Environment.ps1"
$sdkVersion = '10.0.404'
$sdkHash = '330ddd2665b43cd2e428216a0d8ea1ac7bb19b5434fdc530e62010dc3b52823a620bf95489d628e1ed6f5122401793ee4887cca4a019122361a1231497f632ee'
$downloadRoot = Join-Path $projectRoot '.tools\downloads'
New-Item -ItemType Directory -Path $downloadRoot,$env:DOTNET_ROOT -Force | Out-Null
$archive = Join-Path $downloadRoot "dotnet-sdk-$sdkVersion-win-x64.zip"
if (-not (Test-Path -LiteralPath $dotnetExe)) {
    if (-not (Test-Path -LiteralPath $archive)) {
        & curl.exe --noproxy '*' --fail --location --retry 2 --connect-timeout 20 --output $archive "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-win-x64.zip"
        if ($LASTEXITCODE -ne 0) { throw 'Direct SDK download failed; no proxy fallback was attempted.' }
    }
    $actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash.ToLowerInvariant()
    if ($actualHash -ne $sdkHash) { throw 'SDK SHA512 does not match official release metadata. Archive retained for inspection.' }
    & tar.exe -xf $archive -C $env:DOTNET_ROOT
    if ($LASTEXITCODE -ne 0) { throw 'SDK extraction failed.' }
}
& $dotnetExe --info
if ($LASTEXITCODE -ne 0) { throw 'Local SDK did not start.' }
