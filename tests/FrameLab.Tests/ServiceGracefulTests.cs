// Copyright (c) 2026 pcopt-suite contributors. Licensed under the MIT License.
using Microsoft.Extensions.Logging.Abstractions;
using PcOpt.Products.FrameLab.Services;

namespace PcOpt.Products.FrameLab.Tests;

/// <summary>设置 / 历史的持久化与损坏容错测试（保证"读不到不崩溃、按默认处理"）。</summary>
public sealed class ServiceGracefulTests
{
    [Fact]
    public void Settings_RoundTrips()
    {
        var path = NewTemp(".json");
        try
        {
            var first = new SettingsService(NullLogger<SettingsService>.Instance, path);
            first.Current.DurationSeconds = 42;
            first.Current.PreferredAlgorithm = LowOnePercentAlgorithm.TimeBased;
            first.Current.LastProcessName = "cs2.exe";
            first.Save();

            var second = new SettingsService(NullLogger<SettingsService>.Instance, path);
            second.Current.DurationSeconds.Should().Be(42);
            second.Current.PreferredAlgorithm.Should().Be(LowOnePercentAlgorithm.TimeBased);
            second.Current.LastProcessName.Should().Be("cs2.exe");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Settings_CorruptFile_FallsBackToDefaults()
    {
        var path = NewTemp(".json");
        try
        {
            File.WriteAllText(path, "{ this is not json ");

            var settings = new SettingsService(NullLogger<SettingsService>.Instance, path);

            settings.Current.DurationSeconds.Should().Be(60);
            settings.Current.PreferredAlgorithm.Should().Be(LowOnePercentAlgorithm.Percentile);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Settings_MissingFile_UsesDefaults()
    {
        var path = NewTemp(".json");
        try
        {
            var settings = new SettingsService(NullLogger<SettingsService>.Instance, path);
            settings.Current.DurationSeconds.Should().Be(60);
            settings.Current.ShowThermal.Should().BeTrue();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void History_AddPersistsAndClearRemoves()
    {
        var path = NewTemp(".json");
        try
        {
            var history = new HistoryService(NullLogger<HistoryService>.Instance, path);
            history.Entries.Should().BeEmpty();

            history.Add(Session());
            history.Entries.Should().ContainSingle();

            // 重新打开确认落盘
            var reopened = new HistoryService(NullLogger<HistoryService>.Instance, path);
            reopened.Entries.Should().ContainSingle();

            reopened.Clear();
            reopened.Entries.Should().BeEmpty();
            new HistoryService(NullLogger<HistoryService>.Instance, path).Entries.Should().BeEmpty();
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void History_CorruptFile_ReturnsEmpty_NoThrow()
    {
        var path = NewTemp(".json");
        try
        {
            File.WriteAllText(path, "not-json-at-all");
            new HistoryService(NullLogger<HistoryService>.Instance, path).Entries.Should().BeEmpty();
        }
        finally
        {
            Delete(path);
        }
    }

    private static CaptureSession Session()
    {
        var samples = Enumerable.Range(0, 60)
            .Select(i => new FrameSample { FrameTimeMs = 16.0, TimeInSeconds = i / 60.0 })
            .ToArray();
        var stats = FrameAnalyzer.Analyze(samples);
        return new CaptureSession(Guid.NewGuid().ToString("N"), DateTime.UtcNow, "game.exe", 60, samples, stats, "本次测试");
    }

    private static string NewTemp(string extension) =>
        Path.Combine(Path.GetTempPath(), "framelab-svc-" + Guid.NewGuid().ToString("N") + extension);

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
