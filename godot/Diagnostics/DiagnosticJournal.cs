using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IC2.Slice.Diagnostics;

public enum DiagnosticDetail { Unavailable, AppRoot, MainMenu, MainGameScreen, NewGameFlow, LoadGameScreen, SettingsScreen, TacticalBattleScreen, BattleEndedScreen, DiplomacyScreen, ConfirmPrompt, Open, Close, Frame, Announcement, Travel, Arrival, RoutHold, RoutFade, Initial, Placement, Movement, Target, Projectile, Shot, Melee, Rout, Cascade, Removal, Morale, Setup, HalfRound, Controller, CapWithdrawal, Terminal, Reconciliation }

public enum DiagnosticEvent { Startup, Screen, Focus, Quit, Command, Load, Save, Overlay, Result, Turn, Settings, BattleCommand, Frame, Stage, Skip, Cancel, Completion, Exception }

/// <summary>Local diagnostic data only. No gameplay decisions depend on this writer.</summary>
public sealed class DiagnosticJournal : IDisposable
{
    // [designed] T198 limits; scoped preflight found no existing persistent journal.
    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxRecordBytes = 4096;
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly Action? _warning;
    private readonly Action<string>? _fault;
    private FileStream? _stream;
    private long _sequence, _operation;
    private bool _disabled, _writing, _warned, _disposed;
    public string RunId { get; } = Guid.NewGuid().ToString("N");

    public DiagnosticJournal(string directory, Action? warning = null, Action<string>? fault = null)
    {
        _directory = directory;
        _warning = warning;
        _fault = fault;
        lock (_gate)
        {
            try { _fault?.Invoke("create"); Directory.CreateDirectory(_directory); Rotate(); Open(); }
            catch { Disable(); }
        }
    }

    public void Startup(string version, string build, string engine, string runtime, string platform, string pack)
    {
        // Trusted application/SDK metadata is reduced to a bounded identifier alphabet.
        Write(DiagnosticEvent.Startup, "POINT", 0, "app", metadata: new {
            product = "Imperial Conquest 2", version = SafeIdentifier(version, 80), development = version == "development",
            build = SafeIdentifier(build, 120), engine = SafeIdentifier(engine, 80), runtime = SafeIdentifier(runtime, 40),
            platform = SafeIdentifier(platform, 40), pack = SafeIdentifier(pack, 40), backend = "unavailable", adapter = "unavailable", driver = "unavailable" });
    }
    public void Detail(DiagnosticEvent kind, DiagnosticDetail detail, long? turn = null, long? frame = null,
        long? command = null, string? battle = null, int? side = null, int? halfRound = null, bool? computer = null, bool? general = null,
        double? shot = null, double? attack = null, bool? paused = null, bool? fast = null)
    {
        Write(kind, "POINT", 0, "tactical", turn: turn, metadata: new { detail = detail.ToString(), frame, command,
            battle = battle is null ? null : Token(battle), side, halfRound, computer, general,
            shot = shot is { } s && double.IsFinite(s) ? (double?)Math.Clamp(s, 0, 3) : null,
            attack = attack is { } a && double.IsFinite(a) ? (double?)Math.Clamp(a, 0, 3) : null, paused, fast });
    }
    public long Before(DiagnosticEvent kind, string source, string? command = null, string? target = null, long? turn = null)
    {
        lock (_gate)
        {
            long operation = ++_operation;
            Write(kind, "BEFORE", operation, source, command, target, turn);
            return operation;
        }
    }
    public void After(DiagnosticEvent kind, long operation, string source, bool success = true) =>
        Write(kind, success ? "AFTER" : "FAILURE", operation, source);
    public void Mark(DiagnosticEvent kind, string? source = null, long? number = null, string? token = null) =>
        Write(kind, "POINT", 0, source, number: number, token: token);
    public void Exception(Exception exception)
    {
        // Exception messages are untrusted (commands, names and paths). Keep types, cause structure,
        // HResult and method-only application stack frames; deliberately redact the payload message.
        var causes = new List<object>();
        for (Exception? cause = exception; cause is not null && causes.Count < 4; cause = cause.InnerException)
        {
            var frames = new StackTrace(cause, false).GetFrames() ?? Array.Empty<StackFrame>();
            causes.Add(new { type = SafeType(cause.GetType()), message = "[payload redacted]", code = cause.HResult,
                stack = frames.Select(f => f.GetMethod()).Where(m => m?.DeclaringType?.Namespace?.StartsWith("IC2.", StringComparison.Ordinal) == true)
                    .Take(2).Select(m => SafeType(m!.DeclaringType!) + "." + SafeIdentifier(m.Name, 48)).ToArray() });
        }
        Write(DiagnosticEvent.Exception, "FAILURE", 0, "managed", causes: causes);
    }
    public string TargetToken(string path)
    {
        try { return Token(Path.GetFullPath(path).Replace('\\', '/').ToUpperInvariant()); }
        catch { return Token("invalid-target"); }
    }
    public string Token(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RunId + "\n" + value)))[..24].ToLowerInvariant();
    public static string CommandKind(string? command)
    {
        // Never retain operands, even for known commands. Safe numeric context is supplied separately.
        var verb = command?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        return verb is "end" or "save" or "news" or "move" or "attack" or "shoot" or "recruit" or "mobilize" or "tax" or "diplomacy" or "battle" or "general" or "place" or "clear" or "surrender" ? verb : "unknown";
    }
    private static string SafeType(Type type) => SafeIdentifier(type.FullName ?? type.Name, 96);
    private static string SafeIdentifier(string value, int limit) => new(value.Take(limit).Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '+' or '`').ToArray());
    private static string SafeSource(string? source) => source is "app" or "main" or "dialog" or "save" or "check" or "peace" or "map" or "diplomacy" or "news" or "load" or "managed" or "tactical" or "settings" or "lost" or "gained" or "result" or "travel" ? source : "other";
    private void Write(DiagnosticEvent kind, string outcome, long operation, string? source, string? command = null, string? target = null, long? turn = null, long? number = null, string? token = null, object? causes = null, object? metadata = null)
    {
        lock (_gate)
        {
            if (_disabled || _disposed || _writing) return;
            _writing = true;
            try
            {
                var record = new { utc = DateTimeOffset.UtcNow.ToString("O"), sequence = ++_sequence, run = RunId, operation,
                    kind = kind.ToString(), outcome, source = SafeSource(source), command = command is null ? null : CommandKind(command),
                    target = target is null ? null : TargetToken(target), format = target is null ? null : (Path.GetExtension(target).ToLowerInvariant() is ".json" ? "json" : Path.GetExtension(target).ToLowerInvariant() is ".sav" ? "sav" : "other"),
                    turn, number, token = token is null ? null : Token(token), causes, metadata };
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
                if (bytes.Length > MaxRecordBytes) throw new InvalidDataException("Diagnostic record exceeded designed bound.");
                if (_stream!.Length + bytes.Length > MaxFileBytes) { _stream.Dispose(); _stream = null; Rotate(); Open(); }
                _fault?.Invoke("append"); _stream!.Write(bytes);
                _fault?.Invoke("flush"); _stream.Flush(flushToDisk: true);
            }
            catch { Disable(); }
            finally { _writing = false; }
        }
    }
    private void Open() => _stream = new FileStream(Path.Combine(_directory, "journal-0.jsonl"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
    private void Rotate()
    {
        _fault?.Invoke("rotate");
        // Fixed names only: never enumerate, follow or delete arbitrary neighbouring files.
        File.Delete(Path.Combine(_directory, "journal-4.jsonl"));
        for (int i = 3; i >= 0; i--)
        {
            var from = Path.Combine(_directory, $"journal-{i}.jsonl");
            if (File.Exists(from)) File.Move(from, Path.Combine(_directory, $"journal-{i + 1}.jsonl"), true);
        }
    }
    private void Disable()
    {
        _disabled = true;
        try { _stream?.Dispose(); } catch { }
        _stream = null;
        if (_warned) return;
        _warned = true;
        try { _warning?.Invoke(); } catch { }
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; try { _stream?.Dispose(); } catch { Disable(); } _stream = null; }
    }
}
