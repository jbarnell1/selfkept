using SelfKept.Core.Exceptions;

namespace SelfKept.Api.Middleware;

/// <summary>
/// Turns exceptions into RFC 7807 problem details. Expected domain failures map to 4xx with their
/// message; anything else is a 500 whose detail is only included in Development.
/// </summary>
public sealed class ErrorHandlingMiddleware
{
  private readonly RequestDelegate _next;
  private readonly ILogger<ErrorHandlingMiddleware> _logger;
  private readonly IHostEnvironment _environment;

  public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger, IHostEnvironment environment)
  {
    _next = next;
    _logger = logger;
    _environment = environment;
  }

  public async Task InvokeAsync(HttpContext context)
  {
    try
    {
      await _next(context);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
      // The client went away; there is nobody to answer.
      _logger.LogDebug("Request aborted by the client. TraceId: {TraceId}", context.TraceIdentifier);
    }
    catch (Exception ex)
    {
      await HandleExceptionAsync(context, ex);
    }
  }

  private async Task HandleExceptionAsync(HttpContext context, Exception exception)
  {
    if (context.Response.HasStarted)
    {
      _logger.LogWarning(exception, "Response already started; cannot write a problem body. TraceId: {TraceId}", context.TraceIdentifier);
      return;
    }

    var (status, title, detail) = exception switch
    {
      RegistrationClosedException e => (StatusCodes.Status403Forbidden, "Registration closed", e.Message),
      ForbiddenException e => (StatusCodes.Status403Forbidden, "Forbidden", e.Message),
      // Thrown by the base controller when a token has no usable subject: a 403, not a 401, so the
      // client does not mistake a permission problem for an expired session.
      UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden", "You do not have permission to perform this action."),
      DomainValidationException e => (StatusCodes.Status400BadRequest, "Validation failed", e.Message),
      BadHttpRequestException e => (e.StatusCode, "Bad request", e.Message),
      ConflictException e => (StatusCodes.Status409Conflict, "Conflict", e.Message),
      KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found", "The resource does not exist."),
      _ => (StatusCodes.Status500InternalServerError, "Internal server error",
        _environment.IsDevelopment() ? exception.ToString() : "An internal error occurred. Please try again later."),
    };

    if (status >= 500)
    {
      _logger.LogError(exception, "Unhandled exception on {Method} {Path}. TraceId: {TraceId}", context.Request.Method, context.Request.Path.Value, context.TraceIdentifier);
    }
    else
    {
      _logger.LogInformation("{Status} {Title}: {Detail}. TraceId: {TraceId}", status, title, detail, context.TraceIdentifier);
    }

    context.Response.Clear();
    await Results.Problem(
      statusCode: status,
      title: title,
      detail: detail,
      instance: context.Request.Path,
      extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier })
      .ExecuteAsync(context);
  }
}
