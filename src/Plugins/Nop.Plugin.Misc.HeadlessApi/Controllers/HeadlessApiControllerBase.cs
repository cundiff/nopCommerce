using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.HeadlessApi.Services;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Base controller for all storefront API endpoints. Emits camelCase JSON
/// (matching the storefront contract) and resolves the cart/customer token.
/// </summary>
[IgnoreAntiforgeryToken]
public abstract class HeadlessApiControllerBase : Controller
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    protected readonly ApiTokenService _apiTokenService;

    protected HeadlessApiControllerBase(ApiTokenService apiTokenService)
    {
        _apiTokenService = apiTokenService;
    }

    /// <summary>
    /// Serializes the supplied object as camelCase JSON
    /// </summary>
    protected IActionResult JsonApi(object data)
    {
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        return Content(json, "application/json");
    }

    /// <summary>
    /// Reads the raw cart/customer token from the request (header first, then query string)
    /// </summary>
    protected string GetRequestToken()
    {
        if (Request.Headers.TryGetValue(HeadlessApiDefaults.TokenHeaderName, out var headerValue) &&
            !string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.ToString();
        }

        //also accept a standard bearer token
        if (Request.Headers.TryGetValue("Authorization", out var authValue))
        {
            var raw = authValue.ToString();
            if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return raw["Bearer ".Length..].Trim();
        }

        if (Request.Query.TryGetValue("token", out var queryValue) && !string.IsNullOrWhiteSpace(queryValue))
            return queryValue.ToString();

        return null;
    }

    /// <summary>
    /// Resolves the customer associated with the current request token; returns null when absent/invalid
    /// </summary>
    protected async Task<Customer> GetTokenCustomerAsync()
    {
        return await _apiTokenService.GetCustomerFromTokenAsync(GetRequestToken());
    }
}
