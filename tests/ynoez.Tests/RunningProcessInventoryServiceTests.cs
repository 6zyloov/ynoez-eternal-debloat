using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class RunningProcessInventoryServiceTests
{
    [Fact]
    public void NormalizeSnapshot_FiltersInvalidEntriesAndSortsByName()
    {
        var result = RunningProcessInventoryService.NormalizeSnapshot(new[]
        {
            new RunningProcessEntry(42, "XTExecutor", 100),
            new RunningProcessEntry(0, "invalid", 1000),
            new RunningProcessEntry(43, "python", 200),
            new RunningProcessEntry(44, " ", 100)
        });

        Assert.Collection(result,
            item => Assert.Equal((43, "python"), (item.ProcessId, item.ProcessName)),
            item => Assert.Equal((42, "XTExecutor"), (item.ProcessId, item.ProcessName)));
    }

    [Fact]
    public void NormalizeSnapshot_PreservesDifferentPidsForSameProcessName()
    {
        var result = RunningProcessInventoryService.NormalizeSnapshot(new[]
        {
            new RunningProcessEntry(101, "python", 100),
            new RunningProcessEntry(102, "python", 200)
        });

        Assert.Equal(new[] { 101, 102 }, result.Select(x => x.ProcessId));
    }

    [Fact]
    public void NormalizeSnapshot_DeduplicatesPidAndClampsNegativeWorkingSet()
    {
        var result = RunningProcessInventoryService.NormalizeSnapshot(new[]
        {
            new RunningProcessEntry(55, "node", -10),
            new RunningProcessEntry(55, "Node", 500)
        });

        var item = Assert.Single(result);
        Assert.Equal(500, item.WorkingSetBytes);
        Assert.Equal("Node", item.ProcessName);
    }
}
