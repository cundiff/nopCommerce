using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core;
using Nop.Plugin.Misc.HeadlessStorefront.Models;
using Nop.Plugin.Misc.HeadlessStorefront.Services;
using Nop.Services.Helpers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.HeadlessStorefront.Controllers;

[ApiController]
[IgnoreAntiforgeryToken]
[Route($"{HeadlessStorefrontDefaults.ApiRoutePrefix}")]
public class HeadlessCartController : HeadlessApiControllerBase
{
  private readonly IHeadlessCartService _cartService;
  private readonly IWebHelper _webHelper;

  public HeadlessCartController(
    IHeadlessSessionService sessionService,
    IWorkContext workContext,
    IHeadlessCartService cartService,
    IWebHelper webHelper)
    : base(sessionService, workContext)
  {
    _cartService = cartService;
    _webHelper = webHelper;
  }

  [HttpPost("sessions")]
  public async Task<IActionResult> CreateSession()
  {
    var session = await SessionService.CreateSessionAsync();
    return Ok(new HeadlessSessionResponse(session.SessionToken));
  }

  [HttpGet("cart")]
  public async Task<IActionResult> GetCart()
  {
    try
    {
      var (_, customer) = await ResolveSessionAsync();
      var cart = await _cartService.GetCartAsync(customer);
      return Ok(cart);
    }
    catch (InvalidOperationException ex)
    {
      return Unauthorized(new { error = ex.Message });
    }
  }

  [HttpPost("cart/items")]
  public async Task<IActionResult> AddItem([FromBody] HeadlessAddCartItemRequest request)
  {
    try
    {
      var (_, customer) = await ResolveSessionAsync();
      var cart = await _cartService.AddItemAsync(customer, request.ProductId, request.Quantity, request.AttributesXml);
      return Ok(cart);
    }
    catch (InvalidOperationException ex)
    {
      return BadRequest(new { error = ex.Message });
    }
  }

  [HttpPatch("cart/items/{lineId:int}")]
  public async Task<IActionResult> UpdateItem(int lineId, [FromBody] HeadlessUpdateCartItemRequest request)
  {
    try
    {
      var (_, customer) = await ResolveSessionAsync();
      var cart = await _cartService.UpdateItemAsync(customer, lineId, request.Quantity);
      return Ok(cart);
    }
    catch (InvalidOperationException ex)
    {
      return BadRequest(new { error = ex.Message });
    }
  }

  [HttpDelete("cart/items/{lineId:int}")]
  public async Task<IActionResult> RemoveItem(int lineId)
  {
    try
    {
      var (_, customer) = await ResolveSessionAsync();
      var cart = await _cartService.RemoveItemAsync(customer, lineId);
      return Ok(cart);
    }
    catch (InvalidOperationException ex)
    {
      return BadRequest(new { error = ex.Message });
    }
  }

  [HttpPost("checkout")]
  public async Task<IActionResult> Checkout()
  {
    try
    {
      var (session, customer) = await ResolveSessionAsync();
      var cart = await _cartService.GetCartAsync(customer);
      if (!cart.Lines.Any())
        return BadRequest(new { error = "Cart is empty" });

      var handoffToken = await SessionService.CreateCheckoutHandoffAsync(session);
      var storeLocation = _webHelper.GetStoreLocation().TrimEnd('/');
      var checkoutUrl = $"{storeLocation}/{HeadlessStorefrontDefaults.ApiRoutePrefix}/checkout/handoff?token={handoffToken}";

      return Ok(new HeadlessCheckoutResponse(checkoutUrl));
    }
    catch (InvalidOperationException ex)
    {
      return Unauthorized(new { error = ex.Message });
    }
  }

  [HttpGet("checkout/handoff")]
  public async Task<IActionResult> CheckoutHandoff([FromQuery] string token)
  {
    var session = await SessionService.GetSessionByCheckoutHandoffTokenAsync(token);
    if (session == null)
      return NotFound();

    var customer = await SessionService.GetCustomerAsync(session);
    await WorkContext.SetCurrentCustomerAsync(customer);

    var cookieService = HttpContext.RequestServices.GetRequiredService<IHeadlessCustomerCookieService>();
    cookieService.SetCustomerCookie(customer);

    var storeLocation = _webHelper.GetStoreLocation().TrimEnd('/');
    return Redirect($"{storeLocation}/checkout");
  }
}
