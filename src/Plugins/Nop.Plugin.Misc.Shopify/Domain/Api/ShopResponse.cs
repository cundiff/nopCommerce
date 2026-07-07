using Newtonsoft.Json;

namespace Nop.Plugin.Misc.Shopify.Domain.Api;

/// <summary>
/// Represents a Shopify shop response
/// </summary>
public class ShopResponse
{
    [JsonProperty("shop")]
    public Shop Shop { get; set; }
}

/// <summary>
/// Represents a Shopify shop
/// </summary>
public class Shop
{
    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("domain")]
    public string Domain { get; set; }

    [JsonProperty("myshopify_domain")]
    public string MyshopifyDomain { get; set; }
}
