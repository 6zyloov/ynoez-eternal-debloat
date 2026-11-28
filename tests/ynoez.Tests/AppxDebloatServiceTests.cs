using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class AppxDebloatServiceTests
{
    [Fact]
    public void FindCandidates_ReturnsOnlyExactOptionalPackages()
    {
        var packages = new[]
        {
            Package("Microsoft.BingNews", "Microsoft.BingNews_1.0.0.0_x64__8wekyb3d8bbwe"),
            Package("Microsoft.WindowsStore", "Microsoft.WindowsStore_1.0.0.0_x64__8wekyb3d8bbwe"),
            Package("Microsoft.BingWeather", "Microsoft.BingWeather_1.0.0.0_x64__8wekyb3d8bbwe", nonRemovable: true),
            Package("Microsoft.GetHelp", "Microsoft.GetHelp_1.0.0.0_x64__8wekyb3d8bbwe", framework: true),
            Package("Microsoft.SkypeApp", "Microsoft.SkypeApp_1.0.0.0_x64__8wekyb3d8bbwe", resource: true),
            Package("Microsoft.People", "Microsoft.People_1.0.0.0_x64__8wekyb3d8bbwe", bundle: true),
            Package("Microsoft.Getstarted", "Microsoft.Getstarted_1.0.0.0_x64__8wekyb3d8bbwe", status: "Modified")
        };

        var candidates = AppxDebloatService.FindCandidates(packages);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Microsoft.BingNews", candidate.Name);
        Assert.Equal("Notícias", candidate.DisplayName);
    }

    [Fact]
    public void RemovalScriptTargetsExactCurrentUserPackageAndNeverAllUsers()
    {
        var candidate = new AppxRemovalCandidate(
            "Microsoft.BingNews",
            "Notícias",
            "Optional news app",
            "Microsoft.BingNews_1.0.0.0_x64__8wekyb3d8bbwe",
            "1.0.0.0");

        var script = AppxDebloatService.BuildRemovalScript(candidate);

        Assert.Contains("$_.Name -eq $name", script);
        Assert.Contains("$_.PackageFullName -eq $full", script);
        Assert.Contains("Remove-AppxPackage", script);
        Assert.DoesNotContain("Remove-AppxProvisionedPackage", script);
        Assert.DoesNotContain("Get-AppxPackage -AllUsers", script);
        Assert.DoesNotContain("Remove-AppxPackage -AllUsers", script);
    }

    [Fact]
    public void RemovalScript_RejectsPackageOutsideClosedAllowlist()
    {
        var candidate = new AppxRemovalCandidate(
            "Microsoft.WindowsStore",
            "Microsoft Store",
            "Protected",
            "Microsoft.WindowsStore_1.0.0.0_x64__8wekyb3d8bbwe",
            "1.0.0.0");

        Assert.Throws<InvalidOperationException>(() => AppxDebloatService.BuildRemovalScript(candidate));
    }

    [Fact]
    public void Allowlist_ContainsOnlyKnownOptionalAppsAndExcludesSystemCore()
    {
        var names = AppxDebloatService.GetAllowlistedPackageNames();

        Assert.Contains("Microsoft.BingNews", names);
        Assert.Contains("Microsoft.WindowsMaps", names);
        Assert.DoesNotContain("Microsoft.WindowsStore", names);
        Assert.DoesNotContain("Microsoft.SecHealthUI", names);
        Assert.DoesNotContain("Microsoft.Windows.Search", names);
        Assert.DoesNotContain("MicrosoftWindows.Client.CBS", names);
    }

    private static AppxPackageInfo Package(
        string name,
        string packageFullName,
        bool framework = false,
        bool resource = false,
        bool bundle = false,
        bool nonRemovable = false,
        string status = "Ok") =>
        new(name, packageFullName, "1.0.0.0", "Microsoft", status,
            framework, resource, bundle, nonRemovable,
            "Inventário — remoção não avaliada", @"C:\Apps");
}
