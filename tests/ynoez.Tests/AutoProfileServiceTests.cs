using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class AutoProfileServiceTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(15);

    [Fact]
    public void DecideMode_GameWithRecentInput_UsesPerformance()
    {
        var mode = AutoProfileService.DecideMode(
            TimeSpan.FromMinutes(2), Threshold, gameRunning: true, protectedProcessRunning: false);

        Assert.Equal(PowerProfileMode.Performance, mode);
    }

    [Fact]
    public void DecideMode_GameWhileUserIsAfk_UsesProtectedBalancedMode()
    {
        var mode = AutoProfileService.DecideMode(
            TimeSpan.FromMinutes(45), Threshold, gameRunning: true, protectedProcessRunning: false);

        Assert.Equal(PowerProfileMode.ProtectedAfk, mode);
    }

    [Fact]
    public void DecideMode_CustomProtectedProcessWhileUserIsAfk_NeverUsesAggressiveSaver()
    {
        var mode = AutoProfileService.DecideMode(
            TimeSpan.FromHours(3), Threshold, gameRunning: false, protectedProcessRunning: true);

        Assert.Equal(PowerProfileMode.ProtectedAfk, mode);
    }

    [Fact]
    public void DecideMode_OrdinaryUse_UsesBalanced()
    {
        var mode = AutoProfileService.DecideMode(
            TimeSpan.FromMinutes(5), Threshold, gameRunning: false, protectedProcessRunning: false);

        Assert.Equal(PowerProfileMode.Balanced, mode);
    }

    [Fact]
    public void DecideMode_IdleWithoutProtectedWorkload_UsesAfkSaver()
    {
        var mode = AutoProfileService.DecideMode(
            TimeSpan.FromMinutes(16), Threshold, gameRunning: false, protectedProcessRunning: false);

        Assert.Equal(PowerProfileMode.Afk, mode);
    }

    [Fact]
    public void DecideMode_GameAndCustomWorkloadWhileAfk_UsesProtectedBalancedMode()
    {
        var mode = AutoProfileService.DecideMode(
            TimeSpan.FromHours(1), Threshold, gameRunning: true, protectedProcessRunning: true);

        Assert.Equal(PowerProfileMode.ProtectedAfk, mode);
    }

    [Fact]
    public void NormalizeProtectedProcessNames_StripsExeAndDeduplicates()
    {
        var result = AutoProfileService.NormalizeProtectedProcessNames(
            new[] { "RobloxPlayerBeta.exe", "python", " Python.EXE ", "", "AutoHotkey64" });

        Assert.Equal(new[] { "RobloxPlayerBeta", "python", "AutoHotkey64" }, result);
    }

    [Theory]
    [InlineData(@"C:\scripts\automation.exe")]
    [InlineData(@"python.exe --run farm.py")]
    [InlineData("roblox;other")]
    [InlineData("invalid|name")]
    public void NormalizeProtectedProcessNames_RejectsPathsArgumentsAndWildcards(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            AutoProfileService.NormalizeProtectedProcessNames(new[] { value }));
    }

    [Fact]
    public void ParseUserSettings_ValidatesAndNormalizesJson()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "protectedProcessNames": ["RobloxPlayerBeta.exe", "python", "Python.exe"]
        }
        """;

        var result = AutoProfileService.ParseUserSettings(json);

        Assert.Equal(new[] { "RobloxPlayerBeta", "python" }, result);
    }

    [Fact]
    public void ParseUserSettings_RejectsNonStringEntries()
    {
        const string json = """
        {
          "protectedProcessNames": ["RobloxPlayerBeta", 42]
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            AutoProfileService.ParseUserSettings(json));
    }
}
