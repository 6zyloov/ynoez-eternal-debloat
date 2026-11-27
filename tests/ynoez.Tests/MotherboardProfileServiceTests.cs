using Xunit;
using ynoez.Services;

namespace ynoez.Tests;

public sealed class MotherboardProfileServiceTests
{
    private static MotherboardProfile CreateProfile() => new()
    {
        Manufacturer = "GIGABYTE",
        Product = "A520M K V2",
        RevisionLinks =
        {
            new MotherboardRevisionLink { Revision = "1.0", Bios = "https://example.invalid/rev-10-bios" },
            new MotherboardRevisionLink { Revision = "1.1", Bios = "https://example.invalid/rev-11-bios" },
            new MotherboardRevisionLink { Revision = "1.2", Bios = "https://example.invalid/rev-12-bios" }
        }
    };

    [Theory]
    [InlineData("1.0", "1.0")]
    [InlineData("1.1", "1.1")]
    [InlineData("1.2", "1.2")]
    public void GetConfirmedPhysicalRevision_ResolvesOnlyExactCataloguedPcbRevision(string value, string expected)
    {
        var result = MotherboardProfileService.GetConfirmedPhysicalRevision(CreateProfile(), value);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BIOS F7d")]
    [InlineData("rev 1.0")]
    [InlineData("1.3")]
    [InlineData("1.0x")]
    [InlineData("1.10")]
    public void GetConfirmedPhysicalRevision_RejectsUnverifiedOrUnknownText(string? value)
    {
        var result = MotherboardProfileService.GetConfirmedPhysicalRevision(CreateProfile(), value);

        Assert.Null(result);
    }

    [Fact]
    public void GetConfirmedPhysicalRevision_TrimsWhitespaceButDoesNotGuess()
    {
        var result = MotherboardProfileService.GetConfirmedPhysicalRevision(CreateProfile(), " 1.2 ");

        Assert.Equal("1.2", result?.Revision);
    }

    [Fact]
    public void GetFirmwareRevisionHint_IsSeparateFromPhysicalConfirmation()
    {
        var profile = CreateProfile();

        var hint = MotherboardProfileService.GetFirmwareRevisionHint(profile, "1.1");
        var confirmed = MotherboardProfileService.GetConfirmedPhysicalRevision(profile, null);

        Assert.Equal("1.1", hint?.Revision);
        Assert.Null(confirmed);
    }
}
