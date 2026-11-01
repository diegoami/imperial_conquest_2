# T273: headless review maps from our public-source terrain/data, no desktop capture.
# Windows PowerShell 5.1 supplies System.Drawing; ordinary campaign rebuilds stay Python-only.
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$nearRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $nearRoot 'docs\images' }
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$terrain = [System.Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'near-east.png'))
try {
    foreach ($year in @(1350, 750)) {
        $world = Get-Content -LiteralPath (Join-Path $nearRoot "data\worlds\near-east-$($year)bc.json") -Raw -Encoding UTF8 | ConvertFrom-Json
        $image = New-Object System.Drawing.Bitmap 1500,900
        $graphics = [System.Drawing.Graphics]::FromImage($image)
        $font = New-Object System.Drawing.Font 'Arial',10
        $titleFont = New-Object System.Drawing.Font 'Arial',21,([System.Drawing.FontStyle]::Bold)
        $small = New-Object System.Drawing.Font 'Arial',9
        try {
            $graphics.Clear([System.Drawing.Color]::FromArgb(240,231,207))
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
            $graphics.DrawString("Near East, c$year BC",$titleFont,[System.Drawing.Brushes]::Black,16,10)
            $map = New-Object System.Drawing.Rectangle 16,54,1104,740
            $graphics.DrawImage($terrain,$map,0,0,$terrain.Width,$terrain.Height,[System.Drawing.GraphicsUnit]::Pixel)
            $graphics.DrawRectangle([System.Drawing.Pens]::Black,$map)
            $palette = @{}
            foreach ($nation in $world.nations) { $palette[$nation.id] = [System.Drawing.ColorTranslator]::FromHtml($nation.colorHex) }
            foreach ($city in $world.cities) {
                $brush = New-Object System.Drawing.SolidBrush $palette[$city.owner]
                try {
                    $x = 16 + ($city.x + .5) * 4
                    $y = 54 + ($city.y + .5) * 4
                    $graphics.FillEllipse($brush,[single]($x-2),[single]($y-2),4,4)
                    $graphics.DrawEllipse([System.Drawing.Pens]::Black,[single]($x-2),[single]($y-2),4,4)
                } finally { $brush.Dispose() }
            }
            $line = 62
            foreach ($nation in $world.nations | Where-Object capitalCityId) {
                $capital = $world.cities | Where-Object id -eq $nation.capitalCityId | Select-Object -First 1
                $x = 16 + ($capital.x + .5) * 4
                $y = 54 + ($capital.y + .5) * 4
                $size = $graphics.MeasureString([string]$capital.name,$small)
                $labelX = [Math]::Min(1118-$size.Width,[Math]::Max(18,$x+6))
                $labelY = [Math]::Max(56,$y-15)
                $graphics.FillRectangle([System.Drawing.Brushes]::Linen,[single]$labelX,[single]$labelY,[single]$size.Width,[single]$size.Height)
                $graphics.DrawString([string]$capital.name,$small,[System.Drawing.Brushes]::Black,[single]$labelX,[single]$labelY)
                $graphics.DrawEllipse([System.Drawing.Pens]::White,[single]($x-4),[single]($y-4),8,8)
                $brush = New-Object System.Drawing.SolidBrush $palette[$nation.id]
                try { $graphics.FillRectangle($brush,1140,$line,14,14) } finally { $brush.Dispose() }
                $count = @($world.cities | Where-Object owner -eq $nation.id).Count
                $graphics.DrawString("$($nation.name) ($count)",$font,[System.Drawing.Brushes]::Black,1162,$line)
                $line += 28
            }
            $graphics.DrawString('Capitals / administrative centres labelled. Numbers are represented centres.',$font,[System.Drawing.Brushes]::Black,16,810)
            $graphics.DrawString('Political seats, regional centres, population and starting balance are documented game abstractions.',$font,[System.Drawing.Brushes]::Black,16,834)
            $graphics.DrawString('Locations: Pleiades contributors, CC BY 3.0. Geography: Natural Earth public domain. Source references accompany campaign data.',$small,[System.Drawing.Brushes]::Black,16,861)
            $image.Save((Join-Path $OutputDirectory "near-east-$($year)bc.png"),[System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $small.Dispose(); $titleFont.Dispose(); $font.Dispose(); $graphics.Dispose(); $image.Dispose() }
    }
} finally { $terrain.Dispose() }
