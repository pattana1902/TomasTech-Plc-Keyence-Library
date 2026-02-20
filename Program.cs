using System;
using System.Text;
using System.Threading.Tasks;
using TomasTech_Plc_Keyence;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("Keyence PLC Test Console");
        Console.Write("Enter PLC IP (default 192.168.0.10): ");
        var ip = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(ip)) ip = "192.168.0.10";

        Console.Write("Enter Port (default 8501): ");
        var portStr = Console.ReadLine();
        if (!int.TryParse(portStr, out int port)) port = 8501;

        using var client = new KeyenceTcpClient(ip, port);
        
        try
        {
            Console.WriteLine($"Connecting to {ip}:{port}...");
            await client.ConnectAsync();
            Console.WriteLine("Connected!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connection failed: {ex.Message}");
            return;
        }

        while (true)
        {
            Console.WriteLine("\nChoose command:");
            Console.WriteLine("1. Read (RD/RDS - Generic e.g. DM100, DM100.U, DM100.H)");
            Console.WriteLine("2. Write Word (WR - e.g. DM100 123)");
            Console.WriteLine("3. Read String (e.g. DM100 10)");
            Console.WriteLine("4. Write String (e.g. DM100 HELLO)");
            Console.WriteLine("5. Exit");
            Console.Write("> ");
            
            var choice = Console.ReadLine();
            try
            {
                if (choice == "1")
                {
                    Console.Write("Address (e.g. DM100, DM100.H): ");
                    var addr = Console.ReadLine() ?? "";
                    var result = await client.ReadAnyAsync(addr);
                    Console.WriteLine($"Result: {result}");
                }
                else if (choice == "2")
                {
                    Console.Write("Address: ");
                    var addr = Console.ReadLine() ?? "";
                    Console.Write("Value: ");
                    var valStr = Console.ReadLine() ?? "0";
                    if (int.TryParse(valStr, out int val))
                    {
                        await client.WriteWordsAsync(addr, new ushort[] { (ushort)val });
                        Console.WriteLine("Write OK");
                    }
                    else
                    {
                        Console.WriteLine("Invalid value");
                    }
                }
                else if (choice == "3")
                {
                    Console.Write("Address: ");
                    var addr = Console.ReadLine() ?? "";
                    Console.Write("Length (bytes): ");
                    if (int.TryParse(Console.ReadLine(), out int len))
                    {
                        var str = await client.ReadStringAsync(addr, len);
                        Console.WriteLine($"String: '{str}'");
                    }
                }
                else if (choice == "4")
                {
                    Console.Write("Address: ");
                    var addr = Console.ReadLine() ?? "";
                    Console.Write("Text: ");
                    var text = Console.ReadLine() ?? "";
                    await client.WriteStringAsync(addr, text);
                    Console.WriteLine("Write String OK");
                }
                else if (choice == "5") break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
