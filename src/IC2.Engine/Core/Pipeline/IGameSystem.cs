using IC2.Engine.Model;

namespace IC2.Engine.Core;

/// <summary>
/// One unit of turn processing: a pure function from a <see cref="GameState"/> to the next one.
/// </summary>
/// <remarks>
/// <para>
/// A system is discovered by <see cref="SystemRegistry"/> from its <see cref="GameSystemAttribute"/>
/// alone. Adding a system to the game therefore means adding one file inside the adding task's own
/// directory — no registration list, no wiring file, nothing shared to conflict over. That is the
/// mechanism <c>docs/build-orchestration-plan.md</c> §2.3 depends on.
/// </para>
/// <para>
/// <strong>Systems must be stateless.</strong> One instance is created per registry and reused for every
/// turn, so an instance field would be state the save file does not contain and the determinism guard
/// cannot see. Everything a system needs arrives on the <see cref="SystemContext"/>, and everything it
/// changes leaves through the returned <see cref="GameState"/> or through
/// <see cref="SystemContext.Events"/>.
/// </para>
/// </remarks>
public interface IGameSystem
{
    /// <summary>
    /// Runs this system for one phase of one turn, returning the resulting state.
    /// </summary>
    /// <param name="context">Everything the system may read, including its own random stream.</param>
    /// <returns>
    /// The next state. Returning <see cref="SystemContext.State"/> unchanged is the correct way to do
    /// nothing; a system never mutates the state it was given, because <see cref="GameState"/> is an
    /// immutable record tree.
    /// </returns>
    GameState Execute(SystemContext context);
}
