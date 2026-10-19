using System.Reflection;
using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Diagnostics;

/// <summary>Best-effort lifecycle adapter; never replaces the submitted operation.</summary>
public static class AppDiagnostics
{
    private static DiagnosticJournal? _journal;
    private static int _owners;
    private static readonly object LifecycleGate = new();
    internal static int ManagedHandlerRegistrations { get; private set; }
    internal static int ManagedHandlerUnregistrations { get; private set; }
    public static string? DirectoryOverride { get; set; }
    /// <summary>Diagnostic observer runs after durable BEFORE and before the unchanged operation.
    /// Observer errors cannot change gameplay or suppress the operation.</summary>
    public static Action<DiagnosticEvent, long, string>? BeforeObserver { get; set; }
    public static DiagnosticJournal? Journal => _journal;
    public static void Acquire()
    {
        lock (LifecycleGate)
        {
            if (_owners++ != 0) return;
            try
            {
                _journal = new DiagnosticJournal(DirectoryOverride ?? ProjectSettings.GlobalizePath("user://logs/diagnostics"),
                    warning: () => GD.PushWarning("Local diagnostics unavailable; gameplay continues."));
                AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
                TaskScheduler.UnobservedTaskException += OnUnobserved;
                ManagedHandlerRegistrations++;
                var version = ProjectSettings.GetSetting("application/config/version", "development").AsString();
                var build = typeof(AppDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unavailable";
                var pack = SettingsScreen.SelectedPackId ?? IC2.Slice.Assets.AssetPackManifestLoader.DefaultPackId;
                _journal.Startup(version, build, Godot.Engine.GetVersionInfo()["string"].AsString(),
                    System.Environment.Version.ToString(), OS.GetName(), pack);
                // Built-in file logging captures this trusted identity line, linking both local logs.
                GD.Print($"IC2 diagnostics run={_journal.RunId} utc={DateTimeOffset.UtcNow:O} version={MetadataLabel(version, 80)} build={MetadataLabel(build, 120)} pack={MetadataLabel(pack, 40)}");
            }
            catch { /* Diagnostics must not prevent application startup. */ }
        }
    }
    private static string MetadataLabel(string value, int limit) => new(value.Take(limit)
        .Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '+' or '`' or '-').ToArray());
    public static void Release()
    {
        lock (LifecycleGate)
        {
            if (_owners <= 0 || --_owners != 0) return;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
            ManagedHandlerUnregistrations++;
            _journal?.Dispose(); _journal = null;
        }
    }
    private static void OnUnhandled(object sender, UnhandledExceptionEventArgs args)
    { if (args.ExceptionObject is Exception exception) RecordException(exception); }
    private static void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args) => RecordException(args.Exception);
    // Controlled check invokes the same handlers without a fatal exception or debugger.
    public static void ProbeManagedHandlers(Exception exception)
    {
        OnUnhandled(typeof(AppDiagnostics), new UnhandledExceptionEventArgs(exception, false));
        OnUnobserved(null, new UnobservedTaskExceptionEventArgs(new AggregateException(exception)));
    }
    public static void RecordException(Exception exception) { try { _journal?.Exception(exception); } catch { } }
    public static void Mark(DiagnosticEvent kind, string? source = null, long? number = null, string? token = null)
    { try { _journal?.Mark(kind, source, number, token); } catch { } }
    public static T Operation<T>(DiagnosticEvent kind, string source, Func<T> action, string? command = null, string? target = null, long? turn = null, Func<T, bool>? success = null, string? nation = null, bool? computer = null)
    {
        long operation = 0;
        try { operation = _journal?.Before(kind, source, command, target, turn, nation, computer) ?? 0; } catch { }
        try { BeforeObserver?.Invoke(kind, operation, source); } catch { }
        T result;
        try { result = action(); }
        catch (Exception exception)
        {
            try { _journal?.After(kind, operation, source, false); } catch { }
            RecordException(exception);
            throw;
        }
        bool accepted = true;
        try { accepted = success?.Invoke(result) ?? true; } catch { }
        try { _journal?.After(kind, operation, source, accepted); } catch { }
        return result;
    }
    public static void Detail(DiagnosticEvent kind, string label, long? turn = null, bool? paused = null, bool? fast = null)
    {
        try { _journal?.Detail(kind, Enum.TryParse<DiagnosticDetail>(label, out var detail) && Enum.IsDefined(detail) ? detail : DiagnosticDetail.Unavailable,
            turn: turn, shot: SettingsScreen.BattleShotDelaySeconds, attack: SettingsScreen.BattleAttackDelaySeconds, paused: paused, fast: fast,
            pack: SettingsScreen.SelectedPackId ?? IC2.Slice.Assets.AssetPackManifestLoader.DefaultPackId, sound: SettingsScreen.SoundEnabled); } catch { }
    }
    public static void Frame(TacticalBattleFrame frame, long authoritativeTurn, string? stage = null, TacticalPresentationSnapshot? displayed = null, DiagnosticEvent? boundary = null)
    {
        try
        {
            var view = (displayed ?? frame.After).View;
            var side = view.SideToMove;
            _journal?.Detail(boundary ?? (stage is null ? DiagnosticEvent.Frame : DiagnosticEvent.Stage),
                Enum.TryParse<DiagnosticDetail>(stage ?? frame.Kind.ToString(), out var detail) && Enum.IsDefined(detail) ? detail : DiagnosticDetail.Unavailable,
                turn: authoritativeTurn, frame: frame.FrameId, command: frame.CommandId, battle: frame.BattleId,
                side: side, halfRound: view.Counter, computer: view.ComputerControlled[side], general: view.ComputerGeneral[side],
                shot: SettingsScreen.BattleShotDelaySeconds, attack: SettingsScreen.BattleAttackDelaySeconds,
                nation: side == 0 ? view.AttackerNationId : view.DefenderNationId, attacker: view.AttackerArmyId, defender: view.DefenderArmyId,
                actorSlot: frame.Actor?.Slot, targetSlot: frame.Target?.Slot, terminal: view.IsOver, placed: view.Placed,
                pack: SettingsScreen.SelectedPackId ?? IC2.Slice.Assets.AssetPackManifestLoader.DefaultPackId, sound: SettingsScreen.SoundEnabled);
        }
        catch { }
    }
    public static SessionOutput Submit(GameSession session, string command, string source, string? target = null) =>
        Operation(target is null ? DiagnosticEvent.Command : DiagnosticEvent.Save, source, () => session.Submit(command), command, target, session.State.Calendar.TurnIndex, success: output => target is null ? DiagnosticJournal.CommandSucceeded(output.Lines) : output.Lines.Any(line => line.StartsWith("Saved to", StringComparison.Ordinal)), nation: session.State.ActiveNationId, computer: session.State.NationById(session.State.ActiveNationId)?.Control == IC2.Engine.Model.SeatControl.Ai);
}
