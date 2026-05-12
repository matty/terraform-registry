namespace TerraformRegistry.Middleware;

/// <summary>
///     Extensions for creating consistent error responses that follow the Terraform Registry API format
/// </summary>
public static class ErrorResponseExtensions
{
    /// <summary>
    ///     Creates a Terraform Registry compliant error response
    /// </summary>
    /// <param name="statusCode">HTTP status code</param>
    /// <param name="message">Error message</param>
    /// <returns>JSON result with errors array</returns>
    public static IResult TerraformError(int statusCode, string message)
    {
        return Results.Json(new { errors = new[] { message } }, statusCode: statusCode);
    }

    /// <summary>
    ///     Creates a Terraform Registry compliant error response with multiple messages
    /// </summary>
    /// <param name="statusCode">HTTP status code</param>
    /// <param name="messages">Error messages</param>
    /// <returns>JSON result with errors array</returns>
    public static IResult TerraformError(int statusCode, params string[] messages)
    {
        return Results.Json(new { errors = messages }, statusCode: statusCode);
    }

    /// <summary>
    ///     Creates a BadRequest (400) error response
    /// </summary>
    /// <param name="message">Error message</param>
    /// <returns>JSON result with 400 status code</returns>
    public static IResult BadRequest(string message)
    {
        return TerraformError(400, message);
    }

    /// <summary>
    ///     Creates an Unauthorized (401) error response
    /// </summary>
    /// <param name="message">Error message</param>
    /// <returns>JSON result with 401 status code</returns>
    public static IResult Unauthorized(string message)
    {
        return TerraformError(401, message);
    }

    /// <summary>
    ///     Creates a NotFound (404) error response
    /// </summary>
    /// <param name="message">Error message</param>
    /// <returns>JSON result with 404 status code</returns>
    public static IResult NotFound(string message)
    {
        return TerraformError(404, message);
    }

    /// <summary>
    ///     Creates a Conflict (409) error response
    /// </summary>
    /// <param name="message">Error message</param>
    /// <returns>JSON result with 409 status code</returns>
    public static IResult Conflict(string message)
    {
        return TerraformError(409, message);
    }

    /// <summary>
    ///     Creates an UnprocessableEntity (422) error response
    /// </summary>
    /// <param name="message">Error message</param>
    /// <returns>JSON result with 422 status code</returns>
    public static IResult UnprocessableEntity(string message)
    {
        return TerraformError(422, message);
    }

    /// <summary>
    ///     Creates an InternalServerError (500) error response
    /// </summary>
    /// <param name="message">Error message</param>
    /// <returns>JSON result with 500 status code</returns>
    public static IResult InternalServerError(string message)
    {
        return TerraformError(500, message);
    }
}
