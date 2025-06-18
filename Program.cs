using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

class Program
{
    static async Task Main()
    {
        var listener = new TcpListener(IPAddress.Any, 9000);
        listener.Start();
        Console.WriteLine("MCP server listening on port 9000");

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = Task.Run(() => HandleClient(client));
        }
    }

    static async Task HandleClient(TcpClient client)
    {
        using var stream = client.GetStream();
        var buffer = new byte[512];
        int read = await stream.ReadAsync(buffer);
        if (read == 0) return;

        string command = Encoding.UTF8.GetString(buffer, 0, read).Trim();
        Console.WriteLine($"Received: {command}");

        string reply = await ProcessCommand(command);

        var data = Encoding.UTF8.GetBytes(reply + "\n");
        await stream.WriteAsync(data);

        client.Close();
    }

    static async Task<string> ProcessCommand(string cmd)
    {
        if (!cmd.StartsWith("route ")) return "ERROR: Unknown command";

        var parts = cmd.Split(' ');
        if (parts.Length != 3)
            return "ERROR: Use: route <from> <to>";

        string from = parts[1], to = parts[2];
        string url = $"https://api.irail.be/connections/?from={from}&to={to}&format=json";

        try
        {
            using var http = new HttpClient();
            var json = await http.GetStringAsync(url);
            return ParseTrainConnections(json);
        }
        catch
        {
            return "ERROR: Failed to reach SNCB API";
        }
    }

    static string ParseTrainConnections(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("connection", out var conns))
            return "No connections found.";

        var results = conns.EnumerateArray().Take(3).Select(conn =>
        {
            var dep = conn.GetProperty("departure");
            var arr = conn.GetProperty("arrival");

            long d = long.Parse(dep.GetProperty("time").GetString());
            long a = long.Parse(arr.GetProperty("time").GetString());

            var dt = DateTimeOffset.FromUnixTimeSeconds(d).ToLocalTime();
            var at = DateTimeOffset.FromUnixTimeSeconds(a).ToLocalTime();

            string vehicle = dep.GetProperty("vehicle").GetString() ?? "Train";
            string trainType = vehicle.Split('.').Last();

            return $"🚆 {trainType} dep {dt:HH:mm} arr {at:HH:mm}";
        });

        return string.Join("\n", results);
    }
}
