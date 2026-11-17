namespace LoadGenerator.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void Defaults_are_300_per_second_and_a_90_10_matched_unmatched_mix()
    {
        var options = CliOptions.Parse(["load", "--namespace", "x.servicebus.windows.net"]);

        Assert.Equal(300, options.Rate);
        Assert.Equal(0.10, options.UnmatchedRatio);
        Assert.Equal("transactions-in", options.Topic);
    }

    [Fact]
    public void Options_are_read_from_the_command_line()
    {
        var options = CliOptions.Parse(["load", "--dry-run", "--rate", "450.5", "--duration", "600", "--unmatched-ratio", "0.25", "--senders", "8", "--batch-size", "200", "--seed", "9"]);

        Assert.Equal(450.5, options.Rate);
        Assert.Equal(600, options.DurationSeconds);
        Assert.Equal(0.25, options.UnmatchedRatio);
        Assert.Equal((8, 200, 9, true), (options.Senders, options.BatchSize, options.Seed, options.DryRun));
    }

    [Theory]
    [InlineData("--rate", "0")]
    [InlineData("--rate", "fast")]
    [InlineData("--unmatched-ratio", "1.5")]
    [InlineData("--senders", "0")]
    [InlineData("--nonsense", "1")]
    public void Invalid_options_are_rejected_with_an_argument_exception(string name, string value) =>
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["load", "--dry-run", name, value]));

    [Fact]
    public void A_namespace_is_required_unless_this_is_a_dry_run() =>
        Assert.Throws<ArgumentException>(() => CliOptions.Parse(["functional"]));

    [Fact]
    public void Functional_mode_accepts_the_optional_message_flags()
    {
        var options = CliOptions.Parse(["functional", "--dry-run", "--include-missing-sku", "--include-unknown-item"]);

        Assert.True(options.IncludeMissingSku && options.IncludeUnknownItem);
    }
}
