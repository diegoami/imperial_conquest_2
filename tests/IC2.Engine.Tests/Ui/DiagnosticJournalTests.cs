using System.Text.Json;
using IC2.Slice.Diagnostics;
using Xunit;
namespace IC2.Engine.Tests.Ui;
public sealed class DiagnosticJournalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ic2-diagnostics-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    [Fact] public void BeforeIsVisibleBeforeDisposeAndOutcomesCorrelate()
    {
        using var journal = new DiagnosticJournal(_directory);
        var operation = journal.Before(DiagnosticEvent.Command, "map", "move secret-name");
        var before = Read().Single();
        Assert.Equal("BEFORE", before.GetProperty("outcome").GetString());
        Assert.Equal(operation, before.GetProperty("operation").GetInt64());
        journal.After(DiagnosticEvent.Command, operation, "map");
        var records = Read();
        Assert.Equal(new long[] {1,2}, records.Select(r => r.GetProperty("sequence").GetInt64()));
        Assert.Equal(operation, records[1].GetProperty("operation").GetInt64());
        Assert.DoesNotContain("secret-name", ReadText(Path.Combine(_directory,"journal-0.jsonl")));
    }
    [Fact] public void PointIsVisibleToSeparateReaderBeforeDispose()
    {
        using var journal = new DiagnosticJournal(_directory);
        journal.Mark(DiagnosticEvent.Stage, "tactical");
        var point = Read().Single();
        Assert.Equal("POINT", point.GetProperty("outcome").GetString());
        Assert.Equal("Stage", point.GetProperty("kind").GetString());
    }
    [Fact] public void TokensHidePathsAndRemainRecomputable()
    {
        using var journal = new DiagnosticJournal(_directory);
        var token = journal.TargetToken("C:/private/Owner Name/save.json");
        Assert.Equal(token, journal.TargetToken("C:/private/Owner Name/save.json"));
        Assert.DoesNotContain("Owner", token);
        Assert.Equal("unknown", DiagnosticJournal.CommandKind("PRIVATE owner secret"));
    }
    [Fact] public void RotationCapsUtf8RecordsAndRetainsFiveFiles()
    {
        using var journal = new DiagnosticJournal(_directory);
        for(var i=0;i<7;i++) { journal.Mark(DiagnosticEvent.Screen, new string('界',20000)); FillCurrent(); journal.Mark(DiagnosticEvent.Screen); }
        var files=Directory.GetFiles(_directory,"journal-*.jsonl");
        Assert.Equal(5,files.Length);
        foreach(var file in files) { Assert.InRange(new FileInfo(file).Length,1,DiagnosticJournal.MaxFileBytes); foreach(var line in ReadText(file).Split('\n',StringSplitOptions.RemoveEmptyEntries)) { Assert.True(System.Text.Encoding.UTF8.GetByteCount(line)+1<=4096); using var parsed=JsonDocument.Parse(line); } }
    }
    [Fact] public void FailureWarnsOnceAndDoesNotReenter()
    {
        int warnings=0; DiagnosticJournal? journal=null;
        journal=new DiagnosticJournal(_directory, warning:()=> { warnings++; journal?.Mark(DiagnosticEvent.Exception); }, fault: point=> { if(point=="append") throw new IOException("private"); });
        using(journal) { journal.Mark(DiagnosticEvent.Screen); journal.Mark(DiagnosticEvent.Screen); }
        Assert.Equal(1,warnings);
    }
    [Theory] [InlineData("create")] [InlineData("append")] [InlineData("flush")] [InlineData("rotate")]
    public void StorageFailuresNeverEscape(string point)
    {
        int warnings=0;
        bool armed = point != "rotate";
        using var journal=new DiagnosticJournal(_directory, warning:()=>warnings++, fault:p=> {if(armed && p==point) throw new UnauthorizedAccessException();});
        if(point=="rotate") { journal.Mark(DiagnosticEvent.Screen); FillCurrent(); armed=true; }
        journal.Mark(DiagnosticEvent.Screen); journal.Mark(DiagnosticEvent.Screen);
        Assert.Equal(1,warnings);
    }
    [Fact] public void ExceptionsAreBoundedAndNeverContainPayloadPathsOrNames()
    {
        using var journal=new DiagnosticJournal(_directory);
        journal.Exception(new InvalidOperationException("C:/private/Owner Name/save.json arbitrary-player",new IOException("secret")));
        string text=ReadText(Path.Combine(_directory,"journal-0.jsonl"));
        Assert.Contains("InvalidOperationException",text); Assert.Contains("IOException",text);
        Assert.DoesNotContain("Owner",text);Assert.DoesNotContain("secret",text);Assert.DoesNotContain("arbitrary-player",text);
    }
    [Fact] public void ConcurrentWritesHaveStrictSequence()
    {
        using var journal=new DiagnosticJournal(_directory);
        Parallel.For(0,100,i=>journal.Exception(new Exception("private")));
        Assert.Equal(Enumerable.Range(1,100).Select(i=>(long)i),Read().Select(r=>r.GetProperty("sequence").GetInt64()));
    }
    [Theory]
    [InlineData("Move rejected (x): private", false)]
    [InlineData("Refused: Cannot set a melee target during placement.", false)]
    [InlineData("There is no pending peace treaty offer.", false)]
    [InlineData("Unknown nation 'private'.", false)]
    [InlineData("No human seat remains. The game is over.", false)]
    [InlineData("Usage: move <army>", false)]
    [InlineData("Unknown command 'private'", false)]
    [InlineData("move accepted", true)]
    [InlineData("rename-unit accepted: unit now named Rejected", true)]
    public void CommandRejectionsAreFailures(string outcome, bool expected) =>
        Assert.Equal(expected, DiagnosticJournal.CommandSucceeded(new[] { "prelude rejected", "> private", outcome, "News:", "rejected old report" }));
    [Fact] public void ExceptionRetainsSafeMeaningInnerCauseAndCodeFrames()
    {
        using var journal = new DiagnosticJournal(_directory);
        try { ThrowDiagnosticProbe(); }
        catch (Exception exception) { journal.Exception(exception); }
        var causes = Read().Single().GetProperty("causes");
        Assert.Contains("operation failed", causes[0].GetProperty("message").GetString());
        Assert.Contains("access denied", causes[1].GetProperty("message").GetString());
        Assert.Contains("ThrowDiagnosticProbe", causes[0].GetProperty("stack").ToString());
        var text = ReadText(Path.Combine(_directory, "journal-0.jsonl"));
        Assert.DoesNotContain("PrivateName", text);
        Assert.DoesNotContain("Owner Name", text);
    }
    private static void ThrowDiagnosticProbe() => throw new InvalidOperationException(
        "Operation failed for 'PrivateName' at C:/private/Owner Name/file.json", new IOException("Access denied: PrivateName"));
    [Fact] public void MalformedAndMaximumFieldsRemainParseableAndDoNotDisableWriter()
    {
        int warnings = 0;
        using var journal = new DiagnosticJournal(_directory, () => warnings++);
        journal.Startup(new string('\u754c', 100000), new string('x', 100000), "4.7.2", "10", "Windows", "default");
        var id = journal.Before(DiagnosticEvent.Load, "C:/private", "unknown \ud800 private", "bad\0path.json");
        journal.After(DiagnosticEvent.Load, id, "load", false);
        Exception exception = new IOException(new string('\u754c', 100000));
        for (int i = 0; i < 20; i++) exception = new InvalidOperationException(new string('x', 100000), exception);
        journal.Exception(exception);
        journal.Mark(DiagnosticEvent.Focus, "gained");
        Assert.Equal(0, warnings);
        Assert.Equal(5, Read().Length);
        Assert.Equal(4, Read()[3].GetProperty("causes").GetArrayLength());
        foreach (var line in ReadText(Path.Combine(_directory, "journal-0.jsonl")).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(line) + 1, 1, DiagnosticJournal.MaxRecordBytes);
    }
    [Fact] public void RestartRetainsNewestRunsAndDoesNotTouchNeighbours()
    {
        Directory.CreateDirectory(_directory);
        var neighbour = Path.Combine(_directory, "journal-private.jsonl");
        File.WriteAllText(neighbour, "unrelated");
        var runs = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            using var journal = new DiagnosticJournal(_directory);
            runs.Add(journal.RunId);
            journal.Mark(DiagnosticEvent.Startup, "app");
        }
        Assert.Equal("unrelated", File.ReadAllText(neighbour));
        for (int i = 0; i < 5; i++)
        {
            using var record = JsonDocument.Parse(ReadText(Path.Combine(_directory, $"journal-{i}.jsonl")));
            Assert.Equal(runs[7-i], record.RootElement.GetProperty("run").GetString());
        }
        Assert.False(File.Exists(Path.Combine(_directory, "journal-5.jsonl")));
    }
    [Fact] public void OnlyPositionAllowlistedNumericArgumentsAreRecorded()
    {
        using var journal = new DiagnosticJournal(_directory);
        journal.Before(DiagnosticEvent.Command, "dialog", "rename-unit army-0 1 8675309");
        journal.Before(DiagnosticEvent.Command, "dialog", "split-army army-0 8675309 0,1");
        journal.Before(DiagnosticEvent.Command, "map", "move army-0 100 36");
        journal.Before(DiagnosticEvent.Command, "check", "unknown 8675309");
        var text = ReadText(Path.Combine(_directory, "journal-0.jsonl"));
        Assert.DoesNotContain("8675309", text);
        Assert.DoesNotContain("army-0", text);
        Assert.Equal(new long[] { 100, 36 }, Read()[2].GetProperty("arguments").GetProperty("numeric").EnumerateArray().Select(n => n.GetInt64()));
        Assert.Equal(JsonValueKind.Null, Read()[3].GetProperty("arguments").ValueKind);
    }
    [Fact] public void FourMaximumDiagnosticMessagesRemainUnderEncodedRecordCap()
    {
        int warnings = 0;
        using var journal = new DiagnosticJournal(_directory, () => warnings++);
        Exception exception = new IOException(string.Concat(Enumerable.Repeat("unauthorizedaccessexception ", 100)));
        for (int i = 0; i < 4; i++) exception = new InvalidOperationException(exception.Message, exception);
        journal.Exception(exception);
        Assert.Equal(0, warnings);
        Assert.Equal(4, Read().Single().GetProperty("causes").GetArrayLength());
        Assert.InRange(new FileInfo(Path.Combine(_directory, "journal-0.jsonl")).Length, 1, DiagnosticJournal.MaxRecordBytes);
    }
    [Fact] public void ExceptionDoesNotRecombinePrivatePayloadFromAllowedWords()
    {
        using var journal = new DiagnosticJournal(_directory);
        journal.Exception(new InvalidOperationException("Operation failed; owner payload: file write access directory path"));
        Assert.Equal("operation failed", Read().Single().GetProperty("causes")[0].GetProperty("message").GetString());
    }
    private void FillCurrent()
    {
        var path=Path.Combine(_directory,"journal-0.jsonl");
        using var stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete);
        // Prepare real on-disk valid records just below the production byte cap. The next
        // production append must rotate; no copied rotation implementation or long fsync loop.
        var line=System.Text.Encoding.UTF8.GetBytes("{}\n");
        while(stream.Length<DiagnosticJournal.MaxFileBytes-3) stream.Write(line);
    }
    private static string ReadText(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    private JsonElement[] Read()=>ReadText(Path.Combine(_directory,"journal-0.jsonl")).Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(l=>JsonDocument.Parse(l).RootElement.Clone()).ToArray();
}
