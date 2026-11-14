using Gsa.Core.Randomness;
using Xunit;

namespace Gsa.Core.Tests;

public sealed class RandomGaussianTests
{
    [Fact]
    public void SplitMix64MatchesFixedSeedZeroReferenceVector()
    {
        var random = new GaussianRandom(0);
        ulong[] expected =
        [
            0xe220a8397b1dcdafUL,
            0x6e789e6aa1b965f4UL,
            0x06c45d188009454fUL,
            0xf88bb8a8724c81ecUL,
            0x1b39896a51a8749bUL,
        ];
        var actual = expected.Select(_ => random.NextUInt64()).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IdenticalSeedsProduceIdenticalUniformAndGaussianSequences()
    {
        var first = new GaussianRandom(0x4753415f524e47UL);
        var second = new GaussianRandom(0x4753415f524e47UL);
        for (var index = 0; index < 100; index++)
        {
            Assert.Equal(first.NextUInt64(), second.NextUInt64());
            Assert.Equal(first.NextUniformOpenUnit(), second.NextUniformOpenUnit());
            Assert.Equal(first.NextGaussian(), second.NextGaussian());
        }
        Assert.Equal(first.ExportState(), second.ExportState());
    }

    [Fact]
    public void UniformsRemainStrictlyInsideUnitIntervalIncludingExtremeBins()
    {
        // These states make the next SplitMix64 output 0 and UInt64.MaxValue.
        var lowest = new GaussianRandom(unchecked(0UL - 0x9e3779b97f4a7c15UL));
        var highest = new GaussianRandom(0x31628af67b2131abUL);
        Assert.Equal(Math.Pow(2, -53), lowest.NextUniformOpenUnit());
        Assert.Equal(1 - Math.Pow(2, -53), highest.NextUniformOpenUnit());

        var random = new GaussianRandom(0x554e49464f524dUL);
        for (var index = 0; index < 10_000; index++)
        {
            var value = random.NextUniformOpenUnit();
            Assert.True(value > 0 && value < 1);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void SnapshotPreservesExactContinuationWithOrWithoutBufferedGaussian(int consumed)
    {
        var original = new GaussianRandom(0x534e415053484f54UL);
        for (var index = 0; index < consumed; index++)
            original.NextGaussian();
        var saved = original.ExportState();
        var restored = new GaussianRandom(saved);
        Assert.Equal(consumed % 2 == 1, saved.HasSpare);
        for (var index = 0; index < 64; index++)
            Assert.Equal(original.NextGaussian(), restored.NextGaussian());
        Assert.Equal(original.ExportState(), restored.ExportState());
    }

    [Fact]
    public void BufferedSecondGaussianConsumesNoAdditionalUniformDraw()
    {
        var random = new GaussianRandom(27);
        random.NextGaussian();
        var before = random.ExportState();
        Assert.True(before.HasSpare);
        Assert.Equal(before.Spare, random.NextGaussian());
        var after = random.ExportState();
        Assert.Equal(before.State, after.State);
        Assert.False(after.HasSpare);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteBufferedValuesAreRejectedEvenWhenSpareFlagIsFalse(double invalid)
    {
        foreach (var hasSpare in new[] { true, false })
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GaussianRandom(new GaussianRandomState(1, hasSpare, invalid)));
    }

    [Fact]
    public void FixedTenThousandSampleNormalFixtureMeetsRecordedStatisticalTolerance()
    {
        const int count = 10_000;
        const ulong seed = 0x4753415f4e4f524dUL;
        var random = new GaussianRandom(seed);
        double mean = 0;
        double squaredDeviations = 0;
        for (var index = 1; index <= count; index++)
        {
            var sample = random.NextGaussian();
            Assert.True(double.IsFinite(sample));
            var difference = sample - mean;
            mean += difference / index;
            squaredDeviations += difference * (sample - mean);
        }
        var variance = squaredDeviations / (count - 1);
        var threeStandardErrors = 3 / Math.Sqrt(count);
        Assert.InRange(mean, -threeStandardErrors, threeStandardErrors);
        Assert.InRange(variance, 0.8, 1.2);
    }

    [Fact]
    public void ConsumingOneSubsystemStreamDoesNotAdvanceAnother()
    {
        var firstSubsystem = new GaussianRandom(11);
        var secondSubsystem = new GaussianRandom(29);
        var referenceSecondSubsystem = new GaussianRandom(29);
        Assert.NotEqual(firstSubsystem.NextGaussian(), secondSubsystem.NextGaussian());
        referenceSecondSubsystem.NextGaussian();
        for (var index = 0; index < 100; index++)
            firstSubsystem.NextGaussian();
        Assert.Equal(referenceSecondSubsystem.ExportState(), secondSubsystem.ExportState());
        Assert.Equal(referenceSecondSubsystem.NextGaussian(), secondSubsystem.NextGaussian());
    }
}
