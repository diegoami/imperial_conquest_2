using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Export;
using IC2.Engine.Tests.Model;
using IC2.Engine.Tests.TestInfrastructure;
using Xunit;

// Deliberately not "IC2.Engine.Tests.Serialization": tests/IC2.Engine.Tests/Economy's own
// QuarterlyEconomySystemTests.cs references "Serialization.GameDataValidation" unqualified, relying
// on C#'s namespace lookup falling through to IC2.Engine.Serialization because no sibling namespace
// segment named "Serialization" exists under IC2.Engine.Tests. Declaring exactly that namespace here
// would shadow it (the lookup finds IC2.Engine.Tests.Serialization first, has no GameDataValidation
// member, and does not fall back further) and break that file's build -- which is outside this
// task's Owns list to fix. Reported as a follow-up rather than patched there; this file keeps this
// task entirely inside its own Owns list by using a namespace that cannot collide with it.
namespace IC2.Engine.Tests.SerializationTests;

/// <summary>
/// <c>docs/tasks/T62.md</c>'s Done-when lines 1-4: a world's base64 terrain grid may name a sidecar
/// file instead of embedding its data inline, the loader resolves it byte-for-byte before anything
/// else sees the document, a missing sidecar is a typed error naming both paths, and the toy world
/// -- run-length, no sidecar at all -- is unaffected and the exemption is asserted, not assumed.
/// </summary>
public class WorldTerrainSidecarTests
{
    /// <summary>
    /// DoD 1: pinned from the committed world file as it stood immediately before the T62 split,
    /// via <c>jq -j '.terrain.data' data/worlds/classical-mediterranean.json | sha256sum</c> against
    /// that commit. The resolved sidecar content is compared against this directly (string equality
    /// by hash) rather than by re-decoding and diffing 44,800-cell arrays: <see cref="TerrainGrid.Decode"/>'s
    /// bytes-to-cells step is a pure, unmodified function of this string, so hash equality of the
    /// input implies array equality of the output, and a hash is exactly what the task's "the base64
    /// is the DAT's own terrain encoding ... prove it by hash" hazard asks for.
    /// </summary>
    private const string PreSplitTerrainDataSha256 =
        "988cbedb401d7bef246462ec9d87a83ab39204a72d0acb15dc3f8c093eee35b3";

    /// <summary>DoD 1: the resolved terrain data is byte-for-byte what shipped before the sidecar split.</summary>
    [Fact]
    public void Classical_world_terrain_resolves_to_the_pre_split_bytes_exactly()
    {
        var world = GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile);

        Assert.Equal(TerrainEncoding.Base64, world.Terrain.Encoding);
        Assert.NotNull(world.Terrain.Data);

        // Cleared on resolution (GameDataLoader.ResolveWorldTerrainSidecar): a resolved grid must be
        // indistinguishable from one that embedded "data" all along, never carrying both.
        Assert.Null(world.Terrain.DataFile);

        var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(world.Terrain.Data!)));
        Assert.Equal(PreSplitTerrainDataSha256, actualHash, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>DoD 1: the resolved grid still decodes to the confirmed 320x140 cell count.</summary>
    [Fact]
    public void Classical_world_terrain_still_decodes_to_the_confirmed_cell_count()
    {
        var world = GameDataLoader.LoadFile<World>(ExportedDataPaths.WorldFile);
        var cells = world.Terrain.Decode(world.Width, world.Height);

        Assert.Equal(320 * 140, cells.Length);
    }

    /// <summary>
    /// Sanity on the committed file itself: it is the sidecar form, not a leftover inline copy --
    /// this is what a reviewer diffing the JSON directly (without jq) would otherwise have to trust.
    /// </summary>
    [Fact]
    public void The_committed_world_json_points_at_the_sidecar_not_inline_data()
    {
        var node = (JsonObject)JsonNode.Parse(File.ReadAllText(ExportedDataPaths.WorldFile))!;
        var terrain = node["terrain"]!.AsObject();

        Assert.Equal("classical-mediterranean.terrain.b64", (string?)terrain["dataFile"]);
        Assert.Null(terrain["data"]);
    }

    /// <summary>
    /// DoD 4: the toy world stays run-length, which never carries a sidecar -- only a base64 grid
    /// (the encoding the full 320x140 export needs) can name <c>dataFile</c> at all; a run-length
    /// grid's own <c>runs</c> list is already the handful of entries a sidecar exists to avoid. This
    /// is the "per-world rule" the task entry asks to be stated and tested rather than left implicit.
    /// </summary>
    [Fact]
    public void Toy_world_has_no_terrain_sidecar_and_still_loads()
    {
        var node = (JsonObject)JsonNode.Parse(File.ReadAllText(TestPaths.ToyWorldFile))!;
        var terrain = node["terrain"]!.AsObject();

        Assert.Equal("runLength", (string?)terrain["encoding"]);
        Assert.Null(terrain["dataFile"]);

        var world = GameDataLoader.LoadFile<World>(TestPaths.ToyWorldFile);
        var cells = world.Terrain.Decode(world.Width, world.Height);

        Assert.Equal(world.Width * world.Height, cells.Length);
    }

    /// <summary>Both the classical and toy worlds resolve through the same repository load.</summary>
    [Fact]
    public void GameDataRepository_resolves_both_shipped_worlds()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);

        Assert.NotNull(repository.WorldById("classical-mediterranean"));
        Assert.NotNull(repository.WorldById("toy-3city"));
    }

    /// <summary>A base64 grid naming both "data" and "dataFile" is rejected, never silently picking one.</summary>
    [Fact]
    public void A_terrain_grid_naming_both_data_and_dataFile_is_malformed()
    {
        var node = (JsonObject)JsonNode.Parse(File.ReadAllText(TestPaths.ToyWorldFile))!;
        node["terrain"] = new JsonObject
        {
            ["encoding"] = "base64",
            ["runs"] = null,
            ["data"] = "AAA=",
            ["dataFile"] = "whatever.terrain.b64",
        };

        // documentPath is synthetic ("both.json"): the mutual-exclusion check fires before any file
        // I/O is attempted, exactly like TypedLoadErrorTests' other synthetic-document cases.
        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<World>("both.json", node.ToJsonString()));

        Assert.Contains("both", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review finding B3: a run-length grid naming "dataFile" was silently accepted and passed
    /// through unresolved -- the loader's DataFile-handling branch only looked at base64 grids, and
    /// nothing rejected it for any other encoding. This contradicted DoD 4's own stated exemption
    /// ("only a base64 grid ... can name dataFile at all"), which until this test existed only as a
    /// doc comment nothing enforced -- the "comment asserting behaviour at an edge no test visits"
    /// class of defect (build-process.md gate 5). The fix lives in TerrainGrid.Decode's run-length
    /// branch (World.cs), mirroring the sibling "must not carry data" check right next to it.
    /// </summary>
    [Fact]
    public void A_run_length_terrain_grid_naming_dataFile_is_malformed()
    {
        var node = (JsonObject)JsonNode.Parse(File.ReadAllText(TestPaths.ToyWorldFile))!;
        var terrain = node["terrain"]!.AsObject();
        terrain["dataFile"] = "nope.terrain.b64";

        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<World>("toy-with-run-length-dataFile.json", node.ToJsonString()));

        Assert.Contains("dataFile", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// DoD 2: a world whose sidecar is absent fails to load with a typed error naming both the world
    /// path and the resolved sidecar path -- never a null-terrain world, never a bare stack trace.
    /// </summary>
    [Fact]
    public void A_missing_terrain_sidecar_is_a_typed_error_naming_both_paths()
    {
        var tempDir = Directory.CreateTempSubdirectory("ic2-t62-sidecar-");
        try
        {
            var worldPath = WriteWorldWithTerrainDataFile(tempDir.FullName, "does-not-exist.terrain.b64");
            var expectedSidecarPath = Path.GetFullPath(Path.Combine(tempDir.FullName, "does-not-exist.terrain.b64"));

            var error = Assert.Throws<MissingTerrainSidecarException>(
                () => GameDataLoader.LoadFile<World>(worldPath));

            Assert.Equal(worldPath, error.WorldPath);
            Assert.Equal(expectedSidecarPath, error.SidecarPath);
            Assert.Contains(worldPath, error.Message, StringComparison.Ordinal);
            Assert.Contains("does-not-exist.terrain.b64", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Review finding N1 (partly) and N2: an empty or whitespace "dataFile" must be rejected as
    /// malformed rather than silently resolving to the world's own directory and then reporting a
    /// misleading "does not exist".
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_or_whitespace_dataFile_is_malformed(string dataFile)
    {
        var tempDir = Directory.CreateTempSubdirectory("ic2-t62-sidecar-");
        try
        {
            var worldPath = WriteWorldWithTerrainDataFile(tempDir.FullName, dataFile);

            var error = Assert.Throws<MalformedGameDataException>(
                () => GameDataLoader.LoadFile<World>(worldPath));

            Assert.Contains("dataFile", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Review finding N2: DoD 2 says the sidecar is found "relative to the world file", so an
    /// absolute path or one that escapes the world's own directory via ".." must be rejected rather
    /// than silently followed -- otherwise a world could point at a file outside the directory T27's
    /// packaging script (and everything else that treats a world as "its own directory") would copy.
    /// </summary>
    [Theory]
    [InlineData("..")]
    [InlineData("../escaped.terrain.b64")]
    public void A_dataFile_that_escapes_the_worlds_own_directory_is_malformed(string dataFile)
    {
        var tempDir = Directory.CreateTempSubdirectory("ic2-t62-sidecar-");
        try
        {
            // A real file one level up, so a bug that lets the traversal through would otherwise
            // succeed (proving the check itself is what blocks it, not a coincidental missing file).
            var escapeTarget = Path.Combine(tempDir.Parent!.FullName, "escaped.terrain.b64");
            File.WriteAllText(escapeTarget, "AAA=");
            try
            {
                var worldPath = WriteWorldWithTerrainDataFile(tempDir.FullName, dataFile);

                var error = Assert.Throws<MalformedGameDataException>(
                    () => GameDataLoader.LoadFile<World>(worldPath));

                Assert.Contains("dataFile", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(escapeTarget);
            }
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>Review finding N2: an absolute path is rejected the same way.</summary>
    [Fact]
    public void An_absolute_dataFile_path_is_malformed()
    {
        var tempDir = Directory.CreateTempSubdirectory("ic2-t62-sidecar-");
        try
        {
            var elsewhere = Path.Combine(Path.GetTempPath(), "ic2-t62-elsewhere-" + Guid.NewGuid().ToString("N") + ".b64");
            File.WriteAllText(elsewhere, "AAA=");
            try
            {
                var worldPath = WriteWorldWithTerrainDataFile(tempDir.FullName, elsewhere);

                var error = Assert.Throws<MalformedGameDataException>(
                    () => GameDataLoader.LoadFile<World>(worldPath));

                Assert.Contains("dataFile", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(elsewhere);
            }
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Writes a copy of the toy world into <paramref name="directory"/> with its terrain grid
    /// replaced by a base64 grid naming <paramref name="dataFile"/>, and returns the written path.
    /// Shared by every test above that needs a real file on disk to resolve a sidecar path against.
    /// </summary>
    private static string WriteWorldWithTerrainDataFile(string directory, string dataFile)
    {
        var worldPath = Path.Combine(directory, "world-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(worldPath, WorldJsonWithTerrainDataFile(dataFile));
        return worldPath;
    }

    private static string WorldJsonWithTerrainDataFile(string dataFile)
    {
        var node = (JsonObject)JsonNode.Parse(File.ReadAllText(TestPaths.ToyWorldFile))!;
        node["terrain"] = new JsonObject
        {
            ["encoding"] = "base64",
            ["runs"] = null,
            ["data"] = null,
            ["dataFile"] = dataFile,
        };

        return node.ToJsonString();
    }

    /// <summary>T71 (#284 N3): an empty document path with a "dataFile" is a typed error, not an ArgumentException.</summary>
    [Fact]
    public void An_empty_document_path_with_a_dataFile_is_a_typed_error()
    {
        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<World>("", WorldJsonWithTerrainDataFile("x.terrain.b64")));

        Assert.Contains("document path is empty", error.Message, StringComparison.Ordinal);
    }

    /// <summary>T71 (#284 R2-N2): a NUL in "dataFile" is a typed error, not an ArgumentException (the CLI catches only GameDataException).</summary>
    [Fact]
    public void A_dataFile_containing_a_NUL_character_is_a_typed_error()
    {
        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<World>("world.json", WorldJsonWithTerrainDataFile("bad\0name.b64")));

        Assert.Contains("invalid path character", error.Message, StringComparison.Ordinal);
        Assert.Contains("dataFile", error.Message, StringComparison.Ordinal);
    }

    /// <summary>T71 (#284 R2-N2): the other invalid path characters, where the platform has any besides NUL (Windows does; Linux does not).</summary>
    [SkippableFact]
    public void A_dataFile_containing_another_invalid_path_character_is_a_typed_error()
    {
        var others = Path.GetInvalidPathChars().Where(c => c != '\0').ToArray();
        Skip.If(others.Length == 0, "this platform's only invalid path character is NUL, covered by the NUL test.");

        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<World>("world.json", WorldJsonWithTerrainDataFile("bad" + others[0] + "name.b64")));

        Assert.Contains("invalid path character", error.Message, StringComparison.Ordinal);
    }

    /// <summary>T71: an invalid character in the document path itself is typed the same way.</summary>
    [Fact]
    public void A_document_path_containing_a_NUL_character_is_a_typed_error()
    {
        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<World>("wor\0ld.json", WorldJsonWithTerrainDataFile("x.terrain.b64")));

        Assert.Contains("document path", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// T71 (#284 R2-N3): "." names the world's directory, so it is rejected as a directory, not as an
    /// escape from the directory.
    /// </summary>
    [Fact]
    public void A_dot_dataFile_is_rejected_as_a_directory_not_as_an_escape()
    {
        var tempDir = Directory.CreateTempSubdirectory("ic2-t71-sidecar-");
        try
        {
            var worldPath = WriteWorldWithTerrainDataFile(tempDir.FullName, ".");

            var error = Assert.Throws<MalformedGameDataException>(() => GameDataLoader.LoadFile<World>(worldPath));

            Assert.Contains("names a directory, not a file", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("escape", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// T71 (#284 R2-N3): "nul" is a Windows device name; it is rejected as a device, not as an escape.
    /// On other platforms "nul" is an ordinary (missing) file name, so there is nothing to assert.
    /// </summary>
    [SkippableFact]
    public void A_nul_dataFile_is_rejected_as_a_reserved_device_not_as_an_escape()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "\"nul\" is a reserved device name only on Windows; elsewhere it is an ordinary file name.");

        var tempDir = Directory.CreateTempSubdirectory("ic2-t71-sidecar-");
        try
        {
            var worldPath = WriteWorldWithTerrainDataFile(tempDir.FullName, "nul");

            var error = Assert.Throws<MalformedGameDataException>(() => GameDataLoader.LoadFile<World>(worldPath));

            Assert.Contains("reserved device", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("escape", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// T71 (#284 R2-N3): the directory-prefix compare follows the platform's file-name case rule (see
    /// <c>GameDataLoader.PathComparison</c>). "../WORLDS/ok.b64" from a directory named "worlds" is
    /// inside it on a case-insensitive file system (Windows, macOS) and an escape on a case-sensitive
    /// one (Linux); the test asserts whichever rule the platform it runs on has.
    /// </summary>
    [Fact]
    public void The_directory_prefix_compare_follows_the_platforms_case_rule()
    {
        var tempDir = Directory.CreateTempSubdirectory("ic2-t71-sidecar-");
        try
        {
            var worlds = Directory.CreateDirectory(Path.Combine(tempDir.FullName, "worlds"));
            // A sidecar the toy world can really load (its own terrain, re-encoded as base64), so a
            // resolved path is proved by the loaded data, not by the absence of one error message.
            var toy = GameDataLoader.LoadFile<World>(TestPaths.ToyWorldFile);
            var cells = toy.Terrain.Decode(toy.Width, toy.Height);
            var sidecarText = Convert.ToBase64String(cells.SelectMany(c => new[] { (byte)(c & 0xFF), (byte)(c >> 8) }).ToArray());
            File.WriteAllText(Path.Combine(worlds.FullName, "ok.b64"), sidecarText);
            var worldPath = WriteWorldWithTerrainDataFile(worlds.FullName, "../WORLDS/ok.b64");

            // A genuine escape (a real sibling directory with a loadable sidecar) is rejected on every
            // platform, so deleting the prefix guard fails this test everywhere.
            var sibling = Directory.CreateDirectory(Path.Combine(tempDir.FullName, "sibling"));
            File.WriteAllText(Path.Combine(sibling.FullName, "ok.b64"), sidecarText);
            var escaping = Assert.Throws<MalformedGameDataException>(
                () => GameDataLoader.LoadFile<World>(WriteWorldWithTerrainDataFile(worlds.FullName, "../sibling/ok.b64")));
            Assert.Contains("escape", escaping.Message, StringComparison.Ordinal);

            var caseInsensitive = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
            if (caseInsensitive)
            {
                // Inside the directory: resolved and read back as the sidecar's own data. Fails
                // if the compare is made case-sensitive again (the path would then be rejected as an escape).
                var world = GameDataLoader.LoadFile<World>(worldPath);
                Assert.Equal(sidecarText, world.Terrain.Data);
                Assert.Null(world.Terrain.DataFile);
            }
            else
            {
                var error = Assert.Throws<MalformedGameDataException>(() => GameDataLoader.LoadFile<World>(worldPath));
                Assert.Contains("escape", error.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>T71 (#284 R2-N3): the missing-sidecar message names the resolved sidecar path exactly once.</summary>
    [Fact]
    public void The_missing_sidecar_message_names_the_sidecar_path_once()
    {
        var tempDir = Directory.CreateTempSubdirectory("ic2-t71-sidecar-");
        try
        {
            var worldPath = WriteWorldWithTerrainDataFile(tempDir.FullName, "does-not-exist.terrain.b64");
            var error = Assert.Throws<MissingTerrainSidecarException>(() => GameDataLoader.LoadFile<World>(worldPath));

            var occurrences = error.Message.Split(error.SidecarPath).Length - 1;
            Assert.Equal(1, occurrences);
            Assert.Contains("does not exist", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}

/// <summary>
/// DoD 3, the sidecar half: re-running the export must reproduce the committed sidecar byte-for-byte,
/// exactly as <see cref="ExportScriptReproducibilityTests"/> already proves for the world/ruleset/
/// scenario JSON. That test's own SHA256 check does not cover the sidecar (it predates T62 and is
/// outside this task's Owns list to extend), so this is the sidecar's own equivalent, sharing the
/// same DAT-availability gate.
/// </summary>
/// <remarks>
/// <para>
/// Review finding B1: this test and <see cref="ExportScriptReproducibilityTests"/> both invoke
/// <c>dotnet run scripts/export-classical-world.cs</c> as a separate process. Both compile that
/// file-based app into the same content-hashed temp <c>obj/</c> directory, and two concurrent
/// compiles of the same script collide there (<c>CS2012: Cannot open ... for writing</c>) --
/// reproduced 3/3 by touching the script and running both test classes together. Splitting this one
/// test into its own class in a non-parallel collection marked <c>DisableParallelization = true</c>
/// is enough to fix it without touching <c>ExportScriptReproducibilityTests.cs</c> (outside this
/// task's Owns list): xUnit runs every non-parallel collection strictly after all parallel
/// collections -- including the Export tests' default one -- have finished, so the two exports can
/// no longer overlap in time.
/// </para>
/// <para>
/// T74 (bug #320): joins <see cref="DotnetRunScriptCollection"/>, the single non-parallel collection
/// every dotnet-run test now shares in one place (superseding the narrower collection this class used
/// to define and name itself), and runs through the shared <see cref="DotnetRunScriptRunner.Run"/>
/// against <see cref="DotnetRunArtifactsFixture"/>'s isolated build-output directory, sharing the
/// same "export-classical-world" subdirectory <see cref="ExportScriptReproducibilityTests"/> uses --
/// both run this exact, unmodified script (see <see cref="RunExportScript"/>'s own remark for why
/// that sharing is deliberate).
/// </para>
/// </remarks>
[Collection(DotnetRunScriptCollection.Name)]
public class WorldTerrainExportReproducibilityTests
{
    public WorldTerrainExportReproducibilityTests(DotnetRunArtifactsFixture artifacts)
    {
        _artifacts = artifacts;
    }

    private readonly DotnetRunArtifactsFixture _artifacts;

    private const string BackupSuffix = ".before-rerun.bak";

    /// <summary>
    /// A leftover backup is always the committed original (the export only starts after the move that
    /// made it), so it replaces whatever is at <paramref name="sidecarPath"/> -- nothing, or a partial
    /// export from the killed run.
    /// </summary>
    internal static void RestoreSidecarFromLeftoverBackup(string sidecarPath, string backupPath)
    {
        if (File.Exists(backupPath))
        {
            File.Move(backupPath, sidecarPath, overwrite: true);
        }
    }

    /// <summary>
    /// T71 (#284 R2-N1) Done-when 3: delete the sidecar, leave a <c>.bak</c>, and the start-of-run
    /// restore brings the sidecar back. Needs no original game files, so it runs everywhere.
    /// </summary>
    [Fact]
    public void A_killed_run_that_left_the_sidecar_deleted_is_repaired_from_its_backup()
    {
        var tempDir = Directory.CreateTempSubdirectory("ic2-t71-restore-");
        try
        {
            var sidecar = Path.Combine(tempDir.FullName, "x.terrain.b64");
            var backup = sidecar + BackupSuffix;
            File.WriteAllText(backup, "COMMITTED");

            RestoreSidecarFromLeftoverBackup(sidecar, backup);

            Assert.Equal("COMMITTED", File.ReadAllText(sidecar));
            Assert.False(File.Exists(backup));

            // No leftover: nothing changes.
            RestoreSidecarFromLeftoverBackup(sidecar, backup);
            Assert.Equal("COMMITTED", File.ReadAllText(sidecar));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [SkippableFact]
    public void Rerunning_the_export_reproduces_the_committed_terrain_sidecar_byte_for_byte()
    {
        Skip.IfNot(OriginalFilesAvailability.IsConfigured, OriginalFilesAvailability.SkipReason);

        var sidecarPath = Path.Combine(ExportedDataPaths.RepositoryRoot, "data", "worlds", "classical-mediterranean.terrain.b64");
        var backupPath = sidecarPath + BackupSuffix;

        // T71 (#284 R2-N1): a previous run killed between the move below and its restore left the
        // committed sidecar deleted and the only copy in the .bak. Restore it first, before anything
        // reads the sidecar, so the next run repairs rather than fails. (The backup stays beside the
        // sidecar, which keeps a same-volume move atomic; the restore-on-start is what makes a kill safe.)
        RestoreSidecarFromLeftoverBackup(sidecarPath, backupPath);
        var expectedHash = Sha256(sidecarPath);

        // Review finding B2: comparing the file with itself before and after passes even when a
        // mutated exporter stops writing the sidecar at all -- a file nobody touches still compares
        // equal to itself. Moving the committed file out of the way first turns "the exporter didn't
        // write it" into a missing file, not a stale-but-matching one; the export must recreate it
        // for this assertion to have anything to compare.
        File.Move(sidecarPath, backupPath, overwrite: true);
        try
        {
            var (exitCode, stdout, stderr) = RunExportScript();
            Assert.True(exitCode == 0, $"export-classical-world.cs exited {exitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");

            Assert.True(File.Exists(sidecarPath), "the export did not (re)write the terrain sidecar file.");
            Assert.Equal(expectedHash, Sha256(sidecarPath));
        }
        finally
        {
            // Restored unconditionally, success or failure, so a failing run never leaves the
            // worktree's tracked sidecar missing for the next command or test.
            if (File.Exists(backupPath))
            {
                File.Move(backupPath, sidecarPath, overwrite: true);
            }
        }
    }

    /// <summary>
    /// Mirrors <see cref="ExportScriptReproducibilityTests"/>'s own identically-purposed helper.
    /// T74 (bug #320): routes through the shared <see cref="DotnetRunScriptRunner.Run"/>, sharing the
    /// same "export-classical-world" artifacts subdirectory <see cref="ExportScriptReproducibilityTests"/>
    /// uses -- both run this exact, unmodified script, so sharing one build output between them pays
    /// the cold build once for the two of them rather than twice (see that class's own remark on
    /// <c>RunExportScript</c> for the measurement).
    /// </summary>
    private (int ExitCode, string Stdout, string Stderr) RunExportScript()
    {
        var datPath = OriginalFilesAvailability.DatPath
                      ?? throw new InvalidOperationException("DatPath is null despite IsConfigured being true.");
        var assetsDirectory = Path.GetDirectoryName(datPath)
                               ?? throw new InvalidOperationException($"'{datPath}' has no directory component.");

        var tempIni = Path.Combine(Path.GetTempPath(), "ic2-t62-sidecar-repro-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(tempIni, $"[assets]{Environment.NewLine}directory = {assetsDirectory}{Environment.NewLine}");

            var result = DotnetRunScriptRunner.Run(
                ExportedDataPaths.ExportScript,
                Path.Combine(_artifacts.ArtifactsPath, "export-classical-world"),
                ExportedDataPaths.RepositoryRoot,
                tempIni);
            return (result.ExitCode, result.Stdout, result.Stderr);
        }
        finally
        {
            if (File.Exists(tempIni))
            {
                File.Delete(tempIni);
            }
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
