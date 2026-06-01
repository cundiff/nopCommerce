using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.HeadlessApi;

/// <summary>
/// Represents the settings of the Headless Storefront API plugin
/// </summary>
public class HeadlessApiSettings : ISettings
{
    /// <summary>
    /// Gets or sets the secret key used to sign cart/customer tokens (HMAC-SHA256)
    /// </summary>
    public string SecretKey { get; set; }

    /// <summary>
    /// Gets or sets the comma-separated list of origins allowed to call the API (CORS).
    /// Use "*" to allow any origin (note: credentials are not sent with the wildcard origin).
    /// </summary>
    public string AllowedOrigins { get; set; }

    /// <summary>
    /// Gets or sets the number of days a generated cart/customer token remains valid
    /// </summary>
    public int TokenExpirationDays { get; set; }

    /// <summary>
    /// Gets or sets the storefront URL that should receive cache-revalidation webhooks
    /// (e.g. https://my-storefront.vercel.app/api/revalidate)
    /// </summary>
    public string RevalidationWebhookUrl { get; set; }

    /// <summary>
    /// Gets or sets the shared secret appended to the revalidation webhook URL (?secret=...)
    /// </summary>
    public string RevalidationSecret { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether outgoing revalidation webhooks are enabled
    /// </summary>
    public bool EnableWebhooks { get; set; }
}
