using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Services.Authentication.External;

namespace Nop.Plugin.ExternalAuth.Google.Infrastructure;

/// <summary>
/// Represents registrar of Google authentication service
/// </summary>
public class GoogleAuthenticationRegistrar : IExternalAuthenticationRegistrar
{
    /// <summary>
    /// Configure
    /// </summary>
    /// <param name="builder">Authentication builder</param>
    public void Configure(AuthenticationBuilder builder)
    {
        builder.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
        {
            var settings = EngineContext.Current.Resolve<GoogleExternalAuthSettings>();

            options.ClientId = string.IsNullOrEmpty(settings?.ClientId) ? nameof(options.ClientId) : settings.ClientId;
            options.ClientSecret = string.IsNullOrEmpty(settings?.ClientSecret) ? nameof(options.ClientSecret) : settings.ClientSecret;

            options.SaveTokens = true;

            options.Events = new OAuthEvents
            {
                OnRemoteFailure = context =>
                {
                    context.HandleResponse();

                    var errorUrl = context.Properties.GetString(GoogleAuthenticationDefaults.ErrorCallback);
                    context.Response.Redirect(errorUrl);

                    return Task.FromResult(0);
                }
            };
        });
    }
}
