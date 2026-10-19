using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IC2.Slice.Diagnostics;

public enum DiagnosticDetail { Unavailable, AppRoot, MainMenu, MainGameScreen, NewGameFlow, LoadGameScreen, SettingsScreen, TacticalBattleScreen, BattleEndedScreen, DiplomacyScreen, ConfirmPrompt, Open, Close, Frame, Announcement, Travel, Arrival, RoutHold, RoutFade, Initial, Placement, Movement, Target, Projectile, Shot, Melee, Rout, Cascade, Removal, Morale, Setup, HalfRound, Controller, CapWithdrawal, Terminal, Reconciliation, BattleResultScreen, GameEndScreen, HotseatHandoffScreen, PeaceOfferScreen, AboutDialog, ArmyTransferDialog, BalanceSheetDialog, BuildFleetDialog, ChangeUnitsDialog, FileDialog, FindCityDialog, FleetTransferDialog, FortifyDialog, RecruitMercenariesDialog, RecruitUnitDialog, RepairDialog, SplitArmyDialog, SplitFleetDialog, SupplyDialog, TaxationDialog, NewGameScreen, ScenarioSeatScreen, WorldChooserScreen }

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
        double? shot = null, double? attack = null, bool? paused = null, bool? fast = null,
        string? nation = null, string? attacker = null, string? defender = null, int? actorSlot = null, int? targetSlot = null, bool? terminal = null, bool? placed = null, string? pack = null, bool? sound = null)
    {
        Write(kind, "POINT", 0, "tactical", turn: turn, metadata: new { detail = detail.ToString(), frame, command,
            battle = battle is null ? null : Token(battle), side, halfRound, computer, general,
            authoritativeTurn = turn, snapshot = frame is null ? null : "immutable-displayed-frame",
            nation = nation is null ? null : Token(nation), attacker = attacker is null ? null : Token(attacker),
            defender = defender is null ? null : Token(defender), actorSlot, targetSlot, terminal, placed,
            pack = pack is null ? null : SafeIdentifier(pack, 40), sound,
            shot = shot is { } s && double.IsFinite(s) ? (double?)Math.Clamp(s, 0, 3) : null,
            attack = attack is { } a && double.IsFinite(a) ? (double?)Math.Clamp(a, 0, 3) : null, paused, fast });
    }
    public long Before(DiagnosticEvent kind, string source, string? command = null, string? target = null, long? turn = null, string? nation = null, bool? computer = null)
    {
        lock (_gate)
        {
            long operation = ++_operation;
            Write(kind, "BEFORE", operation, source, command, target, turn, metadata: new { nation = nation is null ? null : Token(nation), computer });
            return operation;
        }
    }
    public void After(DiagnosticEvent kind, long operation, string source, bool success = true) =>
        Write(kind, success ? "AFTER" : "FAILURE", operation, source);
    public void Mark(DiagnosticEvent kind, string? source = null, long? number = null, string? token = null) =>
        Write(kind, "POINT", 0, source, number: number, token: token);
    public void Exception(Exception exception)
    {
        // Messages are untrusted. Preserve only diagnostic vocabulary, never arbitrary words,
        // quoted payloads, paths, identifiers or numbers. Keep bounded method-only code frames.
        var causes = new List<object>();
        for (Exception? cause = exception; cause is not null && causes.Count < 4; cause = cause.InnerException)
        {
            var frames = new StackTrace(cause, false).GetFrames() ?? Array.Empty<StackFrame>();
            causes.Add(new { type = SafeType(cause.GetType()), message = SafeExceptionMessage(cause.Message), code = cause.HResult,
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
        var verb = command?.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        return verb is "end" or "save" or "news" or "move" or "attack" or "shoot" or "recruit" or "mobilize" or "tax" or "diplomacy" or "battle" or "general" or "place" or "clear" or "surrender" or "status" or "armies" or "cities" or "map" or "buy" or "attack-army" or "besiege-city" or "attack-fleet" or "disband-army" or "join-armies" or "join-units" or "split-army" or "army-transfer" or "split-unit" or "rename-unit" or "disband-unit" or "order-city" or "declare-war" or "make-peace" or "propose-alliance" or "propose-trade" or "accept-offer" or "peace-yes" or "peace-no" or "hire-mercenary" or "recruit-standing" or "disband-slot" or "move-fleet" or "order-fleet" or "repair-fleet" or "scuttle-fleet" or "split-fleet" or "join-fleets" or "embark-army" or "disembark-army" or "buy-fleet-supply" or "fleet-transfer" or "load" or "balance" or "set-tax" or "transfer-money" ? verb : "unknown";
    }
    // A finite vocabulary retains BCL failure meaning while removing all user supplied payload.
    private static readonly HashSet<string> ExceptionWords = new(
        "access denied permission unauthorized file directory path not found could cannot read write open closed disposed object operation invalid failed failure unexpected index outside bounds array collection range argument null value required format input string incorrect supported stream disk full space insufficient memory timeout timed out cancelled canceled task exception inner caused by sharing violation used another process data corrupt malformed sequence contains no elements state does exist already empty is was the a an to of in for and or at has been being unable load save deserialize serialize json token expected end position line byte number length maximum exceeded invalidoperationexception ioexception unauthorizedaccessexception".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static string SafeExceptionMessage(string message)
    {
        // Bound work before regex/encoding, and remove path/quoted spans before token selection.
        var bounded = message[..Math.Min(message.Length, 1024)];
        bounded = Regex.Replace(bounded, @"[\""'][^\""']*[\""']|(?:[A-Za-z]:[\\/]|/|\\)[^\r\n]*", " [redacted] ");
        var words = Regex.Matches(bounded, @"[A-Za-z]+")
            .Select(m => ExceptionWords.Contains(m.Value) ? m.Value.ToLowerInvariant() : "[redacted]")
            .Take(20).ToArray();
        var safe = string.Join(" ", words);
        return safe[..Math.Min(safe.Length, 160)];
    }
    private static string TargetFormat(string target)
    {
        try { return Path.GetExtension(target).ToLowerInvariant() switch { ".json" => "json", ".sav" => "sav", _ => "other" }; }
        catch { return "other"; }
    }
    /// <summary>Classify the existing rendered outcome, without recording its payload or changing it.</summary>
    public static bool CommandSucceeded(IReadOnlyList<string> lines)
    {
        var start = 0;
        for (int i = 0; i < lines.Count; i++) if (lines[i].StartsWith("> ", StringComparison.Ordinal)) { start = i + 1; break; }
        foreach (var line in lines.Skip(start))
        {
            if (line.Trim() == "News:") break;
            if (Regex.IsMatch(line, @"^[A-Za-z][A-Za-z0-9.-]* rejected(?: \([A-Za-z0-9._-]{1,80}\))?:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || line.StartsWith("Could not ", StringComparison.Ordinal)
                || line.StartsWith("A battle is open:", StringComparison.Ordinal)
                || line.StartsWith("There is no battle to fight.", StringComparison.Ordinal)
                || line.StartsWith("Usage:", StringComparison.Ordinal)
                || line.StartsWith("Unknown command", StringComparison.Ordinal)
                || line.StartsWith("Save failed", StringComparison.Ordinal)
                || line.StartsWith("Load failed", StringComparison.Ordinal)) return false;
        }
        return true;
    }
    private object? SafeArguments(string command)
    {
        var kind = CommandKind(command);
        if (kind == "unknown" || kind is "save" or "load") return null;
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8).ToArray();
        // IDs are opaque run-local tokens. Rename payloads/new entity names are excluded.
        var entity = kind is "move" or "move-fleet" or "attack-army" or "attack-fleet" or "besiege-city" or "rename-unit" or "disband-unit"
            or "declare-war" or "make-peace" or "propose-alliance" or "propose-trade" ? parts.FirstOrDefault() : null;
        var mode = kind == "battle" && parts.FirstOrDefault() is "general" or "place" or "move" or "attack" or "shoot" or "clear" or "end" or "surrender" or "show" ? parts[0] : null;
        int[] positions = kind switch
        {
            "move" or "move-fleet" => new[] { 1, 2 },
            "rename-unit" or "disband-unit" or "hire-mercenary" or "repair-fleet" or "order-fleet" => new[] { 1 },
            "split-unit" => new[] { 1, 2 },
            "buy" or "recruit-standing" or "order-city" or "split-fleet" => new[] { 2 },
            "set-tax" or "mobilize" or "disband-slot" => new[] { 0 },
            "transfer-money" => new[] { 1 },
            "fleet-transfer" => new[] { 2, 3, 4 },
            "battle" when mode is "place" or "move" => new[] { 1, 2, 3 },
            "battle" when mode is "attack" or "shoot" => new[] { 1, 2 },
            "battle" when mode is "clear" => new[] { 1 },
            _ => Array.Empty<int>(),
        };
        var numeric = positions.Where(i => i < parts.Length)
            .Select(i => long.TryParse(parts[i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number) ? (long?)number : null)
            .Where(value => value is not null).ToArray();
        return new { entity = entity is null ? null : Token(entity), numeric, mode };
    }
    private static string SafeType(Type type) => SafeIdentifier(type.FullName ?? type.Name, 96);
    private static string SafeIdentifier(string value, int limit) => new(value.Take(limit).Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '+' or '`' or '-').ToArray());
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
                    target = target is null ? null : TargetToken(target), format = target is null ? null : (TargetFormat(target)),
                    category = command is null ? null : CommandKind(command) is "news" or "status" or "armies" or "cities" or "balance" or "map" ? "query" : "order",
                    arguments = command is null ? null : SafeArguments(command), turn, number, token = token is null ? null : Token(token), causes, metadata };
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
