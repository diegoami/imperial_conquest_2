using Godot;
using IC2.Engine.Serialization;

namespace IC2.Slice.UI;

/// <summary>
/// Loads <see cref="GameDataRepository"/> exactly once per process and hands every screen the same
/// instance — every screen that needs to resolve a scenario (the ruleset chooser's cards, the seat
/// picker, the load-game picker) would otherwise re-read every world/ruleset/scenario file from disk on
/// its own <c>_Ready</c>, which is wasted work and, worse, a second place the "where is the repository
/// root" convention (<see cref="GameSessionFactory.RepositoryRootFromGlobalizedResPath"/>) could drift
/// out of step with <c>Slice.cs</c>'s own.
/// </summary>
public static class GameDataContext
{
    private static GameDataRepository? _repository;
    private static string? _repositoryRoot;

    public static string RepositoryRoot =>
        _repositoryRoot ??= GameSessionFactory.RepositoryRootFromGlobalizedResPath(ProjectSettings.GlobalizePath("res://"));

    public static GameDataRepository Repository =>
        _repository ??= GameSessionFactory.LoadRepository(RepositoryRoot);
}
