using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ReceiptCapture.Web.Configuration;

namespace ReceiptCapture.Web.Security;

/// <summary>
/// A single shared access code in front of the app.
/// </summary>
/// <remarks>
/// This is a gate, not an identity system: it answers "may this browser use this
/// deployment", not "who is this". That is the right shape for a self-hosted,
/// single-household or single-business instance, which is what this app is. Anything
/// with real users and separate books needs proper authentication instead — see the
/// roadmap in docs/PLAN.md.
/// </remarks>
public sealed class AccessGate
{
    public const string CookieName = "rc_access";

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private readonly ReceiptCaptureOptions _options;
    private readonly IDataProtector _protector;

    public AccessGate(IOptions<ReceiptCaptureOptions> options, IDataProtectionProvider protection)
    {
        _options = options.Value;
        _protector = protection.CreateProtector("ReceiptCapture.AccessGate.v1");
    }

    /// <summary>False when no code is configured, in which case the app is open.</summary>
    public bool IsRequired => !string.IsNullOrWhiteSpace(_options.AccessCode);

    /// <summary>
    /// Compares a submitted code without leaking how much of it was right. Both sides
    /// are hashed first so the comparison is also independent of their lengths.
    /// </summary>
    public bool IsCodeValid(string? submitted)
    {
        if (!IsRequired)
        {
            return true;
        }

        if (string.IsNullOrEmpty(submitted))
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[32];
        Span<byte> actual = stackalloc byte[32];

        SHA256.HashData(Encoding.UTF8.GetBytes(_options.AccessCode!), expected);
        SHA256.HashData(Encoding.UTF8.GetBytes(submitted), actual);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>Issues the cookie that marks this browser as admitted.</summary>
    public void Grant(HttpResponse response)
    {
        var payload = _protector.Protect(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

        response.Cookies.Append(CookieName, payload, new CookieOptions
        {
            HttpOnly = true,
            // Set over plain HTTP the cookie would simply not stick, which is the
            // correct outcome: this app is HTTPS-only in any real deployment.
            Secure = response.HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            MaxAge = Lifetime,
            Path = "/",
        });
    }

    public void Revoke(HttpResponse response) => response.Cookies.Delete(CookieName);

    /// <summary>True when the request carries a cookie this server issued and that has not expired.</summary>
    public bool IsAdmitted(HttpRequest request)
    {
        if (!IsRequired)
        {
            return true;
        }

        if (!request.Cookies.TryGetValue(CookieName, out var payload) || string.IsNullOrEmpty(payload))
        {
            return false;
        }

        try
        {
            var issued = _protector.Unprotect(payload);
            return long.TryParse(issued, out var unixSeconds)
                && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unixSeconds) < Lifetime;
        }
        catch (CryptographicException)
        {
            // Forged, tampered with, or protected by a key this server no longer has.
            return false;
        }
    }
}
