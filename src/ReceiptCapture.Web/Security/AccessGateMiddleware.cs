namespace ReceiptCapture.Web.Security;

/// <summary>
/// Refuses API calls from a browser that has not passed <see cref="AccessGate"/>.
/// </summary>
/// <remarks>
/// Only <c>/api</c> is gated. The static front end carries no receipts and no
/// credentials, so serving it to an unadmitted browser costs nothing and lets it
/// render the prompt for the code.
/// </remarks>
public sealed class AccessGateMiddleware
{
    /// <summary>Endpoints reachable before admission: the sign-in call and its status.</summary>
    private static readonly string[] OpenPaths = ["/api/session", "/api/session/status", "/api/health"];

    private readonly RequestDelegate _next;

    public AccessGateMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AccessGate gate)
    {
        var path = context.Request.Path;

        if (!path.StartsWithSegments("/api")
            || OpenPaths.Any(open => path.Equals(open, StringComparison.OrdinalIgnoreCase))
            || gate.IsAdmitted(context.Request))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response
            .WriteAsJsonAsync(new { error = "access_code_required" })
            .ConfigureAwait(false);
    }
}
