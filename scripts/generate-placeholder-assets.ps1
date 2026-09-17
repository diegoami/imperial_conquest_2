#Requires -Version 5.0
<#
.SYNOPSIS
Generates a deterministic placeholder asset pack for testing and development.

.DESCRIPTION
Creates flat-color, uncompressed 24-bit BMP files for unit/terrain/city markers, terrain tiles,
and silent audio stubs. All output is byte-identical across runs, suitable for checking into
version control and testing.

.PARAMETER OutputPath
The directory to write the asset pack to. Created if it doesn't exist.
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$OutputPath,
    [int]$Seed = 42
)

$ErrorActionPreference = "Stop"

# Ensure output directory exists
if (!(Test-Path $OutputPath)) {
    $null = New-Item -ItemType Directory -Path $OutputPath -Force
    Write-Host "Created directory: $OutputPath"
}

# Create subdirectories
foreach ($dir in @('units', 'army', 'fleet', 'city', 'terrain', 'sfx')) {
    $subdir = Join-Path -Path $OutputPath -ChildPath $dir
    if (!(Test-Path $subdir)) {
        $null = New-Item -ItemType Directory -Path $subdir -Force
    }
}

# Placeholder images are uncompressed 24-bit BMP, not PNG: a BMP has no checksum and no zlib
# stream, so the whole class of bug that hit PNG generation here (chunk CRCs computed with
# PowerShell 5.1's signed-32-bit arithmetic misbehaving under -shr/-bxor without careful
# [uint32]/-band 0xFFFFFFFF masking) simply does not exist for this format. See PR #96.
function New-PlaceholderBMP {
    param(
        [string]$Path,
        [byte]$Red = 100,
        [byte]$Green = 100,
        [byte]$Blue = 100
    )

    $width = 32
    $height = 32
    $rowSizeUnpadded = $width * 3
    $rowPadding = (4 - ($rowSizeUnpadded % 4)) % 4
    $rowSize = $rowSizeUnpadded + $rowPadding
    $pixelDataSize = [uint32]($rowSize * $height)
    $pixelDataOffset = [uint32]54  # 14-byte file header + 40-byte DIB header
    $fileSize = [uint32]($pixelDataOffset + $pixelDataSize)

    # BITMAPFILEHEADER (14 bytes), all fields little-endian per the BMP spec.
    $fileHeader = [byte[]]@(0x42, 0x4D)  # "BM"
    $fileHeader += [BitConverter]::GetBytes($fileSize)
    $fileHeader += [byte[]]@(0, 0, 0, 0)  # reserved
    $fileHeader += [BitConverter]::GetBytes($pixelDataOffset)

    # BITMAPINFOHEADER (40 bytes): the classic Windows DIB header.
    $infoHeader = [BitConverter]::GetBytes([uint32]40)                 # header size
    $infoHeader += [BitConverter]::GetBytes([int32]$width)
    $infoHeader += [BitConverter]::GetBytes([int32]$height)            # positive => bottom-up
    $infoHeader += [BitConverter]::GetBytes([uint16]1)                 # color planes
    $infoHeader += [BitConverter]::GetBytes([uint16]24)                # bits per pixel
    $infoHeader += [BitConverter]::GetBytes([uint32]0)                 # BI_RGB, no compression
    $infoHeader += [BitConverter]::GetBytes($pixelDataSize)
    $infoHeader += [BitConverter]::GetBytes([int32]0)                  # X pixels/metre
    $infoHeader += [BitConverter]::GetBytes([int32]0)                  # Y pixels/metre
    $infoHeader += [BitConverter]::GetBytes([uint32]0)                 # colors used
    $infoHeader += [BitConverter]::GetBytes([uint32]0)                 # important colors

    # Pixel array: bottom-up rows, BGR byte order, each row padded to a 4-byte boundary.
    $pixelData = New-Object byte[] $pixelDataSize
    $idx = 0
    for ($y = 0; $y -lt $height; $y++) {
        for ($x = 0; $x -lt $width; $x++) {
            $pixelData[$idx] = $Blue;  $idx++
            $pixelData[$idx] = $Green; $idx++
            $pixelData[$idx] = $Red;   $idx++
        }
        $idx += $rowPadding  # padding bytes stay zero
    }

    $allBytes = $fileHeader + $infoHeader + $pixelData
    [System.IO.File]::WriteAllBytes($Path, $allBytes)
}

function New-SilentWAV {
    param([string]$Path)

    $sampleRate = 44100
    $duration = 1
    $numSamples = $sampleRate * $duration

    $ms = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($ms)

    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("RIFF"))
    $writer.Write([uint32](36 + $numSamples * 2))
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("WAVE"))

    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("fmt "))
    $writer.Write([uint32]16)
    $writer.Write([uint16]1)
    $writer.Write([uint16]1)
    $writer.Write([uint32]$sampleRate)
    $writer.Write([uint32]($sampleRate * 2))
    $writer.Write([uint16]2)
    $writer.Write([uint16]16)

    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("data"))
    $writer.Write([uint32]($numSamples * 2))

    for ($i = 0; $i -lt $numSamples; $i++) {
        $writer.Write([int16]0)
    }

    $writer.Close()
    [System.IO.File]::WriteAllBytes($Path, $ms.ToArray())
}

# Color definitions
$colors = @{
    "light_infantry" = @(200, 150, 100)
    "heavy_infantry" = @(100, 100, 150)
    "archers" = @(150, 200, 100)
    "light_cavalry" = @(200, 200, 100)
    "heavy_cavalry" = @(100, 150, 200)
    "tier1_army" = @(180, 100, 100)
    "tier2_army" = @(200, 150, 100)
    "tier3_army" = @(220, 200, 100)
    "tier1_fleet" = @(100, 180, 200)
    "tier2_fleet" = @(100, 200, 220)
    "tier3_fleet" = @(100, 220, 255)
    "tier1_city" = @(150, 150, 100)
    "tier2_city" = @(180, 180, 100)
    "tier3_city" = @(220, 220, 100)
    "capital" = @(255, 215, 0)
    "plain" = @(144, 238, 144)
    "desert" = @(210, 180, 140)
    "forest" = @(34, 139, 34)
    "mountain" = @(128, 128, 128)
    "river" = @(64, 164, 223)
    "coastal" = @(100, 149, 237)
    "deep" = @(30, 100, 180)
}

Write-Host "Generating BMP files..."

$bmpMappings = @{
    "units/light_infantry.bmp" = $colors["light_infantry"]
    "units/heavy_infantry.bmp" = $colors["heavy_infantry"]
    "units/archers.bmp" = $colors["archers"]
    "units/light_cavalry.bmp" = $colors["light_cavalry"]
    "units/heavy_cavalry.bmp" = $colors["heavy_cavalry"]
    "army/tier1.bmp" = $colors["tier1_army"]
    "army/tier2.bmp" = $colors["tier2_army"]
    "army/tier3.bmp" = $colors["tier3_army"]
    "fleet/tier1.bmp" = $colors["tier1_fleet"]
    "fleet/tier2.bmp" = $colors["tier2_fleet"]
    "fleet/tier3.bmp" = $colors["tier3_fleet"]
    "city/tier1.bmp" = $colors["tier1_city"]
    "city/tier2.bmp" = $colors["tier2_city"]
    "city/tier3.bmp" = $colors["tier3_city"]
    "city/capital.bmp" = $colors["capital"]
    "terrain/plain.bmp" = $colors["plain"]
    "terrain/desert.bmp" = $colors["desert"]
    "terrain/forest.bmp" = $colors["forest"]
    "terrain/mountain.bmp" = $colors["mountain"]
    "terrain/river.bmp" = $colors["river"]
    "terrain/sea_coastal.bmp" = $colors["coastal"]
    "terrain/sea_deep.bmp" = $colors["deep"]
}

foreach ($file in $bmpMappings.Keys) {
    $color = $bmpMappings[$file]
    $fullPath = Join-Path -Path $OutputPath -ChildPath $file
    New-PlaceholderBMP -Path $fullPath -Red $color[0] -Green $color[1] -Blue $color[2]
    Write-Host "  + $file"
}

Write-Host "Generating WAV files..."
foreach ($sfx in @("city_captured", "battle", "unit_move")) {
    $filepath = Join-Path -Path $OutputPath -ChildPath "sfx/$sfx.wav"
    New-SilentWAV -Path $filepath
    Write-Host "  + sfx/$sfx.wav"
}

Write-Host "Generating manifest.json..."
$manifest = @{
    schemaVersion = 1
    name = "Placeholder Asset Pack"
    description = "Generated placeholder assets - flat colors, silent audio stubs, good enough for development and testing."
    assets = @{}
}

foreach ($unit in @("light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry")) {
    $manifest.assets["unit.$unit.icon"] = "units/$unit.bmp"
}

for ($tier = 1; $tier -le 3; $tier++) {
    $manifest.assets["army.tier$tier.icon"] = "army/tier$tier.bmp"
    $manifest.assets["fleet.tier$tier.icon"] = "fleet/tier$tier.bmp"
    $manifest.assets["city.tier$tier.icon"] = "city/tier$tier.bmp"
}

$manifest.assets["city.capital.icon"] = "city/capital.bmp"

foreach ($terrain in @("plain", "desert", "forest", "mountain", "river", "sea_coastal", "sea_deep")) {
    $manifest.assets["terrain.$terrain.tile"] = "terrain/$terrain.bmp"
}

foreach ($sfx in @("city_captured", "battle", "unit_move")) {
    $manifest.assets["sfx.$sfx"] = "sfx/$sfx.wav"
}

$manifestPath = Join-Path -Path $OutputPath -ChildPath "manifest.json"
$json = ConvertTo-Json $manifest -Depth 10
[System.IO.File]::WriteAllText($manifestPath, $json)

Write-Host "Success: Generated placeholder asset pack at: $OutputPath"
