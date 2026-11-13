using System.Text.Json.Serialization;
using BidEuchre.Core;
using BidEuchre.Protocol;
using BidEuchre.App;
using BidEuchre.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<EngineCatalog>();
builder.Services.AddSingleton<WebSessions>();
builder.Services.AddHostedService<WebSessionReaper>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var status = exception is SessionCapacityException ? StatusCodes.Status429TooManyRequests
        : exception is KeyNotFoundException ? StatusCodes.Status404NotFound
        : exception is GameRuleException or ProtocolException or ArgumentException
            ? StatusCodes.Status400BadRequest
            : StatusCodes.Status500InternalServerError;
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new
    {
        error = status == 500 ? "The server could not complete the request." : exception?.Message
    });
}));

app.Use(LocalRequestPolicy.InvokeAsync);

app.UseDefaultFiles();
app.UseStaticFiles();
// Linked shared art is copied beside the server for both dotnet run and publish.
var pixelRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot", "pixel");
if (Directory.Exists(pixelRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(pixelRoot),
        RequestPath = "/pixel"
    });
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", product = "Bid Euchre" }));

app.MapGet("/api/engines", (EngineCatalog catalog) => Results.Ok(catalog.List()));

app.MapPost("/api/engines/load", () => Results.Json(
    new { error = "External engines can only be loaded in the desktop application." }, statusCode: 403));

app.MapDelete("/api/engines/{id}", (string id, EngineCatalog catalog) =>
    catalog.Remove(id) ? Results.NoContent() : Results.NotFound(new { error = "Engine was not found or is built in." }));

app.MapGet("/api/sessions", (WebSessions sessions) => Results.Ok(sessions.List()));

app.MapPost("/api/sessions", async (CreateSessionRequest request, WebSessions sessions) =>
{
    var session = await sessions.CreateAsync(request);
    return Results.Created($"/api/sessions/{session.Id}", session.Summary());
});

app.MapGet("/api/sessions/{id}", async (
    string id,
    int? seat,
    WebSessions sessions,
    CancellationToken cancellationToken) =>
{
    var session = sessions.Get(id);
    if (seat is not null && (seat is < 0 or > 3 || session.Seats[seat.Value].Kind is not PlayerKind.Human))
    {
        return Results.BadRequest(new { error = "Only a human-controlled seat may be used as the private viewer." });
    }

    var view = await session.GetViewAsync(seat, cancellationToken);
    return Results.Ok(new SessionState(session.Id, session.Name, session.Started, session.Seats, view, session.LastError, session.WaitingForDeal));
});

app.MapPost("/api/sessions/{id}/start", async (
    string id,
    bool? animateDeal,
    WebSessions sessions,
    CancellationToken cancellationToken) =>
{
    var session = sessions.Get(id);
    await session.StartAsync(cancellationToken, waitForDeal: animateDeal == true);
    return Results.Ok(session.Summary());
});

app.MapPost("/api/sessions/{id}/deal-complete", async (
    string id, int handNumber, WebSessions sessions, CancellationToken cancellationToken) =>
{
    await sessions.Get(id).CompleteDealAsync(handNumber, cancellationToken);
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/sessions/{id}/actions", async (
    string id,
    GameActionRequest request,
    WebSessions sessions,
    CancellationToken cancellationToken) =>
{
    var session = sessions.Get(id);
    await session.ExecuteAsync(request, cancellationToken);
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/sessions/{id}/next-hand", async (
    string id,
    bool? animateDeal,
    WebSessions sessions,
    CancellationToken cancellationToken) =>
{
    var session = sessions.Get(id);
    await session.StartNextHandAsync(cancellationToken, waitForDeal: animateDeal == true);
    return Results.Ok(session.Summary());
});

app.MapDelete("/api/sessions/{id}", async (string id, WebSessions sessions) =>
    await sessions.RemoveAsync(id) ? Results.NoContent() : Results.NotFound(new { error = "Session was not found." }));

app.MapGet("/api/rules", async (IWebHostEnvironment environment) =>
{
    var path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "bid-euchre-final-rules.md"));
    return Results.Text(await File.ReadAllTextAsync(path), "text/markdown");
});

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
