namespace IC2.Engine.Model;

/// <summary>
/// The one ownership test the city counts share, over the live <see cref="CityState"/> list and the world's
/// <see cref="CityDefinition"/> list at scenario start alike (#106 item 1), so a change to what owning a city
/// means is made once.
/// </summary>
public static class CityOwnership
{
    /// <summary>Whether a city whose owner is <paramref name="owner"/> belongs to <paramref name="nationId"/>.</summary>
    public static bool IsOwnedBy(string? owner, string nationId) =>
        string.Equals(owner, nationId, StringComparison.Ordinal);
}
