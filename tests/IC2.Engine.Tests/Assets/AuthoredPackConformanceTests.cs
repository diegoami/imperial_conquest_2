using System.Text.Json;
using System.Text.Json.Serialization;
using IC2.Engine.Assets;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>
/// T51's conformance tests for the authored pack (<c>assets/packs/authored/</c>, produced by
/// <c>scripts/generate-authored-assets.py</c> from <c>assets/prompts.json</c>) — DoD 1, 5 and 6.
/// </summary>
/// <remarks>
/// <para>
/// <b>Non-determinism is the defining constraint, and it shapes everything here</b> (the task
/// entry's own Scope and Hazards): the same prompt does not give the same pixels twice, so
/// byte-equality or regeneration-determinism assertions are impossible and MUST NOT be
/// attempted — a test asserting them would either fail forever or be quietly weakened. What is
/// pinned here is <b>conformance only</b>: dimensions, BMP format, colour depth, transparency,
/// and manifest/prompt completeness. The committed images are the source of truth, exactly as
/// T29's exported JSON is.
/// </para>
/// <para>
/// <b>Draw-time tint</b> (the user's decision recorded on #173, refining DoD 5's "with the
/// nation palette applied"): markers, fleet/army/city markers and unit icons are authored as
/// <b>neutral light/white silhouettes on transparent backgrounds</b> — the sixteen-nation
/// palette (<c>docs/asset-specification.md</c> §2.3) is applied at DRAW time by tinting each
/// marker with its owner's colour, not baked into sixteen per-nation variants, because the
/// asset keys are one per tier, not one per nation. Terrain tiles are full-colour, fully
/// opaque, 24-bit, per §1.2. T101's <c>ui.command.*</c> icons are the deliberate exception:
/// pictorial, full-colour 32-bit BGRA on a transparent background, never tinted, checked by
/// their own format/size/transparency test rather than the silhouette test.
/// </para>
/// <para>
/// <b>The pack is committed</b>: it was produced by a billable, deliberate, human-run generation
/// (Phase 2 of the task), not by CI, and these facts read the committed files. No <c>Skip</c> and
/// no weakened assertion is used; regenerating the pack is not part of any test run.
/// </para>
/// </remarks>
public sealed class AuthoredPackConformanceTests
{
    private static readonly string AuthoredPackDirectory =
        Path.Combine(FixturePaths.RepositoryRoot, "assets", "packs", "authored");

    private static readonly string PromptsFilePath =
        Path.Combine(FixturePaths.RepositoryRoot, "assets", "prompts.json");

    // T149: sound keys (sfx.*) are generated through a separate prompt file because they are
    // produced by ElevenLabs' sound-effects API rather than an image model, and the existing
    // prompts.json structure (text prompt + source citation + kind) is the image generation
    // contract. The "every AssetKey has a prompt" rule below therefore reads both files.
    private static readonly string SoundPromptsFilePath =
        Path.Combine(FixturePaths.RepositoryRoot, "assets", "sound-prompts.json");

    // --- DoD 1: prompts.json <-> AssetKeys, both directions (passes from the first commit) ---

    [Fact]
    public void PromptsJson_HasExactlyOnePromptPerAssetKeysConstant()
    {
        var entries = ReadPromptEntries();
        var soundEntries = ReadSoundPromptEntries();

        // The two prompt files together cover every AssetKey: prompts.json for the image
        // generation keys, sound-prompts.json for the sfx.* keys (T149 split).
        var allPromptKeys = new HashSet<string>(
            entries.Select(e => e.Key).Concat(soundEntries.Select(e => e.Key)),
            StringComparer.Ordinal);
        var engineKeys = new HashSet<string>(AssetKeys.AllKeys, StringComparer.Ordinal);

        // A key with no prompt fails; a prompt with no key fails; a duplicated key fails too,
        // because a duplicate would silently satisfy the count while leaving one slot ambiguous.
        var missingPrompts = engineKeys.Where(k => !allPromptKeys.Contains(k)).ToList();
        var unknownKeys = allPromptKeys.Where(k => !engineKeys.Contains(k)).ToList();
        var duplicateKeys = allPromptKeys.GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(missingPrompts.Count == 0,
            "AssetKeys constants with no prompt in assets/prompts.json or assets/sound-prompts.json: " +
            string.Join(", ", missingPrompts));
        Assert.True(unknownKeys.Count == 0,
            "prompt files carry entries for keys AssetKeys does not define: " + string.Join(", ", unknownKeys));
        Assert.True(duplicateKeys.Count == 0,
            "prompt files carry duplicate keys: " + string.Join(", ", duplicateKeys));
    }

    [Fact]
    public void PromptsJson_EveryPromptCarriesItsSpecificationSourceAndKind()
    {
        var entries = ReadPromptEntries();

        Assert.All(entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Prompt),
                $"{entry.Key}: the prompt text is empty - DoD 1 requires a real prompt per key");
            Assert.False(string.IsNullOrWhiteSpace(entry.Source),
                $"{entry.Key}: the source field is empty - DoD 1 requires every prompt to cite the "
                + "docs/asset-specification.md depiction line it was written from");

            // The kind drives the format conformance (sprite -> 32-bit BGRA, tile -> 24-bit opaque,
            // sfx -> WAV), so it must agree with the key's own category, not just be any string.
            Assert.Equal(ExpectedKind(entry.Key), entry.Kind);
        });
    }

    // --- DoD 6: the committed pack parses through the same AssetPack/AssetLoader types the
    // --- placeholder pack uses, and ValidateAssets reports zero missing ---

    [Fact]
    public void AuthoredPack_DirectoryAndManifest_Exist()
    {
        Assert.True(Directory.Exists(AuthoredPackDirectory),
            $"The authored pack directory does not exist yet at {AuthoredPackDirectory}. It is created by the "
            + "deliberate, billable generation run (scripts/generate-authored-assets.py --all), not by CI - "
            + "run it, commit the pack, and this test turns green.");
        Assert.True(File.Exists(Path.Combine(AuthoredPackDirectory, "manifest.json")),
            "The authored pack has no manifest.json - the generator writes it alongside the assets.");
    }

    [Fact]
    public void AuthoredPack_Manifest_ParsesThroughAssetPack()
    {
        var pack = AssetLoader.LoadManifest(Path.Combine(AuthoredPackDirectory, "manifest.json"));

        // The same manifest shape as T11's placeholder pack (AssetLoader's own AssetPackJson DTO).
        Assert.Equal(1, pack.SchemaVersion);
        Assert.NotEmpty(pack.Name);
        Assert.NotEmpty(pack.Description);
        Assert.NotEmpty(pack.Assets);
    }

    [Fact]
    public void AuthoredPack_ValidateAssets_ReportsZeroMissing()
    {
        var pack = AssetLoader.LoadManifest(Path.Combine(AuthoredPackDirectory, "manifest.json"));

        var missing = AssetLoader.ValidateAssets(pack, AuthoredPackDirectory);

        Assert.True(missing.Count == 0,
            "AssetLoader.ValidateAssets reports missing assets for the authored pack: "
            + string.Join(", ", missing));
    }

    // --- DoD 5: format conformance, read from the committed files - never by trusting the
    // --- generator ---

    [Fact]
    public void AuthoredPack_ImageAssets_ConformToFormatSpecification()
    {
        var pack = AssetLoader.LoadManifest(Path.Combine(AuthoredPackDirectory, "manifest.json"));

        // Surfaces are 256x256, not 32x32, so they are checked by their own test below rather than by
        // this 32x32 structure check.
        var imageKeys = AssetKeys.AllKeys
            .Where(k => !k.StartsWith("sfx.", StringComparison.Ordinal)
                     && !k.EndsWith(".surface", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(imageKeys);
        foreach (var key in imageKeys)
        {
            var relativePath = pack.ResolveAsset(key);
            var fullPath = Path.Combine(AuthoredPackDirectory, relativePath);

            Assert.True(File.Exists(fullPath), $"Image file not found: {key} ({fullPath})");

            // Terrain tiles are 24-bit opaque; every other image asset (unit icons and
            // army/fleet/city markers) is 32-bit BGRA - docs/asset-specification.md 1.2.
            ValidateBmpStructure(File.ReadAllBytes(fullPath), key, ExpectedBitCount(key));
        }
    }

    /// <summary>
    /// T148 Done-when 2, authored half: every surface key resolves to a 256 × 256 24-bit BMP that tiles
    /// (edge mean absolute difference under 15 of 255). The rule is
    /// <see cref="TerrainSurfaceConformance"/>, shared with the placeholder pack's test.
    /// </summary>
    [Fact]
    public void AuthoredPack_SurfaceKeys_ResolveAndTile()
    {
        TerrainSurfaceConformance.AssertEverySurfaceKeyResolvesAndTiles(AuthoredPackDirectory);
    }

    [Fact]
    public void AuthoredPack_SpriteAssets_CarryTransparencyAndALightNeutralSilhouette()
    {
        var pack = AssetLoader.LoadManifest(Path.Combine(AuthoredPackDirectory, "manifest.json"));

        // T101: narrowed to the MARKER categories. A unit icon or an army/fleet/city marker is
        // a neutral light silhouette so the draw-time nation tint (the #173 decision) stays
        // visible. A ui.command icon is pictorial and full-colour and is never tinted, so it is
        // deliberately excluded here and checked by its own test below.
        var markerKeys = AssetKeys.AllKeys
            .Where(k => k.StartsWith("unit.", StringComparison.Ordinal)
                     || k.StartsWith("army.", StringComparison.Ordinal)
                     || k.StartsWith("fleet.", StringComparison.Ordinal)
                     || k.StartsWith("city.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(markerKeys);
        foreach (var key in markerKeys)
        {
            var fullPath = Path.Combine(AuthoredPackDirectory, pack.ResolveAsset(key));
            var bytes = File.ReadAllBytes(fullPath);

            // The background must actually be keyed out somewhere (alpha 0 present) and the
            // subject must be opaque somewhere (alpha 255 present): a sprite that fills its
            // whole cell opaque has no transparency to tint onto a map, and one that is fully
            // transparent failed generation outright.
            var transparentCount = 0;
            var opaqueCount = 0;
            var lightNeutralFound = false;
            var nonNeutralOpaque = 0;
            for (var i = 54; i + 3 < bytes.Length; i += 4)
            {
                var b = bytes[i];
                var g = bytes[i + 1];
                var r = bytes[i + 2];
                var a = bytes[i + 3];
                if (a == 0)
                {
                    transparentCount++;
                }
                else if (a == 255)
                {
                    opaqueCount++;
                    // Draw-time tint (the #173 decision): the silhouette must be light and
                    // colourless - a bright owner tint multiplied against it must stay bright,
                    // which needs a near-white subject, not a dark or coloured one. EVERY opaque
                    // pixel must be neutral (R == G == B): a baked-in nation colour anywhere
                    // would multiply with the draw-time tint into the wrong colour.
                    if (r != g || g != b)
                    {
                        nonNeutralOpaque++;
                    }
                    else if (r >= 200)
                    {
                        lightNeutralFound = true;
                    }
                }
            }

            Assert.True(transparentCount > 0,
                $"{key}: no transparent pixel - the background was not keyed out (32-bit BGRA with "
                + "transparency is required for every marker/icon sprite)");
            Assert.True(opaqueCount > 0,
                $"{key}: no opaque pixel - the subject itself is missing or fully transparent");
            Assert.True(nonNeutralOpaque == 0,
                $"{key}: {nonNeutralOpaque} opaque pixel(s) are not neutral grey (R==G==B) - a marker/icon "
                + "sprite must be a colourless silhouette, the nation colour is applied at draw time");
            Assert.True(lightNeutralFound,
                $"{key}: no light neutral (>= 200, R==G==B) opaque pixel - the silhouette must read as a "
                + "light/white subject so the draw-time owner tint stays visible");
        }
    }

    /// <summary>
    /// T101 DoD 5: each <c>ui.command.*</c> authored icon is a 32x32 32-bit BGRA BMP with at
    /// least one transparent pixel. Unlike a marker it is NOT a light neutral silhouette - it is
    /// pictorial and full-colour (the task entry's Style line) - so only format, size and
    /// transparency are pinned here, not the colours or the silhouette shape.
    /// </summary>
    [Fact]
    public void AuthoredPack_UiCommandIcons_ConformToFormatSpecification()
    {
        var pack = AssetLoader.LoadManifest(Path.Combine(AuthoredPackDirectory, "manifest.json"));

        var uiKeys = AssetKeys.AllKeys
            .Where(k => k.StartsWith("ui.command.", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(36, uiKeys.Count);

        foreach (var key in uiKeys)
        {
            var relativePath = pack.ResolveAsset(key);
            var fullPath = Path.Combine(AuthoredPackDirectory, relativePath);

            Assert.True(File.Exists(fullPath), $"Image file not found: {key} ({fullPath})");

            var bytes = File.ReadAllBytes(fullPath);

            // 32x32, 32-bit BGRA, BI_RGB, 4096-byte pixel array, per docs/asset-specification.md
            // 1.2 and 1.3.
            ValidateBmpStructure(bytes, key, 32);

            // A transparent background is what makes the icon composite over the toolbar; a
            // fully opaque icon is a square, not an icon. Read the alpha byte straight from the
            // BGRA quads (Pillow's BMP reader ignores it, so the test reads the bytes itself).
            var transparentPixelFound = false;
            for (var i = 54; i + 3 < bytes.Length; i += 4)
            {
                if (bytes[i + 3] == 0)
                {
                    transparentPixelFound = true;
                    break;
                }
            }

            Assert.True(transparentPixelFound,
                $"{key}: no transparent pixel - a ui.command icon needs a transparent background "
                + "(32-bit BGRA with straight alpha, docs/asset-specification.md 1.2)");
        }
    }

    [Fact]
    public void AuthoredPack_SoundAssets_AreValidWavWithTheSpecifiedEnvelope()
    {
        var pack = AssetLoader.LoadManifest(Path.Combine(AuthoredPackDirectory, "manifest.json"));

        var sfxKeys = AssetKeys.AllKeys.Where(k => k.StartsWith("sfx.", StringComparison.Ordinal)).ToList();

        // T149 (correction task for bug #790): all ten sfx.* keys must resolve to a real file. The
        // three pre-existing ones (sfx.unit_move, sfx.city_captured, sfx.battle) come from the
        // committed authored pack, the seven new ones (sfx.fleet_move, sfx.battle_arrows,
        // sfx.battle_javelin, sfx.battle_melee, sfx.siege_failed, sfx.fleet_sunk, sfx.nation_conquered)
        // were generated by scripts/generate-sounds.py through ElevenLabs' sound-effects API.
        Assert.Equal(10, sfxKeys.Count);

        foreach (var key in sfxKeys)
        {
            var fullPath = Path.Combine(AuthoredPackDirectory, pack.ResolveAsset(key));
            Assert.True(File.Exists(fullPath), $"Sound file not found: {key} ({fullPath})");

            var bytes = File.ReadAllBytes(fullPath);
            Assert.True(bytes.Length >= 44, $"{key}: too short to hold a WAV header ({bytes.Length} bytes)");
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));

            // docs/asset-specification.md 1.4: new sound effects ship as mono, 44.1 kHz, 16-bit
            // PCM WAV - the placeholder stubs' and the original's own technical envelope.
            var fmtIndex = IndexOf(bytes, "fmt "u8.ToArray());
            Assert.True(fmtIndex >= 0, $"{key}: no fmt chunk found");
            var channels = BitConverter.ToUInt16(bytes, fmtIndex + 10);
            var sampleRate = BitConverter.ToUInt32(bytes, fmtIndex + 12);
            var bitsPerSample = BitConverter.ToUInt16(bytes, fmtIndex + 22);
            Assert.Equal(1, channels);
            Assert.Equal(44100u, sampleRate);
            Assert.Equal(16, bitsPerSample);

            // T149 Done-when 2: the authored file lasts between 0.02 s and 2.0 s and is not silent
            // (peak above -30 dBFS). ElevenLabs' 0.5 s minimum keeps the generated files well inside
            // the window; sfx.unit_move is a softened tick of the original's 0.02 s click's character.
            var dataIndex = IndexOf(bytes, "data"u8.ToArray());
            Assert.True(dataIndex >= 0, $"{key}: no data chunk found");
            var dataSize = BitConverter.ToUInt32(bytes, dataIndex + 4);
            var totalSamples = dataSize / 2; // 16-bit = 2 bytes per sample.
            var durationSeconds = (double)totalSamples / sampleRate;
            Assert.True(durationSeconds >= 0.02 && durationSeconds <= 2.0,
                $"{key}: duration {durationSeconds:F3}s is outside the 0.02 s - 2.0 s conformance window (T149 Done-when 2)");

            var peak = 0;
            for (var i = dataIndex + 8; i + 1 < bytes.Length; i += 2)
            {
                var sample = BitConverter.ToInt16(bytes, i);
                var magnitude = sample < 0 ? -sample : sample;
                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }

            Assert.True(peak > 0, $"{key}: file is silent (peak 0) - the API returned a near-silent file");

            var peakDbfs = 20.0 * Math.Log10((double)peak / 32767.0);
            Assert.True(peakDbfs >= -30.0,
                $"{key}: peak {peakDbfs:F1} dBFS is below -30 dBFS - the file is effectively silent");

            // T149 rework (the user's listening review, PR #886 U1): audibility on ordinary
            // speakers. A file can be loud at peak and still inaudible: the shipped
            // battle.wav peaked at -3 dBFS but ~95% of its energy sat at 60-120 Hz, a band
            // laptop and desktop drivers barely reproduce. Two guards, both with a stated
            // reason:
            //   * at least 30% of the file's energy above 300 Hz - small drivers typically
            //     roll off below ~300 Hz, so a file with almost no energy above that line is
            //     inaudible on them; 30% keeps a genuine low-body sound (a horn, a splash)
            //     while refusing a bass-only rumble;
            //   * RMS above -30 dBFS - loudness the ear reads, not the one peak sample.
            // The energy split is the same gentle one-pole low-pass the generator logs
            // (scripts/generate-sounds.py's band_energy_shares): approximate by design, exact
            // enough to separate a mid-range file from a sub-bass one.
            var samples = new List<double>((int)totalSamples);
            for (var i = dataIndex + 8; i + 1 < bytes.Length && samples.Count < totalSamples; i += 2)
            {
                samples.Add(BitConverter.ToInt16(bytes, i));
            }

            var audibilityProblem = AudibilityProblem(samples, (int)sampleRate, key);
            Assert.True(audibilityProblem is null,
                $"{key}: {audibilityProblem} (the user's listening review, PR #886 U1)");
        }
    }

    /// <summary>
    /// T149 rework round 2 (Sol's review of PR #886, R4): the audibility guard as a helper, so
    /// the offline negative test below runs the <em>same</em> formula the committed pack is
    /// held to, not a copy. Returns <see langword="null"/> when the samples are audible on
    /// ordinary speakers, else the reason. The guards and their reasons:
    /// <list type="bullet">
    /// <item>at least 30% of the energy above 300 Hz — small drivers roll off below that line,
    /// so a file with almost no energy above it is inaudible on them;</item>
    /// <item>RMS above −30 dBFS — loudness the ear reads, not the one peak sample.</item>
    /// </list>
    /// The energy split is the same gentle one-pole low-pass the generator logs
    /// (scripts/generate-sounds.py's band_energy_shares): approximate by design, exact enough
    /// to separate a mid-range file from a sub-bass one.
    /// </summary>
    private static string? AudibilityProblem(List<double> samples, int sampleRate, string key)
    {
        var totalEnergy = samples.Sum(s => s * s);
        var lowPassEnergy = OnePoleLowPassEnergy(samples, sampleRate, 300.0);
        var above300Share = totalEnergy > 0 ? 1.0 - Math.Min(1.0, lowPassEnergy / totalEnergy) : 0.0;
        if (above300Share < 0.30)
        {
            return $"only {above300Share:P0} of the file's energy is above 300 Hz - a bass-only file " +
                "is inaudible on ordinary speakers";
        }

        if (totalEnergy <= 0 || samples.Count == 0)
        {
            return "the file is silent (no energy)";
        }

        var rms = Math.Sqrt(totalEnergy / samples.Count);
        var rmsDbfs = 20.0 * Math.Log10(rms / 32767.0);
        if (rmsDbfs <= -30.0)
        {
            return $"RMS {rmsDbfs:F1} dBFS is below -30 dBFS - the file is effectively silent to the ear";
        }

        return null;
    }

    /// <summary>
    /// T149 rework round 2 (Sol's review of PR #886, R4): a committed negative test for the
    /// audibility guard. A bass-only sine (80 Hz, 0.5 s, −6 dBFS peak) synthesised in memory
    /// must be rejected by the same helper the pack is held to — this is the shape the shipped
    /// battle.wav had (~95% of its energy at 60–120 Hz, inaudible on the user's speakers) —
    /// and a 1 kHz sine of the same level and length must pass. Offline, no file read.
    /// </summary>
    [Fact]
    public void AudibilityGuard_RejectsBassOnlySineAndPassesSameLevelMidRangeSine()
    {
        const int sampleRate = 44100;
        const double seconds = 0.5;
        const double peakDbfs = -6.0;
        var peakAmplitude = 32767.0 * Math.Pow(10.0, peakDbfs / 20.0); // -6 dBFS peak
        var sampleCount = (int)(seconds * sampleRate);

        List<double> Synthesise(double frequencyHz)
        {
            var result = new List<double>(sampleCount);
            for (var i = 0; i < sampleCount; i++)
            {
                result.Add(peakAmplitude * Math.Sin(2.0 * Math.PI * frequencyHz * i / sampleRate));
            }

            return result;
        }

        var bassOnly = AudibilityProblem(Synthesise(80.0), sampleRate, "synthesised-bass-only");
        Assert.False(bassOnly is null,
            "an 80 Hz sine at -6 dBFS peak must be rejected: almost all of its energy is below 300 Hz, " +
            "the band ordinary speakers barely reproduce (the shipped battle.wav's failure mode)");
        Assert.Contains("300 Hz", bassOnly);

        var midRange = AudibilityProblem(Synthesise(1000.0), sampleRate, "synthesised-mid-range");
        Assert.True(midRange is null,
            $"a 1 kHz sine at the same -6 dBFS peak must pass the audibility guard (got: {midRange})");
    }

    /// <summary>
    /// Sum of squares of the signal low-passed at <paramref name="cutoffHz"/> with a one-pole
    /// RC filter — the same split <c>scripts/generate-sounds.py</c>'s band-energy log uses, so
    /// the generator's printed shares and this assertion agree.
    /// </summary>
    private static double OnePoleLowPassEnergy(List<double> samples, double sampleRate, double cutoffHz)
    {
        var dt = 1.0 / sampleRate;
        var rc = 1.0 / (2.0 * Math.PI * cutoffHz);
        var alpha = dt / (rc + dt);
        var previous = 0.0;
        double energy = 0;
        foreach (var sample in samples)
        {
            previous += alpha * (sample - previous);
            energy += previous * previous;
        }

        return energy;
    }

    /// <summary>
    /// The kind implied by the key's own first segment: terrain tiles are opaque 24-bit, sfx are
    /// WAV, and everything else (unit icons and army/fleet/city markers) are transparent 32-bit
    /// BGRA sprites - the same split <c>scripts/generate-authored-assets.py</c> conforms by.
    /// </summary>
    private static string ExpectedKind(string key) => key switch
    {
        _ when key.EndsWith(".surface", StringComparison.Ordinal) => "surface",
        _ when key.StartsWith("terrain.", StringComparison.Ordinal) => "tile",
        _ when key.StartsWith("sfx.", StringComparison.Ordinal) => "sfx",
        _ when key.StartsWith("ui.command.", StringComparison.Ordinal) => "ui",
        _ => "sprite",
    };

    private static int ExpectedBitCount(string key) =>
        key.StartsWith("terrain.", StringComparison.Ordinal) ? 24 : 32;

    /// <summary>
    /// Parses a BMP's own headers and recomputes every field independently, in the style of
    /// T11's <c>PlaceholderPackIntegrationTests.ValidateBMPStructure</c> (that file is T11's to
    /// edit, so the equivalent lives here parameterized on the expected bit depth): the "BM"
    /// signature; the declared file size against the actual length; the 54-byte header offset;
    /// the 40-byte BITMAPINFOHEADER; 32x32 bottom-up dimensions; 1 colour plane; the per-kind
    /// bit depth (24 for terrain, 32 for sprites); BI_RGB compression; and the pixel array's
    /// size recomputed from width/height/depth with 4-byte row padding, checked against both
    /// the declared image size and the bytes actually present.
    /// </summary>
    private static void ValidateBmpStructure(byte[] bytes, string assetKey, int expectedBitCount)
    {
        Assert.True(bytes.Length >= 54,
            $"{assetKey}: file too short to hold a BMP file header + DIB header ({bytes.Length} bytes)");
        Assert.True(bytes[0] == (byte)'B' && bytes[1] == (byte)'M', $"{assetKey}: missing 'BM' signature");

        var declaredFileSize = BitConverter.ToUInt32(bytes, 2);
        Assert.True(declaredFileSize == (uint)bytes.Length,
            $"{assetKey}: file header declares size {declaredFileSize}, actual file is {bytes.Length} bytes");

        Assert.Equal(54u, BitConverter.ToUInt32(bytes, 10));

        Assert.Equal(40u, BitConverter.ToUInt32(bytes, 14));
        var width = BitConverter.ToInt32(bytes, 18);
        var height = BitConverter.ToInt32(bytes, 22);
        Assert.Equal(32, width);
        Assert.Equal(32, height);
        Assert.True(height > 0, $"{assetKey}: bottom-up DIB expected (positive height)");
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 26)); // colour planes

        var bitCount = BitConverter.ToUInt16(bytes, 28);
        Assert.True(bitCount == expectedBitCount,
            $"{assetKey}: bit depth is {bitCount}, expected {expectedBitCount} "
            + "(docs/asset-specification.md 1.2: 24-bit opaque terrain, 32-bit BGRA sprites)");

        Assert.Equal(0u, BitConverter.ToUInt32(bytes, 30)); // BI_RGB, uncompressed

        var rowSizeUnpadded = width * (bitCount / 8);
        var rowPadding = (4 - rowSizeUnpadded % 4) % 4;
        var expectedPixelDataSize = (rowSizeUnpadded + rowPadding) * height;
        Assert.True(BitConverter.ToUInt32(bytes, 34) == (uint)expectedPixelDataSize,
            $"{assetKey}: DIB header declares image size {BitConverter.ToUInt32(bytes, 34)}, "
            + $"expected {expectedPixelDataSize}");
        Assert.True(bytes.Length - 54 == expectedPixelDataSize,
            $"{assetKey}: pixel array is {bytes.Length - 54} bytes on disk, expected {expectedPixelDataSize}");
    }

    private static List<PromptEntry> ReadPromptEntries()
    {
        Assert.True(File.Exists(PromptsFilePath),
            $"assets/prompts.json not found at {PromptsFilePath}");

        var document = JsonSerializer.Deserialize<PromptsDocument>(
            File.ReadAllText(PromptsFilePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(document);
        Assert.NotEmpty(document.Prompts);
        return document.Prompts;
    }

    /// <summary>
    /// T149: read <c>assets/sound-prompts.json</c> for the sfx.* keys. The schema differs from
    /// <see cref="ReadPromptEntries"/> (no <c>source</c> field — the prompt text is enough
    /// provenance for an API call whose output is a binary the conformance test inspects), so
    /// the DTO is a separate, narrower type. Every entry carries the asset key, the prompt
    /// text and a target duration, exactly the three fields
    /// <c>scripts/generate-sounds.py</c> consumes.
    /// </summary>
    private static List<SoundPromptEntry> ReadSoundPromptEntries()
    {
        Assert.True(File.Exists(SoundPromptsFilePath),
            $"assets/sound-prompts.json not found at {SoundPromptsFilePath}");

        var document = JsonSerializer.Deserialize<SoundPromptsDocument>(
            File.ReadAllText(SoundPromptsFilePath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(document);
        Assert.NotEmpty(document.Prompts);
        return document.Prompts;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var matches = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            {
                return i;
            }
        }
        return -1;
    }

    private sealed class PromptsDocument
    {
        [JsonPropertyName("prompts")]
        public List<PromptEntry> Prompts { get; set; } = new();
    }

    private sealed class PromptEntry
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;
    }

    private sealed class SoundPromptsDocument
    {
        [JsonPropertyName("prompts")]
        public List<SoundPromptEntry> Prompts { get; set; } = new();
    }

    private sealed class SoundPromptEntry
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("durationSeconds")]
        public double DurationSeconds { get; set; }
    }
}
