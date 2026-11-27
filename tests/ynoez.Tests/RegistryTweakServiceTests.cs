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
            Assert.DoesNotContain(@"\\", tweak.KeyPath);
            Assert.StartsWith("Software\\Microsoft\\Windows\\CurrentVersion\\", tweak.KeyPath, StringComparison.OrdinalIgnoreCase);
        });
    }
 

    [Fact]
    public void Inspect_RejectsNonCanonicalRegistryPath()
    {
        var directory = NewBackupDirectory();
        try
        {
            var service = new RegistryTweakService(directory);
            var tweak = MakeTweak("test-invalid-path", @"Software\Microsoft\Windows\CurrentVersion\\Unsafe", "Value", 0);
            Assert.Throws<InvalidOperationException>(() => service.Inspect(tweak));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ApplyAndRestore_PreservesExpandStringTypeAndLiteral()
    {
        var keyPath = TestKeyPath();
        var id = "test-expand-" + Guid.NewGuid().ToString("N");
        var backupDirectory = NewBackupDirectory();
        const string original = "%TEMP%\\ynoez-test";
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                key!.SetValue("Expandable", original, RegistryValueKind.ExpandString);

            var service = new RegistryTweakService(backupDirectory);
            var tweak = MakeTweak(id, keyPath, "Expandable", 0);
            service.Apply(tweak);
            service.Restore(tweak);

            using var restored = Registry.CurrentUser.OpenSubKey(keyPath);
            Assert.Equal(RegistryValueKind.ExpandString, restored!.GetValueKind("Expandable"));
            Assert.Equal(original, restored.GetValue("Expandable", null, RegistryValueOptions.DoNotExpandEnvironmentNames));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, recursive: true);
        }
    }

    [Fact]
    public void Restore_LeavesCorruptBackupForManualRecovery()
    {
        var keyPath = TestKeyPath();
        var id = "test-corrupt-" + Guid.NewGuid().ToString("N");
        var backupDirectory = NewBackupDirectory();
        try
        {
            var service = new RegistryTweakService(backupDirectory);
            var tweak = MakeTweak(id, keyPath, "Sample", 0);
            service.Apply(tweak);
            var backupPath = Path.Combine(backupDirectory, id + ".json");
            File.WriteAllText(backupPath, "{corrupt");

            Assert.ThrowsAny<Exception>(() => service.Restore(tweak));
            Assert.True(File.Exists(backupPath));
            Assert.Equal(TweakState.Enabled, service.Inspect(tweak));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, recursive: true);
        }
    }



    [Fact]
    public void OptionalServiceCatalog_ExcludesProtectedWindowsAndAntiCheatServices()
    {
        var names = ServiceTweakService.GetCatalog().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(names);
        Assert.DoesNotContain("WSearch", names);
        Assert.DoesNotContain("Spooler", names);
        Assert.DoesNotContain("WinDefend", names);
        Assert.DoesNotContain("wuauserv", names);
        Assert.DoesNotContain("vgc", names);
        Assert.DoesNotContain("FaceitService", names);
    }

    [Fact]
    public void OptionalServiceManager_RejectsProtectedServiceEvenIfRequestedDirectly()
    {
        var manager = new ServiceTweakService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var protectedService = new OptionalServiceDefinition("WSearch", "Windows Search", "Protected", "High");
        Assert.Throws<InvalidOperationException>(() => manager.Disable(protectedService));
    }

    [Fact]
    public void PowerProfileSession_UsesUniqueBackupPathUnderBackups()
    {
        var backupRoot = Path.GetFullPath(AppPaths.Backups) + Path.DirectorySeparatorChar;
        var sample = Path.GetFullPath(Path.Combine(AppPaths.Backups, "auto-profile-original-" + Guid.NewGuid().ToString("N") + ".pow"));
        Assert.StartsWith(backupRoot, sample, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(".pow", Path.GetExtension(sample));
        Assert.Matches("^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$", Guid.NewGuid().ToString());
    }

    [Fact]
    public void ScheduledTaskCatalog_ContainsOnlyExplicitOptionalTasks()
    {
        var catalog = ScheduledTaskTweakService.GetCatalog();
        Assert.NotEmpty(catalog);
        Assert.Equal(catalog.Count, catalog.Select(x => x.TaskPath + x.TaskName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(catalog, x => x.TaskName.Contains("Update", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(catalog, x => x.TaskName.Contains("Defender", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(catalog, x => x.TaskName.Contains("Search", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScheduledTaskManager_RejectsProtectedOrUnknownTask()
    {
        var manager = new ScheduledTaskTweakService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var protectedTask = new ScheduledTaskDefinition(@"\Microsoft\Windows\WindowsUpdate\", "Scheduled Start", "Update", "Protected", "High");
        Assert.Throws<InvalidOperationException>(() => manager.Disable(protectedTask));
    }

    [Fact]
    public void PresentMonCsvParser_ComputesAverageAndOnePercentLow()
    {
        var path = Path.Combine(Path.GetTempPath(), "ynoez-presentmon-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var lines = new List<string> { "Application,ProcessID,MsBetweenPresents" };
            for (var i = 0; i < 90; i++) lines.Add("\"Game, Window\",123,10");
            for (var i = 0; i < 10; i++) lines.Add("\"Game, Window\",123,50");
            File.WriteAllLines(path, lines);

            var summary = PresentMonService.ParseCsv(path, new CaptureTarget(123, "Game"));

            Assert.Equal(100, summary.FrameCount);
            Assert.Equal(1000d / 14d, summary.AverageFps, 3);
            Assert.Equal(20d, summary.OnePercentLowFps, 3);
            Assert.Equal(10, summary.FramesOver33Ms);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
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
