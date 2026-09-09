using TupleClient;

const int numCount = 1000;
var spaceName = "addition" + Guid.NewGuid();
using var client = new TupleSpaceClient("127.0.0.1", 8080, spaceName);

// 1. Put numbers into the space
for (var i = 1; i <= numCount; i++)
{
    await client.OutAsync("value", i.ToString());    
}

// 2. Put work tokens into the space. 
// To sum N numbers, we need exactly N-1 additions.
for (var i = 0; i < numCount - 1; i++)
{
    await client.OutAsync("work", "work");
}

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

// 4. Wait for the result (this is a rubbish solution!)
Console.WriteLine("Waiting for result...");
await Task.Delay(5000); // Give time for all additions to complete

var finalResult = await client.InpAsync("value", "*");
Console.WriteLine($"Final Result: {finalResult?[1]}");

public static class AdditionLogic
{
    [RemoteEval]
    public static async Task PerformAddition(TupleSpaceClient c)
    {
        while (true)
        {
            var token = await c.InpAsync("work", "work");
            if (token is null) break;
            var lhs = await c.InAsync("value", "*");
            var rhs = await c.InAsync("value", "*");
            var total = int.Parse(lhs[1]) + int.Parse(rhs[1]);
            await c.OutAsync("value", total.ToString());
        }
    }
}
