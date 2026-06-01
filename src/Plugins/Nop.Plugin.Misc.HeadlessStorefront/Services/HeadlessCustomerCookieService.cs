using Microsoft.AspNetCore.Http;
using Nop.Core.Domain.Customers;
using Nop.Core.Http;
using Nop.Core.Security;
using Nop.Services.Helpers;

namespace Nop.Plugin.Misc.HeadlessStorefront.Services;

public class HeadlessCustomerCookieService : IHeadlessCustomerCookieService
{
  private readonly IHttpContextAccessor _httpContextAccessor;
  private readonly CookieSettings _cookieSettings;
  private readonly IWebHelper _webHelper;

  public HeadlessCustomerCookieService(
    IHttpContextAccessor httpContextAccessor,
    CookieSettings cookieSettings,
    IWebHelper webHelper)
  {
    _httpContextAccessor = httpContextAccessor;
    _cookieSettings = cookieSettings;
    _webHelper = webHelper;
  }

  public virtual void SetCustomerCookie(Customer customer)
  {
    var httpContext = _httpContextAccessor.HttpContext;
    if (httpContext?.Response.HasStarted ?? true)
      return;

    var cookieName = $"{NopCookieDefaults.Prefix}{NopCookieDefaults.CustomerCookie}";
    httpContext.Response.Cookies.Delete(cookieName);

    var cookieExpiresDate = DateTime.Now.AddHours(_cookieSettings.CustomerCookieExpires);
    var options = new CookieOptions
    {
      HttpOnly = true,
      Expires = cookieExpiresDate,
      Secure = _webHelper.IsCurrentConnectionSecured(),
      SameSite = SameSiteMode.Lax
    };

    httpContext.Response.Cookies.Append(cookieName, customer.CustomerGuid.ToString(), options);
  }
}
