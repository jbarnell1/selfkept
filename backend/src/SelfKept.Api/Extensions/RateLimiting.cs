using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SelfKept.Core.Auth;
using Serilog;

namespace SelfKept.Api.Extensions;

/// <summary>The rate-limit policy names the controllers and endpoints refer to.</summary>
public static class RateLimitPolicies
{
  /// <summary>Sign-in and token endpoints: strict and per IP, against brute force.</summary>
  public const string Auth = "auth";

  /// <summary>Every other API call: generous and per user.</summary>
  public const string Api = "api";

  /// <summary>Sized for phones flushing an outbox in pages.</summary>
  public const string Ingest = "ingest";

  /// <summary>An on-demand interpret run reads 28 days for every metric.</summary>
  public const string Recompute = "recompute";

  /// <summary>The clinician summary reads 90 days of five metrics and every panel.</summary>
  public const string Pdf = "pdf";

  /// <summary>An assistant may call several tools for one question.</summary>
  public const string Mcp = "mcp";

  /// <summary>Dynamic client registration is anonymous by protocol: per IP and small.</summary>
  public const string OAuthRegister = "oauth-register";
}

/// <summary>Registers the policies in <see cref="RateLimitPolicies"/> and the 429 problem reply.</summary>
public static class RateLimitingExtensions
{
  public static IServiceCollection AddAppRateLimiting(this IServiceCollection services) =>
    services.AddRateLimiter(options =>
    {
      options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

      options.AddPolicy(RateLimitPolicies.Auth, context => PerIpFixed(context, "auth", 10, TimeSpan.FromMinutes(1)));
      // The general policy keeps its original partition names ("user:", "anon:").
      options.AddPolicy(RateLimitPolicies.Api, context => PerUserSliding(context, user: "user", anonymous: "anon", permit: 300, anonymousPermit: 60));
      options.AddPolicy(RateLimitPolicies.Ingest, context => PerUserSliding(context, "ingest", "ingest-anon", 60));
      options.AddPolicy(RateLimitPolicies.Recompute, context => PerUserFixed(context, "recompute", 5));
      options.AddPolicy(RateLimitPolicies.Pdf, context => PerUserFixed(context, "pdf", 10));
      options.AddPolicy(RateLimitPolicies.Mcp, context => PerUserSliding(context, "mcp", "mcp-anon", 60));
      options.AddPolicy(RateLimitPolicies.OAuthRegister, context => PerIpFixed(context, "oauth-register", 10, TimeSpan.FromHours(1)));

      options.OnRejected = async (context, cancellationToken) =>
      {
        Log.Warning("Rate limit exceeded for {IP} on {Path}", ClientIp(context.HttpContext), context.HttpContext.Request.Path);
        var extensions = new Dictionary<string, object?>();
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
          context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
          extensions["retryAfterSeconds"] = (int)retryAfter.TotalSeconds;
        }
        await Results.Problem(
          statusCode: StatusCodes.Status429TooManyRequests,
          title: "Too many requests",
          detail: "Too many requests. Please try again later.",
          extensions: extensions).ExecuteAsync(context.HttpContext);
      };
    });

  public static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

  private static string? UserId(HttpContext context) => context.User?.FindFirst(TokenClaims.Subject)?.Value;

  private static RateLimitPartition<string> PerIpFixed(HttpContext context, string name, int permit, TimeSpan window) =>
    RateLimitPartition.GetFixedWindowLimiter($"{name}:{ClientIp(context)}", _ => new FixedWindowRateLimiterOptions
    {
      PermitLimit = permit,
      Window = window,
      QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
      QueueLimit = 0,
    });

  /// <summary>Per user once signed in, per IP before; one minute.</summary>
  private static RateLimitPartition<string> PerUserFixed(HttpContext context, string name, int permit)
  {
    var userId = UserId(context);
    return RateLimitPartition.GetFixedWindowLimiter(
      string.IsNullOrEmpty(userId) ? $"{name}-anon:{ClientIp(context)}" : $"{name}:{userId}",
      _ => new FixedWindowRateLimiterOptions
      {
        PermitLimit = permit,
        Window = TimeSpan.FromMinutes(1),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0,
      });
  }

  /// <summary>Per user once signed in, per IP before; a one-minute window in four segments.</summary>
  private static RateLimitPartition<string> PerUserSliding(HttpContext context, string user, string anonymous, int permit, int? anonymousPermit = null)
  {
    var userId = UserId(context);
    var signedIn = !string.IsNullOrEmpty(userId);
    return RateLimitPartition.GetSlidingWindowLimiter(
      signedIn ? $"{user}:{userId}" : $"{anonymous}:{ClientIp(context)}",
      _ => new SlidingWindowRateLimiterOptions
      {
        PermitLimit = signedIn ? permit : anonymousPermit ?? permit,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 4,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0,
      });
  }
}
