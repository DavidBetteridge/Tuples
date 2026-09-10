namespace TupleClient;

public class BulkOutScope : IDisposable
{
    private readonly TupleSpaceClient _client;
    private readonly List<string[]> _tuples = new();
    private bool _disposed;

    public BulkOutScope(TupleSpaceClient client)
    {
        _client = client;
        _client.EnterBulkOutScope(this);
    }

    public void AddTuple(string[] tuple)
    {
        _tuples.Add(tuple);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client.ExitBulkOutScope(this);
            if (_tuples.Count > 0)
            {
                _client.SendBulkOutAsync(_tuples).GetAwaiter().GetResult();
            }
            _disposed = true;
        }
    }
}
