using Nop.Core.Domain.Customers;

namespace Nop.Plugin.Misc.HeadlessStorefront.Services;

public interface IHeadlessCustomerCookieService
{
  void SetCustomerCookie(Customer customer);
}
