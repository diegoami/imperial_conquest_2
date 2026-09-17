<#
.SYNOPSIS
Generates a deterministic placeholder asset pack for testing and development.

.DESCRIPTION
Creates flat-color PNG files for unit/terrain/city markers, terrain tiles, and silent audio stubs.
All output is byte-identical across runs, suitable for checking into version control and testing.

.PARAMETER OutputPath
The directory to write the asset pack to. Created if it doesn't exist.
Typically something like assets/packs/placeholder/.

.PARAMETER Seed
Seed for deterministic color generation (currently unused, reserved for future randomization).
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
@('units', 'army', 'fleet', 'city', 'terrain', 'sfx') | ForEach-Object {
    $dir = Join-Path $OutputPath $_
    if (!(Test-Path $dir)) {
        $null = New-Item -ItemType Directory -Path $dir -Force
    }
}

# Create a minimal valid 1x1 PNG with a fixed color
# This is the absolute minimum PNG: 1x1 pixel, 8-bit grayscale
# PNG signature + IHDR chunk + gAMA chunk + IDAT chunk + IEND chunk
# All deterministic, no compression randomness
function New-PlaceholderPNG {
    param(
        [string]$Path,
        [byte]$Red = 100,
        [byte]$Green = 100,
        [byte]$Blue = 100
    )

    # PNG signature: 137 80 78 71 13 10 26 10
    $pngSig = @(137, 80, 78, 71, 13, 10, 26, 10)

    # IHDR chunk (8 bytes width, 8 bytes height, 1 byte bit depth, 1 byte color type, ...)
    # Width: 32, Height: 32, Bit depth: 8, Color type: 2 (RGB), Compression: 0, Filter: 0, Interlace: 0
    $ihdrData = @(0, 0, 0, 32, 0, 0, 0, 32, 8, 2, 0, 0, 0)
    $ihdrChunk = @(
        0, 0, 0, 13  # Chunk length
        73, 72, 68, 82  # "IHDR"
    ) + $ihdrData + (CRC32 (84, 72, 68, 82) + $ihdrData)

    # IDAT chunk with minimal compressed data for a 32x32 solid color image
    # A solid color image compresses very well
    $pixelData = New-Object 'byte[]' (32 * 32 * 3 + 32)
    $idx = 0

    # Each scanline: filter byte (0 = None) + RGB pixels
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

    # Use DEFLATE compression (minimal for solid color)
    Add-Type -AssemblyName System.IO.Compression
    $ms = New-Object System.IO.MemoryStream
    $gzip = New-Object System.IO.Compression.DeflateStream($ms, [System.IO.Compression.CompressionMode]::Compress)
    $gzip.Write($pixelData, 0, $pixelData.Length)
    $gzip.Close()
    $compressedData = $ms.ToArray()

    $idatChunk = @(
        [byte]($compressedData.Length -shr 24),
        [byte]($compressedData.Length -shr 16),
        [byte]($compressedData.Length -shr 8),
        [byte]$compressedData.Length
        73, 68, 65, 84  # "IDAT"
    ) + $compressedData + (CRC32 @(73, 68, 65, 84) + $compressedData)

    # IEND chunk
    $iendChunk = @(0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130)

    # Write the PNG
    $allBytes = @() + $pngSig + $ihdrChunk + $idatChunk + $iendChunk
    [System.IO.File]::WriteAllBytes($Path, $allBytes)
}

# Simple CRC32 calculation for PNG chunks
function CRC32 {
    param([byte[]]$Data)

    $crcTable = New-Object 'uint[]' 256
    for ($n = 0; $n -lt 256; $n++) {
        $crc = [uint]$n
        for ($k = 0; $k -lt 8; $k++) {
            if (($crc -band 1) -eq 1) {
                $crc = 0xEDB88320 -bxor ($crc -shr 1)
            } else {
                $crc = $crc -shr 1
            }
        }
        $crcTable[$n] = $crc
    }

    $crc = 0xFFFFFFFF
    foreach ($byte in $Data) {
        $crc = $crcTable[($crc -bxor $byte) -band 0xFF] -bxor ($crc -shr 8)
    }

    $crc = $crc -bxor 0xFFFFFFFF
    @(
        [byte]($crc -shr 24),
        [byte]($crc -shr 16),
        [byte]($crc -shr 8),
        [byte]$crc
    )
}

# Create a minimal valid WAV file (silent, 1 second)
function New-SilentWAV {
    param([string]$Path)

    $sampleRate = 44100
    $duration = 1  # 1 second
    $numSamples = $sampleRate * $duration

    # WAV header
    $riffHeader = [System.Text.Encoding]::ASCII.GetBytes("RIFF")
    $waveHeader = [System.Text.Encoding]::ASCII.GetBytes("WAVE")
    $fmtHeader = [System.Text.Encoding]::ASCII.GetBytes("fmt ")
    $dataHeader = [System.Text.Encoding]::ASCII.GetBytes("data")

    # Subchunk1 size: 16 for PCM
    $subchunk1Size = 16
    # Subchunk2 size: 2 bytes per sample × num samples
    $subchunk2Size = 2 * $numSamples
    # Total file size - 8
    $fileSize = 36 + $subchunk2Size

    $wav = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($wav)

    # RIFF header
    $writer.Write($riffHeader)
    $writer.Write([uint32]$fileSize)
    $writer.Write($waveHeader)

    # fmt subchunk
    $writer.Write($fmtHeader)
    $writer.Write([uint32]$subchunk1Size)
    $writer.Write([uint16]1)  # Audio format: PCM
    $writer.Write([uint16]1)  # Channels: mono
    $writer.Write([uint32]$sampleRate)
    $writer.Write([uint32]$sampleRate * 2)  # Byte rate
    $writer.Write([uint16]2)  # Block align
    $writer.Write([uint16]16)  # Bits per sample

    # data subchunk
    $writer.Write($dataHeader)
    $writer.Write([uint32]$subchunk2Size)

    # Silent audio (all zeros)
    for ($i = 0; $i -lt $numSamples; $i++) {
        $writer.Write([int16]0)
    }

    $writer.Close()
    [System.IO.File]::WriteAllBytes($Path, $wav.ToArray())
}

# Define colors for each asset type (deterministic)
$colors = @{
    # Unit icons
    "unit.light_infantry" = @(200, 150, 100)
    "unit.heavy_infantry" = @(100, 100, 150)
    "unit.archers" = @(150, 200, 100)
    "unit.light_cavalry" = @(200, 200, 100)
    "unit.heavy_cavalry" = @(100, 150, 200)

    # Army markers
    "army.tier1" = @(180, 100, 100)
    "army.tier2" = @(200, 150, 100)
    "army.tier3" = @(220, 200, 100)

    # Fleet markers
    "fleet.tier1" = @(100, 180, 200)
    "fleet.tier2" = @(100, 200, 220)
    "fleet.tier3" = @(100, 220, 255)

    # City markers
    "city.tier1" = @(150, 150, 100)
    "city.tier2" = @(180, 180, 100)
    "city.tier3" = @(220, 220, 100)
    "city.capital" = @(255, 215, 0)

    # Terrain
    "terrain.plain" = @(144, 238, 144)
    "terrain.desert" = @(210, 180, 140)
    "terrain.forest" = @(34, 139, 34)
    "terrain.mountain" = @(128, 128, 128)
    "terrain.river" = @(64, 164, 223)
    "terrain.sea_coastal" = @(100, 149, 237)
    "terrain.sea_deep" = @(30, 100, 180)
}

Write-Host "Generating placeholder PNG icons..."

# Generate unit icons
foreach ($unit in @("light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry")) {
    $key = "unit.$unit"
    $color = $colors[$key]
    $path = Join-Path $OutputPath "units" "$unit.png"
    New-PlaceholderPNG -Path $path -Red $color[0] -Green $color[1] -Blue $color[2]
    Write-Host "  Created $path"
}

# Generate army marker icons
foreach ($tier in @("tier1", "tier2", "tier3")) {
    $key = "army.$tier"
    $color = $colors[$key]
    $path = Join-Path $OutputPath "army" "$tier.png"
    New-PlaceholderPNG -Path $path -Red $color[0] -Green $color[1] -Blue $color[2]
    Write-Host "  Created $path"
}

# Generate fleet marker icons
foreach ($tier in @("tier1", "tier2", "tier3")) {
    $key = "fleet.$tier"
    $color = $colors[$key]
    $path = Join-Path $OutputPath "fleet" "$tier.png"
    New-PlaceholderPNG -Path $path -Red $color[0] -Green $color[1] -Blue $color[2]
    Write-Host "  Created $path"
}

# Generate city marker icons
foreach ($tier in @("tier1", "tier2", "tier3")) {
    $key = "city.$tier"
    $color = $colors[$key]
    $path = Join-Path $OutputPath "city" "$tier.png"
    New-PlaceholderPNG -Path $path -Red $color[0] -Green $color[1] -Blue $color[2]
    Write-Host "  Created $path"
}

# Generate capital icon
$capitalColor = $colors["city.capital"]
$capitalPath = Join-Path $OutputPath "city" "capital.png"
New-PlaceholderPNG -Path $capitalPath -Red $capitalColor[0] -Green $capitalColor[1] -Blue $capitalColor[2]
Write-Host "  Created $capitalPath"

# Generate terrain tiles
foreach ($terrain in @("plain", "desert", "forest", "mountain", "river")) {
    $key = "terrain.$terrain"
    $color = $colors[$key]
    $path = Join-Path $OutputPath "terrain" "$terrain.png"
    New-PlaceholderPNG -Path $path -Red $color[0] -Green $color[1] -Blue $color[2]
    Write-Host "  Created $path"
}

# Generate sea tiles
foreach ($sea in @("coastal", "deep")) {
    $key = "terrain.sea_$sea"
    $color = $colors[$key]
    $path = Join-Path $OutputPath "terrain" "sea_$sea.png"
    New-PlaceholderPNG -Path $path -Red $color[0] -Green $color[1] -Blue $color[2]
    Write-Host "  Created $path"
}

Write-Host "Generating placeholder audio files..."

# Generate audio stubs
foreach ($sfx in @("city_captured", "battle", "unit_move")) {
    $path = Join-Path $OutputPath "sfx" "$sfx.wav"
    New-SilentWAV -Path $path
    Write-Host "  Created $path"
}

# Generate manifest JSON
$manifest = @{
    schemaVersion = 1
    name = "Placeholder Asset Pack"
    description = "Generated placeholder assets — flat colors, silent audio stubs, good enough for development and testing."
    assets = @{
        # Units
        "unit.light_infantry.icon" = "units/light_infantry.png"
        "unit.heavy_infantry.icon" = "units/heavy_infantry.png"
        "unit.archers.icon" = "units/archers.png"
        "unit.light_cavalry.icon" = "units/light_cavalry.png"
        "unit.heavy_cavalry.icon" = "units/heavy_cavalry.png"

        # Army
        "army.tier1.icon" = "army/tier1.png"
        "army.tier2.icon" = "army/tier2.png"
        "army.tier3.icon" = "army/tier3.png"

        # Fleet
        "fleet.tier1.icon" = "fleet/tier1.png"
        "fleet.tier2.icon" = "fleet/tier2.png"
        "fleet.tier3.icon" = "fleet/tier3.png"

        # Cities
        "city.tier1.icon" = "city/tier1.png"
        "city.tier2.icon" = "city/tier2.png"
        "city.tier3.icon" = "city/tier3.png"
        "city.capital.icon" = "city/capital.png"

        # Terrain
        "terrain.plain.tile" = "terrain/plain.png"
        "terrain.desert.tile" = "terrain/desert.png"
        "terrain.forest.tile" = "terrain/forest.png"
        "terrain.mountain.tile" = "terrain/mountain.png"
        "terrain.river.tile" = "terrain/river.png"
        "terrain.sea_coastal.tile" = "terrain/sea_coastal.png"
        "terrain.sea_deep.tile" = "terrain/sea_deep.png"

        # Sound effects
        "sfx.city_captured" = "sfx/city_captured.wav"
        "sfx.battle" = "sfx/battle.wav"
        "sfx.unit_move" = "sfx/unit_move.wav"
    }
}

$manifestPath = Join-Path $OutputPath "manifest.json"
$manifestJson = $manifest | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($manifestPath, $manifestJson)
Write-Host "Created manifest: $manifestPath"

Write-Host "Successfully generated placeholder asset pack at: $OutputPath"
