param([switch]$CheckOnly)
. "$PSScriptRoot\Environment.ps1"
$assembly = Join-Path $projectRoot 'src\Town.Desktop\bin\Debug\net10.0\Town.Desktop.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw '请先构建 Windows 版。此启动文件不会下载或安装依赖。' }
if ($CheckOnly) {
    & $dotnetExe --version
    if ($LASTEXITCODE -ne 0) { throw '项目本地 .NET 无法运行。' }
    Write-Output 'Windows 试玩入口检查通过。'
    exit 0
}
$reviewDirectory = Join-Path $projectRoot 'review\windows-manual'
New-Item -ItemType Directory -Path $reviewDirectory -Force | Out-Null
Write-Output '拖左侧工具采集，点掉落物收集；右侧产品拖出叠堆，用锤子合成。长按建筑拖动，右下查看图鉴；按 F12 保存检查截图和状态。'
& $dotnetExe $assembly --review $reviewDirectory
exit $LASTEXITCODE
