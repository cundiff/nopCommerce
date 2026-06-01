using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.HeadlessStorefront.Domain;

namespace Nop.Plugin.Misc.HeadlessStorefront.Services;

public interface IHeadlessSessionService
{
  Task<HeadlessSession> CreateSessionAsync();

  Task<HeadlessSession> GetSessionByTokenAsync(string sessionToken);

  Task<Customer> GetCustomerAsync(HeadlessSession session);

  Task<string> CreateCheckoutHandoffAsync(HeadlessSession session);

  Task<HeadlessSession> GetSessionByCheckoutHandoffTokenAsync(string handoffToken);
}
