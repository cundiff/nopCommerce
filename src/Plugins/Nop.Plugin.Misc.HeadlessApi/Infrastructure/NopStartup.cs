using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.HeadlessApi.Services;

namespace Nop.Plugin.Misc.HeadlessApi.Infrastructure;

/// <summary>
/// Configures services and middleware used by the Headless Storefront API plugin
/// </summary>
public class NopStartup : INopStartup
{
    /// <summary>
    /// Add and configure any of the middleware
    /// </summary>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ApiTokenService>();
        services.AddScoped<HeadlessApiModelFactory>();
        services.AddHttpClient<WebhookNotificationService>();
    }

    /// <summary>
    /// Configure the using of added middleware
    /// </summary>
    public void Configure(IApplicationBuilder application)
    {
        //lightweight CORS handling scoped to the storefront API routes.
        //(Most Vercel Commerce requests are server-to-server, but this enables
        //browser-side calls and local development without extra configuration.)
        application.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (!path.StartsWith("/" + HeadlessApiDefaults.ApiRoutePrefix, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var settings = context.RequestServices.GetService<HeadlessApiSettings>();
            var allowed = settings?.AllowedOrigins ?? "*";
            var origin = context.Request.Headers["Origin"].ToString();

            if (allowed.Trim() == "*")
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            }
            else if (!string.IsNullOrEmpty(origin))
            {
                var origins = allowed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (origins.Any(o => o.Equals(origin, StringComparison.OrdinalIgnoreCase)))
                {
                    context.Response.Headers["Access-Control-Allow-Origin"] = origin;
                    context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
                    context.Response.Headers["Vary"] = "Origin";
                }
            }

            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
            context.Response.Headers["Access-Control-Allow-Headers"] =
                $"Content-Type, Authorization, {HeadlessApiDefaults.TokenHeaderName}";

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next();
        });
    }

    /// <summary>
    /// Gets order of this startup configuration implementation.
    /// Runs after routing (400) so the middleware sits in the request pipeline before endpoint execution.
    /// </summary>
    public int Order => 401;
}
