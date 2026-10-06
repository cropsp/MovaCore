using System;
using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class GpuChoiceTests
    {
        private const ulong GB = 1UL << 30;

        private static readonly GpuDevice Integrated = new(0, "Intel(R) UHD Graphics", false, 2 * GB);
        private static readonly GpuDevice SmallDiscrete = new(1, "NVIDIA GeForce MX330", true, 2 * GB);
        private static readonly GpuDevice LargeDiscrete = new(2, "NVIDIA GeForce RTX 4060", true, 8 * GB);

        [Fact]
        public void Automatic_PrefersADiscreteCard_ThoughVulkanListsTheIntegratedOneFirst()
        {
            Assert.Equal(SmallDiscrete, GpuChoice.Pick(new[] { Integrated, SmallDiscrete }, null));
        }

        [Fact]
        public void Automatic_TakesTheDiscreteCardWithTheMostMemory() =>
            Assert.Equal(LargeDiscrete, GpuChoice.Pick(new[] { Integrated, SmallDiscrete, LargeDiscrete }, null));

        [Fact]
        public void Automatic_TakesAnIntegratedCardWhenThereIsNoOther() =>
            Assert.Equal(Integrated, GpuChoice.Pick(new[] { Integrated }, null));

        [Fact]
        public void NoCards_GiveNone() => Assert.Null(GpuChoice.Pick(Array.Empty<GpuDevice>(), null));

        [Fact]
        public void TheChosenCard_IsTaken_EvenAnIntegratedOne() =>
            Assert.Equal(Integrated, GpuChoice.Pick(new[] { Integrated, LargeDiscrete }, "Intel(R) UHD Graphics"));

        [Fact]
        public void AChosenCardThatIsGone_FallsBackToTheAutomaticChoice() =>
            Assert.Equal(LargeDiscrete, GpuChoice.Pick(new[] { Integrated, LargeDiscrete }, "AMD Radeon RX 6600"));

        [Fact]
        public void BeamSearch_IsForLargeDiscreteCardsOnly()
        {
            Assert.True(GpuChoice.UseBeamSearch(LargeDiscrete));
            Assert.True(GpuChoice.UseBeamSearch(LargeDiscrete with { Memory = GpuChoice.BeamSearchMemory }));
            Assert.False(GpuChoice.UseBeamSearch(SmallDiscrete));
            Assert.False(GpuChoice.UseBeamSearch(Integrated with { Memory = 16 * GB }));
            Assert.False(GpuChoice.UseBeamSearch(null));
        }
    }
}
