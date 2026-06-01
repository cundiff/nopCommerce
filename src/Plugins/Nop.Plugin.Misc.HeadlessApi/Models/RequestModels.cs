namespace Nop.Plugin.Misc.HeadlessApi.Models;

/// <summary>
/// Request payloads accepted by the cart endpoints. Field names mirror the
/// arguments used by Vercel Commerce's cart actions. Incoming JSON is matched
/// case-insensitively, so camelCase from the storefront binds correctly.
/// </summary>
public class AddToCartLineRequest
{
    public string MerchandiseId { get; set; }
    public int Quantity { get; set; }
}

public class AddToCartRequest
{
    public List<AddToCartLineRequest> Lines { get; set; } = new();
}

public class UpdateCartLineRequest
{
    public string Id { get; set; }
    public string MerchandiseId { get; set; }
    public int Quantity { get; set; }
}

public class UpdateCartRequest
{
    public List<UpdateCartLineRequest> Lines { get; set; } = new();
}

public class RemoveFromCartRequest
{
    public List<string> LineIds { get; set; } = new();
}
