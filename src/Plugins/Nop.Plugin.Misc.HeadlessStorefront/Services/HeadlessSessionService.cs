using Nop.Core.Domain.Customers;
using Nop.Data;
using Nop.Plugin.Misc.HeadlessStorefront.Domain;
using Nop.Services.Customers;

namespace Nop.Plugin.Misc.HeadlessStorefront.Services;

public class HeadlessSessionService : IHeadlessSessionService
{
  private readonly IRepository<HeadlessSession> _sessionRepository;
  private readonly ICustomerService _customerService;

  public HeadlessSessionService(
    IRepository<HeadlessSession> sessionRepository,
    ICustomerService customerService)
  {
    _sessionRepository = sessionRepository;
    _customerService = customerService;
  }

  public virtual async Task<HeadlessSession> CreateSessionAsync()
  {
    var customer = await _customerService.InsertGuestCustomerAsync();
    var session = new HeadlessSession
    {
      SessionToken = Guid.NewGuid().ToString("N"),
      CustomerId = customer.Id,
      CreatedOnUtc = DateTime.UtcNow
    };

    await _sessionRepository.InsertAsync(session);

    return session;
  }

  public virtual async Task<HeadlessSession> GetSessionByTokenAsync(string sessionToken)
  {
    if (string.IsNullOrWhiteSpace(sessionToken))
      return null;

    return await _sessionRepository.Table
      .Where(s => s.SessionToken == sessionToken)
      .FirstOrDefaultAsync();
  }

  public virtual async Task<Customer> GetCustomerAsync(HeadlessSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    return await _customerService.GetCustomerByIdAsync(session.CustomerId);
  }

  public virtual async Task<string> CreateCheckoutHandoffAsync(HeadlessSession session)
  {
    ArgumentNullException.ThrowIfNull(session);

    session.CheckoutHandoffToken = Guid.NewGuid().ToString("N");
    session.CheckoutHandoffExpiresUtc = DateTime.UtcNow.Add(HeadlessStorefrontDefaults.CheckoutHandoffLifetime);
    await _sessionRepository.UpdateAsync(session);

    return session.CheckoutHandoffToken;
  }

  public virtual async Task<HeadlessSession> GetSessionByCheckoutHandoffTokenAsync(string handoffToken)
  {
    if (string.IsNullOrWhiteSpace(handoffToken))
      return null;

    var session = await _sessionRepository.Table
      .Where(s => s.CheckoutHandoffToken == handoffToken)
      .FirstOrDefaultAsync();

    if (session == null)
      return null;

    if (session.CheckoutHandoffExpiresUtc == null || session.CheckoutHandoffExpiresUtc < DateTime.UtcNow)
      return null;

    return session;
  }
}
