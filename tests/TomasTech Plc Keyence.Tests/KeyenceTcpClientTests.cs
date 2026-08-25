using TomasTech_Plc_Keyence;
using Xunit;

namespace TomasTech.Plc.Keyence.Tests;

public class KeyenceTcpClientTests
{
    static async Task<KeyenceTcpClient> ConnectedClientAsync(FakeKeyencePlcServer server)
    {
        server.Start();
        var client = new KeyenceTcpClient("127.0.0.1", server.Port);
        await client.ConnectAsync();
        return client;
    }

    [Fact]
    public async Task ReadWordsAsync_SingleWord_SendsRdAndParsesResponse()
    {
        await using var server = new FakeKeyencePlcServer(cmd => cmd == "RD DM100" ? "123" : "E9");
        using var client = await ConnectedClientAsync(server);

        var words = await client.ReadWordsAsync("DM100", 1);

        Assert.Equal(new ushort[] { 123 }, words);
        Assert.Contains("RD DM100", server.ReceivedCommands);
    }

    [Fact]
    public async Task ReadWordsAsync_MultipleWords_SendsRdsAndParsesResponse()
    {
        await using var server = new FakeKeyencePlcServer(cmd => cmd == "RDS DM100 3" ? "1 2 3" : "E9");
        using var client = await ConnectedClientAsync(server);

        var words = await client.ReadWordsAsync("DM100", 3);

        Assert.Equal(new ushort[] { 1, 2, 3 }, words);
    }

    [Fact]
    public async Task ReadWordsAsync_ErrorResponse_ThrowsInvalidOperationException()
    {
        await using var server = new FakeKeyencePlcServer(_ => "E1");
        using var client = await ConnectedClientAsync(server);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ReadWordsAsync("DM100", 1));
        Assert.Contains("E1", ex.Message);
    }

    [Fact]
    public async Task ReadWordsAsync_EmDeviceType_SendsRawPrefixOnTheWire_NotUnknown()
    {
        // Regression test for the real production incident: TomasTech.Plc.Keyence v1.1.0 rebuilt
        // the wire address from WordType.ToString(), so an EM address (a real Keyence Extended Data
        // Memory register) that wasn't yet in the PlcWordType enum silently became "Unknown20000" on
        // the wire, and the PLC rejected it with "PLC Error: E1". This proves the actual bytes sent
        // for "EM20000" are "RD EM20000", not "RD Unknown20000" — the bug is fixed, not just the
        // enum's Parse() result in isolation.
        await using var server = new FakeKeyencePlcServer(cmd => cmd == "RD EM20000" ? "42" : "E1");
        using var client = await ConnectedClientAsync(server);

        var words = await client.ReadWordsAsync("EM20000", 1);

        Assert.Equal(new ushort[] { 42 }, words);
        Assert.Contains("RD EM20000", server.ReceivedCommands);
    }

    [Fact]
    public async Task WriteWordsAsync_SingleWord_SendsWr()
    {
        await using var server = new FakeKeyencePlcServer(cmd => cmd == "WR DM100 123" ? "OK" : "E9");
        using var client = await ConnectedClientAsync(server);

        await client.WriteWordsAsync("DM100", new ushort[] { 123 });

        Assert.Contains("WR DM100 123", server.ReceivedCommands);
    }

    [Fact]
    public async Task WriteWordsAsync_MultipleWords_SendsWrs()
    {
        await using var server = new FakeKeyencePlcServer(cmd => cmd == "WRS DM100 2 10 20" ? "OK" : "E9");
        using var client = await ConnectedClientAsync(server);

        await client.WriteWordsAsync("DM100", new ushort[] { 10, 20 });

        Assert.Contains("WRS DM100 2 10 20", server.ReceivedCommands);
    }

    [Fact]
    public async Task WriteWordsAsync_NonOkResponse_Throws()
    {
        await using var server = new FakeKeyencePlcServer(_ => "E1");
        using var client = await ConnectedClientAsync(server);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.WriteWordsAsync("DM100", new ushort[] { 1 }));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public async Task ReadBoolAsync_InterpretsNonZeroAsTrue(string reply, bool expected)
    {
        await using var server = new FakeKeyencePlcServer(cmd => cmd == "RD MR100" ? reply : "E9");
        using var client = await ConnectedClientAsync(server);

        var result = await client.ReadBoolAsync("MR100");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ReadInt32Async_LowHighWordOrder_CombinesWordsCorrectly()
    {
        // Target value 70000 = 0x00011170 -> low16=0x1170 (4464), high16=0x0001 (1).
        await using var server = new FakeKeyencePlcServer(cmd => cmd == "RDS DM100 2" ? "4464 1" : "E9");
        using var client = await ConnectedClientAsync(server);
        client.WordsOrder = KeyenceTcpClient.WordOrder.LowHigh;

        var result = await client.ReadInt32Async("DM100");

        Assert.Equal(70000, result);
    }

    [Fact]
    public async Task ReadInt32Async_HighLowWordOrder_CombinesWordsCorrectly()
    {
        await using var server = new FakeKeyencePlcServer(cmd => cmd == "RDS DM100 2" ? "1 4464" : "E9");
        using var client = await ConnectedClientAsync(server);
        client.WordsOrder = KeyenceTcpClient.WordOrder.HighLow;

        var result = await client.ReadInt32Async("DM100");

        Assert.Equal(70000, result);
    }

    [Fact]
    public async Task ReadFloatAsync_CombinesWordsIntoIeee754Single()
    {
        // 1.5f = 0x3FC00000 -> low16=0x0000 (0), high16=0x3FC0 (16320).
        await using var server = new FakeKeyencePlcServer(cmd => cmd == "RDS DM100 2" ? "0 16320" : "E9");
        using var client = await ConnectedClientAsync(server);

        var result = await client.ReadFloatAsync("DM100");

        Assert.Equal(1.5f, result);
    }

    [Fact]
    public async Task ReadStringAsync_DecodesBigEndianPerWordAscii()
    {
        // "TEST" -> word1 = ('T'<<8)|'E' = 0x5445 = 21573, word2 = ('S'<<8)|'T' = 0x5354 = 21332.
        await using var server = new FakeKeyencePlcServer(cmd => cmd == "RDS DM200 2" ? "21573 21332" : "E9");
        using var client = await ConnectedClientAsync(server);

        var result = await client.ReadStringAsync("DM200", 4);

        Assert.Equal("TEST", result);
    }

    [Fact]
    public async Task WriteStringAsync_EncodesBigEndianPerWordAscii()
    {
        // "HI" -> word = ('H'<<8)|'I' = 0x4849 = 18505.
        await using var server = new FakeKeyencePlcServer(cmd => cmd == "WR DM200 18505" ? "OK" : "E9");
        using var client = await ConnectedClientAsync(server);

        await client.WriteStringAsync("DM200", "HI");

        Assert.Contains("WR DM200 18505", server.ReceivedCommands);
    }

    [Theory]
    [InlineData("DM100.U", "123", "123")]
    [InlineData("DM100.S", "65535", "-1")]
    [InlineData("DM100.H", "291", "0123")]
    [InlineData("MR100.B", "1", "True")]
    public async Task ReadAnyAsync_DispatchesBySuffix(string address, string wordReply, string expected)
    {
        await using var server = new FakeKeyencePlcServer(cmd => cmd.StartsWith("RD ") ? wordReply : "E9");
        using var client = await ConnectedClientAsync(server);

        var result = await client.ReadAnyAsync(address);

        Assert.Equal(expected, result);
    }
}
