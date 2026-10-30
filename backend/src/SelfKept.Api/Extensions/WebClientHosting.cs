using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace SelfKept.Api.Extensions;

/// <summary>
/// Serves the Flutter web build from <c>wwwroot</c> so one container is both API and web app.
/// <c>/api/*</c> and <c>/swagger</c> stay with their own handlers; every other path that is not a
/// file returns <c>index.html</c>, so client-side routes survive a page refresh.
///
/// Files are revalidated on every load (<c>no-cache</c> plus the static-file ETag), so an upgrade
/// is picked up at once and a 304 costs almost nothing on a LAN.
/// </summary>
public static class WebClientHosting
{
  private const string ApiPrefix = "/api";

  /// <summary>
  /// The default provider refuses extensions it does not know, and a Flutter web build ships some:
  /// the Material ink shader (.frag), CanvasKit's symbol map (.symbols). A refused shader made the
  /// app throw on the first button tap.
  /// </summary>
  public static readonly FileExtensionContentTypeProvider ContentTypes = CreateContentTypes();

  private static FileExtensionContentTypeProvider CreateContentTypes()
  {
    var provider = new FileExtensionContentTypeProvider();
    provider.Mappings[".frag"] = "application/octet-stream";
    provider.Mappings[".symbols"] = "text/plain";
    provider.Mappings[".map"] = "application/json";
    return provider;
  }
  private const string SwaggerPrefix = "/swagger";

  public static IApplicationBuilder UseWebClient(this IApplicationBuilder app)
  {
    app.UseWhen(ctx => !IsReserved(ctx.Request.Path), branch =>
    {
      branch.Use(async (ctx, next) =>
      {
        ctx.Response.OnStarting(() =>
        {
          var h = ctx.Response.Headers;
          h["X-Content-Type-Options"] = "nosniff";
          h["X-Frame-Options"] = "SAMEORIGIN";
          h["Referrer-Policy"] = "strict-origin-when-cross-origin";
          h["X-Robots-Tag"] = "noindex, nofollow";
          return Task.CompletedTask;
        });
        await next();
      });
      branch.UseDefaultFiles();
      branch.UseStaticFiles(new StaticFileOptions
      {
        ContentTypeProvider = ContentTypes,
        OnPrepareResponse = ctx => ctx.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache",
      });
    });
    return app;
  }

  /// <summary>Endpoints: unknown /api paths are a 404 problem; anything else that is not a file is the app shell.</summary>
  public static void MapWebClientFallback(this IEndpointRouteBuilder endpoints)
  {
    endpoints.MapFallback(ApiPrefix + "/{**rest}", () =>
      Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "No such API endpoint."))
      .AllowAnonymous();
    endpoints.MapFallback(SwaggerPrefix + "/{**rest}", () =>
      Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "OpenAPI is not enabled on this server."))
      .AllowAnonymous();
    endpoints.MapFallbackToFile("index.html", new StaticFileOptions
    {
      OnPrepareResponse = ctx => ctx.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache",
    }).AllowAnonymous();
  }

  private static bool IsReserved(PathString path) =>
    path.StartsWithSegments(ApiPrefix) || path.StartsWithSegments(SwaggerPrefix);
}
