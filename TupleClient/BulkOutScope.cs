namespace TupleClient;

public class BulkOutScope : IDisposable
{
    private readonly TupleSpaceClient _client;
    private readonly List<string[]> _tuples = [];
    private readonly Lock _lock = new();
    private bool _disposed;

    public BulkOutScope(TupleSpaceClient client)
    {
        _client = client;
        _client.EnterBulkOutScope(this);
    }

    public void AddTuple(string[] tuple)
    {
        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BulkOutScope));
            _tuples.Add(tuple);
        }
    }

    public void Dispose()
    {
        List<string[]>? tuplesToSend = null;
        
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            
            if (_tuples.Count > 0)
                tuplesToSend = [.. _tuples];
        }
        
        // Send bulk data while still in scope, then exit
        // This ensures no other operations can slip in before data is sent
        if (tuplesToSend != null)
            _client.SendBulkOutAsync(tuplesToSend).GetAwaiter().GetResult();
        
        _client.ExitBulkOutScope(this);
    }
}
