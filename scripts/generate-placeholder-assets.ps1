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
foreach ($dir in @('units', 'army', 'fleet', 'city', 'terrain', 'sfx', 'ui')) {
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

# Toolbar-command stand-ins (T101): 32-bit BGRA with a transparent background, because every
# ui.command.* key is a 32-bit BGRA icon under section 1.2's chrome rule. The pattern is a
# SHA-256-derived identicon (an 8x8 mirrored grid of 4x4 cells) in two flat colours, so each
# key gets a different geometric pattern with no font rendering (fonts differ between
# machines). Everything is integer byte arithmetic - no GDI+, no drawing API, no RNG - so the
# output is byte-identical across runs and machines.
function New-PlaceholderSpriteBMP {
    param(
        [string]$Path,
        [string]$Key
    )

    $width = 32
    $height = 32
    $bytesPerPixel = 4
    $rowSize = $width * $bytesPerPixel  # 128, already 4-byte aligned
    $pixelDataSize = [uint32]($rowSize * $height)
    $pixelDataOffset = [uint32]54
    $fileSize = [uint32]($pixelDataOffset + $pixelDataSize)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Key))

    # The left four columns of an 8x8 cell grid come from 32 hash bits; the right four are a
    # mirror of them. The colours come from other hash bytes, so the pattern and the palette
    # are both derived from the key.
    $filled = New-Object 'bool[,]' 8, 8
    $anyFilled = $false
    for ($cy = 0; $cy -lt 8; $cy++) {
        for ($cx = 0; $cx -lt 4; $cx++) {
            $bitIndex = $cy * 4 + $cx
            $byteIndex = 3 + [int][math]::Floor($bitIndex / 8)
            $bit = ($hash[$byteIndex] -shr ($bitIndex % 8)) -band 1
            $value = ($bit -eq 1)
            $filled[$cx, $cy] = $value
            $filled[(7 - $cx), $cy] = $value
            if ($value) { $anyFilled = $true }
        }
    }
    if (-not $anyFilled) {
        $filled[0, 0] = $true
        $filled[7, 0] = $true
    }
    $anyEmpty = $false
    for ($cy = 0; $cy -lt 8; $cy++) {
        for ($cx = 0; $cx -lt 8; $cx++) {
            if (-not $filled[$cx, $cy]) { $anyEmpty = $true }
        }
    }
    if (-not $anyEmpty) {
        $filled[0, 0] = $false
        $filled[7, 0] = $false
    }

    $red1 = 80 + [int]($hash[8] % 176)
    $green1 = 80 + [int]($hash[9] % 176)
    $blue1 = 80 + [int]($hash[10] % 176)
    $red2 = 40 + [int]($hash[11] % 176)
    $green2 = 40 + [int]($hash[12] % 176)
    $blue2 = 40 + [int]($hash[13] % 176)

    # BITMAPFILEHEADER (14 bytes)
    $fileHeader = [byte[]]@(0x42, 0x4D)  # "BM"
    $fileHeader += [BitConverter]::GetBytes($fileSize)
    $fileHeader += [byte[]]@(0, 0, 0, 0)  # reserved
    $fileHeader += [BitConverter]::GetBytes($pixelDataOffset)

    # BITMAPINFOHEADER (40 bytes): 32 bits per pixel, BI_RGB; the unused byte is alpha.
    $infoHeader = [BitConverter]::GetBytes([uint32]40)
    $infoHeader += [BitConverter]::GetBytes([int32]$width)
    $infoHeader += [BitConverter]::GetBytes([int32]$height)            # positive => bottom-up
    $infoHeader += [BitConverter]::GetBytes([uint16]1)                 # color planes
    $infoHeader += [BitConverter]::GetBytes([uint16]32)                # bits per pixel
    $infoHeader += [BitConverter]::GetBytes([uint32]0)                 # BI_RGB, no compression
    $infoHeader += [BitConverter]::GetBytes($pixelDataSize)
    $infoHeader += [BitConverter]::GetBytes([int32]0)                  # X pixels/metre
    $infoHeader += [BitConverter]::GetBytes([int32]0)                  # Y pixels/metre
    $infoHeader += [BitConverter]::GetBytes([uint32]0)                 # colors used
    $infoHeader += [BitConverter]::GetBytes([uint32]0)                 # important colors

    # Pixel array: BGRA, bottom-up rows (no padding at 32bpp). Transparent background, two
    # flat colours in a checker within the filled cells.
    $pixelData = New-Object byte[] $pixelDataSize
    for ($y = 0; $y -lt $height; $y++) {
        $rowIndex = $height - 1 - $y
        for ($x = 0; $x -lt $width; $x++) {
            $cellX = [int][math]::Floor($x / 4)
            $cellY = [int][math]::Floor($y / 4)
            if ($filled[$cellX, $cellY]) {
                $idx = ($rowIndex * $rowSize) + ($x * $bytesPerPixel)
                if ((($cellX + $cellY) % 2) -eq 0) {
                    $pixelData[$idx] = $blue1; $pixelData[$idx + 1] = $green1; $pixelData[$idx + 2] = $red1
                } else {
                    $pixelData[$idx] = $blue2; $pixelData[$idx + 1] = $green2; $pixelData[$idx + 2] = $red2
                }
                $pixelData[$idx + 3] = 255
            }
        }
    }

    $allBytes = $fileHeader + $infoHeader + $pixelData
    [System.IO.File]::WriteAllBytes($Path, $allBytes)
}

# T148 shore overlays: 32-bit BGRA with a transparent background and a 6 px opaque band of sand
# (plus a 1 px white surf line at its outer edge) along the edge the key names. Six px keeps every
# opaque pixel within the 10 px the task requires of a shore overlay. The band is written in DIB
# coordinates and mapped to the bottom-up file rows, so "n" is the top row of the displayed tile.
function New-PlaceholderShoreBMP {
    param(
        [string]$Path,
        [string]$Edge
    )

    $width = 32
    $height = 32
    $bytesPerPixel = 4
    $rowSize = $width * $bytesPerPixel  # 128, already 4-byte aligned
    $pixelDataSize = [uint32]($rowSize * $height)
    $pixelDataOffset = [uint32]54
    $fileSize = [uint32]($pixelDataOffset + $pixelDataSize)

    $fileHeader = [byte[]]@(0x42, 0x4D)  # "BM"
    $fileHeader += [BitConverter]::GetBytes($fileSize)
    $fileHeader += [byte[]]@(0, 0, 0, 0)  # reserved
    $fileHeader += [BitConverter]::GetBytes($pixelDataOffset)

    $infoHeader = [BitConverter]::GetBytes([uint32]40)
    $infoHeader += [BitConverter]::GetBytes([int32]$width)
    $infoHeader += [BitConverter]::GetBytes([int32]$height)            # positive => bottom-up
    $infoHeader += [BitConverter]::GetBytes([uint16]1)                 # color planes
    $infoHeader += [BitConverter]::GetBytes([uint16]32)                # bits per pixel
    $infoHeader += [BitConverter]::GetBytes([uint32]0)                 # BI_RGB, no compression
    $infoHeader += [BitConverter]::GetBytes($pixelDataSize)
    $infoHeader += [BitConverter]::GetBytes([int32]0)
    $infoHeader += [BitConverter]::GetBytes([int32]0)
    $infoHeader += [BitConverter]::GetBytes([uint32]0)
    $infoHeader += [BitConverter]::GetBytes([uint32]0)

    $pixelData = New-Object byte[] $pixelDataSize
    for ($y = 0; $y -lt $height; $y++) {
        $rowIndex = $height - 1 - $y
        for ($x = 0; $x -lt $width; $x++) {
            $distance = switch ($Edge) {
                "n" { $y }
                "s" { $height - 1 - $y }
                "w" { $x }
                "e" { $width - 1 - $x }
                default { 99 }
            }
            if ($distance -gt 5) { continue }
            $idx = ($rowIndex * $rowSize) + ($x * $bytesPerPixel)
            if ($distance -eq 0) {
                # white surf line at the outer edge
                $pixelData[$idx] = 255; $pixelData[$idx + 1] = 255; $pixelData[$idx + 2] = 255
            } else {
                # sand (BGR): a placeholder stand-in for the authored sand-and-surf band
                $pixelData[$idx] = 128; $pixelData[$idx + 1] = 178; $pixelData[$idx + 2] = 194
            }
            $pixelData[$idx + 3] = 255
        }
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
    # T148 variants: each type's three extra variants are flat stand-ins, slightly offset from
    # variant 1 so a pack consumer can tell them apart; the authored pack supplies the real detail.
    "plain2" = @(134, 228, 134)
    "plain3" = @(154, 248, 154)
    "plain4" = @(124, 218, 124)
    "desert2" = @(200, 170, 130)
    "desert3" = @(220, 190, 150)
    "desert4" = @(190, 160, 120)
    "forest2" = @(44, 149, 44)
    "forest3" = @(24, 129, 24)
    "forest4" = @(54, 159, 54)
    "mountain2" = @(138, 138, 138)
    "mountain3" = @(118, 118, 118)
    "mountain4" = @(148, 148, 148)
    "coastal2" = @(110, 159, 247)
    "coastal3" = @(90, 139, 227)
    "coastal4" = @(120, 169, 255)
    "deep2" = @(40, 110, 190)
    "deep3" = @(20, 90, 170)
    "deep4" = @(50, 120, 200)
    # T148 river connectivity pieces (codes 6..11): flat blue stand-ins, one shade each.
    "river_ew" = @(64, 164, 223)
    "river_ns" = @(54, 154, 213)
    "river_en" = @(74, 174, 233)
    "river_es" = @(44, 144, 203)
    "river_ws" = @(84, 184, 243)
    "river_wn" = @(94, 194, 253)
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
    # T148: the three extra variants of each variant-bearing type. The file name mirrors the
    # authored generator's asset_relpath (`terrain.<type>.tile.<n>` -> terrain/<type>_<n>.bmp).
    "terrain/plain_2.bmp" = $colors["plain2"]
    "terrain/plain_3.bmp" = $colors["plain3"]
    "terrain/plain_4.bmp" = $colors["plain4"]
    "terrain/desert_2.bmp" = $colors["desert2"]
    "terrain/desert_3.bmp" = $colors["desert3"]
    "terrain/desert_4.bmp" = $colors["desert4"]
    "terrain/forest_2.bmp" = $colors["forest2"]
    "terrain/forest_3.bmp" = $colors["forest3"]
    "terrain/forest_4.bmp" = $colors["forest4"]
    "terrain/mountain_2.bmp" = $colors["mountain2"]
    "terrain/mountain_3.bmp" = $colors["mountain3"]
    "terrain/mountain_4.bmp" = $colors["mountain4"]
    "terrain/sea_coastal_2.bmp" = $colors["coastal2"]
    "terrain/sea_coastal_3.bmp" = $colors["coastal3"]
    "terrain/sea_coastal_4.bmp" = $colors["coastal4"]
    "terrain/sea_deep_2.bmp" = $colors["deep2"]
    "terrain/sea_deep_3.bmp" = $colors["deep3"]
    "terrain/sea_deep_4.bmp" = $colors["deep4"]
    # T148: the six river connectivity pieces (grid codes 6..11).
    "terrain/river_ew.bmp" = $colors["river_ew"]
    "terrain/river_ns.bmp" = $colors["river_ns"]
    "terrain/river_en.bmp" = $colors["river_en"]
    "terrain/river_es.bmp" = $colors["river_es"]
    "terrain/river_ws.bmp" = $colors["river_ws"]
    "terrain/river_wn.bmp" = $colors["river_wn"]
}

foreach ($file in $bmpMappings.Keys) {
    $color = $bmpMappings[$file]
    $fullPath = Join-Path -Path $OutputPath -ChildPath $file
    New-PlaceholderBMP -Path $fullPath -Red $color[0] -Green $color[1] -Blue $color[2]
    Write-Host "  + $file"
}

# T148 shore overlays: one per cardinal edge, a transparent tile with a sand/surf band along it.
Write-Host "Generating terrain shore overlays..."
foreach ($edge in @("n", "e", "s", "w")) {
    $file = "terrain/shore_$edge.bmp"
    $fullPath = Join-Path -Path $OutputPath -ChildPath $file
    New-PlaceholderShoreBMP -Path $fullPath -Edge $edge
    Write-Host "  + $file"
}

# Toolbar-command keys (T101): 9 main-toolbar commands, 12 Area-map strip commands and 15
# unit-map strip commands, in AssetKeys.cs order. The file name follows section 1.5's
# convention: `ui.command.<id>.icon` -> `ui/command_<id>.bmp`.
$uiCommands = @(
    "open", "save", "end_turn", "news", "relations", "taxation", "balance_sheet",
    "recruit_unit", "build_fleet",
    "show_cities", "show_capital", "show_armies", "show_fleets", "show_all",
    "show_mercs_light_infantry", "show_mercs_heavy_infantry", "show_mercs_archers",
    "show_mercs_light_cavalry", "show_mercs_heavy_cavalry", "show_mercs_all", "find_city",
    "army_supply", "army_recruit_mercenaries", "army_transfer_unit", "army_split",
    "army_join", "army_change_units", "army_disband",
    "fleet_supply", "fleet_repair", "fleet_transfer_ships", "fleet_split", "fleet_join",
    "fleet_scuttle", "city_fortify", "cancel_selection"
)

Write-Host "Generating toolbar-command placeholder icons..."
foreach ($command in $uiCommands) {
    $key = "ui.command.$command.icon"
    $file = "ui/command_$command.bmp"
    $fullPath = Join-Path -Path $OutputPath -ChildPath $file
    New-PlaceholderSpriteBMP -Path $fullPath -Key $key
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

# T148: the three extra variants of each variant-bearing type.
foreach ($terrain in @("plain", "desert", "forest", "mountain", "sea_coastal", "sea_deep")) {
    for ($variant = 2; $variant -le 4; $variant++) {
        $manifest.assets["terrain.$terrain.tile.$variant"] = "terrain/${terrain}_$variant.bmp"
    }
}

# T148: the six river connectivity pieces (grid codes 6..11, in MapViewer.DrawRiver order).
foreach ($piece in @("ew", "ns", "en", "es", "ws", "wn")) {
    $manifest.assets["terrain.river.$piece"] = "terrain/river_$piece.bmp"
}

# T148: the four shore overlays, one per cardinal edge.
foreach ($edge in @("n", "e", "s", "w")) {
    $manifest.assets["terrain.shore.$edge"] = "terrain/shore_$edge.bmp"
}

foreach ($sfx in @("city_captured", "battle", "unit_move")) {
    $manifest.assets["sfx.$sfx"] = "sfx/$sfx.wav"
}

foreach ($command in $uiCommands) {
    $manifest.assets["ui.command.$command.icon"] = "ui/command_$command.bmp"
}

$manifestPath = Join-Path -Path $OutputPath -ChildPath "manifest.json"
$json = ConvertTo-Json $manifest -Depth 10
# LF, matching the repository's .gitattributes (`* text=auto eol=lf`): ConvertTo-Json emits
# CRLF on Windows, which would make the regenerated manifest differ from the committed one
# byte for byte and fail the determinism check (repo-audit-2026-09-18.md §4.8).
$json = $json -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($manifestPath, $json)

Write-Host "Success: Generated placeholder asset pack at: $OutputPath"
