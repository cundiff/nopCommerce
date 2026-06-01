using Nop.Core;

namespace Nop.Plugin.Misc.HeadlessStorefront.Domain;

/// <summary>
/// Maps a headless storefront session token to a guest customer
/// </summary>
public class HeadlessSession : BaseEntity
{
  public string SessionToken { get; set; }

  public int CustomerId { get; set; }

  public string CheckoutHandoffToken { get; set; }

  public DateTime? CheckoutHandoffExpiresUtc { get; set; }

  public DateTime CreatedOnUtc { get; set; }
}
