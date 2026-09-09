using System.Net.Sockets;
using System.Text;
using System.Linq.Expressions;
using Serialize.Linq.Serializers;

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

    public TupleSpaceClient(string host, int port, string spaceName)
    {
        _host = host;
        _port = port;
        _spaceName = spaceName;
    }

    private async Task<Response> SendCommandAsync(string type, string[]? tuple, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
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

            var command = new { Type = type, SpaceName = _spaceName, Tuple = tuple };
            await _writer!.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(command));
            
            // Release semaphore as soon as the command is sent if we want to support concurrent commands.
            // But we need to read the specific response for this command.
            // The current protocol doesn't have IDs, so responses come in order.
            // If we have multiple concurrent commands, we'd need IDs to match responses.
            // For now, let's keep the semaphore held while waiting for the response, 
            // but the test should use different client instances or we need to fix the deadlock.
            
            var line = await _reader!.ReadLineAsync(cancellationToken);
            if (line == null) throw new Exception("Disconnected from server");
            return System.Text.Json.JsonSerializer.Deserialize<Response>(line) ?? throw new Exception("Invalid response");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task OutAsync(params string[] tuple)
    {
        var response = await SendCommandAsync("ADD", tuple);
        if (response.Status != "OK") throw new Exception(response.Message);
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
            var response = await SendCommandAsync("GET", pattern, cts.Token);
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

    public void Out(params string[] tuple)
    {
        OutAsync(tuple).GetAwaiter().GetResult();
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

    public async Task EvalAsync(Expression<Func<TupleSpaceClient, Task>> actionExpression)
    {
        var serializer = new ExpressionSerializer(new Serialize.Linq.Serializers.JsonSerializer());
        var serializedExpression = serializer.SerializeText(actionExpression);
        
        using var expressionClient = new TupleSpaceClient(_host, _port, "expressions");
        await expressionClient.OutAsync("expression", _spaceName, serializedExpression);
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
