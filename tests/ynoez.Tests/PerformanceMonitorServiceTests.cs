using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class PerformanceMonitorServiceTests
{
    [Fact]
    public void GetMaximum3DEngineUtilization_SumsProcessesSharingSamePhysicalEngine()
    {
        var samples = new[]
        {
            ("pid_100_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D", 24d),
            ("pid_200_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D", 31d),
            ("pid_300_luid_0x00000000_0x00000001_phys_0_eng_1_engtype_3D", 12d)
        };

        Assert.Equal(55d, GpuEngineUtilization.GetMaximum3DEngineUtilization(samples));
    }

    [Fact]
    public void GetMaximum3DEngineUtilization_UsesBusiestEngineInsteadOfAddingDifferentEngines()
    {
        var samples = new[]
        {
            ("pid_100_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D", 62d),
            ("pid_200_luid_0x00000000_0x00000001_phys_0_eng_1_engtype_3D", 48d)
        };

        Assert.Equal(62d, GpuEngineUtilization.GetMaximum3DEngineUtilization(samples));
    }

    [Fact]
    public void GetMaximum3DEngineUtilization_KeepsAdaptersIndependent()
    {
        var samples = new[]
        {
            ("pid_100_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D", 35d),
            ("pid_200_luid_0x00000000_0x00000002_phys_0_eng_0_engtype_3D", 71d)
        };

        Assert.Equal(71d, GpuEngineUtilization.GetMaximum3DEngineUtilization(samples));
    }

    [Fact]
    public void GetMaximum3DEngineUtilization_IgnoresOtherEngineTypesAndInvalidValues()
    {
        var samples = new[]
        {
            ("pid_100_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_Copy", 99d),
            ("pid_100_luid_0x00000000_0x00000001_phys_0_eng_1_engtype_3D", double.NaN),
            ("pid_200_luid_0x00000000_0x00000001_phys_0_eng_1_engtype_3D", 43d)
        };

        Assert.Equal(43d, GpuEngineUtilization.GetMaximum3DEngineUtilization(samples));
    }

    [Fact]
    public void GetMaximum3DEngineUtilization_ClampsSummedEngineToOneHundredPercent()
    {
        var samples = new[]
        {
            ("pid_100_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D", 76d),
            ("pid_200_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D", 52d)
        };

        Assert.Equal(100d, GpuEngineUtilization.GetMaximum3DEngineUtilization(samples));
    }

    [Fact]
    public void GetMaximum3DEngineUtilization_UsesConservativeFallbackForUnknownCounterNames()
    {
        var samples = new[]
        {
            ("legacy_instance_engtype_3D", 28d),
            ("another_legacy_instance_engtype_3D", 47d),
            ("copy_engtype_Copy", 100d)
        };

        Assert.Equal(47d, GpuEngineUtilization.GetMaximum3DEngineUtilization(samples));
    }

    [Fact]
    public void GetMaximum3DEngineUtilization_ReturnsNullWhenNo3DEngineExists()
    {
        Assert.Null(GpuEngineUtilization.GetMaximum3DEngineUtilization(
            new[] { ("pid_100_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_Copy", 50d) }));
    }
}
