using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.HeadlessStorefront.Domain;
using Nop.Plugin.Misc.HeadlessStorefront.Services;

namespace Nop.Plugin.Misc.HeadlessStorefront.Controllers;

public abstract class HeadlessApiControllerBase : Controller
{
  protected readonly IHeadlessSessionService SessionService;
  protected readonly IWorkContext WorkContext;

  protected HeadlessApiControllerBase(IHeadlessSessionService sessionService, IWorkContext workContext)
  {
    SessionService = sessionService;
    WorkContext = workContext;
  }

  protected string GetSessionToken()
  {
    if (Request.Headers.TryGetValue(HeadlessStorefrontDefaults.SessionHeaderName, out var values))
      return values.FirstOrDefault();

    return null;
  }

  protected async Task<(HeadlessSession Session, Customer Customer)> ResolveSessionAsync(bool required = true)
  {
    var token = GetSessionToken();
    var session = await SessionService.GetSessionByTokenAsync(token);
    if (session == null)
    {
      if (required)
        throw new InvalidOperationException("Invalid or missing storefront session");

      return (null, null);
    }

    var customer = await SessionService.GetCustomerAsync(session);
    await WorkContext.SetCurrentCustomerAsync(customer);

    return (session, customer);
  }
}
