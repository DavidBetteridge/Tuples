using TupleClient;

namespace TupleServer.Tests;

[TestFixture]
public class RandomnessTests
{
    private CancellationTokenSource _cts;
    private Task _serverTask;
    private const int Port = 8082;

    [SetUp]
    public void Setup()
    {
        _cts = new CancellationTokenSource();
        var server = new TcpServer(Port, new ServerStatistics());
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
    public async Task TestTupleRandomness_ChiSquared()
    {
        using var client = new TupleSpaceClient("127.0.0.1", Port, "test-random");

        const int numCategories = 10;
        const int numTrials = 1000;

        // Insert tuples that all match the same pattern
        for (int i = 0; i < numCategories; i++)
        {
            await client.OutAsync("item", i.ToString());
        }

        var counts = new int[numCategories];

        for (int i = 0; i < numTrials; i++)
        {
            // We use RdAsync so we don't remove them, 
            // but RdAsync should also be randomized in search start.
            // Wait, my implementation of RdAsync uses GetAsync(..., remove: false).
            // Actually, RdAsync in TupleSpaceClient calls 'rd' command.
            // Let's check how 'rd' is handled in TcpServer.
            
            var result = await client.RdAsync("item", "*");
            int val = int.Parse(result[1]);
            counts[val]++;
        }

        // Chi-Squared Test
        // H0: The tuples are picked uniformly at random.
        // Expected frequency for each category = numTrials / numCategories
        double expected = (double)numTrials / numCategories;
        double chiSquared = 0;

        for (int i = 0; i < numCategories; i++)
        {
            double diff = counts[i] - expected;
            chiSquared += (diff * diff) / expected;
        }

        // Degrees of freedom = numCategories - 1
        int df = numCategories - 1;
        
        // Use a significance level of 0.05
        // We can use MathNet.Numerics for the distribution if available, 
        // otherwise we can hardcode the critical value for df=9, alpha=0.05.
        // Critical value for df=9, alpha=0.05 is 16.919
        
        Console.WriteLine($"Chi-Squared Statistic: {chiSquared}");
        for(int i=0; i<numCategories; i++)
        {
            Console.WriteLine($"Category {i}: {counts[i]}");
        }

        Assert.That(chiSquared, Is.LessThan(16.919), $"Chi-squared statistic {chiSquared} exceeds critical value 16.919");
    }
}
