. "$PSScriptRoot\Environment.ps1"
Add-Type -AssemblyName System.Drawing
$sourceText = (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -Recurse | Where-Object { $_.Name -ne 'FontCharacters.cs' -and $_.FullName -notmatch '\\(obj|bin)\\' } | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join "`n"
$sourceText += Get-Content -LiteralPath (Join-Path $projectRoot 'src\Town.Core\Data\recipes.json') -Raw -Encoding UTF8
$characterSet = [System.Collections.Generic.SortedSet[char]]::new()
32..126 | ForEach-Object { [void]$characterSet.Add([char]$_) }
foreach ($character in $sourceText.ToCharArray()) { if ([int]$character -gt 127 -and -not [char]::IsControl($character)) { [void]$characterSet.Add($character) } }
$characters = -join $characterSet
$requiredHeight = [int]([Math]::Ceiling($characters.Length / 32.0) * 64)
$height = 64
while ($height -lt $requiredHeight) { $height *= 2 }
$bitmap = [System.Drawing.Bitmap]::new(2048, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphicsText = [System.Drawing.Graphics]::FromImage($bitmap)
$graphicsText.Clear([System.Drawing.Color]::Transparent)
$graphicsText.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$font = [System.Drawing.Font]::new('Microsoft YaHei',44,[System.Drawing.FontStyle]::Regular,[System.Drawing.GraphicsUnit]::Pixel)
$brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
for ($i=0; $i -lt $characters.Length; $i++) { $graphicsText.DrawString([string]$characters[$i],$font,$brush,[single](($i % 32)*64),[single]([Math]::Floor($i / 32)*64)) }
$outputDirectory = Join-Path $projectRoot 'src\Town.Content\Generated'
$bitmap.Save((Join-Path $outputDirectory 'ui-font.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$escaped = ($characters.ToCharArray() | ForEach-Object { '\u' + ('{0:x4}' -f [int]$_) }) -join ''
$fontCode = 'namespace Town.Game; internal static class FontCharacters { public const string Characters = "' + $escaped + '"; }'
Set-Content -LiteralPath (Join-Path $projectRoot 'src\Town.Game\FontCharacters.cs') -Value $fontCode -Encoding utf8
$brush.Dispose();$font.Dispose();$graphicsText.Dispose();$bitmap.Dispose()
Write-Output "Built local UI font atlas: $($characters.Length) glyphs, no font download."
