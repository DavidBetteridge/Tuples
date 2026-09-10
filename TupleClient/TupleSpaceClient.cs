using System.Net.Sockets;
using System.Text;

namespace TupleClient;

public class TupleSpaceClient : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _spaceName;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Lock _scopeLock = new();
    private BulkOutScope? _currentBulkScope;

    public TupleSpaceClient(string host, int port, string spaceName)
    {
        _host = host;
        _port = port;
        _spaceName = spaceName;
    }

    internal void EnterBulkOutScope(BulkOutScope scope)
    {
        lock (_scopeLock)
        {
            if (_currentBulkScope != null)
                throw new InvalidOperationException("Already in a bulk out scope");
            _currentBulkScope = scope;
        }
    }

    internal void ExitBulkOutScope(BulkOutScope scope)
    {
        lock (_scopeLock)
        {
            if (_currentBulkScope == scope)
                _currentBulkScope = null;
        }
    }

    internal async Task SendBulkOutAsync(List<string[]> tuples)
    {
        await _semaphore.WaitAsync();
        try
        {
            await EnsureConnectedAsync();
            var command = new { Type = "OUTBULK", SpaceName = _spaceName, Tuples = tuples };
            await _writer!.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(command));
            var line = await _reader!.ReadLineAsync();
            if (line == null) throw new Exception("Disconnected from server");
            var response = System.Text.Json.JsonSerializer.Deserialize<Response>(line) ?? throw new Exception("Invalid response");
            if (response.Status != "OK") throw new Exception(response.Message);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (_client == null || !_client.Connected)
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port, cancellationToken);
            _stream = _client.GetStream();
            _reader = new StreamReader(_stream, Encoding.UTF8);
            _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };

            // Create/Ensure space exists
            var createCommand = new { Type = "CREATE", SpaceName = _spaceName, Tuple = (string[]?)null };
            await _writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(createCommand));
            var createLine = await _reader.ReadLineAsync(cancellationToken);
            if (createLine == null) throw new Exception("Disconnected from server during CREATE");
        }
    }

    private async Task<Response> SendCommandAsync(string type, string[]? tuple, CancellationToken cancellationToken = default)
    {
        if (_currentBulkScope != null) throw new InvalidOperationException($"Cannot perform {type} operation while in a bulk out scope");
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync(cancellationToken);

            var command = new { Type = type, SpaceName = _spaceName, Tuple = tuple };
            await _writer!.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(command));
            
            var line = await _reader!.ReadLineAsync(cancellationToken);
            if (line == null) throw new Exception("Disconnected from server");
            return System.Text.Json.JsonSerializer.Deserialize<Response>(line) ?? throw new Exception("Invalid response");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<string[]> OutAsync(params string[] tuple)
    {
        if (_currentBulkScope != null)
        {
            _currentBulkScope.AddTuple(tuple);
            return tuple;
        }
        var response = await SendCommandAsync("OUT", tuple);
        if (response.Status != "OK") throw new Exception(response.Message);
        return tuple;
    }

    public async Task<string[]> InAsync(params string[] pattern)
    {
        return await InAsync(Timeout.InfiniteTimeSpan, pattern);
    }

    public async Task<string[]> InAsync(TimeSpan timeout, params string[] pattern)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var response = await SendCommandAsync("IN", pattern, cts.Token);
            if (response.Status != "OK") throw new Exception(response.Message);
            return response.Tuple ?? throw new Exception("No tuple returned");
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"InAsync timed out after {timeout}");
        }
    }

    public async Task<string[]> RdAsync(params string[] pattern)
    {
        return await RdAsync(Timeout.InfiniteTimeSpan, pattern);
    }

    public async Task<string[]> RdAsync(TimeSpan timeout, params string[] pattern)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var response = await SendCommandAsync("RD", pattern, cts.Token);
            if (response.Status != "OK") throw new Exception(response.Message);
            return response.Tuple ?? throw new Exception("No tuple returned");
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"RdAsync timed out after {timeout}");
        }
    }

    public async Task<string[]?> InpAsync(params string[] pattern)
    {
        var response = await SendCommandAsync("INP", pattern);
        if (response.Status == "OK") return response.Tuple;
        return null;
    }

    public async Task<string[]?> RdpAsync(params string[] pattern)
    {
        var response = await SendCommandAsync("RDP", pattern);
        if (response.Status == "OK") return response.Tuple;
        return null;
    }

    public string[] Out(params string[] tuple)
    {
        return OutAsync(tuple).GetAwaiter().GetResult();
    }

    public string[] In(params string[] pattern)
    {
        return InAsync(pattern).GetAwaiter().GetResult();
    }

    public string[] Rd(params string[] pattern)
    {
        return RdAsync(pattern).GetAwaiter().GetResult();
    }

    public string[]? Inp(params string[] pattern)
    {
        return InpAsync(pattern).GetAwaiter().GetResult();
    }

    public string[]? Rdp(params string[] pattern)
    {
        return RdpAsync(pattern).GetAwaiter().GetResult();
    }

    public async Task EvalAsync(string code)
    {
        using var expressionClient = new TupleSpaceClient(_host, _port, "expressions");
        await expressionClient.OutAsync("expression", _spaceName, code);
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _stream?.Dispose();
        _client?.Dispose();
        _semaphore.Dispose();
    }

    private class Response
    {
        public string Status { get; set; } = "";
        public string? Message { get; set; }
        public string[]? Tuple { get; set; }
    }
}
