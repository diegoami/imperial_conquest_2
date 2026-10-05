namespace IC2.Slice.UI;

/// <summary>
/// T132: whether the right-hand info column of <c>MainGameScreen</c> is shown, which key toggles it, and
/// the toggle button's caption and tooltip. Deliberately <strong>Godot-free</strong> so a plain xunit
/// test can pin it; the key arrives as the name Godot's <c>Key</c> enum prints (<c>"F12"</c>).
/// </summary>
/// <remarks>
/// The state is a static, like <c>SettingsScreen.SelectedPackId</c>: it is remembered for the run (a
/// New Game or a Load keeps it) and not across launches. Every design call here is [designed]: the
/// original's menu shortcuts are unread (audit §5 gap 2).
/// </remarks>
public sealed class SidePanelToggle
{
    /// <summary>The name of the toggle key, as <c>Key.F12.ToString()</c> prints it.</summary>
    public const string ToggleKeyName = "F12";

    /// <summary>The caption while the column is shown: pressing it hides.</summary>
    public const string HideCaption = "»";

    /// <summary>The caption while the column is hidden: pressing it shows.</summary>
    public const string ShowCaption = "«";

    private static bool s_shown = true;

    /// <summary>Raised whenever the shared state changes, so every live screen follows it.</summary>
    public static event Action? Changed;

    /// <summary>Whether the column is shown (the state is shared by every instance in the process).</summary>
    public bool IsShown => s_shown;

    /// <summary>The button's caption for the current state.</summary>
    public string Caption => s_shown ? HideCaption : ShowCaption;

    /// <summary>The button's tooltip for the current state.</summary>
    public string Tooltip => s_shown ? $"Hide the panel ({ToggleKeyName})" : $"Show the panel ({ToggleKeyName})";

    /// <summary>Flips the state and returns the new <see cref="IsShown"/>.</summary>
    public bool Toggle()
    {
        s_shown = !s_shown;
        Changed?.Invoke();
        return s_shown;
    }

    /// <summary>Sets the state explicitly (a check restores the default with it).</summary>
    public void Set(bool shown)
    {
        if (s_shown == shown)
        {
            return;
        }

        s_shown = shown;
        Changed?.Invoke();
    }

    /// <summary>Whether a pressed key is the toggle: F12 with no modifier, nothing else.</summary>
    public static bool IsToggleKey(string keyName, bool ctrl, bool shift, bool alt) =>
        string.Equals(keyName, ToggleKeyName, StringComparison.Ordinal) && !ctrl && !shift && !alt;
}
