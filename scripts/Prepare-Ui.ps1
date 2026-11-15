. "$PSScriptRoot\Environment.ps1"
Add-Type -AssemblyName System.Drawing
$oldResources = [System.IO.Path]::GetFullPath((Join-Path $projectRoot '..\town-evolution\assets\resources'))
$uiDirectory = Join-Path $projectRoot 'src\Town.Content\Ui'
New-Item -ItemType Directory -Path $uiDirectory -Force | Out-Null
function Export-Icon([string]$source,[string]$name,[System.Drawing.Rectangle]$crop) {
    $image = [System.Drawing.Image]::FromFile($source)
    try {
        if ($crop.Width -eq 0) { $crop = [System.Drawing.Rectangle]::new(0,0,$image.Width,$image.Height) }
        $bitmap = [System.Drawing.Bitmap]::new(256,256,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $drawing = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $drawing.Clear([System.Drawing.Color]::Transparent)
            $drawing.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scale = [Math]::Min(248.0/$crop.Width,248.0/$crop.Height)
            $destination = [System.Drawing.Rectangle]::new([int]((256-$crop.Width*$scale)/2),[int]((256-$crop.Height*$scale)/2),[int]($crop.Width*$scale),[int]($crop.Height*$scale))
            $drawing.DrawImage($image,$destination,$crop,[System.Drawing.GraphicsUnit]::Pixel)
            $bitmap.Save((Join-Path $uiDirectory "$name.png"),[System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $drawing.Dispose();$bitmap.Dispose() }
    } finally { $image.Dispose() }
}
foreach ($icon in @('digging-tool','hammer','axe','sickle','watering-can','saw','digging-tools','digging-pickaxe','carrot','rice','mud','tree-seed','sapling','flour','milled-rice','bread','roasted-carrot','fertilizer','wood','stone','wheat','lettuce')) { Export-Icon (Join-Path $oldResources "crop-icons\$icon.png") $icon ([System.Drawing.Rectangle]::Empty) }
Export-Icon (Join-Path $oldResources 'crop-icons\soil.png') 'soil' ([System.Drawing.Rectangle]::new(310,208,897,621))
Export-Icon (Join-Path $oldResources 'ui\settings.png') 'settings' ([System.Drawing.Rectangle]::Empty)
Export-Icon (Join-Path $oldResources 'ui\camera-rotate.png') 'camera-rotate' ([System.Drawing.Rectangle]::Empty)
$atlasPath = Join-Path $oldResources 'handbook\actions.png'
$atlasInfo = [System.Drawing.Image]::FromFile($atlasPath);$cellWidth=[int]($atlasInfo.Width/4);$cellHeight=[int]($atlasInfo.Height/4);$atlasInfo.Dispose()
$actions = @('build','manage','orders','tools','buy','sell','save','rotate','upgrade','ship','seed','close','quantity','return','collect','town')
for($i=0;$i -lt $actions.Length;$i++) { Export-Icon $atlasPath $actions[$i] ([System.Drawing.Rectangle]::new(($i%4)*$cellWidth,[int]([Math]::Floor($i/4)*$cellHeight),$cellWidth,$cellHeight)) }
Write-Output 'Prepared existing old-project HUD icons at bounded 256px size; no new artwork or download.'
Export-Icon (Join-Path $oldResources 'handbook\atlas.png') 'handbook' ([System.Drawing.Rectangle]::new(105,30,440,540))
