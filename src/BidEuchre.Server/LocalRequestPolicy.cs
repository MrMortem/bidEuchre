using System.Net;

namespace BidEuchre.Server;

// The browser client is a local hot-seat interface, not a network multiplayer service.
public static class LocalRequestPolicy
{
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        var peer = context.Connection.RemoteIpAddress;
        if (peer is null || !IPAddress.IsLoopback(peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer)
            || !IsLoopbackHost(request.Host.Host))
        {
            await RejectAsync(context, 403, "The web interface is available only on this computer.");
            return;
        }

        var origin = request.Headers.Origin;
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if ((fetchSite.Length > 0 && fetchSite is not ("same-origin" or "none"))
            || (origin.Count > 0 && (origin.Count != 1 || !IsSameOrigin(origin[0], request))))
        {
            await RejectAsync(context, 403, "Requests must come from this web interface's origin.");
            return;
        }

        if (request.Path.StartsWithSegments("/api")
            && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method) && !request.HasJsonContentType())
        {
            await RejectAsync(context, 415, "API changes require application/json.");
            return;
        }

        await next(context);
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    private static bool IsSameOrigin(string? origin, HttpRequest request)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0
            || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return false;

        return Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var target)
            && uri.Scheme == target.Scheme && uri.IdnHost == target.IdnHost && uri.Port == target.Port;
    }

    private static Task RejectAsync(HttpContext context, int status, string error)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error });
    }
}
