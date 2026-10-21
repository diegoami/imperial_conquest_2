using System;
using Godot;

namespace IC2.Slice.Checks;

/// <summary>
/// Bug #1301 (with #1276 and the closed #1252): a headless check that passed every assertion could still fail at
/// exit. C# wrappers of RefCounted objects the screens made (asset-pack textures, sound streams, panel
/// styleboxes, synthetic input events) were still unfinalized when the engine shut down. Godot then reported
/// "Leaked unsafe reference to object" and, now and then, a native abort (<c>FATAL: Condition "!rc_owner"</c>)
/// that turned the run red. This quit lets queued frees run, then collects and finalizes the managed side while
/// the engine is still alive, and only then quits.
/// </summary>
public static class CheckExit
{
    private static bool _quitting;

    /// <summary>Quits with <paramref name="exitCode"/> after the managed cleanup; later calls are ignored.</summary>
    public static async void Quit(Node node, int exitCode)
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        var tree = node.GetTree();
        for (var frame = 0; frame < 2; frame++)
        {
            await node.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        tree.Quit(exitCode);
    }
}
