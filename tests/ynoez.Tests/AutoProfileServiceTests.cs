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
    public void DecideMode_CorruptedProtectionConfig_NeverUsesAggressiveSaverWhenUserIsAfk()
    {
        var mode = AutoProfileService.DecideMode(
            TimeSpan.FromHours(3), Threshold,
            gameRunning: false,
            protectedProcessRunning: false,
            protectionConfigInvalid: true);

        Assert.Equal(PowerProfileMode.ProtectedAfk, mode);
    }

    [Fact]
    public void DecideMode_CorruptedConfigDoesNotOverrideActiveGamePerformanceWhileUserIsPresent()
    {
        var mode = AutoProfileService.DecideMode(
            TimeSpan.FromMinutes(2), Threshold,
            gameRunning: true,
            protectedProcessRunning: false,
            protectionConfigInvalid: true);

        Assert.Equal(PowerProfileMode.Performance, mode);
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

    [Fact]
    public void MergeProtectedProcessNames_UserSettingsCannotRemoveShippedScriptRuntimes()
    {
        var shipped = new[] { "python", "AutoHotkey64", "powershell" };
        var user = new[] { "XTExecutor.exe", "PYTHON.exe" };

        var result = AutoProfileService.MergeProtectedProcessNames(shipped, user);

        Assert.Equal(new[] { "python", "AutoHotkey64", "powershell", "XTExecutor" }, result);
    }


    [Fact]
    public void GetProtectionConfigurationWarning_InvalidConfigurationExplainsSafeFallback()
    {
        var warning = AutoProfileService.GetProtectionConfigurationWarning(configurationInvalid: true);

        Assert.NotNull(warning);
        Assert.Contains("AFK protegido", warning);
        Assert.Contains("economia agressiva", warning);
    }

    [Fact]
    public void GetProtectionConfigurationWarning_ValidConfigurationHasNoWarning()
    {
        Assert.Null(AutoProfileService.GetProtectionConfigurationWarning(configurationInvalid: false));
    }


    [Fact]
    public void ParseShippedProcessConfiguration_RequiresNonEmptyGameAndProtectionLists()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "processNames": ["RobloxPlayerBeta"]
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            AutoProfileService.ParseShippedProcessConfiguration(json));
    }

    [Fact]
    public void ParseShippedProcessConfiguration_RejectsInvalidNamesAndUnsupportedSchema()
    {
        const string invalidNames = """
        {
          "schemaVersion": 1,
          "processNames": ["RobloxPlayerBeta"],
          "protectedProcessNames": ["python", 42]
        }
        """;
        const string invalidSchema = """
        {
          "schemaVersion": 2,
          "processNames": ["RobloxPlayerBeta"],
          "protectedProcessNames": ["python"]
        }
        """;

        Assert.Throws<InvalidDataException>(() =>
            AutoProfileService.ParseShippedProcessConfiguration(invalidNames));
        Assert.Throws<InvalidDataException>(() =>
            AutoProfileService.ParseShippedProcessConfiguration(invalidSchema));
    }

    [Fact]
    public void ParseShippedProcessConfiguration_ParsesAndNormalizesValidProfile()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "idleThresholdMinutes": 15,
          "pollIntervalSeconds": 15,
          "processNames": ["RobloxPlayerBeta", "VALORANT-Win64-Shipping"],
          "protectedProcessNames": ["python", "Python.exe", "AutoHotkey64"]
        }
        """;

        var result = AutoProfileService.ParseShippedProcessConfiguration(json);

        Assert.Equal(new[] { "RobloxPlayerBeta", "VALORANT-Win64-Shipping" }, result.GameProcessNames);
        Assert.Equal(new[] { "python", "AutoHotkey64" }, result.ProtectedProcessNames);
        Assert.Equal(15, result.IdleThresholdMinutes);
        Assert.Equal(15, result.PollIntervalSeconds);
    }

}
