namespace IC2.Engine.Ai;

/// <summary>
/// Original scorer winner storage, not the candidate formula. Research scorer-word-storage130
/// (895adfb): compare the 32-bit candidate to a sign-extended word, store its low word, then
/// add the distance as a word and read the signed result. Shared by the original target/hunt scorers.
/// </summary>
public readonly record struct OriginalWordScore(short Value, short Distance)
{
    public static OriginalWordScore Empty => new(-10000, 0);
    public bool IsBeatenBy(int candidate) => candidate > Value;
    public OriginalWordScore WithWinner(int candidate, int distance) =>
        new(unchecked((short)candidate), unchecked((short)distance));
    public int ReturnedScore => unchecked((short)(Value + Distance));
}
