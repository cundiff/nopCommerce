using Nop.Core;

namespace Nop.Plugin.Misc.Shopify;

/// <summary>
/// Represents plugin constants
/// </summary>
public class ShopifyDefaults
{
    /// <summary>
    /// Gets the plugin system name
    /// </summary>
    public static string SystemName => "Misc.Shopify";

    /// <summary>
    /// Gets the Shopify Admin API version
    /// </summary>
    public static string ApiVersion => "2025-01";

    /// <summary>
    /// Gets the user agent used to request Shopify services
    /// </summary>
    public static string UserAgent => $"nopCommerce-{NopVersion.CURRENT_VERSION}";

    /// <summary>
    /// Gets a default period (in seconds) before the request times out
    /// </summary>
    public static int RequestTimeout => 30;

    /// <summary>
    /// Gets the maximum number of retry attempts for rate-limited requests
    /// </summary>
    public static int MaxRetryAttempts => 3;

    /// <summary>
    /// Gets the delay between sync requests in milliseconds
    /// </summary>
    public static int SyncRequestDelayMs => 500;

    /// <summary>
    /// Gets the configuration route name
    /// </summary>
    public static string ConfigurationRouteName => "Plugin.Misc.Shopify.Configure";

    /// <summary>
    /// Gets a name, type and period (in seconds) of the auto synchronization task
    /// </summary>
    public static (string Name, string Type, int Period) SynchronizationTask =>
        ("Synchronization (Shopify plugin)", "Nop.Plugin.Misc.Shopify.Services.ShopifySyncTask", 3600);
}
