using SCSKiller.Core.Warming;

namespace SCSKiller.Tests.Platform;

public class ShadPs4WarmerTests
{
    [Theory]
    [InlineData("Options:\n  --warmup-cache    Warm recorded pipelines", true)]
    [InlineData("Options:\n  --game TEXT    Game path", false)]
    [InlineData("Use a build supporting --warmup-cache", false)]
    [InlineData("  --warmup-cache-fake  unrelated flag", false)]
    public void Only_an_advertised_warmup_option_is_accepted(string help, bool supported) =>
        Assert.Equal(supported, ShadPs4Warmer.SupportsWarmup(help));

    [Theory]
    [InlineData("SCSKILLER_WARM {\"loaded\":0,\"total\":0}", 0)]
    [InlineData("SCSKILLER_WARM {\"loaded\":2,\"total\":3}", 0)]
    [InlineData("SCSKILLER_WARM {\"loaded\":3,\"total\":3}", 1)]
    [InlineData("Preloaded 3 pipelines", 0)]
    [InlineData("SCSKILLER_WARM {\"loaded\":3,\"total\":3}\nSCSKILLER_WARM {\"loaded\":3,\"total\":3}", 0)]
    public void Empty_partial_failed_or_ambiguous_warmups_are_not_success(string output, int exitCode) =>
        Assert.Throws<InvalidDataException>(() => ShadPs4Warmer.ParseResult(output, exitCode));

    [Fact]
    public void A_complete_warmup_can_be_reported_among_other_output() =>
        Assert.Equal(new ShadPs4WarmResult(376, 376), ShadPs4Warmer.ParseResult("Starting\nSCSKILLER_WARM {\"loaded\":376,\"total\":376}\r\nDone", 0));
}
