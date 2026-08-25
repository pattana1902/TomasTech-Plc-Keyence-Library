using TomasTech_Plc_Keyence;
using Xunit;

namespace TomasTech.Plc.Keyence.Tests;

public class PlcAddressTests
{
    [Theory]
    [InlineData("DM100", PlcWordType.DM, 100)]
    [InlineData("D100", PlcWordType.D, 100)]
    [InlineData("MR100", PlcWordType.MR, 100)]
    [InlineData("ZF520000", PlcWordType.ZF, 520000)]
    [InlineData("HR10", PlcWordType.HR, 10)]
    [InlineData("CIO10", PlcWordType.CIO, 10)]
    [InlineData("LR1000", PlcWordType.LR, 1000)]
    [InlineData("EM20000", PlcWordType.EM, 20000)]
    [InlineData("W10", PlcWordType.W, 10)]
    [InlineData("R200", PlcWordType.R, 200)]
    [InlineData("CR10", PlcWordType.CR, 10)]
    [InlineData("TN5", PlcWordType.TN, 5)]
    [InlineData("CN5", PlcWordType.CN, 5)]
    [InlineData("T5", PlcWordType.T, 5)]
    [InlineData("C5", PlcWordType.C, 5)]
    [InlineData("AT5", PlcWordType.AT, 5)]
    [InlineData("CM5", PlcWordType.CM, 5)]
    public void Parse_RecognizedPrefix_MapsToExpectedWordTypeAndOffset(string input, PlcWordType expectedType, int expectedOffset)
    {
        var address = PlcAddress.Parse(input);

        Assert.Equal(expectedType, address.WordType);
        Assert.Equal(expectedOffset, address.Offset);
    }

    [Fact]
    public void Parse_LowercaseInput_IsCaseInsensitive()
    {
        var address = PlcAddress.Parse("em20000");

        Assert.Equal(PlcWordType.EM, address.WordType);
        Assert.Equal("EM20000", address.BaseAddress);
    }

    [Theory]
    [InlineData("DM100.U", PlcDataType.U, "DM100")]
    [InlineData("DM100.S", PlcDataType.S, "DM100")]
    [InlineData("DM100.D", PlcDataType.D, "DM100")]
    [InlineData("DM100.H", PlcDataType.H, "DM100")]
    [InlineData("DM100.L", PlcDataType.L, "DM100")]
    [InlineData("MR100.B", PlcDataType.B, "MR100")]
    [InlineData("DM100", PlcDataType.None, "DM100")]
    public void Parse_Suffix_SetsDataTypeAndStripsFromBaseAddress(string input, PlcDataType expectedDataType, string expectedBaseAddress)
    {
        var address = PlcAddress.Parse(input);

        Assert.Equal(expectedDataType, address.DataType);
        Assert.Equal(expectedBaseAddress, address.BaseAddress);
    }

    [Fact]
    public void Parse_UnrecognizedSuffix_IsTreatedAsPartOfNoSuffixOffset()
    {
        // ".A" is an application-level marker some consumers use (e.g. ASCII16BIT) that this
        // library does not itself interpret as a suffix — since the offset parser only accepts
        // digits after the letters, an address like "EM20000.A" would fail to parse as a plain
        // address. Consumers that use such app-level markers must pass the bare address (no
        // suffix) into this library themselves, as TomasTech.Plc.Keyence never sees the ".A".
        Assert.Throws<FormatException>(() => PlcAddress.Parse("EM20000.A"));
    }

    [Fact]
    public void Parse_UnrecognizedDeviceTypePrefix_FallsBackToUnknownWordType_ButBaseAddressStillRoundTrips()
    {
        // Regression test for the real incident this fixes: before v1.2.0, BaseAddress rebuilt the
        // address from WordType.ToString(), so any prefix missing from the PlcWordType enum silently
        // became the literal device name "Unknown{offset}" on the wire — which the PLC then rejected
        // with a bare "PLC Error: E1" that gave no hint the real cause was an unrecognized prefix.
        // BaseAddress now always reflects the exact prefix the caller typed, so even a prefix this
        // library has never heard of (a newer/rarer Keyence device area) reaches the PLC correctly
        // instead of being silently corrupted.
        var address = PlcAddress.Parse("XX20000");

        Assert.Equal(PlcWordType.Unknown, address.WordType);
        Assert.Equal("XX20000", address.BaseAddress);
    }

    [Fact]
    public void Parse_PreservesOriginalRawInput()
    {
        var address = PlcAddress.Parse("dm100.u");

        Assert.Equal("dm100.u", address.Raw);
        Assert.Equal("dm100.u", address.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullOrWhitespaceInput_Throws(string input)
    {
        Assert.Throws<ArgumentNullException>(() => PlcAddress.Parse(input));
    }

    [Fact]
    public void Parse_NoLetterPrefix_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => PlcAddress.Parse("12345"));
    }

    [Fact]
    public void Parse_NoNumericOffset_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => PlcAddress.Parse("DM"));
    }
}
