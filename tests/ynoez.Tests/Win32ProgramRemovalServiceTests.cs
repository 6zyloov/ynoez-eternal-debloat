using Microsoft.Win32;
using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class Win32ProgramRemovalServiceTests
{
    [Fact]
    public void MsiUninstallCommand_IsNormalizedToProductRemoval()
    {
        var invocation = Win32ProgramRemovalService.BuildInvocation(
            "{11111111-2222-3333-4444-555555555555}",
            true,
            "MsiExec.exe /I{11111111-2222-3333-4444-555555555555}");

        Assert.NotNull(invocation);
        Assert.Equal("msiexec.exe", Path.GetFileName(invocation.Value.ExecutablePath));
        Assert.Equal("/x {11111111-2222-3333-4444-555555555555}", invocation.Value.Arguments);
        Assert.Equal("{11111111-2222-3333-4444-555555555555}", invocation.Value.ProductCode);
    }

    [Theory]
    [InlineData("cmd.exe /c del C:\\important.txt")]
    [InlineData("powershell.exe -EncodedCommand AAA")]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL")]
    [InlineData("relative-uninstaller.exe /uninstall")]
    [InlineData("")]
    public void NonMsiCommand_RejectsCommandHostsAndNonAbsolutePaths(string command)
    {
        Assert.Null(Win32ProgramRemovalService.BuildInvocation("SomeApp", false, command));
    }

    [Fact]
    public void NonMsiCommand_RejectsMissingExecutable()
    {
        Assert.Null(Win32ProgramRemovalService.BuildInvocation(
            "SomeApp", false, @"""C:\DoesNotExist\uninstall.exe"" /remove"));
    }

    [Fact]
    public void NonMsiCommand_RequiresARealExecutablePath()
    {
        var executable = Path.Combine(Path.GetTempPath(), "ynoez-test-uninstaller-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllText(executable, "test");
            var command = "\"" + executable + "\" /uninstall";
            var result = Win32ProgramRemovalService.BuildInvocation("SomeApp", false, command);
            Assert.NotNull(result);
            Assert.Equal(executable, result.Value.ExecutablePath);
            Assert.Equal("/uninstall", result.Value.Arguments);
        }
        finally
        {
            try { if (File.Exists(executable)) File.Delete(executable); } catch { }
        }
    }

    [Fact]
    public void ProtectedExecutableNamesAreNotLaunchedAsUninstallers()
    {
        Assert.Null(Win32ProgramRemovalService.BuildInvocation(
            "SomeApp", false, @"""C:\Windows\System32\cmd.exe"" /c echo nope"));
    }
}
