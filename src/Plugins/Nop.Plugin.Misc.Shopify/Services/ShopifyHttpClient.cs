using System.Net;
using System.Text;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Nop.Core;
using Nop.Plugin.Misc.Shopify.Domain.Api;

namespace Nop.Plugin.Misc.Shopify.Services;

/// <summary>
/// Represents HTTP client to request Shopify Admin API
/// </summary>
public class ShopifyHttpClient
{
    #region Fields

    protected readonly HttpClient _httpClient;
    protected readonly ShopifySettings _shopifySettings;

    #endregion

    #region Ctor

    public ShopifyHttpClient(HttpClient httpClient, ShopifySettings shopifySettings)
    {
        httpClient.Timeout = TimeSpan.FromSeconds(ShopifyDefaults.RequestTimeout);
        httpClient.DefaultRequestHeaders.Add(HeaderNames.UserAgent, ShopifyDefaults.UserAgent);
        httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept, MimeTypes.ApplicationJson);

        _httpClient = httpClient;
        _shopifySettings = shopifySettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Prepare base API URL
    /// </summary>
    protected string GetBaseUrl()
    {
        var domain = NormalizeShopDomain(_shopifySettings.ShopDomain);
        return $"https://{domain}/admin/api/{ShopifyDefaults.ApiVersion}/";
    }

    /// <summary>
    /// Normalize shop domain
    /// </summary>
    protected static string NormalizeShopDomain(string shopDomain)
    {
        if (string.IsNullOrWhiteSpace(shopDomain))
            throw new NopException("Shop domain is not set");

        var domain = shopDomain.Trim().TrimEnd('/');

        if (domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            domain = domain["https://".Length..];

        if (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            domain = domain["http://".Length..];

        if (!domain.Contains('.'))
            domain = $"{domain}.myshopify.com";

        return domain;
    }

    /// <summary>
    /// Send HTTP request with retry on rate limiting
    /// </summary>
    protected async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string path, object body = null)
    {
        if (string.IsNullOrEmpty(_shopifySettings.AccessToken))
            throw new NopException("Access token is not set");

        var attempt = 0;

        while (true)
        {
            using var requestMessage = new HttpRequestMessage(method, new Uri(new Uri(GetBaseUrl()), path));
            requestMessage.Headers.Add("X-Shopify-Access-Token", _shopifySettings.AccessToken);

            if (body is not null)
            {
                var requestString = JsonConvert.SerializeObject(body);
                requestMessage.Content = new StringContent(requestString, Encoding.UTF8, MimeTypes.ApplicationJson);
            }

            var httpResponse = await _httpClient.SendAsync(requestMessage);
            var responseString = await httpResponse.Content.ReadAsStringAsync();

            if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests && attempt < ShopifyDefaults.MaxRetryAttempts)
            {
                attempt++;
                var retryAfter = httpResponse.Headers.RetryAfter?.Delta?.TotalMilliseconds ?? 1000 * attempt;
                await Task.Delay((int)Math.Max(retryAfter, 500));
                continue;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                var error = string.IsNullOrEmpty(responseString)
                    ? httpResponse.ReasonPhrase
                    : responseString;
                throw new NopException($"Shopify API error ({(int)httpResponse.StatusCode}): {error}");
            }

            if (typeof(TResponse) == typeof(string))
                return (TResponse)(object)responseString;

            if (string.IsNullOrWhiteSpace(responseString))
                return default;

            return JsonConvert.DeserializeObject<TResponse>(responseString);
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// Test connection to Shopify
    /// </summary>
    public async Task<Shop> GetShopAsync()
    {
        var response = await SendAsync<ShopResponse>(HttpMethod.Get, "shop.json");
        return response?.Shop;
    }

    /// <summary>
    /// Get Shopify locations
    /// </summary>
    public async Task<IList<Location>> GetLocationsAsync()
    {
        var response = await SendAsync<LocationsResponse>(HttpMethod.Get, "locations.json");
        return response?.Locations ?? new List<Location>();
    }

    /// <summary>
    /// Create a product in Shopify
    /// </summary>
    public async Task<ProductPayload> CreateProductAsync(ProductRequest request)
    {
        var response = await SendAsync<ProductResponse>(HttpMethod.Post, "products.json", request);
        return response?.Product;
    }

    /// <summary>
    /// Update a product in Shopify
    /// </summary>
    public async Task<ProductPayload> UpdateProductAsync(long productId, ProductRequest request)
    {
        var response = await SendAsync<ProductResponse>(HttpMethod.Put, $"products/{productId}.json", request);
        return response?.Product;
    }

    /// <summary>
    /// Delete a product in Shopify
    /// </summary>
    public async Task DeleteProductAsync(long productId)
    {
        await SendAsync<string>(HttpMethod.Delete, $"products/{productId}.json");
    }

    /// <summary>
    /// Set inventory level in Shopify
    /// </summary>
    public async Task SetInventoryLevelAsync(InventoryLevelRequest request)
    {
        await SendAsync<InventoryLevelResponse>(HttpMethod.Post, "inventory_levels/set.json", request);
    }

    #endregion
}
