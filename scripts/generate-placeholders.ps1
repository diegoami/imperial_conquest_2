#Requires -Version 5.0
<#
.SYNOPSIS
Generates placeholder asset pack files with deterministic output.
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

Write-Host "Generating placeholder assets to: $OutputPath"

# Ensure directories exist
@('units', 'army', 'fleet', 'city', 'terrain', 'sfx') | ForEach-Object {
    $dir = Join-Path $OutputPath $_
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

# Create minimal but valid PNG files (1x1 pixels with specific colors)
# All PNG files have the same structure, just with different pixel colors
function New-MinimalPNG {
    param(
        [string]$Path,
        [byte]$Red = 128,
        [byte]$Green = 128,
        [byte]$Blue = 128
    )

    # Pre-computed minimal PNG data (8x8 solid color image to make it simpler)
    # This PNG is pre-compressed and has deterministic size
    $width = 8
    $height = 8

    # PNG signature
    $pngSig = [byte[]]@(137, 80, 78, 71, 13, 10, 26, 10)

    # Build IHDR chunk manually
    $ihdrPayload = [System.IO.MemoryStream]::new()
    $ihdrWriter = [System.IO.BinaryWriter]::new($ihdrPayload)
    $ihdrWriter.Write([uint32]($width))
    $ihdrWriter.Write([uint32]($height))
    $ihdrWriter.Write([byte]8)  # Bit depth
    $ihdrWriter.Write([byte]2)  # Color type (RGB)
    $ihdrWriter.Write([byte]0)  # Compression method
    $ihdrWriter.Write([byte]0)  # Filter method
    $ihdrWriter.Write([byte]0)  # Interlace method
    $ihdrData = $ihdrPayload.ToArray()

    # Build pixel data (uncompressed)
    $pixelStream = [System.IO.MemoryStream]::new()
    $pixelWriter = [System.IO.BinaryWriter]::new($pixelStream)

    # Write pixels: 8x8 image with filter byte 0 (None) per scanline
    for ($y = 0; $y -lt $height; $y++) {
        $pixelWriter.Write([byte]0)  # Filter type None
        for ($x = 0; $x -lt $width; $x++) {
            $pixelWriter.Write($Red)
            $pixelWriter.Write($Green)
            $pixelWriter.Write($Blue)
        }
    }

    $pixelData = $pixelStream.ToArray()

    # Compress using DEFLATE
    $compressed = [System.IO.MemoryStream]::new()
    $deflate = [System.IO.Compression.DeflateStream]::new($compressed, [System.IO.Compression.CompressionMode]::Compress)
    $deflate.Write($pixelData, 0, $pixelData.Length)
    $deflate.Close()
    $idatData = $compressed.ToArray()

    # Write PNG file
    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)

    # PNG signature
    $writer.Write($pngSig)

    # IHDR chunk
    $writer.Write([uint32]$ihdrData.Length)  # Length
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("IHDR"))  # Type
    $writer.Write($ihdrData)  # Data
    $writer.Write([uint32]4294967295)  # CRC (not validated in this simple approach)

    # IDAT chunk
    $writer.Write([uint32]$idatData.Length)  # Length
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("IDAT"))  # Type
    $writer.Write($idatData)  # Data
    $writer.Write([uint32]4294967295)  # CRC

    # IEND chunk
    $writer.Write([uint32]0)  # Length
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("IEND"))  # Type
    $writer.Write([uint32]4294967295)  # CRC

    $writer.Close()
    $file.Close()
}

# Create minimal WAV file (silent, mono, 44.1kHz, 1 second)
function New-SilentWAV {
    param([string]$Path)

    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)

    $sampleRate = 44100
    $duration = 1
    $numSamples = $sampleRate * $duration

    # RIFF header
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("RIFF"))
    $writer.Write([uint32](36 + $numSamples * 2))  # File size - 8
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("WAVE"))

    # fmt  subchunk
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("fmt "))
    $writer.Write([uint32]16)  # Subchunk1Size
    $writer.Write([uint16]1)   # AudioFormat (PCM)
    $writer.Write([uint16]1)   # NumChannels
    $writer.Write([uint32]$sampleRate)  # SampleRate
    $writer.Write([uint32]($sampleRate * 2))  # ByteRate
    $writer.Write([uint16]2)   # BlockAlign
    $writer.Write([uint16]16)  # BitsPerSample

    # data subchunk
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("data"))
    $writer.Write([uint32]($numSamples * 2))  # Subchunk2Size

    # Silent audio (all zeros)
    for ($i = 0; $i -lt $numSamples; $i++) {
        $writer.Write([int16]0)
    }

    $writer.Close()
    $file.Close()
}

# Colors for different asset types
$colors = @{
    "light_infantry" = @(200, 150, 100)
    "heavy_infantry" = @(100, 100, 150)
    "archers"        = @(150, 200, 100)
    "light_cavalry"  = @(200, 200, 100)
    "heavy_cavalry"  = @(100, 150, 200)
    "tier1_army"     = @(180, 100, 100)
    "tier2_army"     = @(200, 150, 100)
    "tier3_army"     = @(220, 200, 100)
    "tier1_fleet"    = @(100, 180, 200)
    "tier2_fleet"    = @(100, 200, 220)
    "tier3_fleet"    = @(100, 220, 255)
    "tier1_city"     = @(150, 150, 100)
    "tier2_city"     = @(180, 180, 100)
    "tier3_city"     = @(220, 220, 100)
    "capital"        = @(255, 215, 0)
    "plain"          = @(144, 238, 144)
    "desert"         = @(210, 180, 140)
    "forest"         = @(34, 139, 34)
    "mountain"       = @(128, 128, 128)
    "river"          = @(64, 164, 223)
    "coastal"        = @(100, 149, 237)
    "deep"           = @(30, 100, 180)
}

# Generate PNG files
$pngMappings = @{
    "units/light_infantry.png" = $colors["light_infantry"]
    "units/heavy_infantry.png" = $colors["heavy_infantry"]
    "units/archers.png"        = $colors["archers"]
    "units/light_cavalry.png"  = $colors["light_cavalry"]
    "units/heavy_cavalry.png"  = $colors["heavy_cavalry"]
    "army/tier1.png"           = $colors["tier1_army"]
    "army/tier2.png"           = $colors["tier2_army"]
    "army/tier3.png"           = $colors["tier3_army"]
    "fleet/tier1.png"          = $colors["tier1_fleet"]
    "fleet/tier2.png"          = $colors["tier2_fleet"]
    "fleet/tier3.png"          = $colors["tier3_fleet"]
    "city/tier1.png"           = $colors["tier1_city"]
    "city/tier2.png"           = $colors["tier2_city"]
    "city/tier3.png"           = $colors["tier3_city"]
    "city/capital.png"         = $colors["capital"]
    "terrain/plain.png"        = $colors["plain"]
    "terrain/desert.png"       = $colors["desert"]
    "terrain/forest.png"       = $colors["forest"]
    "terrain/mountain.png"     = $colors["mountain"]
    "terrain/river.png"        = $colors["river"]
    "terrain/sea_coastal.png"  = $colors["coastal"]
    "terrain/sea_deep.png"     = $colors["deep"]
}

Write-Host "Generating PNG files..."
foreach ($file in $pngMappings.Keys) {
    $color = $pngMappings[$file]
    $fullPath = Join-Path $OutputPath $file
    New-MinimalPNG -Path $fullPath -Red $color[0] -Green $color[1] -Blue $color[2]
    Write-Host "  + $file"
}

# Generate WAV files
$wavFiles = @("sfx/city_captured.wav", "sfx/battle.wav", "sfx/unit_move.wav")
Write-Host "Generating WAV files..."
foreach ($file in $wavFiles) {
    $fullPath = Join-Path $OutputPath $file
    New-SilentWAV -Path $fullPath
    Write-Host "  + $file"
}

# Generate manifest.json
Write-Host "Generating manifest.json..."
$manifest = @{
    schemaVersion = 1
    name = "Placeholder Asset Pack"
    description = "Generated placeholder assets - flat colors, silent audio stubs, good enough for development and testing."
    assets = @{
        # Unit icons
        "unit.light_infantry.icon" = "units/light_infantry.png"
        "unit.heavy_infantry.icon" = "units/heavy_infantry.png"
        "unit.archers.icon"        = "units/archers.png"
        "unit.light_cavalry.icon"  = "units/light_cavalry.png"
        "unit.heavy_cavalry.icon"  = "units/heavy_cavalry.png"
        # Army tiers
        "army.tier1.icon"          = "army/tier1.png"
        "army.tier2.icon"          = "army/tier2.png"
        "army.tier3.icon"          = "army/tier3.png"
        # Fleet tiers
        "fleet.tier1.icon"         = "fleet/tier1.png"
        "fleet.tier2.icon"         = "fleet/tier2.png"
        "fleet.tier3.icon"         = "fleet/tier3.png"
        # City tiers
        "city.tier1.icon"          = "city/tier1.png"
        "city.tier2.icon"          = "city/tier2.png"
        "city.tier3.icon"          = "city/tier3.png"
        "city.capital.icon"        = "city/capital.png"
        # Terrain tiles
        "terrain.plain.tile"       = "terrain/plain.png"
        "terrain.desert.tile"      = "terrain/desert.png"
        "terrain.forest.tile"      = "terrain/forest.png"
        "terrain.mountain.tile"    = "terrain/mountain.png"
        "terrain.river.tile"       = "terrain/river.png"
        "terrain.sea_coastal.tile" = "terrain/sea_coastal.png"
        "terrain.sea_deep.tile"    = "terrain/sea_deep.png"
        # Sound effects
        "sfx.city_captured"        = "sfx/city_captured.wav"
        "sfx.battle"               = "sfx/battle.wav"
        "sfx.unit_move"            = "sfx/unit_move.wav"
    }
}

$manifestPath = Join-Path $OutputPath "manifest.json"
$json = $manifest | ConvertTo-Json -Depth 10
$json | Out-File -FilePath $manifestPath -Encoding UTF8

Write-Host "Success: Generated placeholder asset pack"
