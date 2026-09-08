using System.Security.Claims;
using Nop.Services.Authentication.External;
using Nop.Services.Customers;
using Nop.Services.Events;

namespace Nop.Plugin.ExternalAuth.Google.Infrastructure;

/// <summary>
/// Google authentication event consumer (used for saving customer fields on registration)
/// </summary>
public class GoogleAuthenticationEventConsumer : IConsumer<CustomerAutoRegisteredByExternalMethodEvent>
{
    #region Fields

    protected readonly ICustomerService _customerService;

    #endregion

    #region Ctor

    public GoogleAuthenticationEventConsumer(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Handle event
    /// </summary>
    /// <param name="eventMessage">Event message</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task HandleEventAsync(CustomerAutoRegisteredByExternalMethodEvent eventMessage)
    {
        if (eventMessage?.Customer == null || eventMessage.AuthenticationParameters == null)
            return;

        if (!eventMessage.AuthenticationParameters.ProviderSystemName.Equals(GoogleAuthenticationDefaults.SystemName))
            return;

        var customer = eventMessage.Customer;

        var firstName = eventMessage.AuthenticationParameters.Claims?.FirstOrDefault(claim => claim.Type == ClaimTypes.GivenName)?.Value;
        if (!string.IsNullOrEmpty(firstName))
            customer.FirstName = firstName;

        var lastName = eventMessage.AuthenticationParameters.Claims?.FirstOrDefault(claim => claim.Type == ClaimTypes.Surname)?.Value;
        if (!string.IsNullOrEmpty(lastName))
            customer.LastName = lastName;

        await _customerService.UpdateCustomerAsync(customer);
    }

    #endregion
}
