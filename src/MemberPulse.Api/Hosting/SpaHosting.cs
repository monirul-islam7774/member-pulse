namespace MemberPulse.Api.Hosting;

/// <summary>Serves the built web app (wwwroot) from the same origin as the API.</summary>
public static class SpaHosting
{
    /// <summary>
    /// Path prefixes that belong to the server and must never be answered with the web app's index.html.
    /// Matched case-insensitively by the route constraint below.
    /// </summary>
    private const string ServerPaths = "api|health|openapi";

    /// <summary>
    /// Serves index.html for / and the files in wwwroot. Call this BEFORE authentication (the app requires a login for
    /// anything unmatched, which would otherwise lock anonymous visitors out of the page that contains the login form)
    /// and BEFORE <c>UseRouting()</c> (static files are skipped for requests that already matched an endpoint, and the
    /// SPA fallback matches almost everything).
    /// </summary>
    public static WebApplication UseSpaStaticFiles(this WebApplication app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        return app;
    }

    /// <summary>
    /// Answers every other GET that is not an API, health or OpenAPI path and has no file extension with index.html (and answers 404 for missing files),
    /// so refreshing or deep-linking a client-side route works. Does nothing visible when no UI has been built.
    /// </summary>
    public static WebApplication MapSpaFallback(this WebApplication app)
    {
        app.MapFallback("{**path:regex(^(?!(?:" + ServerPaths + ")(?:/|$)).*$)}", (HttpContext context, IWebHostEnvironment env) =>
            {
                // A path that looks like a file (/missing.js) is a missing file, not a client route.
                if (!HttpMethods.IsGet(context.Request.Method) || Path.HasExtension(context.Request.Path.Value))
                {
                    return Results.NotFound();
                }

                var index = env.WebRootFileProvider.GetFileInfo("index.html");
                return index.Exists ? Results.File(index.CreateReadStream(), "text/html") : Results.NotFound();
            })
            .AllowAnonymous();

        return app;
    }
}
