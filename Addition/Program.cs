using TupleClient;

// Given the numbers 1...numCount this example will sum them all together.
const int numCount = 1000;

// For this example, we are going to create a tuple space with a unique name, starting addition
var spaceName = "addition" + Guid.NewGuid();

// Connect to the tuple space server.
// When running on different computers, don't use a local address!
using var client = new TupleSpaceClient("127.0.0.1", 8080, spaceName);

// We write all the numbers we wish to add along with some meta-data and tokens to the tuple space.
await SetupData(client, numCount);


// Now send the code in RemoteCode.PerformAddition to taskCount different machines.  
// This is done by creating taskCount tuples in an expressions tuplespace.  The contents
// of the tuple is the code in RemoteCode.PerformAddition 
Console.WriteLine("Adding");
var taskCount = 10;
var tasks = new Task[taskCount - 1];
for (var i = 0; i < taskCount - 1; i++)
    tasks[i] = client.EvalAsync(RemoteCode.PerformAddition, RemoteCode.TupleDefinitions);
await Task.WhenAll(tasks);


// When the final addition has completed,  the result is written to the tuple (total, *)
Console.WriteLine("Waiting for result...");
var finalResult = await client.InAsync<TotalTuple>("total", "*");
Console.WriteLine($"Final Result: {finalResult.Total}");

async Task SetupData(TupleSpaceClient tupleSpaceClient, int size)
{
    using var _ = new BulkOutScope(tupleSpaceClient);

    // The numbers to add
    for (var i = 1; i <= size; i++)
        await tupleSpaceClient.OutAsync(new ValueTuple { Label = "value", Value = i, Count = 1 });

    // Each pair of numbers needs a work token
    for (var i = 0; i < size - 1; i++)
        await tupleSpaceClient.OutAsync(new WorkTuple { Label = "work", Content = "work" });

    await tupleSpaceClient.OutAsync(new SizeTuple { Label = "size", Size = size });
}

[TupleDefinition]
public struct ValueTuple
{
    public string Label { get; set; }
    public int Value { get; set; }
    public int Count { get; set; }
}

[TupleDefinition]
public struct WorkTuple
{
    public string Label { get; set; }
    public string Content { get; set; }
}

[TupleDefinition]
public struct SizeTuple
{
    public string Label { get; set; }
    public int Size { get; set; }
}

[TupleDefinition]
public struct TotalTuple
{
    public string Label { get; set; }
    public int Total { get; set; }
}

public static class AdditionLogic
{
    [RemoteEval]
    public static async Task PerformAddition(TupleSpaceClient c)
    {
        var target = await c.RdAsync<SizeTuple>("size", "*");
        var finalCount = target.Size;

        while (true)
        {
            var token = await c.InpAsync<WorkTuple>("work", "work");
            if (token is null) break;
            
            var lhs = await c.InAsync<ValueTuple>("value", "*", "*");
            var rhs = await c.InAsync<ValueTuple>("value", "*", "*");
            
            var total = lhs.Value + rhs.Value;
            var count = lhs.Count + rhs.Count;
            
            if (count == finalCount)
                await c.OutAsync(new TotalTuple { Label = "total", Total = total });
            else
                await c.OutAsync(new ValueTuple { Label = "value", Value = total, Count = count });
        }
    }
}