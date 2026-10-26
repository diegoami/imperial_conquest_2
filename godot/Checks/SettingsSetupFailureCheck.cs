using System;
using System.Diagnostics;
using Godot;

namespace IC2.Slice.Checks;

/// <summary>Proves SettingsCheck cannot report success after initialization throws.</summary>
public partial class SettingsSetupFailureCheck : Node
{
    public override void _Ready()
    {
        var ok = false;
        try
        {
            var start = new ProcessStartInfo(System.Environment.ProcessPath!)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (var argument in new[] { "--headless", "--path", ProjectSettings.GlobalizePath("res://"),
                         "res://Checks/SettingsCheck.tscn", "--quit-after", "120" })
                start.ArgumentList.Add(argument);
            start.Environment["IC2_SETTINGS_CHECK_FAIL_SETUP"] = "1";
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start SettingsCheck.");
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(15000))
            {
                child.Kill(entireProcessTree: true);
                throw new TimeoutException("SettingsCheck setup failure did not exit.");
            }
            var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            ok = child.ExitCode == 1 && output.Contains("forced setup failure", StringComparison.Ordinal)
                && output.Contains("SettingsCheck: exiting with code 1.", StringComparison.Ordinal)
                && !output.Contains("SettingsCheck: exiting with code 0.", StringComparison.Ordinal);
            GD.Print(ok ? "PASS: SettingsCheck setup failure exits 1 with no success marker" : $"FAIL: exit={child.ExitCode}; {output}");
        }
        catch (Exception ex) { GD.PrintErr($"SettingsSetupFailureCheck: {ex}"); }
        GD.Print($"SettingsSetupFailureCheck: exiting with code {(ok ? 0 : 1)}.");
        CheckExit.Quit(this, ok ? 0 : 1);
    }
}
