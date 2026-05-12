using System.Text.Json;
using TerraformRegistry.API.Logging;

namespace TerraformRegistry.Middleware;

/// <summary>
///     Global exception handling middleware that catches unhandled exceptions and returns consistent error responses
/// </summary>
public class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly RequestDelegate _next;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            RegistryLog.Error(_logger, ex, "An unhandled exception occurred while processing request {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.StatusCode = GetStatusCode(exception);
        context.Response.ContentType = "application/json";

        var response = CreateErrorResponse(exception, context.Response.StatusCode);

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private static int GetStatusCode(Exception exception)
    {
        return exception switch
        {
            ArgumentNullException => 400,
            ArgumentException => 400,
            UnauthorizedAccessException => 401,
            FileNotFoundException => 404,
            DirectoryNotFoundException => 404,
            NotSupportedException => 405,
            TimeoutException => 408,
            InvalidOperationException when exception.Message.Contains("already exists", StringComparison.Ordinal) => 409,
            InvalidOperationException => 422,
            _ => 500
        };
    }

    private static object CreateErrorResponse(Exception exception, int statusCode)
    {
        // Follow Terraform Registry API error format
        var errorMessage = statusCode == 500
            ? "An internal server error occurred"
            : exception.Message;

        return new
        {
            errors = new[] { errorMessage }
        };
    }
}
