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
    public void ApplyAndRestore_PreservesBinaryOriginalValue()
    {
        var keyPath = TestKeyPath();
        var backupDirectory = NewBackupDirectory();
        var original = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFF };
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                key!.SetValue("Binary", original, RegistryValueKind.Binary);

            var service = new RegistryTweakService(backupDirectory);
            var tweak = MakeTweak("test-binary-" + Guid.NewGuid().ToString("N"), keyPath, "Binary", 0);
            service.Apply(tweak);
            service.Restore(tweak);

            using var restored = Registry.CurrentUser.OpenSubKey(keyPath);
            Assert.Equal(RegistryValueKind.Binary, restored!.GetValueKind("Binary"));
            Assert.Equal(original, (byte[])restored.GetValue("Binary")!);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, recursive: true);
        }
    }

    [Fact]
    public void ApplyAndRestore_PreservesMultiStringOriginalValue()
    {
        var keyPath = TestKeyPath();
        var backupDirectory = NewBackupDirectory();
        var original = new[] { "first", "second", "with spaces" };
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                key!.SetValue("Multi", original, RegistryValueKind.MultiString);

            var service = new RegistryTweakService(backupDirectory);
            var tweak = MakeTweak("test-multi-" + Guid.NewGuid().ToString("N"), keyPath, "Multi", 0);
            service.Apply(tweak);
            service.Restore(tweak);

            using var restored = Registry.CurrentUser.OpenSubKey(keyPath);
            Assert.Equal(RegistryValueKind.MultiString, restored!.GetValueKind("Multi"));
            Assert.Equal(original, (string[])restored.GetValue("Multi")!);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, recursive: true);
        }
    }

    [Fact]
    public void ApplyAndRestore_PreservesQwordOriginalValue()
    {
        var keyPath = TestKeyPath();
        var backupDirectory = NewBackupDirectory();
        const long original = 9_223_372_036_854_000;
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                key!.SetValue("Qword", original, RegistryValueKind.QWord);

            var service = new RegistryTweakService(backupDirectory);
            var tweak = MakeTweak("test-qword-" + Guid.NewGuid().ToString("N"), keyPath, "Qword", 0);
            service.Apply(tweak);
            service.Restore(tweak);

            using var restored = Registry.CurrentUser.OpenSubKey(keyPath);
            Assert.Equal(RegistryValueKind.QWord, restored!.GetValueKind("Qword"));
            Assert.Equal(original, (long)restored.GetValue("Qword")!);
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
    public void OptionalServiceManager_RejectsUnrestorableServiceStartModes()
    {
        Assert.True(ServiceTweakService.IsRestorableStartMode("Auto"));
        Assert.True(ServiceTweakService.IsRestorableStartMode("Manual"));
        Assert.True(ServiceTweakService.IsRestorableStartMode("auto"));
        Assert.False(ServiceTweakService.IsRestorableStartMode("Disabled"));
        Assert.False(ServiceTweakService.IsRestorableStartMode("Boot"));
        Assert.False(ServiceTweakService.IsRestorableStartMode("System"));
        Assert.False(ServiceTweakService.IsRestorableStartMode("Unknown"));
        Assert.False(ServiceTweakService.IsRestorableStartMode(null));
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
    public void NetworkWindow_UsesActualLossAndKeepsNoGatewaySeparate()
    {
        var samples = new[]
        {
            new NetworkMonitorSample(DateTimeOffset.UtcNow, "", "NO_GATEWAY", null, "OK", 20, "OK", 30, 0, 0, 0, null, null, null),
            new NetworkMonitorSample(DateTimeOffset.UtcNow, "", "NO_GATEWAY", null, "OK", 30, "TIMEOUT", null, 0, 0, 0, null, null, null),
            new NetworkMonitorSample(DateTimeOffset.UtcNow, "", "NO_GATEWAY", null, "TIMEOUT", null, "OK", 50, 0, 0, 0, null, null, null),
            new NetworkMonitorSample(DateTimeOffset.UtcNow, "", "NO_GATEWAY", null, "OK", 40, "OK", 70, 0, 0, 0, null, null, null)
        };

        var summary = NetworkMonitorService.SummarizeWindow(samples);

        Assert.Equal(0, summary.GatewayLossPercent);
        Assert.Equal(25, summary.ExternalLossPercent);
        Assert.Equal(25, summary.ExternalTcpFailurePercent);
        Assert.Null(summary.GatewayJitterMilliseconds);
        Assert.Equal(10, summary.ExternalJitterMilliseconds);
        Assert.Equal(20, summary.ExternalTcpJitterMilliseconds!.Value);
    }

    [Fact]
    public void PowerProfileBackupExportPath_RejectsDirectoryNamedPowFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ynoez-power-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var nestedFolder = Path.Combine(root, "nested");
            Directory.CreateDirectory(nestedFolder);
            Assert.Throws<InvalidDataException>(() =>
                PowerProfileService.ValidatePowerSchemeExportPath(Path.Combine(nestedFolder, "bad.pow"), root));

            var directoryNamedPow = Path.Combine(root, "candidate.pow");
            Directory.CreateDirectory(directoryNamedPow);
            Assert.Throws<InvalidDataException>(() =>
                PowerProfileService.ValidatePowerSchemeExportPath(directoryNamedPow, root));

            var futureExport = Path.Combine(root, "future-export.pow");
            Assert.Equal(Path.GetFullPath(futureExport),
                PowerProfileService.ValidatePowerSchemeExportPath(futureExport, root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PowerProfileExportPath_OnlyAcceptsDirectPowFilesInsideBackupRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ynoez-power-backups-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var valid = Path.Combine(root, "automatic-session.pow");
            Assert.Equal(Path.GetFullPath(valid),
                PowerProfileService.ValidatePowerSchemeExportPath(valid, root));

            var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N") + ".pow");
            Assert.Throws<InvalidDataException>(() =>
                PowerProfileService.ValidatePowerSchemeExportPath(outside, root));

            var nestedFolder = Path.Combine(root, "nested");
            Directory.CreateDirectory(nestedFolder);
            var nested = Path.Combine(nestedFolder, "automatic-session.pow");
            Assert.Throws<InvalidDataException>(() =>
                PowerProfileService.ValidatePowerSchemeExportPath(nested, root));

            var wrongExtension = Path.Combine(root, "automatic-session.exe");
            Assert.Throws<InvalidDataException>(() =>
                PowerProfileService.ValidatePowerSchemeExportPath(wrongExtension, root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PowerProfileResolution_FallsBackToBalancedWhenOptionalPlanIsMissing()
    {
        var balancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        var resolution = PowerProfileService.ResolveTarget(PowerProfileMode.Afk,
            alias => alias == "SCHEME_BALANCED" ? balancedGuid : null);

        Assert.Equal(PowerProfileMode.Afk, resolution.RequestedMode);
        Assert.Equal(PowerProfileMode.Balanced, resolution.AppliedMode);
        Assert.True(resolution.UsedFallback);
        Assert.Equal("SCHEME_BALANCED", resolution.AppliedAlias);
        Assert.Equal(balancedGuid, resolution.Guid);
    }

    [Fact]
    public void PowerProfileResolution_DoesNotInventFallbackWhenBalancedIsMissing()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PowerProfileService.ResolveTarget(PowerProfileMode.Performance, _ => null));
    }

    [Fact]
    public async Task SnapshotManifest_RecordsFileSizesAndSha256()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ynoez-snapshot-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            const string content = "{\"cpu\":\"test\"}";
            var filePath = Path.Combine(folder, "hardware.json");
            await File.WriteAllTextAsync(filePath, content);
            var manifest = await BackupService.BuildSnapshotManifestAsync(folder);
            var file = Assert.Single(manifest);
            Assert.Equal("hardware.json", file.Name);
            Assert.Equal(new FileInfo(filePath).Length, file.Length);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant(), file.Sha256);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotVerification_ConfirmsHashesAndDetectsChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "ynoez-snapshot-verify-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "snapshot-test");
        Directory.CreateDirectory(folder);
        try
        {
            const string content = "{\"adapter\":\"Ethernet\"}";
            var file = Path.Combine(folder, "network.json");
            await File.WriteAllTextAsync(file, content);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            var backup = new BackupResult
            {
                Folder = folder,
                CreatedAt = DateTime.UtcNow,
                Files = new[] { new BackupFileInfo("network.json", new FileInfo(file).Length, hash) }
            };
            await File.WriteAllTextAsync(Path.Combine(folder, "backup.json"),
                System.Text.Json.JsonSerializer.Serialize(backup));

            var valid = await BackupService.VerifyLocalSnapshotAsync(folder, root);
            Assert.True(valid.IsValid);
            Assert.Equal(1, valid.VerifiedFiles);

            await File.WriteAllTextAsync(file, content + "tampered");
            var invalid = await BackupService.VerifyLocalSnapshotAsync(folder, root);
            Assert.False(invalid.IsValid);
            Assert.Contains("tamanho diferente", invalid.Details, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotVerification_RejectsUnexpectedDirectoriesAndNullEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "ynoez-snapshot-extra-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "snapshot-test");
        Directory.CreateDirectory(folder);
        try
        {
            const string content = "verified";
            var file = Path.Combine(folder, "data.txt");
            await File.WriteAllTextAsync(file, content);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            var backup = new BackupResult
            {
                Folder = folder,
                CreatedAt = DateTime.UtcNow,
                Files = new[] { new BackupFileInfo("data.txt", new FileInfo(file).Length, hash) }
            };
            await File.WriteAllTextAsync(Path.Combine(folder, "backup.json"),
                System.Text.Json.JsonSerializer.Serialize(backup));
            Directory.CreateDirectory(Path.Combine(folder, "unexpected"));

            var result = await BackupService.VerifyLocalSnapshotAsync(folder, root);
            Assert.False(result.IsValid);
            Assert.Contains("pasta extra", result.Details, StringComparison.OrdinalIgnoreCase);

            backup = new BackupResult
            {
                Folder = folder,
                CreatedAt = DateTime.UtcNow,
                Files = new BackupFileInfo[] { null! }
            };
            await File.WriteAllTextAsync(Path.Combine(folder, "backup.json"),
                System.Text.Json.JsonSerializer.Serialize(backup));
            var nullEntry = await BackupService.VerifyLocalSnapshotAsync(folder, root);
            Assert.False(nullEntry.IsValid);
            Assert.Contains("Nome de arquivo", nullEntry.Details, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotVerification_RejectsFoldersOutsideBackupRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ynoez-backup-root-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "ynoez-snapshot-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => BackupService.VerifyLocalSnapshotAsync(outside, root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void NetworkDiagnosticJitter_DoesNotBridgeAcrossFailedPings()
    {
        Assert.Null(NetworkDiagnosticService.CalculateJitter(new long?[] { 20, null, 170 }));
        Assert.Equal(15, NetworkDiagnosticService.CalculateJitter(new long?[] { 20, 30, 50 }));
        Assert.Null(NetworkDiagnosticService.CalculateJitter(new long?[] { 20, null, 30, null }));
    }

    [Fact]
    public void NetworkWindow_TracksSecondaryDestinationAndDnsFailures()
    {
        var samples = new[]
        {
            new NetworkMonitorSample(DateTimeOffset.UtcNow, "192.168.1.1", "OK", 1,
                "OK", 15, "OK", 20, 0, 0, 0, null, null, null,
                "TIMEOUT", null, "TIMEOUT", null, "TIMEOUT", null),
            new NetworkMonitorSample(DateTimeOffset.UtcNow, "192.168.1.1", "OK", 1,
                "OK", 16, "OK", 21, 0, 0, 0, null, null, null,
                "OK", 18, "OK", 22, "OK", 8)
        };

        var summary = NetworkMonitorService.SummarizeWindow(samples);

        Assert.Equal(50, summary.SecondaryExternalLossPercent);
        Assert.Equal(50, summary.SecondaryExternalTcpFailurePercent);
        Assert.Equal(50, summary.DnsFailurePercent);
    }

    [Fact]
    public void NetworkRootCause_SeparatesLocalDnsAndUpstreamPatterns()
    {
        var local = MakeNetworkSample(gatewayLoss: 25);
        Assert.Equal("REDE LOCAL", NetworkRootCauseService.Analyze(local).Category);

        var dns = MakeNetworkSample(dnsFailure: 50);
        Assert.Equal("DNS", NetworkRootCauseService.Analyze(dns).Category);

        var upstream = MakeNetworkSample(firstLoss: 50, secondLoss: 50, firstTcp: 50, secondTcp: 50);
        Assert.Equal("ROTA / PROVEDOR", NetworkRootCauseService.Analyze(upstream).Category);

        var icmpFiltered = MakeNetworkSample(firstLoss: 50, secondLoss: 50);
        Assert.Equal("ICMP / FILTRAGEM", NetworkRootCauseService.Analyze(icmpFiltered).Category);
    }

    private static NetworkMonitorSample MakeNetworkSample(
        double gatewayLoss = 0, double firstLoss = 0, double secondLoss = 0,
        double firstTcp = 0, double secondTcp = 0, double dnsFailure = 0) =>
        new(DateTimeOffset.UtcNow, "192.168.1.1", "OK", 1, "OK", 15, "OK", 20,
            gatewayLoss, firstLoss, firstTcp, 0, 0, 0,
            "OK", 15, "OK", 20, "OK", 5,
            secondLoss, secondTcp, dnsFailure);

    [Fact]
    public void NetworkJitter_DoesNotBridgeAcrossPacketLoss()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            new NetworkMonitorSample(now, "", "NO_GATEWAY", null, "OK", 20, "OK", 30, 0, 0, 0, null, null, null),
            new NetworkMonitorSample(now, "", "NO_GATEWAY", null, "TIMEOUT", null, "TIMEOUT", null, 0, 0, 0, null, null, null),
            new NetworkMonitorSample(now, "", "NO_GATEWAY", null, "OK", 170, "OK", 190, 0, 0, 0, null, null, null)
        };

        var summary = NetworkMonitorService.SummarizeWindow(samples);

        Assert.Null(summary.GatewayJitterMilliseconds);
        Assert.Null(summary.ExternalJitterMilliseconds);
        Assert.Null(summary.ExternalTcpJitterMilliseconds);
        Assert.Equal(33.333333, summary.ExternalLossPercent, 5);
        Assert.Equal(33.333333, summary.ExternalTcpFailurePercent, 5);
    }

    [Fact]
    public void InstalledProgramInventory_ProtectsDriversRuntimesUpdatesAndComponents()
    {
        Assert.StartsWith("Protegido", InstalledProgramInventoryService.Classify(
            "Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft Corporation", "", false, ""));
        Assert.StartsWith("Protegido", InstalledProgramInventoryService.Classify(
            "NVIDIA Graphics Driver", "NVIDIA Corporation", "", false, ""));
        Assert.StartsWith("Protegido", InstalledProgramInventoryService.Classify(
            "Security Update for Windows", "Microsoft", "Security Update", false, ""));
        Assert.StartsWith("Protegido", InstalledProgramInventoryService.Classify(
            "Optional Component", "Publisher", "", true, ""));
        Assert.StartsWith("Protegido", InstalledProgramInventoryService.Classify(
            "Add-on", "Publisher", "", false, "ParentSuite"));
    }

    [Fact]
    public void InstalledProgramInventory_LeavesOrdinaryAppsReviewableAndEscapesCsvFormulas()
    {
        Assert.StartsWith("Aplicativo desktop", InstalledProgramInventoryService.Classify(
            "Brave", "Brave Software Inc.", "", false, ""));
        Assert.Equal("\"normal, value\"", InstalledProgramInventoryService.EscapeCsv("normal, value"));
        Assert.Equal("\"'=HYPERLINK(1)\"", InstalledProgramInventoryService.EscapeCsv("=HYPERLINK(1)"));
        Assert.Equal("\"' +SUM(1,2)\"", InstalledProgramInventoryService.EscapeCsv(" +SUM(1,2)"));
    }

    [Fact]
    public void SystemAppxInventory_ParsesAndClassifiesInstalledAndProvisionedPackages()
    {
        const string json = "[{\"Name\":\"Microsoft.WindowsStore\",\"PackageName\":\"Microsoft.WindowsStore_1.0_x64\",\"Version\":\"1.0.0.0\",\"Architecture\":\"X64\",\"Scope\":\"Todos os usuários\"},{\"Name\":\"Example.App\",\"PackageName\":\"Example.App_1.0_x64\",\"Version\":\"1.0.0.0\",\"Architecture\":\"X64\",\"Scope\":\"Provisionado\"}]";
        var packages = AppxSystemInventoryService.ParseJson(json);

        Assert.Equal(2, packages.Count);
        Assert.Contains(packages, x => x.Name == "Microsoft.WindowsStore" && x.Classification.StartsWith("Protegido", StringComparison.Ordinal));
        Assert.Contains(packages, x => x.Name == "Example.App" && x.Scope == "Provisionado");
    }

    [Fact]
    public void SystemAppxInventory_CsvNeutralizesSpreadsheetFormulae()
    {
        Assert.Equal("\"'=HYPERLINK(1)\"", AppxSystemInventoryService.EscapeCsv("=HYPERLINK(1)"));
        Assert.Equal("\"normal\"", AppxSystemInventoryService.EscapeCsv("normal"));
    }

    [Fact]
    public void AppxInventory_LabelsCoreWindowsPackagesAsProtected()
    {
        const string json = "[{\"Name\":\"Microsoft.WindowsStore\",\"PackageFullName\":\"Microsoft.WindowsStore_1.0_x64\",\"Version\":\"1.0.0.0\",\"Publisher\":\"CN=Microsoft\",\"Status\":\"Ok\",\"IsFramework\":false,\"IsResourcePackage\":false,\"NonRemovable\":false},{\"Name\":\"Example.Framework\",\"PackageFullName\":\"Example.Framework_1.0_x64\",\"Version\":\"1.0.0.0\",\"Publisher\":\"CN=Example\",\"Status\":\"Ok\",\"IsFramework\":true,\"IsResourcePackage\":false,\"NonRemovable\":false}]";
        var packages = AppPackageInventoryService.ParseJson(json);

        Assert.Equal(2, packages.Count);
        Assert.Contains(packages, x => x.Name == "Microsoft.WindowsStore" && x.Classification.StartsWith("Protegido", StringComparison.Ordinal));
        Assert.Contains(packages, x => x.Name == "Example.Framework" && x.IsFramework && x.Classification.StartsWith("Protegido", StringComparison.Ordinal));
    }

    [Fact]
    public void AppxInventory_CsvEscapesFormulaLikeFields()
    {
        var package = new AppxPackageInfo(
            "=HYPERLINK(\"https://example.invalid\")", "Package", "+1.0", "@Publisher",
            "Ok", false, false, false, false, "Review", "-InstallLocation");

        var csv = AppPackageInventoryService.BuildCsv(new[] { package });

        Assert.Contains("\"'=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.Contains("\"'+1.0\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"'@Publisher\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"'-InstallLocation\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void AppxInventory_ParsesSinglePackageObject()
    {
        var packages = AppPackageInventoryService.ParseJson("{\"Name\":\"Example.App\",\"PackageFullName\":\"Example.App_1.0_x64\",\"Version\":\"1.0.0.0\"}");
        var package = Assert.Single(packages);
        Assert.Equal("Example.App", package.Name);
        Assert.False(package.IsBundle);
        Assert.Equal("Inventário — remoção não avaliada", package.Classification);
    }

    [Fact]
    public void AdapterPowerRestoreInventory_ListsValidBackupAndRejectsCorruptFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ynoez-adapter-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new AdapterPowerManagementService(directory);
            var backup = new AdapterPowerBackup("Ethernet", "Enabled", "Disabled", DateTimeOffset.UtcNow);
            File.WriteAllText(Path.Combine(directory, "Ethernet.json"), System.Text.Json.JsonSerializer.Serialize(backup));
            Assert.Equal(backup, Assert.Single(service.GetPendingRestores()));

            File.WriteAllText(Path.Combine(directory, "broken.json"), "{corrupt");
            Assert.Throws<InvalidDataException>(() => service.GetPendingRestores());
            Assert.True(File.Exists(Path.Combine(directory, "broken.json")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AdapterPowerBackupFileName_DisambiguatesSanitizedNames()
    {
        var first = AdapterPowerManagementService.GetBackupFileName("Ethernet 2");
        var second = AdapterPowerManagementService.GetBackupFileName("Ethernet_2");

        Assert.NotEqual(first, second);
        Assert.Equal(first, AdapterPowerManagementService.GetBackupFileName("Ethernet 2"));
        Assert.EndsWith(".json", first, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => AdapterPowerManagementService.GetBackupFileName(""));
    }

    [Fact]
    public void OptionalServiceCatalog_ContainsNoDuplicateNames()
    {
        var names = ServiceTweakService.GetCatalog().Select(x => x.Name).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void OptionalServiceRestoreInventory_ExposesOnlyWhitelistedBackups()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ynoez-service-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var backup = new OptionalServiceBackup("DiagTrack", "Manual", false, false, DateTimeOffset.UtcNow);
            File.WriteAllText(Path.Combine(directory, "DiagTrack.json"), System.Text.Json.JsonSerializer.Serialize(backup));
            var manager = new ServiceTweakService(directory);

            Assert.Equal("DiagTrack", Assert.Single(manager.GetPendingRestoreDefinitions()).Name);
            Assert.True(manager.CanRestore("DiagTrack"));
            Assert.Throws<InvalidOperationException>(() => manager.CanRestore("WSearch"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NetworkAdapterInventory_CsvEscapesFormulaLikeValues()
    {
        Assert.Equal("\"'=HYPERLINK(1)\"", NetworkAdapterInventoryService.EscapeCsv("=HYPERLINK(1)"));
        Assert.Equal("\"'@driver\"", NetworkAdapterInventoryService.EscapeCsv("@driver"));
        Assert.Equal("\"normal adapter\"", NetworkAdapterInventoryService.EscapeCsv("normal adapter"));
    }

    [Fact]
    public void NetworkAdapterInventory_MtuDisplayHandlesUnavailableValues()
    {
        Assert.Equal("1500 / 1280", NetworkAdapterInventoryService.FormatMtu(1500, 1280));
        Assert.Equal("— / 1280", NetworkAdapterInventoryService.FormatMtu(null, 1280));
        Assert.Equal("— / —", NetworkAdapterInventoryService.FormatMtu(null, null));
    }

    [Fact]
    public void NetworkStatistics_CalculateErrorAndDiscardRatesSafely()
    {
        Assert.Equal(5.2631579, NetworkAdapterStatisticsService.CalculateProblemPercent(900, 20, 30)!.Value, 5);
        Assert.Null(NetworkAdapterStatisticsService.CalculateProblemPercent(0, 0, 0));
        Assert.Equal("1.0 KB", NetworkAdapterStatisticsService.FormatBytes(1024));
        Assert.Equal("1 Gbps", NetworkAdapterStatisticsService.FormatLinkSpeed(1_000_000_000));
    }

    [Fact]
    public void ScheduledTaskCatalog_ContainsOnlyExplicitOptionalTasks()
    {
        var catalog = ScheduledTaskTweakService.GetCatalog();
        Assert.NotEmpty(catalog);
        Assert.Equal(catalog.Count, catalog.Select(x => x.TaskPath + x.TaskName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(catalog, x => x.TaskPath.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase));
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
    public void UpdateService_ParsesStableReleaseAndComparesVersions()
    {
        const string json = "{\"tag_name\":\"v0.4.0\",\"html_url\":\"https://github.com/6zyloov/ynoez-eternal-debloat/releases/tag/v0.4.0\",\"published_at\":\"2026-11-27T12:00:00Z\",\"draft\":false,\"prerelease\":false,\"body\":\"Melhorias e correções.\"}";
        var update = UpdateService.ParseLatestRelease(json, "0.3.0");

        Assert.Equal("0.3.0", update.CurrentVersion);
        Assert.Equal("0.4.0", update.LatestVersion);
        Assert.True(update.IsUpdateAvailable);
        Assert.Equal("v0.4.0", update.TagName);
        Assert.Contains("Melhorias", update.ReleaseNotes);
    }

    [Fact]
    public void UpdateService_RejectsOfficialButMismatchedReleaseTagUrl()
    {
        const string json = "{\"tag_name\":\"v0.4.0\",\"html_url\":\"https://github.com/6zyloov/ynoez-eternal-debloat/releases/tag/v0.3.0\",\"draft\":false,\"prerelease\":false}";
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseLatestRelease(json, "0.3.0"));
    }

    [Fact]
    public void UpdateService_ReportsCurrentVersionAndRejectsUntrustedReleaseUrls()
    {
        const string json = "{\"tag_name\":\"v0.3.0\",\"html_url\":\"https://github.com/6zyloov/ynoez-eternal-debloat/releases/tag/v0.3.0\",\"draft\":false,\"prerelease\":false}";
        var update = UpdateService.ParseLatestRelease(json, "0.3.0");

        Assert.False(update.IsUpdateAvailable);
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseLatestRelease(
            "{\"tag_name\":\"v0.4.0\",\"html_url\":\"https://example.invalid/releases/tag/v0.4.0\",\"draft\":false,\"prerelease\":false}", "0.3.0"));
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseLatestRelease(
            "{\"tag_name\":\"v0.4.0-beta\",\"html_url\":\"https://github.com/6zyloov/ynoez-eternal-debloat/releases/tag/v0.4.0-beta\",\"draft\":false,\"prerelease\":false}", "0.3.0"));
        Assert.Throws<InvalidDataException>(() => UpdateService.ParseReleaseUrl("https://github.com.evil.invalid/6zyloov/ynoez-eternal-debloat/releases/tag/v0.4.0"));
    }

    [Fact]
    public void BenchmarkSessions_UsesMediansAndComparesThreeRunsPerPhase()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ynoez-benchmark-session-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new BenchmarkSessionService(directory);
            var baseline = new[]
            {
                MakeBenchmark("Game", 90, 45, 20, 11, 1),
                MakeBenchmark("Game", 100, 50, 25, 10, 2),
                MakeBenchmark("Game", 110, 55, 30, 9, 3)
            };
            var session = service.CreateBaseline("Game", 60, "Mapa treino", baseline);
            Assert.NotNull(service.FindLatestReference("Game", 60));
            Assert.Null(service.FindLatestReference("OtherGame", 60));
            Assert.Null(service.FindLatestReference("Game", 30));

            var after = new[]
            {
                MakeBenchmark("Game", 108, 54, 27, 9.2, 4),
                MakeBenchmark("Game", 120, 60, 30, 8.5, 5),
                MakeBenchmark("Game", 132, 66, 33, 8.0, 6)
            };
            var comparison = service.RecordAfterRuns(session.Id, "Mesmo mapa treino", after);

            Assert.Equal(100, comparison.Baseline.MedianAverageFps, 3);
            Assert.Equal(120, comparison.After.MedianAverageFps, 3);
            Assert.Equal(20, comparison.Changes.AverageFpsChangePercent, 3);
            Assert.Equal(20, comparison.Changes.OnePercentLowChangePercent, 3);
            Assert.Equal(20, comparison.Changes.OneTenthPercentLowChangePercent, 3);
            Assert.False(File.Exists(Path.Combine(directory, session.Id + ".tmp")));
            Assert.Null(service.FindLatestReference("Game", 60));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BenchmarkSessions_RejectsCorruptPartialAfterSeriesAndPreservesFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ynoez-benchmark-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new BenchmarkSessionService(directory);
            var baseline = new[]
            {
                MakeBenchmark("Game", 90, 45, 20, 11, 1),
                MakeBenchmark("Game", 100, 50, 25, 10, 2),
                MakeBenchmark("Game", 110, 55, 30, 9, 3)
            };
            var session = service.CreateBaseline("Game", 60, "", baseline);
            var path = Path.Combine(directory, session.Id + ".json");
            var original = File.ReadAllText(path);
            var corrupt = original.Replace("\"AfterRuns\": []", "\"AfterRuns\": [{}]", StringComparison.Ordinal);
            Assert.NotEqual(original, corrupt);
            File.WriteAllText(path, corrupt);

            Assert.Null(service.FindLatestReference("Game", 60));
            Assert.Equal(corrupt, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BenchmarkSessions_RejectIncompleteOrMismatchedSeries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ynoez-benchmark-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new BenchmarkSessionService(directory);
            Assert.Throws<ArgumentException>(() =>
                service.CreateBaseline("Game", 60, "", new[] { MakeBenchmark("Game", 90, 45, 20, 10, 1) }));
            Assert.Throws<InvalidDataException>(() =>
                service.CreateBaseline("Game", 60, "", new[]
                {
                    MakeBenchmark("Game", 90, 45, 20, 10, 1),
                    MakeBenchmark("OtherGame", 100, 50, 25, 9, 2),
                    MakeBenchmark("Game", 110, 55, 30, 8, 3)
                }));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static FrameBenchmarkSummary MakeBenchmark(
        string processName, double averageFps, double onePercent, double oneTenth, double frameTime, int minute)
    {
        var capturedAt = DateTimeOffset.UtcNow.AddMinutes(minute);
        return new FrameBenchmarkSummary(processName, 123, "capture-" + minute + ".csv", 10000,
            averageFps, onePercent, oneTenth, frameTime,
            frameTime * 2, frameTime * 3, 1, 0, capturedAt);
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
            lines.Add("\"Unrelated Process\",999,900");
            File.WriteAllLines(path, lines);

            var summary = PresentMonService.ParseCsv(path, new CaptureTarget(123, "Game"));

            Assert.Equal(100, summary.FrameCount);
            Assert.Equal(1000d / 14d, summary.AverageFps, 3);
            Assert.Equal(20d, summary.OnePercentLowFps, 3);
            Assert.Equal(20d, summary.OneTenthPercentLowFps, 3);
            Assert.Equal(50d, summary.P99_9FrameTimeMs, 3);
            Assert.Equal(10, summary.FramesOver33Ms);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PresentMonComparison_ComputesRelativeChanges()
    {
        var previous = new FrameBenchmarkSummary("Game", 1, "old.csv", 100,
            100, 50, 25, 10, 20, 40, 0, 0, DateTimeOffset.UtcNow.AddMinutes(-5));
        var current = new FrameBenchmarkSummary("Game", 2, "new.csv", 100,
            110, 60, 30, 9, 16.67, 33.33, 0, 0, DateTimeOffset.UtcNow);

        var comparison = PresentMonService.Compare(current, previous);

        Assert.Equal(10, comparison.AverageFpsChangePercent, 3);
        Assert.Equal(20, comparison.OnePercentLowChangePercent, 3);
        Assert.Equal(20, comparison.OneTenthPercentLowChangePercent, 3);
        Assert.Equal(-10, comparison.AverageFrameTimeChangePercent, 3);
    }

    [Fact]
    public async Task SnapshotVerification_AcceptsMatchingManifestAndRejectsTampering()
    {
        var root = Path.Combine(Path.GetTempPath(), "ynoez-snapshot-test-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "snapshot-valid");
        Directory.CreateDirectory(folder);
        try
        {
            var payloadPath = Path.Combine(folder, "hardware.json");
            await File.WriteAllTextAsync(payloadPath, "{\"cpu\":\"Test CPU\"}");
            var files = await BackupService.BuildSnapshotManifestAsync(folder);
            var manifest = new BackupResult
            {
                Folder = folder,
                CreatedAt = DateTime.UtcNow,
                Files = files
            };
            await File.WriteAllTextAsync(Path.Combine(folder, "backup.json"), System.Text.Json.JsonSerializer.Serialize(manifest));

            var valid = await BackupService.VerifyLocalSnapshotAsync(folder, root);
            Assert.True(valid.IsValid, valid.Details);
            Assert.Equal(1, valid.VerifiedFiles);

            await File.AppendAllTextAsync(payloadPath, "tampered");
            var tampered = await BackupService.VerifyLocalSnapshotAsync(folder, root);
            Assert.False(tampered.IsValid);
            Assert.Contains("tamanho diferente", tampered.Details, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotVerification_RejectsUnmanifestedFilesAndUnsafePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "ynoez-snapshot-unsafe-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "snapshot");
        Directory.CreateDirectory(folder);
        try
        {
            var payload = Path.Combine(folder, "hardware.json");
            await File.WriteAllTextAsync(payload, "safe");
            var files = await BackupService.BuildSnapshotManifestAsync(folder);
            var manifest = new BackupResult { Folder = folder, CreatedAt = DateTime.UtcNow, Files = files };
            await File.WriteAllTextAsync(Path.Combine(folder, "backup.json"), System.Text.Json.JsonSerializer.Serialize(manifest));
            await File.WriteAllTextAsync(Path.Combine(folder, "unexpected.txt"), "not in manifest");
            var extra = await BackupService.VerifyLocalSnapshotAsync(folder, root);
            Assert.False(extra.IsValid);
            Assert.Contains("fora do manifesto", extra.Details, StringComparison.OrdinalIgnoreCase);

            var unsafeManifest = manifest with
            {
                Files = new[] { new BackupFileInfo("..\\outside.txt", 4, new string('0', 64)) }
            };
            await File.WriteAllTextAsync(Path.Combine(folder, "backup.json"), System.Text.Json.JsonSerializer.Serialize(unsafeManifest));
            var unsafeResult = await BackupService.VerifyLocalSnapshotAsync(folder, root);
            Assert.False(unsafeResult.IsValid);
            Assert.Contains("Nome de arquivo inválido", unsafeResult.Details, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotVerification_RequiresSnapshotDirectlyInsideBackupRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ynoez-snapshot-parent-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "nested", "snapshot");
        Directory.CreateDirectory(nested);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => BackupService.VerifyLocalSnapshotAsync(nested, root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StartupFolderEntry_DisableAndRestoreMovesExactFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ynoez-startup-folder-" + Guid.NewGuid().ToString("N"));
        var startup = Path.Combine(root, "Startup");
        var state = Path.Combine(root, "State");
        Directory.CreateDirectory(startup);
        var startupFile = Path.Combine(startup, "ordinary-startup.cmd");
        const string original = "@echo off\r\necho test\r\n";
        File.WriteAllText(startupFile, original);
        try
        {
            var manager = new StartupManagerService(state, startup);
            var entry = Assert.Single(manager.GetActiveEntries().Where(x => x.FilePath == startupFile));
            Assert.True(entry.CanDisable);
            manager.Disable(entry);
            Assert.False(File.Exists(startupFile));
            var disabled = Assert.Single(manager.GetDisabledEntries());
            Assert.True(disabled.IsFile);
            manager.Restore(disabled);
            Assert.Equal(original, File.ReadAllText(startupFile));
            Assert.Empty(manager.GetDisabledEntries());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StartupManifestCorrupt_DisableKeepsFileAndRegistryEntryUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "ynoez-startup-corrupt-" + Guid.NewGuid().ToString("N"));
        var startup = Path.Combine(root, "Startup");
        var state = Path.Combine(root, "State");
        Directory.CreateDirectory(startup);
        var startupFile = Path.Combine(startup, "ordinary.cmd");
        File.WriteAllText(startupFile, "@echo off");
        const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        var valueName = "ynoez-corrupt-" + Guid.NewGuid().ToString("N");
        var manifest = Path.Combine(state, "disabled-startup.json");
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(runPath))
                key!.SetValue(valueName, "ordinary.exe", RegistryValueKind.String);
            Directory.CreateDirectory(state);
            File.WriteAllText(manifest, "{corrupt");

            var manager = new StartupManagerService(state, startup);
            var fileEntry = Assert.Single(manager.GetActiveEntries().Where(x => x.FilePath == startupFile));
            var registryEntry = Assert.Single(manager.GetActiveEntries().Where(x => x.Name == valueName && x.Scope == "Usuário atual"));

            Assert.Throws<InvalidDataException>(() => manager.GetDisabledEntries());
            Assert.Throws<InvalidDataException>(() => manager.Disable(fileEntry));
            Assert.Throws<InvalidDataException>(() => manager.Disable(registryEntry));

            Assert.True(File.Exists(startupFile));
            using var keyAfter = Registry.CurrentUser.OpenSubKey(runPath);
            Assert.Equal("ordinary.exe", keyAfter!.GetValue(valueName));
            Assert.Equal("{corrupt", File.ReadAllText(manifest));
        }
        finally
        {
            using var key = Registry.CurrentUser.OpenSubKey(runPath, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StartupManifestCorrupt_RestoreKeepsFileInBackup()
    {
        var root = Path.Combine(Path.GetTempPath(), "ynoez-startup-corrupt-restore-" + Guid.NewGuid().ToString("N"));
        var startup = Path.Combine(root, "Startup");
        var state = Path.Combine(root, "State");
        Directory.CreateDirectory(startup);
        var startupFile = Path.Combine(startup, "ordinary.cmd");
        const string original = "@echo off\r\necho preserved";
        File.WriteAllText(startupFile, original);
        var manifest = Path.Combine(state, "disabled-startup.json");
        try
        {
            var manager = new StartupManagerService(state, startup);
            var entry = Assert.Single(manager.GetActiveEntries().Where(x => x.FilePath == startupFile));
            manager.Disable(entry);
            var disabled = Assert.Single(manager.GetDisabledEntries());
            Assert.False(File.Exists(startupFile));
            var backupPath = disabled.BackupFilePath!;
            Assert.True(File.Exists(backupPath));

            File.WriteAllText(manifest, "{corrupt");
            Assert.Throws<InvalidDataException>(() => manager.Restore(disabled));

            Assert.False(File.Exists(startupFile));
            Assert.True(File.Exists(backupPath));
            Assert.Equal("{corrupt", File.ReadAllText(manifest));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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
