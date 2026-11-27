using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class WindowsScheduledTaskInventoryServiceTests
{
    [Theory]
    [InlineData(@"\Microsoft\Windows\Customer Experience Improvement Program\", "Consolidator")]
    [InlineData(@"\Microsoft\Windows\Customer Experience Improvement Program\", "UsbCeip")]
    [InlineData(@"\Microsoft\Windows\Feedback\Siuf\", "DmClient")]
    [InlineData(@"\Microsoft\Windows\Feedback\Siuf\", "DmClientOnScenarioDownload")]
    [InlineData(@"\Microsoft\Windows\Maps\", "MapsToastTask")]
    [InlineData(@"\Microsoft\Windows\Maps\", "MapsUpdateTask")]
    public void ClassifyTask_ExplicitDebloatWhitelist_IsOptionalEvaluated(string path, string name)
    {
        var classification = WindowsScheduledTaskInventoryService.ClassifyTask(path, name);

        Assert.False(classification.IsProtected);
        Assert.True(classification.IsOptional);
        Assert.Equal("Opcional avaliada", classification.Category);
        Assert.Contains("whitelist restrita", classification.Explanation);
    }

    [Theory]
    [InlineData(@"\Microsoft\Windows\WindowsUpdate\", "Scheduled Start")]
    [InlineData(@"\Microsoft\Windows\UpdateOrchestrator\", "Schedule Scan")]
    [InlineData(@"\Microsoft\Windows\Windows Defender\", "Windows Defender Cache Maintenance")]
    [InlineData(@"\Microsoft\Windows\Customer Experience Improvement Program\", "UnreviewedTask")]
    [InlineData(@"\ThirdParty\Riot Vanguard\", "VanguardUpdate")]
    public void ClassifyTask_CriticalPathsAndAntiCheat_AreProtected(string path, string name)
    {
        var classification = WindowsScheduledTaskInventoryService.ClassifyTask(path, name);

        Assert.True(classification.IsProtected);
        Assert.False(classification.IsOptional);
        Assert.Equal("Protegida pelo ynoez", classification.Category);
    }

    [Theory]
    [InlineData(@"\Vendor\Custom\", "VendorUpdater")]
    [InlineData(@"\Microsoft\Windows\SomeNewFeature\", "NewTask")]
    public void ClassifyTask_UnreviewedTasks_RemainReadOnly(string path, string name)
    {
        var classification = WindowsScheduledTaskInventoryService.ClassifyTask(path, name);

        Assert.False(classification.IsProtected);
        Assert.False(classification.IsOptional);
        Assert.Equal("Somente leitura", classification.Category);
        Assert.Contains("Não existe uma política de alteração aprovada", classification.Explanation);
    }

    [Theory]
    [InlineData("", "Task")]
    [InlineData(@"\Vendor\", "")]
    public void ClassifyTask_RejectsMissingPathOrName(string path, string name)
    {
        Assert.Throws<ArgumentException>(() =>
            WindowsScheduledTaskInventoryService.ClassifyTask(path, name));
    }

    [Fact]
    public void ParseJson_ParsesInventoryAndAppliesSafetyClassification()
    {
        const string json = """
        [
          {
            "TaskPath": "\\Microsoft\\Windows\\Maps\\",
            "TaskName": "MapsUpdateTask",
            "State": "Ready",
            "Author": "Microsoft Corporation",
            "Description": "Offline map maintenance",
            "LastRunTime": "2026-11-27T10:30:00-03:00",
            "NextRunTime": "",
            "LastTaskResult": "0"
          },
          {
            "TaskPath": "\\Microsoft\\Windows\\WindowsUpdate\\",
            "TaskName": "Scheduled Start",
            "State": "Disabled",
            "Author": "",
            "Description": "",
            "LastRunTime": "",
            "NextRunTime": "",
            "LastTaskResult": ""
          }
        ]
        """;

        var tasks = WindowsScheduledTaskInventoryService.ParseJson(json);

        Assert.Equal(2, tasks.Count);
        var optional = tasks.Single(x => x.TaskName == "MapsUpdateTask");
        Assert.Equal("Pronta", optional.StateDisplay);
        Assert.Equal("Opcional avaliada", optional.Safety.Category);
        Assert.Equal("27/11/2026 10:30:00", optional.LastRunDisplay);
        Assert.Equal("Não agendada", optional.NextRunDisplay);
        Assert.Equal("0", optional.LastResultDisplay);

        var protectedTask = tasks.Single(x => x.TaskName == "Scheduled Start");
        Assert.Equal("Desativada", protectedTask.StateDisplay);
        Assert.Equal("Protegida pelo ynoez", protectedTask.Safety.Category);
    }

    [Fact]
    public void ParseJson_AcceptsSingleObjectAndSkipsIncompleteRows()
    {
        const string json = """
        {
          "TaskPath": "\\Vendor\\",
          "TaskName": "TaskOne",
          "State": "Running"
        }
        """;

        var tasks = WindowsScheduledTaskInventoryService.ParseJson(json);

        var task = Assert.Single(tasks);
        Assert.Equal("TaskOne", task.TaskName);
        Assert.Equal("Em execução", task.StateDisplay);
        Assert.Equal("Somente leitura", task.Safety.Category);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("42")]
    public void ParseJson_RejectsEmptyOrUnsupportedDocuments(string json)
    {
        Assert.ThrowsAny<Exception>(() => WindowsScheduledTaskInventoryService.ParseJson(json));
    }

    [Fact]
    public void BuildCsv_ExportsTaskMetadataAndNeutralizesFormulaInjection()
    {
        const string json = """
        {
          "TaskPath": "\\Vendor\\",
          "TaskName": "=InjectedTask",
          "State": "Ready",
          "Author": "+FakeAuthor",
          "Description": "@FakeDescription",
          "LastTaskResult": "-1"
        }
        """;
        var task = Assert.Single(WindowsScheduledTaskInventoryService.ParseJson(json));

        var csv = WindowsScheduledTaskInventoryService.BuildCsv(new[] { task });

        Assert.Contains("\"'=InjectedTask\"", csv);
        Assert.Contains("\"'+FakeAuthor\"", csv);
        Assert.Contains("\"'@FakeDescription\"", csv);
        Assert.Contains("\"Somente leitura\"", csv);
        Assert.Contains("\"'-1\"", csv);
    }

    [Fact]
    public void EscapeCsv_QuotesEmbeddedQuotesAndNeutralizesFormulaPrefixes()
    {
        Assert.Equal("\"'=SUM(1,2)\"", WindowsScheduledTaskInventoryService.EscapeCsv("=SUM(1,2)"));
        Assert.Equal("\"'@vendor\"", WindowsScheduledTaskInventoryService.EscapeCsv("@vendor"));
        Assert.Equal("\"normal \"\"quoted\"\" value\"", WindowsScheduledTaskInventoryService.EscapeCsv("normal \"quoted\" value"));
        Assert.Equal("\"\"", WindowsScheduledTaskInventoryService.EscapeCsv(null));
    }
}
