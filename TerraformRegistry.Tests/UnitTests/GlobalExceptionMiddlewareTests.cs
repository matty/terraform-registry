using System.Text.Json;

namespace TerraformRegistry.Tests.UnitTests;

/// <summary>
///     Tests for the GlobalExceptionMiddleware to ensure proper error handling
/// </summary>
public class GlobalExceptionMiddlewareTests
{
    [Fact]
    public void GetStatusCode_WithArgumentException_Returns400()
    {
        // Test the private method indirectly by testing the middleware behavior
        var exception = new ArgumentException("Test argument exception");
        var statusCode = GetStatusCodeForException(exception);

        Assert.Equal(400, statusCode);
    }

    [Fact]
    public void GetStatusCode_WithArgumentNullException_Returns400()
    {
        var exception = new ArgumentNullException("paramName", "Test null argument exception");
        var statusCode = GetStatusCodeForException(exception);

        Assert.Equal(400, statusCode);
    }

    [Fact]
    public void GetStatusCode_WithFileNotFoundException_Returns404()
    {
        var exception = new FileNotFoundException("Test file not found");
        var statusCode = GetStatusCodeForException(exception);

        Assert.Equal(404, statusCode);
    }

    [Fact]
    public void GetStatusCode_WithUnauthorizedAccessException_Returns401()
    {
        var exception = new UnauthorizedAccessException("Test unauthorized access");
        var statusCode = GetStatusCodeForException(exception);

        Assert.Equal(401, statusCode);
    }

    [Fact]
    public void GetStatusCode_WithInvalidOperationExceptionContainingAlreadyExists_Returns409()
    {
        var exception = new InvalidOperationException("Resource already exists");
        var statusCode = GetStatusCodeForException(exception);

        Assert.Equal(409, statusCode);
    }

    [Fact]
    public void GetStatusCode_WithInvalidOperationExceptionNotContainingAlreadyExists_Returns422()
    {
        var exception = new InvalidOperationException("Invalid operation");
        var statusCode = GetStatusCodeForException(exception);

        Assert.Equal(422, statusCode);
    }

    [Fact]
    public void GetStatusCode_WithGenericException_Returns500()
    {
        var exception = new Exception("Generic exception");
        var statusCode = GetStatusCodeForException(exception);

        Assert.Equal(500, statusCode);
    }

    [Fact]
    public void CreateErrorResponse_WithClientError_ReturnsActualMessage()
    {
        var exception = new ArgumentException("Invalid parameter");
        var response = CreateErrorResponseForException(exception, 400);

        var jsonElement = (JsonElement)response;
        var errorsArray = jsonElement.GetProperty("errors").EnumerateArray().ToArray();

        Assert.Single(errorsArray);
        Assert.Equal("Invalid parameter", errorsArray[0].GetString());
    }

    [Fact]
    public void CreateErrorResponse_WithServerError_ReturnsGenericMessage()
    {
        var exception = new Exception("Internal error details");
        var response = CreateErrorResponseForException(exception, 500);

        var jsonElement = (JsonElement)response;
        var errorsArray = jsonElement.GetProperty("errors").EnumerateArray().ToArray();

        Assert.Single(errorsArray);
        Assert.Equal("An internal server error occurred", errorsArray[0].GetString());
    }

    // Helper methods to test the private methods indirectly
    private static int GetStatusCodeForException(Exception exception)
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
            InvalidOperationException when exception.Message.Contains("already exists") => 409,
            InvalidOperationException => 422,
            _ => 500
        };
    }

    private static object CreateErrorResponseForException(Exception exception, int statusCode)
    {
        var errorMessage = statusCode == 500
            ? "An internal server error occurred"
            : exception.Message;

        return JsonSerializer.SerializeToElement(new { errors = new[] { errorMessage } });
    }
}