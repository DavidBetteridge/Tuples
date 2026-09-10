using TupleClient;

const int numCount = 1000;
var spaceName = "addition" + Guid.NewGuid();
using var client = new TupleSpaceClient("127.0.0.1", 8080, spaceName);

// 1. Put numbers into the space
await SetupData(client, numCount);

Console.WriteLine("Adding");

// 3. Submit work to the expression runner using the generated code string
var taskCount = 10;
var tasks = new Task[taskCount - 1];
for (var i = 0; i < taskCount - 1; i++)
{
    // This comes from AdditionLogic.PerformAddition below
    tasks[i] = client.EvalAsync(RemoteCode.PerformAddition);
}

await Task.WhenAll(tasks);

// 4. Wait for the result
Console.WriteLine("Waiting for result...");
var finalResult = await client.InAsync("total", "*");
Console.WriteLine($"Final Result: {finalResult?[1]}");

async Task SetupData(TupleSpaceClient tupleSpaceClient, int size)
{
    using var _ = new BulkOutScope(tupleSpaceClient);

    // The numbers to add
    for (var i = 1; i <= size; i++)
        await tupleSpaceClient.OutAsync("value", i.ToString(), "1");

    // Each pair of numbers needs a work token
    for (var i = 0; i < size - 1; i++)
        await tupleSpaceClient.OutAsync("work", "work");

    await tupleSpaceClient.OutAsync("size", size.ToString());
}

public static class AdditionLogic
{
    [RemoteEval]
    public static async Task PerformAddition(TupleSpaceClient c)
    {
        var target = await c.RdAsync("size", "*");
        var finalCount = int.Parse(target[1]);

        while (true)
        {
            var token = await c.InpAsync("work", "work");
            if (token is null) break;
            var lhs = await c.InAsync("value", "*", "*");
            var rhs = await c.InAsync("value", "*", "*");
            var total = int.Parse(lhs[1]) + int.Parse(rhs[1]);
            var count = int.Parse(lhs[2]) + int.Parse(rhs[2]);
            if (count == finalCount)
                await c.OutAsync("total", total.ToString());
            else
                await c.OutAsync("value", total.ToString(), count.ToString());
        }
    }
}