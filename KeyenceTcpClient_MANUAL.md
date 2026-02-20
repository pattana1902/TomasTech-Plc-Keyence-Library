# KeyenceTcpClient Library — AI Integration Manual

This document describes the `TomasTech_Plc_Keyence` library (file: `Class1.cs`) and how an AI or other project should use it.

> **Note**: This library implements the Keyence Upper Link Protocol (ASCII). Ensure the PLC is configured for Upper Link communication (ASCII) on the specified port.

## Overview

- **Protocol**: Keyence Upper Link (ASCII).
- **Terminator**: CR (`\r`).
- **Default Port**: `8501`.
- **Encoding**: ASCII.

## Addressed & Data Types

The library supports standard Keyence address notation (e.g., `DM100`, `R0`) and **Extended Suffixes** to specify data interpretation.

### Supported Suffixes
Append these to the address string to control how the data is read/formatted.

| Suffix | Meaning | Example | Return Type (ReadAny) |
| :--- | :--- | :--- | :--- |
| **(None)** | Unsigned 16-bit word (Default) | `DM100` | `string` ("12345") |
| **.U** | Unsigned 16-bit word (`ushort`) | `DM100.U` | `string` ("12345") |
| **.S** | Signed 16-bit word (`short`) | `DM100.S` | `string` ("-123") |
| **.D** | Signed 32-bit integer (`int`) | `DM100.D` | `string` ("12345678") |
| **.L** | Long (Signed 32-bit) | `DM100.L` | `string` ("12345678") |
| **.H** | Hexadecimal (16-bit) | `DM100.H` | `string` ("ABCD") |

### Word Order
- Controlled by `WordsOrder` property (`LowHigh` or `HighLow`).
- Affects `.D`, `.L`, `.F` (Float), and 32-bit operations.

## API Reference

### Connection
```csharp
using var client = new KeyenceTcpClient("192.168.0.10");
await client.ConnectAsync();
```

### Reading Values (Typed)
Use specific methods for best performance and type safety.

- `ReadWordsAsync(address, count)`: Reads generic 16-bit words (unsigned).
- `ReadInt32Async(address)`: Reads 2 words as 32-bit int.
- `ReadFloatAsync(address)`: Reads 2 words as 32-bit float.

### Reading/Writing with Suffixes (Generic)
Use `ReadAnyAsync` to read based on the suffix.

```csharp
string val1 = await client.ReadAnyAsync("DM100.U"); // "123"
string val2 = await client.ReadAnyAsync("DM100.S"); // "-123"
string val3 = await client.ReadAnyAsync("DM100.H"); // "007B"
```

### ASCII Strings
Keyence PLCs typically store ASCII strings as bytes within words (2 chars per word).
Use `ReadStringAsync` / `WriteStringAsync`.

```csharp
// Reads 10 bytes (5 words) and decodes as ASCII
string text = await client.ReadStringAsync("DM200", 10);

// Writes text to DM200+
await client.WriteStringAsync("DM200", "HELLO");
```

## Protocol Details (For AI Context)

### Command Format
- **Read Single**: `RD <Address>\r`
- **Read Multi**: `RDS <Address> <Count>\r`
- **Write Single**: `WR <Address> <Value>\r`
- **Write Multi**: `WRS <Address> <Count> <Val1> <Val2>...\r`

> **Note on Strings**: This library assumes standard Upper Link ASCII format where characters are stored **Big Endian per word** (High Byte = 1st Char, Low Byte = 2nd Char). E.g. "HE" is stored as `0x4845`. This matches observed Keyence behavior ("TEST" -> incorrect "ETTS" if using Little Endian).

### Response Format
- **Read Success**: Space-separated values (e.g. `123 456`) followed by `\r\n` (or just `\r`). The library handles stripping any `OK` prefix if present (though standard Upper Link Read often omits it).
- **Write Success**: `OK\r\n`.
- **Error**: `E0`, `E1`, etc.

## Troubleshooting

- **"Command not recognized"**: Ensure standard Upper Link is enabled on the device.
- **Values are wrong**: Check `WordsOrder` (LowHigh vs HighLow).
- **Timeout**: Increase `OperationTimeout` (Default 5s).