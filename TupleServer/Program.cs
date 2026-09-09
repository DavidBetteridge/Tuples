using TupleServer;

var cts = new CancellationTokenSource();
var server = new TcpServer(8080);

Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await server.StartAsync(cts.Token);
