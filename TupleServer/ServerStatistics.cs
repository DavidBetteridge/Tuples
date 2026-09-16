namespace TupleServer;

public class ServerStatistics
{
    private long _totalOut;
    private long _totalIn;
    private long _totalRd;
    private long _totalInp;
    private long _totalRdp;
    private long _activeConnections;

    public long TotalOut => Interlocked.Read(ref _totalOut);
    public long TotalIn => Interlocked.Read(ref _totalIn);
    public long TotalRd => Interlocked.Read(ref _totalRd);
    public long TotalInp => Interlocked.Read(ref _totalInp);
    public long TotalRdp => Interlocked.Read(ref _totalRdp);
    public long ActiveConnections => Interlocked.Read(ref _activeConnections);

    public void IncrementOut(int count = 1) => Interlocked.Add(ref _totalOut, count);
    public void IncrementIn() => Interlocked.Increment(ref _totalIn);
    public void IncrementRd() => Interlocked.Increment(ref _totalRd);
    public void IncrementInp() => Interlocked.Increment(ref _totalInp);
    public void IncrementRdp() => Interlocked.Increment(ref _totalRdp);
    
    public void ConnectionStarted() => Interlocked.Increment(ref _activeConnections);
    public void ConnectionEnded() => Interlocked.Decrement(ref _activeConnections);
}
