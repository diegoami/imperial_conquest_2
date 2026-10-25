using IC2.Slice.Diagnostics;
using Godot;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>A thin file-browser host: Load opens Browse immediately, with no intermediate save list.</summary>
public partial class LoadGameScreen : Control
{
    public event Action? BackRequested;
    public event Action<GameSession>? GameResumed;

    public string? LastLoadedPath { get; private set; }
    public string LastLoadMessage { get; private set; } = string.Empty;
    public string? SavesDirectoryForCheck { private get; set; }
    public string? RecentFolderForCheck { private get; set; }

    private const string SavesDirectory = "user://saves";
    private const string RecentFolderPreference = "user://last-save-folder.txt";
    private static string? _lastBrowsedFolder;
    private FileDialog? _browseDialog;
    private AcceptDialog? _errorDialog;
    private bool _completed;

    public FileDialog BrowseDialog => _browseDialog ??= CreateBrowseDialog();
    public AcceptDialog ErrorDialog => _errorDialog ??= CreateErrorDialog();

    public override void _Ready()
    {
        // AppRoot finishes attaching the host before the modal browser opens.
        Callable.From(OpenBrowser).CallDeferred();
    }

    public void OpenBrowser()
    {
        if (_completed || !IsInsideTree() || IsQueuedForDeletion()) return;
        var folder = RecentFolder() is { } last && Directory.Exists(last)
            ? last : SavesDirectoryForCheck ?? ProjectSettings.GlobalizePath(SavesDirectory);
        if (!Directory.Exists(folder)) folder = OS.GetSystemDir(OS.SystemDir.Documents);
        BrowseDialog.CurrentDir = folder;
        BrowseDialog.PopupCentered();
    }

    private FileDialog CreateBrowseDialog()
    {
        var dialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Title = "Load Game",
            Filters = new[] { "*.json, *.sav ; Saved games" },
            Size = new Vector2I(760, 520),
        };
        dialog.FileSelected += OnBrowseFileSelected;
        dialog.Canceled += Cancel;
        AddChild(dialog);
        return dialog;
    }

    private AcceptDialog CreateErrorDialog()
    {
        var dialog = new AcceptDialog { Title = "Could not load saved game" };
        dialog.Confirmed += Retry;
        dialog.Canceled += Retry;
        AddChild(dialog);
        return dialog;
    }

    private void Retry()
    {
        if (!_completed) Callable.From(OpenBrowser).CallDeferred();
    }

    public void Cancel()
    {
        if (_completed) return;
        _completed = true;
        _browseDialog?.Hide();
        _errorDialog?.Hide();
        BackRequested?.Invoke();
    }

    private void OnBrowseFileSelected(string path)
    {
        _lastBrowsedFolder = Path.GetDirectoryName(path);
        if (SavesDirectoryForCheck is null)
            LoadGameModel.RememberRecentFolder(ProjectSettings.GlobalizePath(RecentFolderPreference), _lastBrowsedFolder);
        OnContinuePressed(path);
    }

    private void OnContinuePressed(string path)
    {
        var outcome = AppDiagnostics.Operation(DiagnosticEvent.Load, "load",
            () => LoadGameModel.Open(path, GameDataContext.Repository, HumanBattleMode.Tactical),
            target: path, success: result => result.Session is not null);
        LastLoadMessage = outcome.Message;
        if (outcome.Session is not { } session)
        {
            _browseDialog?.Hide();
            ErrorDialog.DialogText = outcome.Message;
            ErrorDialog.PopupCentered();
            return;
        }

        _completed = true;
        _browseDialog?.Hide();
        _errorDialog?.Hide();
        // Original SAV imports never become the native Save writeback path.
        LastLoadedPath = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? path : null;
        GameResumed?.Invoke(session);
    }

    private string? RecentFolder() => SavesDirectoryForCheck is not null ? RecentFolderForCheck
        : _lastBrowsedFolder ?? LoadGameModel.ReadRecentFolder(ProjectSettings.GlobalizePath(RecentFolderPreference));

    public void BrowseForCheck(string path) => OnBrowseFileSelected(path);
    public void ContinueForCheck(string path) => OnContinuePressed(path);
    public static string? LastBrowsedFolderForCheck => _lastBrowsedFolder;
}
