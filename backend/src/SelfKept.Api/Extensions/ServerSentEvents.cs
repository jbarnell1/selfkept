using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;

namespace SelfKept.Api.Extensions;

/// <summary>
/// The text/event-stream frames the assistant routes answer with: "event: name", "data: one line
/// of JSON", a blank line, each flushed at once so a proxy does not hold it back.
/// </summary>
public static class ServerSentEvents
{
  public static void Start(HttpContext context)
  {
    var response = context.Response;
    response.StatusCode = StatusCodes.Status200OK;
    response.ContentType = "text/event-stream; charset=utf-8";
    response.Headers.CacheControl = "no-cache, no-store";
    response.Headers["X-Accel-Buffering"] = "no";
    context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
  }

  public static async Task WriteAsync(HttpResponse response, string name, object data, JsonSerializerOptions json, CancellationToken ct)
  {
    var payload = JsonSerializer.Serialize(data, data.GetType(), json);
    var frame = Encoding.UTF8.GetBytes($"event: {name}\ndata: {payload}\n\n");
    await response.Body.WriteAsync(frame, ct);
    await response.Body.FlushAsync(ct);
  }
}
