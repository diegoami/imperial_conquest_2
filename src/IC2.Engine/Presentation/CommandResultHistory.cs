namespace IC2.Engine.Presentation;

/// <summary>Ephemeral UI result blocks, bounded independently of campaign/news history.</summary>
public sealed class CommandResultHistory
{
    public const int Capacity = 100;
    private readonly List<string> _entries = new();
    private string? _viewer;
    public IReadOnlyList<string> Entries => _entries.AsReadOnly();
    public int Count => _entries.Count;
    public string LatestResult { get; private set; } = string.Empty;

    public void SetViewer(string viewer)
    {
        if (_viewer == viewer) return;
        _viewer = viewer;
        _entries.Clear();
        LatestResult = string.Empty;
    }

    public void Append(string result)
    {
        LatestResult = result;
        if (string.IsNullOrWhiteSpace(result)) return;
        _entries.Add(result);
        if (_entries.Count > Capacity) _entries.RemoveAt(0);
    }
}
