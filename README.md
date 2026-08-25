# TomasTech.Plc.Keyence

[![NuGet](https://img.shields.io/nuget/v/TomasTech.Plc.Keyence.svg)](https://www.nuget.org/packages/TomasTech.Plc.Keyence)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A lightweight .NET client for talking to Keyence PLCs over the **Upper Link (ASCII)** protocol — read and write words, bits, 32-bit integers, floats, and ASCII strings over a plain TCP socket. Targets `netstandard2.0` and `net8.0`.

## Install

```bash
dotnet add package TomasTech.Plc.Keyence
```

## Quick start

```csharp
using TomasTech_Plc_Keyence;

using var client = new KeyenceTcpClient("192.168.0.10"); // default port 8501
await client.ConnectAsync();

// Typed reads
ushort[] words = await client.ReadWordsAsync("DM100", 3);
int position = await client.ReadInt32Async("DM100.D");
float temp = await client.ReadFloatAsync("DM100.D");
bool running = await client.ReadBoolAsync("MR100");
string label = await client.ReadStringAsync("DM200", 10); // 10 bytes = 5 words

// Generic read/write dispatched by suffix
string value = await client.ReadAnyAsync("DM100.H"); // hex string
await client.WriteWordsAsync("DM100", new ushort[] { 123 });
```

## Supported devices

`PlcAddress.Parse` recognizes these Keyence KV-series memory area prefixes: `DM, D, MR, ZF, HR, CIO, LR, EM, W, R, CR, TN, CN, T, C, AT, CM`.

An address with a prefix outside this list still works correctly — as of v1.2.0, the exact bytes you typed are always sent to the PLC as-is, never silently rewritten. The recognized-prefix list only affects the strongly-typed `PlcAddress.WordType` enum value, not what is actually sent on the wire.

## Address suffixes

Append these to an address to control how `ReadAnyAsync`/`ReadAnyArrayAsync` interpret and format the value:

| Suffix | Meaning | Example | Return type (`ReadAny`) |
| :--- | :--- | :--- | :--- |
| *(none)* | Unsigned 16-bit word | `DM100` | `string` ("12345") |
| `.U` | Unsigned 16-bit (`ushort`) | `DM100.U` | `string` ("12345") |
| `.S` | Signed 16-bit (`short`) | `DM100.S` | `string` ("-123") |
| `.D` | Signed 32-bit (`int`) | `DM100.D` | `string` ("12345678") |
| `.L` | Long / signed 32-bit | `DM100.L` | `string` ("12345678") |
| `.H` | Hexadecimal (16-bit) | `DM100.H` | `string` ("ABCD") |
| `.B` | Bit (boolean) | `MR100.B` | `string` ("True" / "False") |

32-bit operations (`.D`, `.L`, `ReadInt32*`, `ReadFloat*`) respect `KeyenceTcpClient.WordsOrder` (`LowHigh` or `HighLow`) for combining the two words.

## Protocol details

- Commands: `RD`/`RDS` (read), `WR`/`WRS` (write), terminated with `\r`.
- ASCII strings are packed **big-endian per word** (high byte = first character) — e.g. `"HE"` is stored as `0x4845`.
- Errors come back as bare codes (`E0`, `E1`, ...) and surface as `InvalidOperationException`.

See [`KeyenceTcpClient_MANUAL.md`](KeyenceTcpClient_MANUAL.md) for the full protocol reference.

## Changelog

See [`CHANGELOG.md`](CHANGELOG.md).

## Testing

```bash
dotnet test
```

The test suite includes pure unit tests for address parsing plus loopback-socket integration tests that run a fake Keyence server in-process — no real PLC hardware needed to validate the wire protocol.

## Contributing

Issues and pull requests are welcome at the [GitHub repository](https://github.com/pattana1902/TomasTech-Plc-Keyence-Library).

## License

[MIT](LICENSE)
