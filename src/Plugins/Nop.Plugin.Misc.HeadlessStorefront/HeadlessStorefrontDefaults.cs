namespace Nop.Plugin.Misc.HeadlessStorefront;

/// <summary>
/// Headless storefront plugin defaults
/// </summary>
public static class HeadlessStorefrontDefaults
{
  public const string SessionHeaderName = "X-Storefront-Session";

  public const string ApiRoutePrefix = "headless/v1";

  public static readonly TimeSpan CheckoutHandoffLifetime = TimeSpan.FromMinutes(15);
}
