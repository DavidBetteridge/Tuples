# Linda Tuple Space for .NET

A distributed coordination system based on the Linda coordination language, featuring remote execution via Roslyn scripting and C# Source Generators.

## Architecture

The system consists of four main components:

1.  **TupleServer**: A TCP-based server that manages multiple named "tuple spaces". It supports atomic coordination primitives like `IN` (consume), `OUT` (produce), and `RD` (read).
2.  **TupleClient**: A library providing the `TupleSpaceClient` for interacting with the server.
3.  **ExpressionRunner**: A distributed worker that polls an `expressions` tuple space for C# code strings to execute against specific tuple spaces.
4.  **TupleClient.Generators**: A C# Source Generator that allows developers to write type-safe code for remote execution.

## Core Operations (Linda Primitives)

The `TupleSpaceClient` provides the following operations:

| Operation | Description |
| :--- | :--- |
| `OutAsync(tuple)` | **Produce**: Adds a tuple to the space. |
| `InAsync(pattern)` | **Consume**: Blocks until a tuple matching the pattern is available, then removes and returns it. |
| `RdAsync(pattern)` | **Read**: Blocks until a tuple matching the pattern is available, then returns it without removing it. |
| `InpAsync(pattern)` | **Probe Consume**: Non-blocking version of `IN`. Returns null if no match is found immediately. |
| `RdpAsync(pattern)` | **Probe Read**: Non-blocking version of `RD`. Returns null if no match is found immediately. |

*Patterns support wildcards using the `"*"` string.*

## Remote Execution (`EvalAsync`)

The system supports a powerful `EvalAsync` mechanism that allows code to be shipped to a remote `ExpressionRunner` for execution close to the data.

### 1. Define Remote Logic
Mark a static method with the `[RemoteEval]` attribute. The method must take a `TupleSpaceClient` as its first argument (usually named `c`).

```csharp
public static class MyLogic
{
    [RemoteEval]
    public static async Task PerformWork(TupleSpaceClient c)
    {
        var data = await c.InAsync("data", "*");
        // ... process data ...
        await c.OutAsync("result", "processed");
    }
}
```

### 2. Invoke Remotely
The Source Generator automatically extracts the method body as a string and puts it in a `RemoteCode` class. Use `EvalAsync` to send this code to the runner:

```csharp
await client.EvalAsync(RemoteCode.PerformWork);
```

## Getting Started

### 1. Start the Server
```bash
dotnet run --project TupleServer/TupleServer.csproj
```

### 2. Start the Expression Runner
You can start multiple runners to handle parallel workloads.
```bash
dotnet run --project ExpressionRunner/Program.cs
```

### 3. Run the Addition Example
The `Addition` project demonstrates summing numbers 1 to 1000 by distributing addition tasks across the runners.
```bash
dotnet run --project Addition/Program.cs
```

## Project Structure

- `TupleServer/`: The core TCP server.
- `TupleClient/`: The shared library and `TupleSpaceClient`.
- `TupleClient.Generators/`: The Roslyn Source Generator.
- `ExpressionRunner/`: The worker process using `Microsoft.CodeAnalysis.CSharp.Scripting`.
- `Addition/`: An example application utilizing all components.
- `TupleServer.Tests/`: Integration tests for the core protocol.
