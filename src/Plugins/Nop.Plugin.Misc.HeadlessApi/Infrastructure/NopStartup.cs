using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Configuration;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.HeadlessApi.Infrastructure;

/// <summary>
/// Configures services and middleware used by the Headless Storefront API plugin
/// </summary>
public class NopStartup : INopStartup
{
    private static async Task LogRequestAsync(ILogger logger, HttpContext context, double duration, string requestId, Exception exception = null)
    {
        if (logger == null)
            return;

        var message =
            $"Headless API request {context.Request.Method} {context.Request.Path} -> {context.Response.StatusCode} in {duration:0.##}ms ({requestId})";

        if (exception != null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            await logger.ErrorAsync(message, exception);
        else
            await logger.InformationAsync(message);
    }

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

            var logger = context.RequestServices.GetService<ILogger>();
            var requestId = context.Request.Headers.TryGetValue(HeadlessApiDefaults.RequestIdHeaderName, out var requestIdHeader) &&
                            !string.IsNullOrWhiteSpace(requestIdHeader)
                ? requestIdHeader.ToString()
                : context.TraceIdentifier;
            var started = System.Diagnostics.Stopwatch.StartNew();

            context.Response.OnStarting(() =>
            {
                var duration = started.Elapsed.TotalMilliseconds;
                context.Response.Headers[HeadlessApiDefaults.RequestIdHeaderName] = requestId;
                context.Response.Headers["Server-Timing"] = $"nop;dur={duration:0.##}";
                return Task.CompletedTask;
            });

            var settingService = context.RequestServices.GetService<ISettingService>();
            var settings = settingService != null
                ? await settingService.LoadSettingAsync<HeadlessApiSettings>()
                : null;
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
                $"Content-Type, Authorization, {HeadlessApiDefaults.TokenHeaderName}, {HeadlessApiDefaults.RequestIdHeaderName}";
            context.Response.Headers["Access-Control-Expose-Headers"] =
                $"{HeadlessApiDefaults.RequestIdHeaderName}, Server-Timing";

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                started.Stop();
                var preflightDuration = started.Elapsed.TotalMilliseconds;
                await LogRequestAsync(logger, context, preflightDuration, requestId);
                return;
            }

            try
            {
                await next();
            }
            catch (Exception ex)
            {
                started.Stop();
                var failedDuration = started.Elapsed.TotalMilliseconds;
                await LogRequestAsync(logger, context, failedDuration, requestId, ex);
                throw;
            }

            started.Stop();
            var duration = started.Elapsed.TotalMilliseconds;
            await LogRequestAsync(logger, context, duration, requestId);
        });
    }

    /// <summary>
    /// Gets order of this startup configuration implementation.
    /// Runs after routing (400) so the middleware sits in the request pipeline before endpoint execution.
    /// </summary>
    public int Order => 401;
}
