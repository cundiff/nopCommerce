using Nop.Core.Domain.Customers;

namespace Nop.Services.Admin;

/// <summary>
/// Admin filter preference service interface
/// </summary>
public partial interface IAdminFilterPreferenceService
{
    /// <summary>
    /// Gets a value indicating whether sticky filters are enabled
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains a value indicating whether sticky filters are enabled
    /// </returns>
    Task<bool> IsEnabledAsync();

    /// <summary>
    /// Save filter preferences from JSON
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="key">Preference key</param>
    /// <param name="filtersJson">Filters JSON</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task SaveFromJsonAsync(Customer customer, string key, string filtersJson);

    /// <summary>
    /// Apply saved filter preferences to a search model
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="key">Preference key</param>
    /// <param name="searchModel">Search model</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task ApplyToSearchModelAsync(Customer customer, string key, object searchModel);

    /// <summary>
    /// Apply saved filter preferences to a view model
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="controller">Controller name</param>
    /// <param name="action">Action name</param>
    /// <param name="model">View model</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task ApplyToViewModelAsync(Customer customer, string controller, string action, object model);
}
