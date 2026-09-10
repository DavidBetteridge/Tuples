using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TupleServer;

public class TcpServer
{
    private readonly int _port;
    private readonly TupleSpaceManager _manager;

    public TcpServer(int port)
    {
        _port = port;
        _manager = new TupleSpaceManager();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        TcpListener listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        Console.WriteLine($"Server started on port {_port}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
        using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
        {
            var writerLock = new SemaphoreSlim(1, 1);
            try
            {
                var tasks = new List<Task>();
                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    string? line = await reader.ReadLineAsync(cancellationToken);
                    if (line == null) break;

                    var command = JsonSerializer.Deserialize<Command>(line);
                    if (command == null) continue;

                    var task = ProcessCommandAsync(command, writer, writerLock, cancellationToken);
                    tasks.Add(task);
                    
                    // Clean up completed tasks
                    tasks.RemoveAll(t => t.IsCompleted);
                }
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {ex.Message}");
            }
        }
    }

    private async Task ProcessCommandAsync(Command command, StreamWriter writer, SemaphoreSlim writerLock, CancellationToken cancellationToken)
    {
        Console.WriteLine("Process command " + command);
        var space = _manager.GetOrCreateSpace(command.SpaceName);
        object? response = null;

        switch (command.Type.ToUpperInvariant())
        {
            case "CREATE":
                response = new { Status = "OK" };
                break;

            case "OUT":
                if (command.Tuple != null)
                {
                    space.Add(command.Tuple);
                    response = new { Status = "OK" };
                }
                break;

            case "OUTBULK":
                if (command.Tuples != null)
                {
                    space.AddBulk(command.Tuples);
                    response = new { Status = "OK" };
                }
                break;

            case "IN":
                var tuple = await space.GetAsync(command.Tuple, true, cancellationToken);
                response = new { Status = "OK", Tuple = tuple };
                break;

            case "RD":
                var rdTuple = await space.GetAsync(command.Tuple, false, cancellationToken);
                response = new { Status = "OK", Tuple = rdTuple };
                break;

            case "INP":
                var inpTuple = space.TryGet(command.Tuple, true);
                if (inpTuple != null)
                    response = new { Status = "OK", Tuple = inpTuple };
                else
                    response = new { Status = "Error", Message = "No matching tuple found" };
                break;

            case "RDP":
                var rdpTuple = space.TryGet(command.Tuple, false);
                if (rdpTuple != null)
                    response = new { Status = "OK", Tuple = rdpTuple };
                else
                    response = new { Status = "Error", Message = "No matching tuple found" };
                break;

            case "ISEMPTY":
                bool isEmpty = space.IsEmpty();
                response = new { Status = "OK", IsEmpty = isEmpty };
                break;

            default:
                response = new { Status = "Error", Message = "Unknown command" };
                break;
        }

        if (response != null)
        {
            await writerLock.WaitAsync(cancellationToken);
            try
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            finally
            {
                writerLock.Release();
            }
        }
    }

    public class Command
    {
        public string Type { get; set; } = "";
        public string SpaceName { get; set; } = "";
        public string[]? Tuple { get; set; }
        public List<string[]>? Tuples { get; set; }
    }
}
