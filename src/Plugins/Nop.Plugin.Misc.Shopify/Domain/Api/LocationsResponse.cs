using Newtonsoft.Json;

namespace Nop.Plugin.Misc.Shopify.Domain.Api;

/// <summary>
/// Represents a Shopify locations response
/// </summary>
public class LocationsResponse
{
    [JsonProperty("locations")]
    public IList<Location> Locations { get; set; } = new List<Location>();
}

/// <summary>
/// Represents a Shopify location
/// </summary>
public class Location
{
    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("active")]
    public bool Active { get; set; }
}
