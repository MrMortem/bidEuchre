using BidEuchre.App;
using BidEuchre.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System.Net;

foreach (var (peer, host, expected) in new (string, string, int)[]
{
    ("127.0.0.1", "localhost:5050", 200),
    ("::1", "[::1]:5050", 200),
    ("::ffff:127.0.0.1", "127.0.0.1:5050", 200),
    ("192.0.2.1", "localhost:5050", 403),
    ("127.0.0.1", "localhost.evil.example:5050", 403)
})
{
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
    context.Request.Scheme = "http";
    context.Request.Host = new HostString(host);
    context.Request.Headers.Origin = $"http://{host}";
    context.Response.Body = new MemoryStream();
    var reachedEndpoint = false;
    await LocalRequestPolicy.InvokeAsync(context, _ => { reachedEndpoint = true; return Task.CompletedTask; });
    if (context.Response.StatusCode != expected || reachedEndpoint != (expected == 200))
        throw new Exception($"Unexpected access for peer {peer} and host {host}.");
}
Console.WriteLine("PASS  Local access policy accepts IPv4, IPv6 and mapped loopback and rejects remote peers and hosts");

var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["WebSessions:IdleTimeoutSeconds"] = "1",
    ["WebSessions:MaxSessions"] = "1"
}).Build();
await using var sessions = new WebSessions(new EngineCatalog(), configuration);
using var reaper = new WebSessionReaper(sessions);
var request = new CreateSessionRequest("Idle bots", Enumerable.Range(0, 4)
    .Select(i => new CreateSeatRequest($"Bot {i}", PlayerKind.Bot, null)).ToArray(), 21);
var session = await sessions.CreateAsync(request);
await session.StartAsync(waitForDeal: true);
await reaper.StartAsync(CancellationToken.None);
try
{
    // No registry Get/List/Create calls: only the background service can release this session.
    var deadline = DateTime.UtcNow.AddSeconds(5);
    var disposed = false;
    while (DateTime.UtcNow < deadline)
    {
        await Task.Delay(100);
        try { await session.GetViewAsync(null); }
        catch (ObjectDisposedException) { disposed = true; break; }
    }
    if (!disposed) throw new Exception("Idle bot session was not disposed by the background reaper.");
    var replacement = await sessions.CreateAsync(request);
    if (replacement.Id == session.Id) throw new Exception("Expired table was reused.");
    Console.WriteLine("PASS  Background idle expiry disposes a started bot table and releases its capacity");
}
finally { await reaper.StopAsync(CancellationToken.None); }
