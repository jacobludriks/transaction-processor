namespace RunReport.Tests;

/// <summary>
/// The arithmetic of the report, against synthetic runs whose answers are known by construction. This validates
/// the computation only; real Azure data needs a live run.
/// </summary>
public class RunAnalyzerTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static RunRecord Analyze(SyntheticRun run, Func<RunConfiguration, RunConfiguration>? adjust = null, bool held = true, MetricsExport? export = null) =>
        RunAnalyzer.Analyze(run.Configuration(adjust), export ?? run.Export(), run.Generator(held), Recorded);

    private static CriterionResult Criterion(RunRecord record, string name) =>
        Assert.Single(record.Verdict.Criteria, c => c.Name == name);

    // ---- sustained throughput ------------------------------------------------------------------------------

    [Fact]
    public void Sustained_tps_is_the_output_rate_over_the_whole_minutes_inside_the_run()
    {
        // 14 minutes from 10:00:20: the whole minutes 10:01 to 10:13 are 13; one minute is trimmed at each end, leaving 10:02 to 10:12.
        var record = Analyze(new SyntheticRun());

        Assert.Equal(660, record.Window.DurationSeconds);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 10, 2, 0, TimeSpan.Zero), record.Window.StartUtc);
        Assert.Equal(11, record.Window.BinsWithData);
        Assert.Equal(300 * 660, record.Throughput.OutputsInWindow);
        Assert.Equal(300, record.Throughput.SustainedTps, precision: 6);
        Assert.Equal(300, record.Throughput.MinBinTps, precision: 6);
        Assert.Equal(300, record.Throughput.MaxBinTps, precision: 6);
        Assert.All(record.Throughput.PerBinTps, tps => Assert.Equal(300, tps, precision: 6));
    }

    [Fact]
    public void The_whole_minutes_next_to_the_ends_of_the_run_are_left_out_because_exported_metrics_are_smeared_across_them()
    {
        // Metrics are exported once a minute at each instance's own phase: the first and last whole minutes under-count.
        var run = new SyntheticRun();
        var export = run.Export();
        var smeared = export.Outcomes
            .Select(o => o.Timestamp.Minute is 1 or 13 ? o with { Count = o.Count / 2 } : o)
            .ToList();

        var record = Analyze(run, export: export with { Outcomes = smeared });

        Assert.Equal(300, record.Throughput.SustainedTps, precision: 6);
    }

    [Fact]
    public void A_processor_that_drains_slower_than_the_publish_rate_has_a_lower_sustained_tps()
    {
        var record = Analyze(new SyntheticRun { ProcessTps = 250 });

        Assert.Equal(250, record.Throughput.SustainedTps, precision: 6);
        Assert.False(Criterion(record, "Sustained throughput").Passed);
    }

    [Fact]
    public void Tps_within_the_tolerance_of_300_passes_and_just_below_it_fails()
    {
        Assert.True(Criterion(Analyze(new SyntheticRun { PublishTps = 300, ProcessTps = 295 }), "Sustained throughput").Passed);
        Assert.False(Criterion(Analyze(new SyntheticRun { PublishTps = 300, ProcessTps = 290 }), "Sustained throughput").Passed);
    }

    [Fact]
    public void A_run_with_less_than_ten_whole_minutes_cannot_show_ten_minutes_of_sustained_throughput()
    {
        // 12 minutes from 10:00:20 leaves 11 whole minutes, 9 once the edges are trimmed.
        var record = Analyze(new SyntheticRun { Duration = TimeSpan.FromMinutes(12) });

        var criterion = Criterion(record, "Sustained throughput");
        Assert.False(criterion.Passed);
        Assert.Contains("10 minutes", criterion.Detail);
    }

    [Fact]
    public void A_target_rate_below_300_cannot_show_300_tps()
    {
        var record = Analyze(new SyntheticRun { PublishTps = 150, ProcessTps = 150 });

        Assert.False(Criterion(record, "Sustained throughput").Passed);
    }

    [Fact]
    public void The_run_window_comes_from_the_configuration_or_else_from_the_generator_summary()
    {
        var run = new SyntheticRun();
        var fromGenerator = Analyze(run);
        // The configuration moves the start 5 minutes later: fewer whole minutes remain.
        var overridden = Analyze(run, c => c with { StartUtc = run.Start.AddMinutes(5), EndUtc = run.End });

        Assert.Equal(11, fromGenerator.Window.BinsWithData);
        Assert.Equal(6, overridden.Window.BinsWithData);
    }

    // ---- latency percentiles -------------------------------------------------------------------------------

    [Fact]
    public void Latency_percentiles_come_from_the_histogram_buckets()
    {
        // Cumulative shares 10%, 50%, 80%, 95%, 99%, 100% at bounds 0.1, 0.25, 0.5, 1, 2, 5 seconds.
        var latency = Analyze(new SyntheticRun()).Latency;

        Assert.Equal(11 * 18000, latency.SampleCount);
        Assert.Equal(0.25, latency.P50Seconds!.Value, precision: 6);
        Assert.Equal(1.0, latency.P95Seconds!.Value, precision: 6);
        Assert.Equal(2.0, latency.P99Seconds!.Value, precision: 6);
        Assert.Equal((0.25, 1.0, 2.0), (latency.P50UpperBoundSeconds!.Value, latency.P95UpperBoundSeconds!.Value, latency.P99UpperBoundSeconds!.Value));
    }

    [Fact]
    public void A_percentile_inside_a_bucket_is_interpolated_and_its_upper_bound_is_the_bucket_edge()
    {
        // Cumulative 10%, 50%, 80%, 90%, 94%, 100%: p95 is 1/6 of the way through (2, 5], p99 is 5/6 of the way.
        var latency = Analyze(new SyntheticRun { LatencyShares = [0.10, 0.40, 0.30, 0.10, 0.04, 0.06, 0.0] }).Latency;

        Assert.Equal(2.5, latency.P95Seconds!.Value, precision: 6);
        Assert.Equal(4.5, latency.P99Seconds!.Value, precision: 6);
        Assert.Equal(5.0, latency.P95UpperBoundSeconds);
    }

    [Fact]
    public void A_percentile_in_the_overflow_bucket_has_no_upper_bound()
    {
        // Cumulative 10%, 50%, 80%, 90%, 93%, 95%, 100%: p99 is past the last finite bound.
        var latency = Analyze(new SyntheticRun { LatencyShares = [0.10, 0.40, 0.30, 0.10, 0.03, 0.02, 0.05] }).Latency;

        Assert.Equal(5.0, latency.P95UpperBoundSeconds);
        Assert.Null(latency.P99UpperBoundSeconds);
        Assert.Equal(5.0, latency.P99Seconds);
    }

    [Fact]
    public void P95_passes_when_its_bucket_ends_at_or_below_2_seconds_and_fails_above()
    {
        Assert.True(Criterion(Analyze(new SyntheticRun()), "p95 latency under 2 s").Passed);
        // 1% of messages over 2 s still leaves p95 at or under 2 s; 6% does not.
        Assert.False(Criterion(Analyze(new SyntheticRun { LatencyShares = [0.10, 0.40, 0.30, 0.10, 0.04, 0.06, 0.0] }), "p95 latency under 2 s").Passed);
        Assert.False(Criterion(Analyze(new SyntheticRun { LatencyShares = [0.10, 0.40, 0.30, 0.10, 0.03, 0.02, 0.05] }), "p95 latency under 2 s").Passed);
    }

    [Fact]
    public void Latency_is_taken_from_the_measurement_window_only()
    {
        // Messages in the minutes just outside the window are very slow; they must not move the percentiles.
        var run = new SyntheticRun();
        var export = run.Export();
        var slowEdges = export.LatencyBuckets
            .Concat([
                new LatencyBucketSample(new DateTimeOffset(2026, 11, 17, 10, 1, 0, TimeSpan.Zero), "+Inf", 1_000_000),
                new LatencyBucketSample(new DateTimeOffset(2026, 11, 17, 10, 13, 0, TimeSpan.Zero), "+Inf", 1_000_000),
            ]).ToList();

        var record = Analyze(run, export: export with { LatencyBuckets = slowEdges });

        Assert.Equal(1.0, record.Latency.P95Seconds!.Value, precision: 6);
    }

    // ---- backlog trend -------------------------------------------------------------------------------------

    [Fact]
    public void A_steady_backlog_has_not_grown()
    {
        var backlog = Analyze(new SyntheticRun()).Backlog;

        Assert.True(backlog.HasData);
        Assert.False(backlog.Grew);
        Assert.Equal(0, backlog.SlopePerSecond, precision: 6);
        Assert.Equal(120, backlog.Max);
    }

    [Fact]
    public void A_backlog_growing_by_50_messages_a_second_has_grown()
    {
        var run = new SyntheticRun { PublishTps = 300, ProcessTps = 250 };
        var growing = new SyntheticRun
        {
            PublishTps = 300, ProcessTps = 250,
            Backlog = t => t < run.End ? 120 + 50 * Math.Max(0, (t - run.Start).TotalSeconds) : 30_000,
        };

        var record = Analyze(growing);

        Assert.True(record.Backlog.Grew);
        Assert.Equal(50, record.Backlog.SlopePerSecond, precision: 3);
        Assert.Equal(33_000, record.Backlog.GrowthOverWindow, precision: 0);
        Assert.False(Criterion(record, "Backlog not growing").Passed);
    }

    [Fact]
    public void A_backlog_that_spikes_and_recovers_has_not_grown()
    {
        var run = new SyntheticRun();
        var middleOfWindow = new DateTimeOffset(2026, 11, 17, 10, 7, 0, TimeSpan.Zero);
        var spiky = new SyntheticRun
        {
            Backlog = t => t >= run.End ? 0 : 100 + 4000 * Math.Max(0, 1 - Math.Abs((t - middleOfWindow).TotalMinutes) / 3),
        };

        Assert.False(Analyze(spiky).Backlog.Grew);
    }

    [Fact]
    public void A_backlog_that_is_draining_has_not_grown()
    {
        var run = new SyntheticRun();
        var draining = new SyntheticRun { Backlog = t => t >= run.End ? 0 : Math.Max(0, 6000 - 10 * (t - run.Start).TotalSeconds) };

        var record = Analyze(draining);

        Assert.False(record.Backlog.Grew);
        Assert.True(record.Backlog.SlopePerSecond < 0);
    }

    [Fact]
    public void Without_backlog_data_the_backlog_criterion_cannot_pass()
    {
        var run = new SyntheticRun();
        var record = Analyze(run, export: run.Export() with { Backlog = [] });

        Assert.False(record.Backlog.HasData);
        Assert.False(Criterion(record, "Backlog not growing").Passed);
    }

    // ---- errors and dead-letters ---------------------------------------------------------------------------

    private static SyntheticRun WithDeadLetters(long itemNotFound, long invalidXml, double dlqAtEnd)
    {
        var run = new SyntheticRun
        {
            OutputsMissing = itemNotFound + invalidXml,
            DeadLetterQueue = t => t >= new DateTimeOffset(2026, 11, 17, 10, 5, 0, TimeSpan.Zero) ? dlqAtEnd : 0,
        };
        var minute = new DateTimeOffset(2026, 11, 17, 10, 5, 0, TimeSpan.Zero);
        if (itemNotFound > 0) run.ExtraOutcomes.Add(new OutcomeSample(minute, "dead_lettered", "ItemNotFound", itemNotFound));
        if (invalidXml > 0) run.ExtraOutcomes.Add(new OutcomeSample(minute, "dead_lettered", "InvalidXml", invalidXml));
        return run;
    }

    [Fact]
    public void Dead_letters_are_counted_by_reason()
    {
        var record = Analyze(WithDeadLetters(itemNotFound: 7, invalidXml: 2, dlqAtEnd: 9));

        Assert.Equal(9, record.Outcomes.DeadLettered);
        Assert.Equal(7, record.Outcomes.DeadLetteredByReason["ItemNotFound"]);
        Assert.Equal(2, record.Outcomes.DeadLetteredByReason["InvalidXml"]);
    }

    [Fact]
    public void Dead_letters_nobody_expected_are_unexplained_and_fail_the_criterion()
    {
        var record = Analyze(WithDeadLetters(7, 2, 9));

        Assert.Equal(9, record.DeadLetters.Unexplained);
        Assert.False(Criterion(record, "No unexplained dead-letters").Passed);
    }

    [Fact]
    public void Dead_letters_for_reasons_the_run_expected_are_explained()
    {
        var record = Analyze(WithDeadLetters(7, 2, 9), c => c with { ExpectedDeadLetterReasons = ["ItemNotFound", "InvalidXml"] });

        Assert.Equal(0, record.DeadLetters.Unexplained);
        Assert.True(Criterion(record, "No unexplained dead-letters").Passed);
    }

    [Fact]
    public void Only_the_expected_reasons_are_explained()
    {
        var record = Analyze(WithDeadLetters(7, 2, 9), c => c with { ExpectedDeadLetterReasons = ["InvalidXml"] });

        Assert.Equal(7, record.DeadLetters.Unexplained);
        Assert.Equal(7, record.DeadLetters.UnexpectedReasons);
    }

    [Fact]
    public void Dead_letters_the_host_never_counted_are_unexplained_and_are_not_lost_messages()
    {
        // 4 messages hit the maximum delivery count: Service Bus dead-letters them, the host never sees an outcome.
        var run = new SyntheticRun
        {
            OutputsMissing = 4,
            DeadLetterQueue = t => t >= new DateTimeOffset(2026, 11, 17, 10, 5, 0, TimeSpan.Zero) ? 4 : 0,
        };

        var record = Analyze(run);

        Assert.Equal(4, record.DeadLetters.ServerSideUnaccounted);
        Assert.Equal(4, record.DeadLetters.Unexplained);
        Assert.Equal(0, record.Reconciliation.Lost);
        Assert.False(Criterion(record, "No unexplained dead-letters").Passed);
    }

    [Fact]
    public void Without_a_dead_letter_queue_series_unexplained_dead_letters_cannot_be_ruled_out()
    {
        var run = new SyntheticRun();
        var record = Analyze(run, export: run.Export() with { DeadLetterQueue = [] });

        Assert.False(record.DeadLetters.ServerSideDataAvailable);
        Assert.False(Criterion(record, "No unexplained dead-letters").Passed);
    }

    [Fact]
    public void Abandoned_messages_are_counted_by_reason_as_errors()
    {
        var run = new SyntheticRun();
        var minute = new DateTimeOffset(2026, 11, 17, 10, 3, 0, TimeSpan.Zero);
        run.ExtraOutcomes.Add(new OutcomeSample(minute, "abandoned", "transient_failure", 30));
        run.ExtraOutcomes.Add(new OutcomeSample(minute.AddMinutes(1), "abandoned", "transient_failure", 12));
        run.ExtraOutcomes.Add(new OutcomeSample(minute, "abandoned", "unhandled_exception", 1));

        var outcomes = Analyze(run).Outcomes;

        Assert.Equal(43, outcomes.Abandoned);
        Assert.Equal(42, outcomes.AbandonedByReason["transient_failure"]);
        Assert.Equal(1, outcomes.AbandonedByReason["unhandled_exception"]);
    }

    // ---- reconciliation ------------------------------------------------------------------------------------

    [Fact]
    public void Every_message_the_generator_sent_is_accounted_for_by_an_output()
    {
        var run = new SyntheticRun();
        var record = Analyze(run);

        var r = record.Reconciliation;
        Assert.Equal(300 * 840, r.GeneratorSent);
        Assert.Equal(r.GeneratorSent, r.SentToQueue + r.SentToEventHub + r.DeadLettered);
        Assert.Equal(0, r.Lost);
        Assert.Equal(0, r.Duplicates);
        Assert.True(Criterion(record, "Zero lost messages").Passed);
    }

    [Fact]
    public void Missing_outputs_are_lost_messages()
    {
        var record = Analyze(new SyntheticRun { OutputsMissing = 25 });

        Assert.Equal(25, record.Reconciliation.Lost);
        var criterion = Criterion(record, "Zero lost messages");
        Assert.False(criterion.Passed);
        Assert.Contains("25", criterion.Detail);
    }

    [Fact]
    public void Extra_outputs_are_duplicates_from_at_least_once_delivery_not_lost_messages()
    {
        var record = Analyze(new SyntheticRun { OutputsMissing = -12 });

        Assert.Equal(12, record.Reconciliation.Duplicates);
        Assert.Equal(0, record.Reconciliation.Lost);
        Assert.True(Criterion(record, "Zero lost messages").Passed);
    }

    [Fact]
    public void Messages_still_in_the_backlog_are_in_flight_not_lost_but_the_run_cannot_be_judged_until_it_has_drained()
    {
        var run = new SyntheticRun { OutputsMissing = 120, Backlog = _ => 120 };

        var record = Analyze(run);

        Assert.Equal(0, record.Reconciliation.Lost);
        Assert.Equal(120, record.Reconciliation.StillInBacklog);
        var criterion = Criterion(record, "Zero lost messages");
        Assert.False(criterion.Passed);
        Assert.Contains("drain", criterion.Detail);
    }

    // ---- verdict -------------------------------------------------------------------------------------------

    [Fact]
    public void A_healthy_300_tps_run_meets_the_go_criteria()
    {
        var record = Analyze(new SyntheticRun());

        Assert.True(record.Verdict.MeetsGoCriteria);
        Assert.All(record.Verdict.Criteria, c => Assert.True(c.Passed, $"{c.Name}: {c.Detail}"));
        Assert.Empty(record.Verdict.Warnings);
    }

    [Fact]
    public void A_run_where_the_generator_did_not_hold_its_rate_does_not_meet_the_criteria()
    {
        var record = Analyze(new SyntheticRun(), held: false);

        Assert.False(Criterion(record, "Generator held the target rate").Passed);
        Assert.False(record.Verdict.MeetsGoCriteria);
    }

    [Theory]
    [InlineData("Sustained throughput")]
    [InlineData("Backlog not growing")]
    [InlineData("p95 latency under 2 s")]
    [InlineData("Zero lost messages")]
    [InlineData("No unexplained dead-letters")]
    [InlineData("Generator held the target rate")]
    public void The_verdict_has_a_criterion_for_each_go_condition_and_each_is_needed(string name)
    {
        var record = Analyze(new SyntheticRun());

        Assert.True(Criterion(record, name).Passed);
        // Failing any one criterion fails the verdict.
        var failed = record.Verdict.Criteria.Select(c => c.Name == name ? c with { Passed = false } : c).ToList();
        Assert.False(RunAnalyzer.MeetsAll(failed));
    }

    [Fact]
    public void The_record_keeps_the_configuration_so_runs_can_be_compared()
    {
        var record = Analyze(new SyntheticRun(), c => c with { InstanceCount = 5, MaxConcurrentCalls = 64, DatabaseTier = "GP_Gen5_8" });

        Assert.Equal((5, 64, "GP_Gen5_8", 300.0), (record.Configuration.InstanceCount, record.Configuration.MaxConcurrentCalls, record.Configuration.DatabaseTier, record.Configuration.TargetPublishRate));
    }

    [Fact]
    public void Gaps_in_the_measurement_window_are_warned_about()
    {
        var run = new SyntheticRun();
        var export = run.Export();
        var gap = new DateTimeOffset(2026, 11, 17, 10, 6, 0, TimeSpan.Zero);
        var record = Analyze(run, export: export with { Outcomes = export.Outcomes.Where(o => o.Timestamp != gap).ToList() });

        Assert.Equal(10, record.Window.BinsWithData);
        Assert.Contains(record.Verdict.Warnings, w => w.Contains("10 of 11"));
    }
}
