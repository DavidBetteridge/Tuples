using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using TupleClient;

Console.WriteLine("Expression Runner started...");

using var expressionClient = new TupleSpaceClient("127.0.0.1", 8080, "expressions");

// Set up script options with necessary references and imports
var scriptOptions = ScriptOptions.Default
    .AddReferences(typeof(TupleSpaceClient).Assembly)
    .AddImports("System", "System.Threading.Tasks", "TupleClient");

while (true)
{
    try
    {
        // Tuple format: ["expression", spaceName, code]
        var tuple = await expressionClient.InAsync("expression", "*", "*");
        var spaceName = tuple[1];
        var code = tuple[2];

        Console.WriteLine($"Received code for space: {spaceName}");

        _ = Task.Run(async () =>
        {
            using var client = new TupleSpaceClient("127.0.0.1", 8080, spaceName);
            try
            {
                Console.WriteLine($"Starting execution for space: {spaceName}");
                
                // Create a script that has access to the client via a global variable 'c'
                var script = CSharpScript.Create<Task>(
                    code,
                    scriptOptions,
                    globalsType: typeof(ScriptGlobals));
                
                var compiled = script.Compile();
                if (compiled.Length > 0)
                {
                    Console.WriteLine($"Compilation errors for space {spaceName}:");
                    foreach (var diagnostic in compiled)
                    {
                        Console.WriteLine($"  {diagnostic}");
                    }
                    return;
                }
                
                var globals = new ScriptGlobals { c = client };
                var result = await script.RunAsync(globals);
                
                // If the script returns a Task, await it
                if (result.ReturnValue is Task task)
                {
                    await task;
                }
                
                Console.WriteLine($"Completed execution for space: {spaceName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error executing code for space {spaceName}: {ex}");
            }
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in Expression Runner loop: {ex.Message}");
        await Task.Delay(1000);
    }
}

public class ScriptGlobals
{
    public TupleSpaceClient c { get; set; } = null!;
}
