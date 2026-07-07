using Newtonsoft.Json;

namespace Nop.Plugin.Misc.Shopify.Domain.Api;

/// <summary>
/// Represents a Shopify inventory level set request
/// </summary>
public class InventoryLevelRequest
{
    [JsonProperty("location_id")]
    public long LocationId { get; set; }

    [JsonProperty("inventory_item_id")]
    public long InventoryItemId { get; set; }

    [JsonProperty("available")]
    public int Available { get; set; }
}

/// <summary>
/// Represents a Shopify inventory level response wrapper
/// </summary>
public class InventoryLevelResponse
{
    [JsonProperty("inventory_level")]
    public InventoryLevel InventoryLevel { get; set; }
}

/// <summary>
/// Represents a Shopify inventory level
/// </summary>
public class InventoryLevel
{
    [JsonProperty("inventory_item_id")]
    public long InventoryItemId { get; set; }

    [JsonProperty("location_id")]
    public long LocationId { get; set; }

    [JsonProperty("available")]
    public int Available { get; set; }
}
