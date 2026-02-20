using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TomasTech_Plc_Keyence
{
    /// <summary>
    /// Common PLC word types (expand as needed).
    /// </summary>
    public enum PlcWordType
    {
        Unknown,
        DM,
        D,
        MR,
        ZF,
        HR,
        CIO,
        LR
    }

    /// <summary>
    /// Suffix for data interpretation.
    /// </summary>
    public enum PlcDataType
    {
        None,
        U, // .U: Unsigned 16-bit (ushort)
        S, // .S: Signed 16-bit (short)
        D, // .D: Signed 32-bit (int)
        H, // .H: Hex string (16-bit hex)
        L, // .L: Long/Signed 32-bit (same as D often, but distinct suffix)
        // Add more as needed
    }

    /// <summary>
    /// Represents a PLC memory address such as "DM100", "DM100.U", "DM100.D".
    /// </summary>
    public sealed class PlcAddress
    {
        public PlcWordType WordType { get; }
        public int Offset { get; }
        public string Raw { get; }
        public PlcDataType DataType { get; }

        PlcAddress(string raw, PlcWordType type, int offset, PlcDataType dataType)
        {
            Raw = raw;
            WordType = type;
            Offset = offset;
            DataType = dataType;
        }

        public static PlcAddress Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) throw new ArgumentNullException(nameof(input));
            var s = input.Trim().ToUpperInvariant();

            // Detect suffix
            var dataType = PlcDataType.None;
            int dotIndex = s.LastIndexOf('.');
            if (dotIndex > 0 && dotIndex < s.Length - 1)
            {
                var suffix = s.Substring(dotIndex + 1);
                dataType = suffix switch
                {
                    "U" => PlcDataType.U,
                    "S" => PlcDataType.S,
                    "D" => PlcDataType.D,
                    "H" => PlcDataType.H,
                    "L" => PlcDataType.L,
                    _ => PlcDataType.None
                };

                if (dataType != PlcDataType.None)
                {
                    s = s.Substring(0, dotIndex);
                }
            }

            // split alpha prefix and numeric suffix
            int i = 0;
            while (i < s.Length && char.IsLetter(s[i])) i++;
            if (i == 0) throw new FormatException($"Address must start with a word type. Input: {input}");
            var prefix = s.Substring(0, i);
            var rest = s.Substring(i);

            if (rest.Length == 0 || !int.TryParse(rest, out var offset))
                throw new FormatException($"Address must contain a numeric offset. Input: {input}");

            PlcWordType type = prefix switch
            {
                "DM" => PlcWordType.DM,
                "D" => PlcWordType.D,
                "MR" => PlcWordType.MR,
                "ZF" => PlcWordType.ZF,
                "HR" => PlcWordType.HR,
                "CIO" => PlcWordType.CIO,
                "LR" => PlcWordType.LR,
                _ => PlcWordType.Unknown
            };

            return new PlcAddress(input, type, offset, dataType); // Keep original raw input or normalized? Input better for logging
        }

        public override string ToString() => Raw; // Or rebuild from parts? Keep valid original.
        
        /// <summary>
        /// Returns the base address string without suffix (e.g. "DM100.U" -> "DM100").
        /// </summary>
        public string BaseAddress => $"{WordType}{Offset}";
    }

    /// <summary>
    /// Simple Keyence TCP client.
    /// Commands: RD, RDS, WR, WRS.
    /// Protocol: Upper Link (ASCII).
    /// Terminator: CR (\r).
    /// </summary>
    public class KeyenceTcpClient : IDisposable
    {
        public enum WordOrder
        {
            LowHigh,
            HighLow
        }

        public WordOrder WordsOrder { get; set; } = WordOrder.LowHigh;

        readonly string _host;
        readonly int _port;
        readonly Encoding _encoding;
        TcpClient? _tcp;
        NetworkStream? _stream;
        readonly SemaphoreSlim _lock = new(1, 1);

        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(5);

        public KeyenceTcpClient(string host, int port = 8501, Encoding? encoding = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _port = port;
            _encoding = encoding ?? Encoding.ASCII;
        }

        public bool IsConnected => _tcp?.Connected ?? false;

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsConnected) return;

            _tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(OperationTimeout);

#if NET5_0_OR_GREATER
            await _tcp.ConnectAsync(_host, _port, cts.Token).ConfigureAwait(false);
#else
            using (cts.Token.Register(() => _tcp.Dispose()))
            {
                try
                {
                    await _tcp.ConnectAsync(_host, _port).ConfigureAwait(false);
                }
                catch (Exception) when (cts.Token.IsCancellationRequested)
                {
                     // Rethrow as OperationCanceledException if it was cancelled
                     throw new OperationCanceledException("Connection timed out or cancelled", cts.Token);
                }
            }
#endif
            _stream = _tcp.GetStream();
            _stream.ReadTimeout = (int)OperationTimeout.TotalMilliseconds;
            _stream.WriteTimeout = (int)OperationTimeout.TotalMilliseconds;
        }

        public void Disconnect()
        {
            try
            {
                _stream?.Close();
                _tcp?.Close();
            }
            finally
            {
                _stream = null;
                _tcp = null;
            }
        }

        public async Task<ushort[]?> ReadWordsAsync(string address, int count, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentNullException(nameof(address));
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            
            // Parse address to strip any suffix for the raw command
            var pa = PlcAddress.Parse(address);
            var cleanAddress = pa.BaseAddress;

            // Use RDS for both single and multiple to be safe/consistent, or RD for single.
            // Standard Upper Link: 
            // RD <dev> -> returns value
            // RDS <dev> <count> -> returns values
            string cmd;
            if (count == 1)
                cmd = $"RD {cleanAddress}";
            else
                cmd = $"RDS {cleanAddress} {count}";

            var resp = await SendCommandAsync(cmd, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(resp)) return null;

            // Response check:
            // "E0", "E1" etc on error.
            if (resp.StartsWith("E") && resp.Length < 10) // Simple heuristic for error code
                throw new InvalidOperationException($"PLC Error: {resp}");

            // Note: Upper Link RD/RDS usually returns just values directly.
            // e.g. "12345" or "10 20 30"
            // Some modes might prefix "OK".
            // We'll strip "OK" if present.
            var valueStr = resp;
            if (valueStr.StartsWith("OK")) valueStr = valueStr.Substring(2).Trim();

            var parts = valueStr.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            var values = new List<ushort>(count);
            for (int i = 0; i < parts.Length && values.Count < count; i++)
            {
                // Parse as long to handle potential signed output (e.g. "-1") or large unsigned
                if (long.TryParse(parts[i], out var v)) 
                    values.Add((ushort)(v & 0xFFFF));
                else 
                    values.Add(0);
            }
         

            return values.ToArray();
        }

        /// <summary>
        /// Writes words. Uses WR for single, WRS for multiple.
        /// </summary>
        public async Task WriteWordsAsync(string address, IReadOnlyList<ushort> values, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentNullException(nameof(address));
            if (values == null || values.Count == 0) throw new ArgumentNullException(nameof(values));

            var pa = PlcAddress.Parse(address);
            var cleanAddress = pa.BaseAddress;

            var sb = new StringBuilder();
            if (values.Count == 1)
            {
                sb.Append($"WR {cleanAddress} {values[0]}");
            }
            else
            {
                sb.Append($"WRS {cleanAddress} {values.Count}");
                foreach (var v in values) sb.Append(' ').Append(v);
            }

            var resp = await SendCommandAsync(sb.ToString(), cancellationToken).ConfigureAwait(false);
            // Write commands usually return "OK"
            if (resp?.Trim() != "OK") throw new InvalidOperationException($"Write failed: {resp}");
        }
        
        // Helper to interpret suffixes (Generic read)
        public async Task<string> ReadAnyAsync(string address, CancellationToken cancellationToken = default)
        {
            var pa = PlcAddress.Parse(address);
            
            // If no suffix, default to ushort (decimal string)
            if (pa.DataType == PlcDataType.None || pa.DataType == PlcDataType.U)
            {
                var val = await ReadWordsAsync(pa.Raw, 1, cancellationToken).ConfigureAwait(false);
                return val != null && val.Length > 0 ? val[0].ToString() : "0";
            }
            
            if (pa.DataType == PlcDataType.S)
            {
                var val = await ReadWordsAsync(pa.Raw, 1, cancellationToken).ConfigureAwait(false);
                if (val == null || val.Length == 0) return "0";
                return ((short)val[0]).ToString(); // Signed 16-bit
            }

            if (pa.DataType == PlcDataType.H)
            {
                var val = await ReadWordsAsync(pa.Raw, 1, cancellationToken).ConfigureAwait(false);
                if (val == null || val.Length == 0) return "0000";
                return val[0].ToString("X4"); // Hex 16-bit
            }

            if (pa.DataType == PlcDataType.D || pa.DataType == PlcDataType.L)
            {
                // 32-bit integer
                var i = await ReadInt32Async(pa.Raw, cancellationToken).ConfigureAwait(false);
                return i.ToString();
            }

            throw new NotSupportedException($"Suffix {pa.DataType} not supported for generic read yet.");
        }

        public async Task<int> ReadInt32Async(string address, CancellationToken cancellationToken = default)
        {
            var pa = PlcAddress.Parse(address);
            // Read 2 words
            var words = await ReadWordsAsync(pa.BaseAddress, 2, cancellationToken).ConfigureAwait(false);
            if (words == null || words.Length < 2) throw new InvalidOperationException("Insufficient data for Int32");

            uint low, high;
            if (WordsOrder == WordOrder.LowHigh)
            {
                low = words[0]; high = words[1];
            }
            else
            {
                low = words[1]; high = words[0];
            }

            uint combined = (high << 16) | low;
            return unchecked((int)combined);
        }

        public async Task WriteInt32Async(string address, int value, CancellationToken cancellationToken = default)
        {
            // Parse for base address only
            var pa = PlcAddress.Parse(address); 
            uint u = unchecked((uint)value);
            ushort low = (ushort)(u & 0xFFFF);
            ushort high = (ushort)(u >> 16);
            ushort w1 = WordsOrder == WordOrder.LowHigh ? low : high;
            ushort w2 = WordsOrder == WordOrder.LowHigh ? high : low;
            await WriteWordsAsync(pa.BaseAddress, new[] { w1, w2 }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<float> ReadFloatAsync(string address, CancellationToken cancellationToken = default)
        {
            var pa = PlcAddress.Parse(address);
            var words = await ReadWordsAsync(pa.BaseAddress, 2, cancellationToken).ConfigureAwait(false);
            if (words == null || words.Length < 2) throw new InvalidOperationException("Insufficient data for Float");

            uint low, high;
            if (WordsOrder == WordOrder.LowHigh)
            {
                low = words[0]; high = words[1];
            }
            else
            {
                low = words[1]; high = words[0];
            }

            uint combined = (high << 16) | low;
            var bytes = BitConverter.GetBytes(combined);
            // Should verify endianness here if needed, but assuming system endianness matches (usually Little Endian)
            return BitConverter.ToSingle(bytes, 0);
        }

        public async Task WriteFloatAsync(string address, float value, CancellationToken cancellationToken = default)
        {
             var pa = PlcAddress.Parse(address);
             var bytes = BitConverter.GetBytes(value);
             uint u = BitConverter.ToUInt32(bytes, 0);
             ushort low = (ushort)(u & 0xFFFF);
             ushort high = (ushort)(u >> 16);
             ushort w1 = WordsOrder == WordOrder.LowHigh ? low : high;
             ushort w2 = WordsOrder == WordOrder.LowHigh ? high : low;
             await WriteWordsAsync(pa.BaseAddress, new[] { w1, w2 }, cancellationToken).ConfigureAwait(false);
        }

        // ASCII String support via Words
        public async Task<string?> ReadStringAsync(string address, int length, CancellationToken cancellationToken = default)
        {
            var pa = PlcAddress.Parse(address);
            // Calculate number of words needed for length bytes
            // 1 word = 2 bytes
            int wordCount = (length + 1) / 2;
            var words = await ReadWordsAsync(pa.BaseAddress, wordCount, cancellationToken).ConfigureAwait(false);
            if (words == null) return null;

            // Decode
            var bytes = new List<byte>();
            foreach(var w in words)
            {
                // Keyence Host Link / Upper Link often uses High Byte for 1st Char, Low Byte for 2nd Char.
                // (Big Endian per word for strings)
                // Example: "HE" -> 0x4845 (H=0x48, E=0x45)
                // We receive 0x4845. 
                // High Byte = 0x48 ('H'), Low Byte = 0x45 ('E').
                
                byte b1 = (byte)((w >> 8) & 0xFF); // High Byte (1st char)
                byte b2 = (byte)(w & 0xFF);        // Low Byte (2nd char)
                
                bytes.Add(b1);
                bytes.Add(b2);
            }

            // Trim nulls or take exact length
            var str = _encoding.GetString(bytes.ToArray(), 0, Math.Min(bytes.Count, length));
            // Remove trailing nulls if any
            int nullIdx = str.IndexOf('\0');
            if (nullIdx >= 0) str = str.Substring(0, nullIdx);
            return str;
        }

        public async Task WriteStringAsync(string address, string text, CancellationToken cancellationToken = default)
        {
             if (text == null) text = string.Empty;
             var bytes = _encoding.GetBytes(text);
             // Pad to even length if needed (optional, but word alignment is good)
             // Convert to words
             var words = new List<ushort>();
             for(int i = 0; i < bytes.Length; i+=2)
             {
                 byte b1 = bytes[i]; // 1st Char
                 byte b2 = (i + 1 < bytes.Length) ? bytes[i+1] : (byte)0; // 2nd Char
                 
                 // Pack: High Byte = b1, Low Byte = b2
                 // w = (b1 << 8) | b2
                 ushort w = (ushort)((b1 << 8) | b2);
                 words.Add(w);
             }
             if (words.Count == 0) words.Add(0);

             var pa = PlcAddress.Parse(address);
             await WriteWordsAsync(pa.BaseAddress, words, cancellationToken).ConfigureAwait(false);
        }
        
        public Task<string?> ReadAsciiAsync(string address, int length, CancellationToken cancellationToken = default)
            => ReadStringAsync(address, length, cancellationToken);
        
        public Task WriteAsciiAsync(string address, string text, CancellationToken cancellationToken = default)
            => WriteStringAsync(address, text, cancellationToken);

        async Task<string?> SendCommandAsync(string command, CancellationToken cancellationToken)
        {
            if (_stream == null) throw new InvalidOperationException("Not connected");

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(OperationTimeout);

                // Ensure terminator is CR only (\r) for Keyence Upper Link usually.
                // Or try \r\n if \r fails. Standard is \r using terminal.
                // The user says "RD ... not work". 
                // Let's use \r.
                var data = _encoding.GetBytes(command + "\r");
                
                // Clear buffer? No, simple request/response.
                
                await _stream.WriteAsync(data, 0, data.Length, cts.Token).ConfigureAwait(false);

                var response = await ReadResponseAsync(_stream, cts.Token).ConfigureAwait(false);
                return response;
            }
            finally
            {
                _lock.Release();
            }
        }

        static async Task<string?> ReadResponseAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var ms = new MemoryStream();
            var buffer = new byte[256];
            // Read until \r or \n
            while (true)
            {
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    if (ms.Length == 0) return null;
                    break;
                }
                
                // Scan for delimiter
                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] == 0x0D || buffer[i] == 0x0A) // CR or LF
                    {
                        ms.Write(buffer, 0, i);
                        // Consume any subsequent LF if we hit CR?
                        // This is tricky without buffering next reads. 
                        // But usually response ends there.
                        return Encoding.ASCII.GetString(ms.ToArray());
                    }
                }
                ms.Write(buffer, 0, read);
            }
            return Encoding.ASCII.GetString(ms.ToArray());
        }

        public void Dispose()
        {
            Disconnect();
            _lock.Dispose();
        }
    }
}
