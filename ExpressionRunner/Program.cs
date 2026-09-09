using System.Linq.Expressions;
using Serialize.Linq.Serializers;
using TupleClient;

Console.WriteLine("Expression Runner started...");

using var expressionClient = new TupleSpaceClient("127.0.0.1", 8080, "expressions");
var serializer = new ExpressionSerializer(new Serialize.Linq.Serializers.JsonSerializer());

while (true)
{
    try
    {
        // Tuple format: ["expression", spaceName, serializedContent]
        var tuple = await expressionClient.InAsync("expression", "*", "*");
        var spaceName = tuple[1];
        var serializedContent = tuple[2];

        Console.WriteLine($"Received expression for space: {spaceName}");

        if (serializer.DeserializeText(serializedContent) is not Expression<Func<TupleSpaceClient, Task>> deserializedExpression)
        {
            Console.WriteLine("Failed to deserialize expression");
            continue;
        }

        var action = deserializedExpression.Compile();

        _ = Task.Run(async () =>
        {
            using var client = new TupleSpaceClient("127.0.0.1", 8080, spaceName);
            try
            {
                Console.WriteLine($"Starting execution of expression for space: {spaceName}");
                await action(client);
                Console.WriteLine($"Completed expression for space: {spaceName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing expression for space {spaceName}: {ex}");
            }
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in Expression Runner loop: {ex.Message}");
        await Task.Delay(1000);
    }
}
