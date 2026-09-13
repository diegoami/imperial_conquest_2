namespace IC2.Engine.Core;

/// <summary>
/// One thing a seat asks the game to do: move an army, hire a mercenary, order a fleet, end a turn.
/// </summary>
/// <remarks>
/// <para>
/// A command is data, not behaviour — a record with the arguments of the request and nothing else. The
/// rule lives in its <see cref="ICommandHandler{TCommand}"/>. Keeping the two apart is what lets
/// <c>docs/game-design.md</c> principle 4's "ship commands + seed, not state" work: a command list plus a
/// seed replays a game exactly, which is both the save-format story and the road to lockstep multiplayer.
/// </para>
/// <para>
/// Every command names the nation issuing it. <see cref="CommandDispatcher"/> checks that against the
/// active seat before any handler runs, so a handler never has to remember to.
/// </para>
/// </remarks>
public interface ICommand
{
    /// <summary>
    /// A stable id in <c>area.name</c> form — <c>"army.move"</c>, <c>"city.fortify"</c>. Used as the name
    /// of the command's random stream and as the natural key in a replay log, so it must not be derived
    /// from the C# type name.
    /// </summary>
    string Kind { get; }

    /// <summary>The nation issuing the command.</summary>
    string IssuingNationId { get; }
}
