using ynoez.Services;

namespace ynoez.Tests;

public sealed class ProcessPriorityProfileServiceTests
{
    [Fact]
    public void MatchesExecutableName_IgnoresExtensionAndCase()
    {
        Assert.True(ProcessPriorityProfileService.MatchesExecutableName(
            "RobloxPlayerBeta", @"C:\Games\RobloxPlayerBeta.EXE"));
        Assert.True(ProcessPriorityProfileService.MatchesExecutableName(
            "cs2.exe", @"C:\Steam\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\CS2.EXE"));
        Assert.False(ProcessPriorityProfileService.MatchesExecutableName(
            "RobloxPlayer", @"C:\Games\RobloxPlayerBeta.exe"));
    }

    [Fact]
    public void NormalizeSettings_RemovesFocusFromBackgroundAndDeduplicatesByExecutableName()
    {
        var settings = new ProcessPriorityProfileSettings
        {
            Enabled = true,
            FocusExecutablePath = @"C:\Games\CS2.exe",
            BackgroundExecutablePaths =
            [
                @"C:\Roblox\RobloxPlayerBeta.exe",
                @"D:\OtherCopy\ROBLOXPLAYERBETA.EXE",
                @"C:\Games\cs2.EXE",
                @"C:\Tools\XT.exe"
            ]
        };

        var normalized = ProcessPriorityProfileService.NormalizeSettings(settings);

        Assert.Equal(@"C:\Games\CS2.exe", normalized.FocusExecutablePath);
        Assert.Equal(2, normalized.BackgroundExecutablePaths.Count);
        Assert.Contains(normalized.BackgroundExecutablePaths, p => Path.GetFileName(p).Equals("RobloxPlayerBeta.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(normalized.BackgroundExecutablePaths, p => Path.GetFileName(p).Equals("XT.exe", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(normalized.BackgroundExecutablePaths, p => Path.GetFileNameWithoutExtension(p).Equals("CS2", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("csrss", true)]
    [InlineData("CSRSS.exe", true)]
    [InlineData("svchost", true)]
    [InlineData("ynoez.exe", true)]
    [InlineData("RobloxPlayerBeta", false)]
    [InlineData("cs2", false)]
    public void IsProtectedProcessName_ProtectsWindowsAndControllerProcesses(string name, bool expected)
    {
        Assert.Equal(expected, ProcessPriorityProfileService.IsProtectedProcessName(name));
    }

    [Fact]
    public void NormalizeSettings_HandlesNullBackgroundList()
    {
        var settings = new ProcessPriorityProfileSettings
        {
            FocusExecutablePath = @"C:\Games\CS2.exe",
            BackgroundExecutablePaths = null!
        };

        var normalized = ProcessPriorityProfileService.NormalizeSettings(settings);

        Assert.Empty(normalized.BackgroundExecutablePaths);
    }
}
