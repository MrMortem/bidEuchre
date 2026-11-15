using System.Text.Json;

namespace BidEuchre.Server;

public static class CampaignEndpoints
{
    private const int MaximumBody = 16 * 1024;
    public static void MapCampaignEndpoints(this WebApplication app)
    {
        app.MapGet("/api/campaign", async (HttpContext context, WebCampaigns campaigns) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            RejectQuery(context);
            var token = context.Request.Cookies[campaigns.CookieName];
            if (!WebCampaigns.ValidToken(token))
            {
                token = WebCampaigns.NewToken();
                context.Response.Cookies.Append(campaigns.CookieName, token, new CookieOptions
                { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/api/campaign", Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromDays(365), IsEssential = true });
            }
            return Results.Ok(await campaigns.GetAsync(token!));
        });
        app.MapGet("/api/campaign/catalog", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store"; RejectQuery(context);
            return Results.Ok(new { features = CampaignProjection.Catalog() });
        });
        foreach (var action in new[] { "new", "continue", "start-match", "forfeit", "next-hand", "commands", "arrange-hand",
            "shop/buy", "shop/buy-use", "shop/pull", "shop/sell", "shop/reroll", "items/use", "items/discard", "result/continue", "victory/continue", "shop/leave" })
        {
            var operation = action;
            app.MapPost($"/api/campaign/{operation}", (HttpContext context, WebCampaigns campaigns) => Mutate(context, campaigns, operation));
        }
        app.MapDelete("/api/campaign", (HttpContext context, WebCampaigns campaigns) => Mutate(context, campaigns, "delete"));
    }
    private static void RejectQuery(HttpContext context)
    { if (context.Request.QueryString.HasValue) throw CampaignApiException.Invalid("Campaign requests do not accept query parameters."); }
    private static async Task<IResult> Mutate(HttpContext context, WebCampaigns campaigns, string action)
    {
        context.Response.Headers.CacheControl = "no-store"; RejectQuery(context);
        var token = context.Request.Cookies[campaigns.CookieName];
        if (!WebCampaigns.ValidToken(token)) throw new CampaignApiException(409, "profile_required", "Open campaign first to establish a browser profile.");
        if (context.Request.ContentLength > MaximumBody) throw CampaignApiException.Invalid("Campaign request is too large.");
        // Explicitly cap streaming requests too, before JSON materialization.
        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        while (true)
        {
            var count = await context.Request.Body.ReadAsync(bytes, context.RequestAborted);
            if (count == 0) break;
            if (buffer.Length + count > MaximumBody) throw CampaignApiException.Invalid("Campaign request is too large.");
            buffer.Write(bytes, 0, count);
        }
        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16, AllowDuplicateProperties = false });
            return Results.Ok(await campaigns.MutateAsync(token!, action, document.RootElement));
        }
        catch (JsonException) { throw CampaignApiException.Invalid("Malformed campaign JSON."); }
    }
}
