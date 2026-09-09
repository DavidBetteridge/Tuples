using System.Collections.Concurrent;

namespace TupleServer;

public class TupleSpace
{
    private readonly List<string[]> _tuples = new();
    private readonly object _lock = new();
    private readonly List<Waiter> _waitingGetters = new();

    private record Waiter(string[]? Pattern, TaskCompletionSource<string[]> Tcs, bool Remove);

    public void Add(string[] tuple)
    {
        var toNotify = new List<Waiter>();
        lock (_lock)
        {
            var alreadyMatchedGet = false;
            for (int i = 0; i < _waitingGetters.Count; )
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
                            // We found a GET waiter. We can stop now because one tuple can only satisfy one GET.
                            // But wait, there might be earlier RD waiters that we should also satisfy?
                            // No, the loop goes from 0 to Count. We should satisfy ALL RD waiters that match
                            // and AT MOST one GET waiter.
                        }
                        else
                        {
                            // Already matched a GET waiter, this other GET waiter must keep waiting.
                            i++;
                        }
                    }
                    else
                    {
                        // RD waiter
                        toNotify.Add(waiter);
                        _waitingGetters.RemoveAt(i);
                    }
                }
                else
                {
                    i++;
                }
            }

            // If no GET waiter consumed it, add it to the space
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
        lock (_lock)
        {
            for (int i = 0; i < _tuples.Count; i++)
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
            _waitingGetters.Add(new Waiter(pattern, tcs, remove));
        }

        using (cancellationToken.Register(() => tcs.TrySetCanceled()))
        {
            return await tcs.Task;
        }
    }

    public string[]? TryGet(string[]? pattern, bool remove)
    {
        lock (_lock)
        {
            for (int i = 0; i < _tuples.Count; i++)
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
        for (int i = 0; i < tuple.Length; i++)
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
}

public class TupleSpaceManager
{
    private readonly ConcurrentDictionary<string, TupleSpace> _spaces = new();

    public TupleSpace GetOrCreateSpace(string name)
    {
        return _spaces.GetOrAdd(name, _ => new TupleSpace());
    }
}
