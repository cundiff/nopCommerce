using Nop.Core.Configuration;

namespace Nop.Plugin.ExternalAuth.Google;

/// <summary>
/// Represents settings of the Google authentication method
/// </summary>
public class GoogleExternalAuthSettings : ISettings
{
    /// <summary>
    /// Gets or sets OAuth2 client identifier
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// Gets or sets OAuth2 client secret
    /// </summary>
    public string ClientSecret { get; set; }
}
