using System.Reflection;
using Newtonsoft.Json.Linq;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Services.Common;

namespace Nop.Services.Admin;

/// <summary>
/// Admin filter preference service
/// </summary>
public partial class AdminFilterPreferenceService : IAdminFilterPreferenceService
{
    #region Fields

    protected readonly AdminAreaSettings _adminAreaSettings;
    protected readonly IGenericAttributeService _genericAttributeService;

    protected static readonly HashSet<string> ExcludedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Draw",
        "Start",
        "Length",
        "AvailablePageSizes",
        "Page",
        "PageSize"
    };

    #endregion

    #region Ctor

    public AdminFilterPreferenceService(AdminAreaSettings adminAreaSettings,
        IGenericAttributeService genericAttributeService)
    {
        _adminAreaSettings = adminAreaSettings;
        _genericAttributeService = genericAttributeService;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Gets a value indicating whether the property can be restored from saved filters
    /// </summary>
    /// <param name="property">Property</param>
    /// <returns>A value indicating whether the property can be restored</returns>
    protected static bool CanRestoreProperty(PropertyInfo property)
    {
        if (!property.CanWrite || property.SetMethod == null)
            return false;

        if (property.Name.StartsWith("Available", StringComparison.Ordinal))
            return false;

        if (ExcludedPropertyNames.Contains(property.Name))
            return false;

        return true;
    }

    /// <summary>
    /// Converts a JSON token to a property value
    /// </summary>
    /// <param name="token">JSON token</param>
    /// <param name="propertyType">Property type</param>
    /// <param name="currentValue">Current property value</param>
    /// <returns>Converted value</returns>
    protected virtual object ConvertToken(JToken token, Type propertyType, object currentValue)
    {
        if (token == null || token.Type == JTokenType.Null)
            return GetDefaultValue(propertyType);

        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (typeof(IList<int>).IsAssignableFrom(propertyType))
            return ConvertToIntList(token, currentValue as IList<int>);

        if (underlyingType == typeof(string))
            return token.Type == JTokenType.Array ? null : token.ToString();

        if (underlyingType == typeof(bool))
            return token.Type == JTokenType.Boolean ? token.Value<bool>() : bool.TryParse(token.ToString(), out var boolValue) && boolValue;

        if (underlyingType == typeof(int))
            return token.Type == JTokenType.Integer ? token.Value<int>() : int.TryParse(token.ToString(), out var intValue) ? intValue : 0;

        if (underlyingType == typeof(DateTime))
            return DateTime.TryParse(token.ToString(), out var dateValue) ? dateValue : GetDefaultValue(propertyType);

        if (underlyingType.IsEnum)
            return Enum.ToObject(underlyingType, Convert.ToInt32(token));

        return token.ToObject(propertyType);
    }

    /// <summary>
    /// Converts a JSON token to a list of integers
    /// </summary>
    /// <param name="token">JSON token</param>
    /// <param name="currentList">Current list</param>
    /// <returns>List of integers</returns>
    protected virtual IList<int> ConvertToIntList(JToken token, IList<int> currentList)
    {
        var result = currentList ?? new List<int>();
        result.Clear();

        if (token.Type != JTokenType.Array)
            return result;

        foreach (var item in token.Children())
        {
            if (item.Type == JTokenType.Integer)
                result.Add(item.Value<int>());
            else if (int.TryParse(item.ToString(), out var intValue))
                result.Add(intValue);
        }

        return result;
    }

    /// <summary>
    /// Gets the default value for a type
    /// </summary>
    /// <param name="type">Type</param>
    /// <returns>Default value</returns>
    protected static object GetDefaultValue(Type type)
    {
        if (Nullable.GetUnderlyingType(type) != null)
            return null;

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    /// <summary>
    /// Applies saved filters to a search model
    /// </summary>
    /// <param name="searchModel">Search model</param>
    /// <param name="savedFilters">Saved filters</param>
    protected virtual void ApplyFilters(object searchModel, JObject savedFilters)
    {
        ArgumentNullException.ThrowIfNull(searchModel);
        ArgumentNullException.ThrowIfNull(savedFilters);

        foreach (var property in searchModel.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!CanRestoreProperty(property))
                continue;

            if (!savedFilters.TryGetValue(property.Name, StringComparison.OrdinalIgnoreCase, out var token))
                continue;

            var convertedValue = ConvertToken(token, property.PropertyType, property.GetValue(searchModel));

            if (convertedValue is IList<int> intList && property.GetValue(searchModel) is IList<int> targetList)
            {
                targetList.Clear();
                foreach (var item in intList)
                    targetList.Add(item);
                continue;
            }

            property.SetValue(searchModel, convertedValue);
        }
    }

    /// <summary>
    /// Applies saved filters to nested search models
    /// </summary>
    /// <param name="model">View model</param>
    /// <param name="customer">Customer</param>
    /// <param name="controller">Controller name</param>
    /// <param name="action">Action name</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task ApplyToModelAsync(object model, Customer customer, string controller, string action)
    {
        if (model == null)
            return;

        var modelType = model.GetType();
        if (modelType.Name.EndsWith("SearchModel", StringComparison.Ordinal))
        {
            var key = AdminFilterPreferenceDefaults.BuildKey(controller, action);
            await ApplyToSearchModelAsync(customer, key, model);
            return;
        }

        foreach (var property in modelType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.PropertyType.Name.EndsWith("SearchModel", StringComparison.Ordinal))
                continue;

            var nestedModel = property.GetValue(model);
            if (nestedModel == null)
                continue;

            var key = AdminFilterPreferenceDefaults.BuildKey(controller, action, property.Name);
            await ApplyToSearchModelAsync(customer, key, nestedModel);
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a value indicating whether sticky filters are enabled
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains a value indicating whether sticky filters are enabled
    /// </returns>
    public virtual Task<bool> IsEnabledAsync()
    {
        return Task.FromResult(_adminAreaSettings.EnableStickyFilters);
    }

    /// <summary>
    /// Save filter preferences from JSON
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="key">Preference key</param>
    /// <param name="filtersJson">Filters JSON</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task SaveFromJsonAsync(Customer customer, string key, string filtersJson)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (!await IsEnabledAsync())
            return;

        if (string.IsNullOrWhiteSpace(filtersJson))
            filtersJson = "{}";

        await _genericAttributeService.SaveAttributeAsync(customer, key, filtersJson);
    }

    /// <summary>
    /// Apply saved filter preferences to a search model
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="key">Preference key</param>
    /// <param name="searchModel">Search model</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task ApplyToSearchModelAsync(Customer customer, string key, object searchModel)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(searchModel);

        if (!await IsEnabledAsync())
            return;

        var filtersJson = await _genericAttributeService.GetAttributeAsync<string>(customer, key);
        if (string.IsNullOrWhiteSpace(filtersJson))
            return;

        var savedFilters = JObject.Parse(filtersJson);
        ApplyFilters(searchModel, savedFilters);
    }

    /// <summary>
    /// Apply saved filter preferences to a view model
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="controller">Controller name</param>
    /// <param name="action">Action name</param>
    /// <param name="model">View model</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task ApplyToViewModelAsync(Customer customer, string controller, string action, object model)
    {
        if (!await IsEnabledAsync() || model == null)
            return;

        await ApplyToModelAsync(model, customer, controller, action);
    }

    #endregion
}
