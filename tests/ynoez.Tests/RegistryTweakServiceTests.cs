using Xunit;
using Microsoft.Win32;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class RegistryTweakServiceTests
{
    [Fact]
    public void ApplyAndRestore_PreservesExistingDword()
    {
        var keyPath = TestKeyPath();
        var id = "test-existing-" + Guid.NewGuid().ToString("N");
        var backupDirectory = NewBackupDirectory();
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                key!.SetValue("Sample", 123, RegistryValueKind.DWord);

            var service = new RegistryTweakService(backupDirectory);
            var tweak = MakeTweak(id, keyPath, "Sample", 0);

            Assert.Equal(TweakState.Disabled, service.Inspect(tweak));
            service.Apply(tweak);
            Assert.Equal(TweakState.Enabled, service.Inspect(tweak));
            Assert.True(service.CanRestore(id));

            service.Restore(tweak);

            using var restored = Registry.CurrentUser.OpenSubKey(keyPath);
            Assert.Equal(123, restored!.GetValue("Sample"));
            Assert.False(service.CanRestore(id));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, recursive: true);
        }
    }

    [Fact]
    public void ApplyAndRestore_RemovesValueThatDidNotPreviouslyExist()
    {
        var keyPath = TestKeyPath();
        var id = "test-missing-" + Guid.NewGuid().ToString("N");
        var backupDirectory = NewBackupDirectory();
        try
        {
            using (Registry.CurrentUser.CreateSubKey(keyPath)) { }
            var service = new RegistryTweakService(backupDirectory);
            var tweak = MakeTweak(id, keyPath, "NewValue", 0);

            Assert.Equal(TweakState.NotConfigured, service.Inspect(tweak));
            service.Apply(tweak);
            Assert.Equal(TweakState.Enabled, service.Inspect(tweak));

            service.Restore(tweak);

            using var restored = Registry.CurrentUser.OpenSubKey(keyPath);
            Assert.DoesNotContain("NewValue", restored!.GetValueNames());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, recursive: true);
        }
    }

    [Fact]
    public void Catalog_LoadsUniqueAndSupportedTweaks()
    {
        var tweaks = OptimizationCatalogService.LoadRegistryTweaks();

        Assert.NotEmpty(tweaks);
        Assert.Equal(tweaks.Count, tweaks.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(tweaks, tweak =>
        {
            Assert.Equal(RegistryHive.CurrentUser, tweak.Hive);
            Assert.Equal(RegistryValueKind.DWord, tweak.Kind);
        });
    }
 
    [Fact]
    public void StartupEntry_DisableAndRestoreIsReversible()
    {
        const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        var valueName = "ynoez-test-" + Guid.NewGuid().ToString("N");
        var tempDirectory = Path.Combine(Path.GetTempPath(), "ynoez-startup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(runPath))
                key!.SetValue(valueName, "test-command.exe", RegistryValueKind.String);

            var manager = new StartupManagerService(tempDirectory);
            var entry = manager.GetActiveEntries().Single(x => x.Name == valueName && x.Scope == "Usuário atual");
            Assert.True(entry.CanDisable);

            manager.Disable(entry);

            using (var key = Registry.CurrentUser.OpenSubKey(runPath))
                Assert.DoesNotContain(valueName, key!.GetValueNames());
            var disabled = Assert.Single(manager.GetDisabledEntries());
            manager.Restore(disabled);

            using (var key = Registry.CurrentUser.OpenSubKey(runPath))
                Assert.Equal("test-command.exe", key!.GetValue(valueName));
            Assert.Empty(manager.GetDisabledEntries());
        }
        finally
        {
            using var key = Registry.CurrentUser.OpenSubKey(runPath, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static RegistryTweak MakeTweak(string id, string keyPath, string valueName, int value) =>
        new(id, "Teste", "Teste isolado", "Teste", "Baixo", RegistryHive.CurrentUser,
            keyPath, valueName, value, RegistryValueKind.DWord);

    private static string TestKeyPath() =>
        "Software\\Microsoft\\Windows\\CurrentVersion\\ynoez-tests\\" + Guid.NewGuid().ToString("N");

    private static string NewBackupDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ynoez-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
