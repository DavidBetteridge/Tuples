using System.Collections.Concurrent;

namespace TupleServer;

public class TupleSpace
{
    private readonly List<string[]> _tuples = new();
    private readonly Lock _lock = new();
    private readonly List<Waiter> _waitingGetters = new();

    private record Waiter(string[]? Pattern, TaskCompletionSource<string[]> Tcs, bool Remove);

    public int Count
    {
        get
        {
            lock (_lock) return _tuples.Count;
        }
    }

    public int WaiterCount
    {
        get
        {
            lock (_lock) return _waitingGetters.Count;
        }
    }

    public void Add(string[] tuple)
    {
        var toNotify = new List<Waiter>();
        lock (_lock)
        {
            var alreadyMatchedGet = false;
            for (var i = 0; i < _waitingGetters.Count; )
            {
                var waiter = _waitingGetters[i];
                if (Matches(tuple, waiter.Pattern))
                {
                    if (waiter.Remove)
                    {
                        if (!alreadyMatchedGet)
                        {
                            toNotify.Add(waiter);
                            _waitingGetters.RemoveAt(i);
                            alreadyMatchedGet = true;
                        }
                        else
                        {
                            i++;
                        }
                    }
                    else
                    {
                        toNotify.Add(waiter);
                        _waitingGetters.RemoveAt(i);
                    }
                }
                else
                {
                    i++;
                }
            }

            if (!alreadyMatchedGet)
            {
                _tuples.Add(tuple);
            }
        }

        foreach (var waiter in toNotify)
        {
            waiter.Tcs.TrySetResult(tuple);
        }
    }

    public async Task<string[]> GetAsync(string[]? pattern, bool remove, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<string[]> tcs;
        Waiter waiter;
        lock (_lock)
        {
            for (var i = 0; i < _tuples.Count; i++)
            {
                if (Matches(_tuples[i], pattern))
                {
                    var tuple = _tuples[i];
                    if (remove)
                    {
                        _tuples.RemoveAt(i);
                    }
                    return tuple;
                }
            }

            tcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiter = new Waiter(pattern, tcs, remove);
            _waitingGetters.Add(waiter);
        }

        using var _ = cancellationToken.Register(() =>
        {
            lock (_lock)
            {
                _waitingGetters.Remove(waiter);
            }
            tcs.TrySetCanceled();
        });
        return await tcs.Task;
    }

    public string[]? TryGet(string[]? pattern, bool remove)
    {
        lock (_lock)
        {
            for (var i = 0; i < _tuples.Count; i++)
            {
                if (Matches(_tuples[i], pattern))
                {
                    var tuple = _tuples[i];
                    if (remove)
                    {
                        _tuples.RemoveAt(i);
                    }
                    return tuple;
                }
            }
            return null;
        }
    }

    private static bool Matches(string[] tuple, string[]? pattern)
    {
        if (pattern == null) return true;
        if (tuple.Length != pattern.Length) return false;
        for (var i = 0; i < tuple.Length; i++)
        {
            if (pattern[i] != "*" && tuple[i] != pattern[i])
            {
                return false;
            }
        }
        return true;
    }

    public bool IsEmpty()
    {
        lock (_lock)
        {
            return _tuples.Count == 0;
        }
    }

    public void AddBulk(List<string[]> tuples)
    {
        foreach (var tuple in tuples)
        {
            Add(tuple);
        }
    }
}

public class TupleSpaceManager
{
    private readonly ConcurrentDictionary<string, TupleSpace> _spaces = new();

    public TupleSpace GetOrCreateSpace(string name)
    {
        return _spaces.GetOrAdd(name, _ => new TupleSpace());
    }

    public IEnumerable<(string Name, TupleSpace Space)> GetActiveSpaces()
    {
        return _spaces.Select(kv => (kv.Key, kv.Value));
    }

    public int SpaceCount => _spaces.Count;
}
