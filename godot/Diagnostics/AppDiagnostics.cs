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
    public static string? DirectoryOverride { get; set; }
    /// <summary>Diagnostic observer runs after durable BEFORE and before the unchanged operation.
    /// Observer errors cannot change gameplay or suppress the operation.</summary>
    public static Action<DiagnosticEvent, long, string>? BeforeObserver { get; set; }
    public static DiagnosticJournal? Journal => _journal;
    public static void Acquire()
    {
        if (_owners++ != 0) return;
        try
        {
            _journal = new DiagnosticJournal(DirectoryOverride ?? ProjectSettings.GlobalizePath("user://logs/diagnostics"),
                warning: () => GD.PushWarning("Local diagnostics unavailable; gameplay continues."));
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            TaskScheduler.UnobservedTaskException += OnUnobserved;
            _journal.Startup(
                ProjectSettings.GetSetting("application/config/version", "development").AsString(),
                typeof(AppDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unavailable",
                Godot.Engine.GetVersionInfo()["string"].AsString(), System.Environment.Version.ToString(), OS.GetName(), SettingsScreen.SelectedPackId);
        }
        catch { /* Diagnostics must not prevent application startup. */ }
    }
    public static void Release()
    {
        if (_owners <= 0 || --_owners != 0) return;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
        _journal?.Dispose(); _journal = null;
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
    public static T Operation<T>(DiagnosticEvent kind, string source, Func<T> action, string? command = null, string? target = null, long? turn = null, Func<T, bool>? success = null)
    {
        long operation = 0;
        try { operation = _journal?.Before(kind, source, command, target, turn) ?? 0; } catch { }
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
    public static SessionOutput Submit(GameSession session, string command, string source, string? target = null) =>
        Operation(target is null ? DiagnosticEvent.Command : DiagnosticEvent.Save, source, () => session.Submit(command), command, target, session.State.Calendar.TurnIndex);
}
