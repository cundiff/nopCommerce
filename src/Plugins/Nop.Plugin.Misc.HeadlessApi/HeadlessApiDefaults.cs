namespace Nop.Plugin.Misc.HeadlessApi;

/// <summary>
/// Represents constants for the Headless Storefront API plugin
/// </summary>
public static class HeadlessApiDefaults
{
    /// <summary>
    /// Gets the plugin system name
    /// </summary>
    public const string SystemName = "Misc.HeadlessApi";

    /// <summary>
    /// Gets the configuration route name
    /// </summary>
    public const string ConfigurationRouteName = "Plugin.Misc.HeadlessApi.Configure";

    /// <summary>
    /// Gets the common URL prefix used by all storefront API endpoints
    /// </summary>
    public const string ApiRoutePrefix = "api/storefront";

    /// <summary>
    /// Gets the name of the HTTP header that carries the cart/customer token
    /// </summary>
    public const string TokenHeaderName = "X-Nop-Cart-Token";

    /// <summary>
    /// Gets the name of the HTTP header sent with revalidation webhooks to identify the affected entity type
    /// </summary>
    public const string WebhookTopicHeaderName = "x-nop-topic";

    /// <summary>
    /// Gets the topic value sent when products change
    /// </summary>
    public const string ProductsTopic = "products/update";

    /// <summary>
    /// Gets the topic value sent when categories change
    /// </summary>
    public const string CollectionsTopic = "collections/update";
}
