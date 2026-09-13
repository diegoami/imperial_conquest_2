using IC2.Engine.Tests.Fixtures;
using Xunit;

// Deliberately NOT `namespace IC2.Engine.Tests.Fixtures` -- the whole point of this file is to
// prove the fix for T04's round-1 review finding C1 (HIGH): a type named `Fixtures`, declared in
// a namespace whose last segment is also `Fixtures`, could not be referred to by its simple name
// from any sibling namespace (CS0234), because C# simple-name lookup binds to the enclosing
// *namespace* member before it ever consults a `using` directive. Every later task's own tests
// live in their own subfolder/namespace (e.g. IC2.Engine.Tests.Calendar for T06,
// IC2.Engine.Tests.Economy for T08) and will write exactly the call this file writes -- the one
// docs/build-orchestration-plan.md §2.4 promises: "later tasks assert against
// Fixtures.Get(\"rome.taxBase\")". This file is physically inside tests/IC2.Engine.Tests/Fixtures/
// (this task's Owns list) but is declared under a sibling namespace to stand in for a downstream
// task's own subfolder without creating a file outside that Owns list.
namespace IC2.Engine.Tests.Calendar;

/// <summary>
/// Proves the C1 fix from T04's round-1 review: <see cref="FixtureCorpus"/> is reachable by its
/// simple, unqualified name from a namespace other than <c>IC2.Engine.Tests.Fixtures</c> -- the
/// exact shape of call every downstream task (T06 onward) will make. If this type is ever
/// renamed back to <c>Fixtures</c> (colliding with its own namespace again), this file fails to
/// compile, which is the point: the failure surfaces here, in this task's own Owns list, rather
/// than being rediscovered independently by ~14 later tasks.
/// </summary>
public class DownstreamNamespaceUsageTests
{
    [Fact]
    public void FixtureCorpusIsCallableUnqualifiedFromASiblingNamespace()
    {
        Assert.Equal(2440, FixtureCorpus.Get("tax.nationTaxBaseRome").AsInt());
        Assert.Equal(FixtureTag.Confirmed, FixtureCorpus.Get("tax.nationTaxBaseRome").ParsedTag());
    }
}
