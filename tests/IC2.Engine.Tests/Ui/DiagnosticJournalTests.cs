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
