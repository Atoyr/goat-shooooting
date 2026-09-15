using GoatShooooting.Runtime;
using Xunit;

namespace GoatShooooting.Runtime.Tests;

public sealed class SeededRandomSourceTests
{
    [Fact]
    public void SameSeedProducesSameSequenceAndResetRestoresIt()
    {
        var first = new SeededRandomSource(1234);
        var second = new SeededRandomSource(1234);

        var expected = Enumerable.Range(0, 8).Select(_ => first.NextUInt32()).ToArray();
        var actual = Enumerable.Range(0, 8).Select(_ => second.NextUInt32()).ToArray();
        Assert.Equal(expected, actual);

        first.Reset(1234);
        Assert.Equal(expected, Enumerable.Range(0, 8).Select(_ => first.NextUInt32()).ToArray());
    }

    [Fact]
    public void NextSingleStaysInsideHalfOpenUnitRange()
    {
        var random = new SeededRandomSource(long.MinValue);

        Assert.All(Enumerable.Range(0, 1_000), _ =>
        {
            var value = random.NextSingle();
            Assert.True(value >= 0);
            Assert.True(value < 1);
        });
    }
}
