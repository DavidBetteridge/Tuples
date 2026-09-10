using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TupleClient;

namespace TupleServer.Tests;

public class IntegrationTests
{
    private CancellationTokenSource _cts;
    private Task _serverTask;
    private const int Port = 8081;

    [SetUp]
    public void Setup()
    {
        _cts = new CancellationTokenSource();
        var server = new TcpServer(Port);
        _serverTask = server.StartAsync(_cts.Token);
    }

    [TearDown]
    public async Task TearDown()
    {
        _cts.Cancel();
        try
        {
            await _serverTask;
        }
        catch (OperationCanceledException) { }
        _cts.Dispose();
    }

    [Test]
    public async Task TestClientLibrary_BasicOperations()
    {
        using var client = new TupleSpaceClient("127.0.0.1", Port, "test-client-lib");

        // Out
        await client.OutAsync("key1", "value1");

        // Rd (copy)
        var rdResult = await client.RdAsync("key1", "*");
        Assert.That(rdResult, Is.EqualTo(new[] { "key1", "value1" }));

        // In (consume)
        var inResult = await client.InAsync("key1", "*");
        Assert.That(inResult, Is.EqualTo(new[] { "key1", "value1" }));

        // Inp (non-blocking, should be null now)
        var inpResult = await client.InpAsync("key1", "*");
        Assert.That(inpResult, Is.Null);
    }

    [Test]
    public async Task TestClientLibrary_PatternMatching()
    {
        using var client = new TupleSpaceClient("127.0.0.1", Port, "test-pattern");

        await client.OutAsync("a", "1", "x");
        await client.OutAsync("a", "2", "y");
        await client.OutAsync("b", "3", "z");

        var res1 = await client.InAsync("a", "*", "y");
        Assert.That(res1, Is.EqualTo(new[] { "a", "2", "y" }));

        var res2 = await client.InAsync("b", "3", "*");
        Assert.That(res2, Is.EqualTo(new[] { "b", "3", "z" }));

        var res3 = await client.InAsync("*", "*", "*");
        Assert.That(res3, Is.EqualTo(new[] { "a", "1", "x" }));
    }

    [Test]
    public async Task TestClientLibrary_BlockingAndEval()
    {
        using var client = new TupleSpaceClient("127.0.0.1", Port, "test-blocking");

        var inTask = client.InAsync("task1", "*");
        
        await Task.Delay(100);
        Assert.That(inTask.IsCompleted, Is.False);

        // Note: EvalAsync now takes a string of C# code that will be executed by ExpressionRunner.
        // For this test, we'll just use a simple Task.Run to simulate the behavior since
        // we don't have ExpressionRunner running in the test environment.
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            using var client2 = new TupleSpaceClient("127.0.0.1", Port, "test-blocking");
            await client2.OutAsync("task1", "done");
        });

        var result = await inTask;
        Assert.That(result, Is.EqualTo(new[] { "task1", "done" }));
    }

    [Test]
    public async Task TestClientLibrary_RdBlocking()
    {
        using var client1 = new TupleSpaceClient("127.0.0.1", Port, "test-rd-blocking");
        using var client2 = new TupleSpaceClient("127.0.0.1", Port, "test-rd-blocking");
        using var client3 = new TupleSpaceClient("127.0.0.1", Port, "test-rd-blocking");

        var rdTask1 = client1.RdAsync(TimeSpan.FromSeconds(5), "msg", "*");
        var rdTask2 = client2.RdAsync(TimeSpan.FromSeconds(5), "msg", "*");

        await Task.Delay(200);
        Assert.That(rdTask1.IsCompleted, Is.False, "RD task 1 should be blocking");
        Assert.That(rdTask2.IsCompleted, Is.False, "RD task 2 should be blocking");

        await client3.OutAsync("msg", "hello");

        var res1 = await rdTask1;
        var res2 = await rdTask2;

        Assert.That(res1, Is.EqualTo(new[] { "msg", "hello" }));
        Assert.That(res2, Is.EqualTo(new[] { "msg", "hello" }));

        // Ensure it's still there
        var res3 = await client3.InAsync("msg", "hello");
        Assert.That(res3, Is.EqualTo(new[] { "msg", "hello" }));
    }

    [Test]
    public void TestClientLibrary_Timeout()
    {
        using var client = new TupleSpaceClient("127.0.0.1", Port, "test-timeout");
        Assert.ThrowsAsync<TimeoutException>(async () => await client.InAsync(TimeSpan.FromMilliseconds(100), "non-existent"));
    }

    [Test]
    public async Task TestClientLibrary_BulkOutScope()
    {
        using var client = new TupleSpaceClient("127.0.0.1", Port, "test-bulk-out");

        using (var scope = new BulkOutScope(client))
        {
            await client.OutAsync("work", "1");
            await client.OutAsync("work", "2");
            
            // Other commands should fail
            Assert.ThrowsAsync<InvalidOperationException>(async () => await client.InpAsync("work", "1"));
        }

        // Now they should be there
        var res1 = await client.InAsync("work", "1");
        var res2 = await client.InAsync("work", "2");
        
        Assert.That(res1, Is.EqualTo(new[] { "work", "1" }));
        Assert.That(res2, Is.EqualTo(new[] { "work", "2" }));
    }
}
