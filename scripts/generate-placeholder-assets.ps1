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
    [string]$OutputPath = (Join-Path -Path $PSScriptRoot -ChildPath "../assets/packs/placeholder"),
    [int]$Seed = 42
)

$ErrorActionPreference = "Stop"

# Ensure output directory exists
if (!(Test-Path $OutputPath)) {
    $null = New-Item -ItemType Directory -Path $OutputPath -Force
    Write-Host "Created directory: $OutputPath"
}

# Create subdirectories
foreach ($dir in @('units', 'army', 'fleet', 'city', 'terrain', 'sfx', 'ui', 'battle')) {
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

# T148 surface stand-ins: 256x256 24-bit BMPs with a gentle periodic pattern, the drop-in for the
# authored pack's six seamless surfaces. The pattern uses whole 256-px sine periods, so its left and
# right columns (and top and bottom rows) are exactly equal and it tiles with no seam. 24-bit per
# docs/asset-specification.md 1.2; the authored pack supplies the real detail.
function New-PlaceholderSurfaceBMP {
    param(
        [string]$Path,
        [int]$Red,
        [int]$Green,
        [int]$Blue
    )

    $width = 256
    $height = 256
    $bytesPerPixel = 3
    $rowSize = $width * $bytesPerPixel  # 768, already 4-byte aligned
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
    $infoHeader += [BitConverter]::GetBytes([uint16]24)                # bits per pixel
    $infoHeader += [BitConverter]::GetBytes([uint32]0)                 # BI_RGB, no compression
    $infoHeader += [BitConverter]::GetBytes($pixelDataSize)
    $infoHeader += [BitConverter]::GetBytes([int32]0)
    $infoHeader += [BitConverter]::GetBytes([int32]0)
    $infoHeader += [BitConverter]::GetBytes([uint32]0)
    $infoHeader += [BitConverter]::GetBytes([uint32]0)

    $pixelData = New-Object byte[] $pixelDataSize
    $twoPi = 2.0 * [Math]::PI
    for ($y = 0; $y -lt $height; $y++) {
        $rowIndex = $height - 1 - $y
        for ($x = 0; $x -lt $width; $x++) {
            # Whole periods in 256 px: the pattern is exactly periodic, so the tile's opposite
            # edges are byte-identical.
            $wave = [Math]::Sin($twoPi * $x / $width) * [Math]::Cos($twoPi * $y / $height)
            $r = [Math]::Max(0, [Math]::Min(255, [int]($Red + 18 * $wave)))
            $g = [Math]::Max(0, [Math]::Min(255, [int]($Green + 18 * $wave)))
            $b = [Math]::Max(0, [Math]::Min(255, [int]($Blue + 18 * $wave)))
            $idx = ($rowIndex * $rowSize) + ($x * $bytesPerPixel)
            $pixelData[$idx] = [byte]$b
            $pixelData[$idx + 1] = [byte]$g
            $pixelData[$idx + 2] = [byte]$r
        }
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
    "tier4_city" = @(240, 130, 60)
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
    "city/tier4.bmp" = $colors["tier4_city"]
    "city/capital.bmp" = $colors["capital"]
    "terrain/plain.bmp" = $colors["plain"]
    "terrain/desert.bmp" = $colors["desert"]
    "terrain/forest.bmp" = $colors["forest"]
    "terrain/mountain.bmp" = $colors["mountain"]
    "terrain/river.bmp" = $colors["river"]
    "terrain/sea_calm.bmp" = $colors["coastal"]
    "terrain/sea_rough.bmp" = $colors["deep"]
}

foreach ($file in $bmpMappings.Keys) {
    $color = $bmpMappings[$file]
    $fullPath = Join-Path -Path $OutputPath -ChildPath $file
    New-PlaceholderBMP -Path $fullPath -Red $color[0] -Green $color[1] -Blue $color[2]
    Write-Host "  + $file"
}

# T148: the six seamless surface textures, one 256x256 24-bit procedural BMP per surface class.
Write-Host "Generating terrain surface stand-ins..."
$surfaceColors = @{
    "plain" = $colors["plain"]
    "desert" = $colors["desert"]
    "forest" = $colors["forest"]
    "mountain" = $colors["mountain"]
    "sea_shallow" = $colors["coastal"]
    "sea_open" = $colors["deep"]
}
foreach ($surface in $surfaceColors.Keys) {
    $color = $surfaceColors[$surface]
    $file = "terrain/${surface}_surface.bmp"
    $fullPath = Join-Path -Path $OutputPath -ChildPath $file
    New-PlaceholderSurfaceBMP -Path $fullPath -Red $color[0] -Green $color[1] -Blue $color[2]
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

# --- T128: the tactical battle screen's assets (docs/asset-specification.md 4.7) -------------------
# 15 recolourable unit icons (32x32, 32-bit BGRA, alpha 255 throughout), the ground tile (32x32, 24-bit), the cursor
# (32x32, 32-bit BGRA) and eight toolbar glyphs (20x20, 32-bit BGRA). A unit icon is drawn in the
# three marker colours the nation recolour replaces (purple 0x800080, white, blue 0x0000FF) plus a
# black outline and the lime dotted frame the original keeps; the figure grows with the size index.
# Everything is integer arithmetic on a byte buffer - no GDI+, no RNG - so a second run is
# byte-identical.
function New-RgbaBuffer {
    param([int]$Width, [int]$Height, [int[]]$Fill)
    $buffer = New-Object byte[] ($Width * $Height * 4)
    for ($i = 0; $i -lt ($Width * $Height); $i++) {
        $buffer[$i * 4] = [byte]$Fill[0]; $buffer[$i * 4 + 1] = [byte]$Fill[1]
        $buffer[$i * 4 + 2] = [byte]$Fill[2]; $buffer[$i * 4 + 3] = [byte]$Fill[3]
    }
    return ,$buffer
}

function Set-RgbaPixel {
    param([byte[]]$Buffer, [int]$Width, [int]$Height, [int]$X, [int]$Y, [int[]]$Color)
    if ($X -lt 0 -or $Y -lt 0 -or $X -ge $Width -or $Y -ge $Height) { return }
    $i = ($Y * $Width + $X) * 4
    $Buffer[$i] = [byte]$Color[0]; $Buffer[$i + 1] = [byte]$Color[1]
    $Buffer[$i + 2] = [byte]$Color[2]; $Buffer[$i + 3] = [byte]$Color[3]
}

function Set-RgbaRect {
    param([byte[]]$Buffer, [int]$Width, [int]$Height, [int]$X0, [int]$Y0, [int]$X1, [int]$Y1, [int[]]$Color)
    for ($y = $Y0; $y -le $Y1; $y++) {
        for ($x = $X0; $x -le $X1; $x++) {
            Set-RgbaPixel -Buffer $Buffer -Width $Width -Height $Height -X $x -Y $y -Color $Color
        }
    }
}

# A filled rectangle with a one-pixel black outline.
function Set-OutlinedRect {
    param([byte[]]$Buffer, [int]$Width, [int]$Height, [int]$X0, [int]$Y0, [int]$X1, [int]$Y1, [int[]]$Fill)
    Set-RgbaRect -Buffer $Buffer -Width $Width -Height $Height -X0 ($X0 - 1) -Y0 ($Y0 - 1) -X1 ($X1 + 1) -Y1 ($Y1 + 1) -Color @(0, 0, 0, 255)
    Set-RgbaRect -Buffer $Buffer -Width $Width -Height $Height -X0 $X0 -Y0 $Y0 -X1 $X1 -Y1 $Y1 -Color $Fill
}

# Writes an RGBA buffer (top-down) as an uncompressed BMP, 24-bit (alpha dropped) or 32-bit BGRA.
function Write-BmpFromRgba {
    param([string]$Path, [byte[]]$Buffer, [int]$Width, [int]$Height, [int]$BitCount)
    $bytesPerPixel = $BitCount / 8
    $rowUnpadded = $Width * $bytesPerPixel
    $rowPadding = (4 - ($rowUnpadded % 4)) % 4
    $rowSize = $rowUnpadded + $rowPadding
    $pixelDataSize = [uint32]($rowSize * $Height)
    $pixelDataOffset = [uint32]54
    $fileSize = [uint32]($pixelDataOffset + $pixelDataSize)

    $fileHeader = [byte[]]@(0x42, 0x4D)
    $fileHeader += [BitConverter]::GetBytes($fileSize)
    $fileHeader += [byte[]]@(0, 0, 0, 0)
    $fileHeader += [BitConverter]::GetBytes($pixelDataOffset)

    $infoHeader = [BitConverter]::GetBytes([uint32]40)
    $infoHeader += [BitConverter]::GetBytes([int32]$Width)
    $infoHeader += [BitConverter]::GetBytes([int32]$Height)
    $infoHeader += [BitConverter]::GetBytes([uint16]1)
    $infoHeader += [BitConverter]::GetBytes([uint16]$BitCount)
    $infoHeader += [BitConverter]::GetBytes([uint32]0)
    $infoHeader += [BitConverter]::GetBytes($pixelDataSize)
    $infoHeader += [BitConverter]::GetBytes([int32]0)
    $infoHeader += [BitConverter]::GetBytes([int32]0)
    $infoHeader += [BitConverter]::GetBytes([uint32]0)
    $infoHeader += [BitConverter]::GetBytes([uint32]0)

    $pixelData = New-Object byte[] $pixelDataSize
    for ($y = 0; $y -lt $Height; $y++) {
        $rowIndex = $Height - 1 - $y
        for ($x = 0; $x -lt $Width; $x++) {
            $src = ($y * $Width + $x) * 4
            $idx = ($rowIndex * $rowSize) + ($x * $bytesPerPixel)
            $pixelData[$idx] = $Buffer[$src + 2]
            $pixelData[$idx + 1] = $Buffer[$src + 1]
            $pixelData[$idx + 2] = $Buffer[$src]
            if ($bytesPerPixel -eq 4) { $pixelData[$idx + 3] = $Buffer[$src + 3] }
        }
    }

    [System.IO.File]::WriteAllBytes($Path, ($fileHeader + $infoHeader + $pixelData))
}

# One unit icon: a purple field, a white figure that grows with the size index (0 small .. 2 large),
# a blue accent whose place depends on the type, a black outline and the lime dotted frame.
function New-BattleUnitBMP {
    param([string]$Path, [int]$TypeIndex, [int]$SizeIndex)
    $n = 32
    $purple = @(128, 0, 128, 255); $white = @(255, 255, 255, 255)
    $blue = @(0, 0, 255, 255); $lime = @(0, 255, 0, 255)
    $buffer = New-RgbaBuffer -Width $n -Height $n -Fill $purple

    $half = 3 + 2 * $SizeIndex            # half width of the body: 3, 5 or 7
    $top = 16 - (4 + 2 * $SizeIndex)      # the body's top row
    $bottom = 16 + (4 + 2 * $SizeIndex)   # and bottom row
    $cx = 16

    switch ($TypeIndex) {
        0 { # light infantry: a body and a spear
            Set-OutlinedRect $buffer $n $n ($cx - $half + 2) ($top + 3) ($cx + $half - 2) $bottom $white
            Set-RgbaRect $buffer $n $n ($cx + $half - 1) 4 ($cx + $half - 1) ($bottom - 2) $white
            Set-RgbaRect $buffer $n $n ($cx - $half + 3) ($top + 5) ($cx - $half + 4) ($top + 6) $blue
        }
        1 { # heavy infantry: a broad body and a shield
            Set-OutlinedRect $buffer $n $n ($cx - $half) ($top + 2) ($cx + $half) $bottom $white
            Set-RgbaRect $buffer $n $n ($cx - 1) ($top + 3) ($cx + 1) ($bottom - 1) $blue
            Set-RgbaRect $buffer $n $n ($cx - $half + 1) ($top + 5) ($cx + $half - 1) ($top + 6) $blue
        }
        2 { # archers: a body and a bow
            Set-OutlinedRect $buffer $n $n ($cx - $half + 3) ($top + 3) ($cx + $half - 3) $bottom $white
            for ($k = 0; $k -le (2 * $half); $k++) {
                $arc = [int](($k - $half) * ($k - $half) / (2 * $half))
                Set-RgbaPixel $buffer $n $n ($cx + $half - $arc) ($top + $k) $blue
                Set-RgbaPixel $buffer $n $n ($cx + $half - $arc + 1) ($top + $k) $blue
            }
        }
        3 { # light cavalry: a long low body and a rider
            Set-OutlinedRect $buffer $n $n ($cx - $half - 3) ($top + 6) ($cx + $half + 3) ($bottom - 2) $white
            Set-RgbaRect $buffer $n $n ($cx - 1) ($top) ($cx + 1) ($top + 5) $white
            Set-RgbaRect $buffer $n $n ($cx - $half - 1) ($top + 8) ($cx - $half + 1) ($top + 9) $blue
        }
        4 { # heavy cavalry: a long body, a rider and a blue barding stripe
            Set-OutlinedRect $buffer $n $n ($cx - $half - 3) ($top + 6) ($cx + $half + 3) $bottom $white
            Set-OutlinedRect $buffer $n $n ($cx - 2) ($top - 1) ($cx + 2) ($top + 5) $white
            Set-RgbaRect $buffer $n $n ($cx - $half - 2) ($bottom - 4) ($cx + $half + 2) ($bottom - 3) $blue
        }
    }

    # The lime dotted frame: every second pixel of the outermost ring.
    for ($i = 0; $i -lt $n; $i += 2) {
        Set-RgbaPixel $buffer $n $n $i 0 $lime
        Set-RgbaPixel $buffer $n $n $i ($n - 1) $lime
        Set-RgbaPixel $buffer $n $n 0 $i $lime
        Set-RgbaPixel $buffer $n $n ($n - 1) $i $lime
    }
    Write-BmpFromRgba -Path $Path -Buffer $buffer -Width $n -Height $n -BitCount 32
}

# The ground: grass green with brown contour lines (a battlefield has no terrain).
function New-BattleGroundBMP {
    param([string]$Path)
    $n = 32
    $buffer = New-RgbaBuffer -Width $n -Height $n -Fill @(70, 140, 60, 255)
    for ($y = 0; $y -lt $n; $y++) {
        for ($x = 0; $x -lt $n; $x++) {
            if ((($x + 2 * $y) % 11) -eq 0) {
                Set-RgbaPixel $buffer $n $n $x $y @(120, 90, 50, 255)
            } elseif ((($x * 3 + $y) % 17) -eq 0) {
                Set-RgbaPixel $buffer $n $n $x $y @(90, 160, 70, 255)
            }
        }
    }
    Write-BmpFromRgba -Path $Path -Buffer $buffer -Width $n -Height $n -BitCount 24
}

# The cursor: white corner brackets with a black inner shadow on a transparent cell.
function New-BattleCursorBMP {
    param([string]$Path)
    $n = 32
    $buffer = New-RgbaBuffer -Width $n -Height $n -Fill @(0, 0, 0, 0)
    foreach ($ring in @(@(0, @(255, 255, 255, 255)), @(1, @(0, 0, 0, 255)))) {
        $o = $ring[0]; $c = $ring[1]
        for ($i = $o; $i -lt ($n - $o); $i++) {
            if (($i - $o) -lt 8 -or ($i - $o) -ge ($n - 2 * $o - 8)) {
                Set-RgbaPixel $buffer $n $n $i $o $c
                Set-RgbaPixel $buffer $n $n $i ($n - 1 - $o) $c
                Set-RgbaPixel $buffer $n $n $o $i $c
                Set-RgbaPixel $buffer $n $n ($n - 1 - $o) $i $c
            }
        }
    }
    Write-BmpFromRgba -Path $Path -Buffer $buffer -Width $n -Height $n -BitCount 32
}

# A toolbar glyph stand-in: a SHA-256-derived mirrored identicon of 2-pixel cells (5 x 10 mirrored
# to 10 x 10) in two flat colours on a transparent 20x20 cell, as for the ui.command stand-ins.
function New-BattleGlyphBMP {
    param([string]$Path, [string]$Key)
    $n = 20
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Key))
    $buffer = New-RgbaBuffer -Width $n -Height $n -Fill @(0, 0, 0, 0)
    $c1 = @((60 + [int]($hash[8] % 150)), (60 + [int]($hash[9] % 150)), (60 + [int]($hash[10] % 150)), 255)
    $c2 = @((20 + [int]($hash[11] % 120)), (20 + [int]($hash[12] % 120)), (20 + [int]($hash[13] % 120)), 255)
    for ($cy = 0; $cy -lt 10; $cy++) {
        for ($cx = 0; $cx -lt 5; $cx++) {
            $bitIndex = $cy * 5 + $cx
            $bit = ($hash[3 + [int][math]::Floor($bitIndex / 8)] -shr ($bitIndex % 8)) -band 1
            if ($bit -eq 1) {
                $color = if ((($cx + $cy) % 2) -eq 0) { $c1 } else { $c2 }
                Set-RgbaRect $buffer $n $n (2 * $cx) (2 * $cy) (2 * $cx + 1) (2 * $cy + 1) $color
                Set-RgbaRect $buffer $n $n (2 * (9 - $cx)) (2 * $cy) (2 * (9 - $cx) + 1) (2 * $cy + 1) $color
            }
        }
    }
    Write-BmpFromRgba -Path $Path -Buffer $buffer -Width $n -Height $n -BitCount 32
}

$battleUnitTypes = @("light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry")
$battleSizes = @("small", "medium", "large")
$battleToolbar = @("unit_moves", "friendly_units", "enemy_units", "cancel_selection", "end_turn", "change_pauses", "computer_general", "surrender")

Write-Host "Generating battle screen placeholder assets..."
for ($t = 0; $t -lt $battleUnitTypes.Count; $t++) {
    for ($s = 0; $s -lt 3; $s++) {
        $file = "battle/unit_$($battleUnitTypes[$t])_$($battleSizes[$s]).bmp"
        New-BattleUnitBMP -Path (Join-Path -Path $OutputPath -ChildPath $file) -TypeIndex $t -SizeIndex $s
        Write-Host "  + $file"
    }
}
New-BattleGroundBMP -Path (Join-Path -Path $OutputPath -ChildPath "battle/ground.bmp")
Write-Host "  + battle/ground.bmp"
New-BattleCursorBMP -Path (Join-Path -Path $OutputPath -ChildPath "battle/cursor.bmp")
Write-Host "  + battle/cursor.bmp"
foreach ($button in $battleToolbar) {
    $file = "battle/toolbar_$button.bmp"
    New-BattleGlyphBMP -Path (Join-Path -Path $OutputPath -ChildPath $file) -Key "battle.toolbar.$button"
    Write-Host "  + $file"
}

Write-Host "Generating WAV files..."
foreach ($sfx in @("city_captured", "battle", "unit_move", "fleet_move", "battle_arrows", "battle_javelin", "battle_melee", "siege_failed", "fleet_sunk", "nation_conquered")) {
    $filepath = Join-Path -Path $OutputPath -ChildPath "sfx/$sfx.wav"
    New-SilentWAV -Path $filepath
    Write-Host "  + sfx/$sfx.wav"
}

Write-Host "Generating manifest.json..."
$manifest = [ordered]@{
    schemaVersion = 1
    name = "Placeholder Asset Pack"
    description = "Generated placeholder assets - flat colors, silent audio stubs, good enough for development and testing."
    assets = [ordered]@{}
}

foreach ($unit in @("light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry")) {
    $manifest.assets["unit.$unit.icon"] = "units/$unit.bmp"
}

for ($tier = 1; $tier -le 3; $tier++) {
    $manifest.assets["army.tier$tier.icon"] = "army/tier$tier.bmp"
    $manifest.assets["fleet.tier$tier.icon"] = "fleet/tier$tier.bmp"
    $manifest.assets["city.tier$tier.icon"] = "city/tier$tier.bmp"
}
$manifest.assets["city.tier4.icon"] = "city/tier4.bmp"

$manifest.assets["city.capital.icon"] = "city/capital.bmp"

foreach ($terrain in @("plain", "desert", "forest", "mountain", "river", "sea_calm", "sea_rough")) {
    $manifest.assets["terrain.$terrain.tile"] = "terrain/$terrain.bmp"
}

# T148: the six seamless surface textures.
foreach ($surface in @("plain", "desert", "forest", "mountain", "sea_shallow", "sea_open")) {
    $manifest.assets["terrain.$surface.surface"] = "terrain/${surface}_surface.bmp"
}

foreach ($sfx in @("city_captured", "battle", "unit_move", "fleet_move", "battle_arrows", "battle_javelin", "battle_melee", "siege_failed", "fleet_sunk", "nation_conquered")) {
    $manifest.assets["sfx.$sfx"] = "sfx/$sfx.wav"
}

foreach ($command in $uiCommands) {
    $manifest.assets["ui.command.$command.icon"] = "ui/command_$command.bmp"
}

# T128: the battle screen's 25 keys, in a separate ordered list that is appended after the
# serialised JSON (below): a hashtable reshuffles every existing line when a key is added, and the
# battle keys must be additions only.
$battleManifest = [ordered]@{}
for ($t = 0; $t -lt $battleUnitTypes.Count; $t++) {
    for ($s = 0; $s -lt 3; $s++) {
        $battleManifest["battle.unit.$($battleUnitTypes[$t]).$($battleSizes[$s])"] = "battle/unit_$($battleUnitTypes[$t])_$($battleSizes[$s]).bmp"
    }
}
$battleManifest["battle.ground"] = "battle/ground.bmp"
$battleManifest["battle.cursor"] = "battle/cursor.bmp"
foreach ($button in $battleToolbar) {
    $battleManifest["battle.toolbar.$button"] = "battle/toolbar_$button.bmp"
}

$manifestPath = Join-Path -Path $OutputPath -ChildPath "manifest.json"
$json = ConvertTo-Json $manifest -Depth 10
# LF, matching the repository's .gitattributes (`* text=auto eol=lf`): ConvertTo-Json emits
# CRLF on Windows, which would make the regenerated manifest differ from the committed one
# byte for byte and fail the determinism check (repo-audit-2026-09-18.md §4.8).
$json = $json -replace "`r`n", "`n"
# T128: append the battle keys after the last "assets" line, in whatever indent and separator the
# serialiser used for it (Windows PowerShell 5.1 and PowerShell 7 format ConvertTo-Json differently).
$assetsStart = $json.IndexOf('"assets"')
if ($assetsStart -lt 0) { throw "Could not find the assets block in the manifest JSON." }
$assetsClose = $assetsStart + [regex]::Match($json.Substring($assetsStart), '
[ ]*\}').Index
$lastLine = [regex]::Match($json.Substring(0, $assetsClose), '(?<indent>[ ]+)"[^"]+":(?<sep>[ ]+)"[^"]*"\z')
if (-not $lastLine.Success) { throw "Could not find the last assets line in the manifest JSON." }
$battleLines = foreach ($key in $battleManifest.Keys) {
    $lastLine.Groups['indent'].Value + '"' + $key + '":' + $lastLine.Groups['sep'].Value + '"' + $battleManifest[$key] + '"'
}
$json = $json.Substring(0, $assetsClose) + ",`n" + ($battleLines -join ",`n") + $json.Substring($assetsClose)
[System.IO.File]::WriteAllText($manifestPath, $json)

Write-Host "Success: Generated placeholder asset pack at: $OutputPath"

# T182: replace the legacy generic markers with reproducible recognizable geometry.
# This writes only the 21 battle/army/fleet icons and preserves every other pack asset.
& python (Join-Path $PSScriptRoot 'generate-unit-glyphs.py') --pack-dir $OutputPath
if ($LASTEXITCODE -ne 0) { throw 'Unit glyph generation failed.' }
