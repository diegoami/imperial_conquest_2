#Requires -Version 5.0
<#
.SYNOPSIS
Generates a deterministic placeholder asset pack for testing and development.

.DESCRIPTION
Creates flat-color PNG files for unit/terrain/city markers, terrain tiles, and silent audio stubs.
All output is byte-identical across runs, suitable for checking into version control and testing.

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

# CRC32 table - computed once
$script:crcTable = $null

function Initialize-CRC32Table {
    if ($script:crcTable -ne $null) { return }
    $script:crcTable = @(0) * 256
    for ($n = 0; $n -lt 256; $n++) {
        $crc = $n
        for ($k = 0; $k -lt 8; $k++) {
            if (($crc -band 1) -eq 1) {
                $crc = 0xEDB88320 -bxor ($crc -shr 1)
            } else {
                $crc = $crc -shr 1
            }
        }
        $script:crcTable[$n] = $crc
    }
}

function Calculate-CRC32 {
    param([byte[]]$Data)
    Initialize-CRC32Table

    $crc = 0xFFFFFFFF
    foreach ($byte in $Data) {
        $crc = $script:crcTable[($crc -bxor $byte) -band 0xFF] -bxor ($crc -shr 8)
    }
    $crc -bxor 0xFFFFFFFF
}

function New-PlaceholderPNG {
    param(
        [string]$Path,
        [byte]$Red = 100,
        [byte]$Green = 100,
        [byte]$Blue = 100
    )

    # PNG signature
    $pngSig = [byte[]]@(137, 80, 78, 71, 13, 10, 26, 10)

    # IHDR chunk: 32x32 RGB image
    $ihdrData = [byte[]]@(0, 0, 0, 32, 0, 0, 0, 32, 8, 2, 0, 0, 0)
    $ihdrType = [byte[]]@(73, 72, 68, 82)  # "IHDR"
    $ihdrCrc = Calculate-CRC32 ($ihdrType + $ihdrData)
    $ihdrLength = [byte[]]@(0, 0, 0, 13)  # big-endian

    # Build IHDR chunk: length + type + data + CRC
    $ihdrChunk = $ihdrLength + $ihdrType + $ihdrData + [byte[]]@(
        [byte](($ihdrCrc -shr 24) -band 0xFF),
        [byte](($ihdrCrc -shr 16) -band 0xFF),
        [byte](($ihdrCrc -shr 8) -band 0xFF),
        [byte]($ihdrCrc -band 0xFF)
    )

    # Generate pixel data: 32x32 RGB
    $pixelData = New-Object byte[] (32 * 32 * 3 + 32)
    $idx = 0

    for ($y = 0; $y -lt 32; $y++) {
        $pixelData[$idx] = 0  # Filter type: None
        $idx += 1

        for ($x = 0; $x -lt 32; $x++) {
            $pixelData[$idx] = $Red
            $idx += 1
            $pixelData[$idx] = $Green
            $idx += 1
            $pixelData[$idx] = $Blue
            $idx += 1
        }
    }

    # Compress with DEFLATE
    Add-Type -AssemblyName System.IO.Compression
    $ms = New-Object System.IO.MemoryStream
    $deflate = New-Object System.IO.Compression.DeflateStream($ms, [System.IO.Compression.CompressionMode]::Compress)
    $deflate.Write($pixelData, 0, $pixelData.Length)
    $deflate.Close()
    $compressedData = $ms.ToArray()

    # IDAT chunk: length + type + data + CRC
    $idatType = [byte[]]@(73, 68, 65, 84)  # "IDAT"
    $idatCrc = Calculate-CRC32 ($idatType + $compressedData)
    $idatLength = [byte[]]@(
        [byte](($compressedData.Length -shr 24) -band 0xFF),
        [byte](($compressedData.Length -shr 16) -band 0xFF),
        [byte](($compressedData.Length -shr 8) -band 0xFF),
        [byte]($compressedData.Length -band 0xFF)
    )

    $idatChunk = $idatLength + $idatType + $compressedData + [byte[]]@(
        [byte](($idatCrc -shr 24) -band 0xFF),
        [byte](($idatCrc -shr 16) -band 0xFF),
        [byte](($idatCrc -shr 8) -band 0xFF),
        [byte]($idatCrc -band 0xFF)
    )

    # IEND chunk: length + type + CRC
    $iendType = [byte[]]@(73, 69, 78, 68)  # "IEND"
    $iendCrc = Calculate-CRC32 $iendType
    $iendChunk = [byte[]]@(0, 0, 0, 0) + $iendType + [byte[]]@(
        [byte](($iendCrc -shr 24) -band 0xFF),
        [byte](($iendCrc -shr 16) -band 0xFF),
        [byte](($iendCrc -shr 8) -band 0xFF),
        [byte]($iendCrc -band 0xFF)
    )

    # Write PNG file
    $allBytes = $pngSig + $ihdrChunk + $idatChunk + $iendChunk
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

Write-Host "Generating PNG files..."

$pngMappings = @{
    "units/light_infantry.png" = $colors["light_infantry"]
    "units/heavy_infantry.png" = $colors["heavy_infantry"]
    "units/archers.png" = $colors["archers"]
    "units/light_cavalry.png" = $colors["light_cavalry"]
    "units/heavy_cavalry.png" = $colors["heavy_cavalry"]
    "army/tier1.png" = $colors["tier1_army"]
    "army/tier2.png" = $colors["tier2_army"]
    "army/tier3.png" = $colors["tier3_army"]
    "fleet/tier1.png" = $colors["tier1_fleet"]
    "fleet/tier2.png" = $colors["tier2_fleet"]
    "fleet/tier3.png" = $colors["tier3_fleet"]
    "city/tier1.png" = $colors["tier1_city"]
    "city/tier2.png" = $colors["tier2_city"]
    "city/tier3.png" = $colors["tier3_city"]
    "city/capital.png" = $colors["capital"]
    "terrain/plain.png" = $colors["plain"]
    "terrain/desert.png" = $colors["desert"]
    "terrain/forest.png" = $colors["forest"]
    "terrain/mountain.png" = $colors["mountain"]
    "terrain/river.png" = $colors["river"]
    "terrain/sea_coastal.png" = $colors["coastal"]
    "terrain/sea_deep.png" = $colors["deep"]
}

foreach ($file in $pngMappings.Keys) {
    $color = $pngMappings[$file]
    $fullPath = Join-Path -Path $OutputPath -ChildPath $file
    New-PlaceholderPNG -Path $fullPath -Red $color[0] -Green $color[1] -Blue $color[2]
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
    $manifest.assets["unit.$unit.icon"] = "units/$unit.png"
}

for ($tier = 1; $tier -le 3; $tier++) {
    $manifest.assets["army.tier$tier.icon"] = "army/tier$tier.png"
    $manifest.assets["fleet.tier$tier.icon"] = "fleet/tier$tier.png"
    $manifest.assets["city.tier$tier.icon"] = "city/tier$tier.png"
}

$manifest.assets["city.capital.icon"] = "city/capital.png"

foreach ($terrain in @("plain", "desert", "forest", "mountain", "river", "sea_coastal", "sea_deep")) {
    $manifest.assets["terrain.$terrain.tile"] = "terrain/$terrain.png"
}

foreach ($sfx in @("city_captured", "battle", "unit_move")) {
    $manifest.assets["sfx.$sfx"] = "sfx/$sfx.wav"
}

$manifestPath = Join-Path -Path $OutputPath -ChildPath "manifest.json"
$json = ConvertTo-Json $manifest -Depth 10
[System.IO.File]::WriteAllText($manifestPath, $json)

Write-Host "Success: Generated placeholder asset pack at: $OutputPath"
