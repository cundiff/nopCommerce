using Newtonsoft.Json;

namespace Nop.Plugin.Misc.Shopify.Domain.Api;

/// <summary>
/// Represents a Shopify product request wrapper
/// </summary>
public class ProductRequest
{
    [JsonProperty("product")]
    public ProductPayload Product { get; set; }
}

/// <summary>
/// Represents a Shopify product response wrapper
/// </summary>
public class ProductResponse
{
    [JsonProperty("product")]
    public ProductPayload Product { get; set; }
}

/// <summary>
/// Represents a Shopify product payload
/// </summary>
public class ProductPayload
{
    [JsonProperty("id")]
    public long? Id { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("body_html")]
    public string BodyHtml { get; set; }

    [JsonProperty("vendor")]
    public string Vendor { get; set; }

    [JsonProperty("product_type")]
    public string ProductType { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("variants")]
    public IList<VariantPayload> Variants { get; set; } = new List<VariantPayload>();

    [JsonProperty("images")]
    public IList<ImagePayload> Images { get; set; } = new List<ImagePayload>();
}

/// <summary>
/// Represents a Shopify variant payload
/// </summary>
public class VariantPayload
{
    [JsonProperty("id")]
    public long? Id { get; set; }

    [JsonProperty("sku")]
    public string Sku { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("inventory_management")]
    public string InventoryManagement { get; set; }

    [JsonProperty("inventory_quantity")]
    public int? InventoryQuantity { get; set; }

    [JsonProperty("inventory_item_id")]
    public long? InventoryItemId { get; set; }
}

/// <summary>
/// Represents a Shopify image payload
/// </summary>
public class ImagePayload
{
    [JsonProperty("src")]
    public string Src { get; set; }
}
