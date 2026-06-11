using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using EtisalatSaasCallback.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EtisalatSaasCallback.Authentication;

public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IsvSettings _isvSettings;
    private readonly ILogger<BasicAuthenticationHandler> _logger;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IOptions<IsvSettings> isvSettings)
        : base(options, loggerFactory, encoder)
    {
        _isvSettings = isvSettings.Value;
        _logger = loggerFactory.CreateLogger<BasicAuthenticationHandler>();
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header"));
        }

        try
        {
            var authHeaderValue = authHeader.ToString();
            if (!authHeaderValue.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization scheme"));
            }

            var encodedCredentials = authHeaderValue["Basic ".Length..].Trim();
            var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
            var colonIndex = decodedCredentials.IndexOf(':');

            if (colonIndex < 0)
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid credentials format"));
            }

            var username = decodedCredentials[..colonIndex];
            var password = decodedCredentials[(colonIndex + 1)..];

            if (username != _isvSettings.Username || password != _isvSettings.Password)
            {
                _logger.LogWarning("Authentication failed for user: {Username}", username);
                return Task.FromResult(AuthenticateResult.Fail("Invalid username or password"));
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, "ISV")
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            _logger.LogInformation("Authentication successful for user: {Username}", username);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (FormatException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Base64 encoding"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error");
            return Task.FromResult(AuthenticateResult.Fail("Authentication error"));
        }
    }
}

public class IpWhitelistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IsvSettings _isvSettings;
    private readonly ILogger<IpWhitelistMiddleware> _logger;

    public IpWhitelistMiddleware(
        RequestDelegate next,
        IOptions<IsvSettings> isvSettings,
        ILogger<IpWhitelistMiddleware> logger)
    {
        _next = next;
        _isvSettings = isvSettings.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_isvSettings.EnableIpWhitelisting)
        {
            await _next(context);
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();

        if (string.IsNullOrEmpty(remoteIp))
        {
            _logger.LogWarning("Could not determine remote IP address");
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Could not determine IP address" });
            return;
        }

        if (!_isvSettings.WhitelistedIps.Contains(remoteIp) &&
            !_isvSettings.WhitelistedIps.Contains("*"))
        {
            _logger.LogWarning("IP not whitelisted: {RemoteIp}", remoteIp);
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Originator not whitelisted",
                code = "8"
            });
            return;
        }

        await _next(context);
    }
}

public static class IpWhitelistMiddlewareExtensions
{
    public static IApplicationBuilder UseIpWhitelist(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<IpWhitelistMiddleware>();
    }
}
