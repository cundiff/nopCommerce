using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nop.Core.Domain.Customers;
using Nop.Services.Customers;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Issues and validates lightweight HMAC-signed tokens that bind a storefront
/// session to a nopCommerce (guest or registered) customer. The token is
/// intentionally dependency-free (no external JWT package) so the plugin stays
/// fully open-source.
/// </summary>
public class ApiTokenService
{
    #region Fields

    protected readonly HeadlessApiSettings _settings;
    protected readonly ICustomerService _customerService;

    #endregion

    #region Ctor

    public ApiTokenService(HeadlessApiSettings settings, ICustomerService customerService)
    {
        _settings = settings;
        _customerService = customerService;
    }

    #endregion

    #region Utilities

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }

        return Convert.FromBase64String(output);
    }

    private byte[] ComputeSignature(string payload)
    {
        var key = Encoding.UTF8.GetBytes(_settings.SecretKey ?? string.Empty);
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private sealed class TokenPayload
    {
        public string g { get; set; }
        public long e { get; set; }
    }

    #endregion

    #region Methods

    /// <summary>
    /// Generates a signed token for the specified customer GUID
    /// </summary>
    public string GenerateToken(Guid customerGuid)
    {
        var expirationDays = _settings.TokenExpirationDays > 0 ? _settings.TokenExpirationDays : 30;
        var payload = new TokenPayload
        {
            g = customerGuid.ToString("N"),
            e = DateTimeOffset.UtcNow.AddDays(expirationDays).ToUnixTimeSeconds()
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadPart = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signaturePart = Base64UrlEncode(ComputeSignature(payloadPart));

        return $"{payloadPart}.{signaturePart}";
    }

    /// <summary>
    /// Validates the token and resolves the associated customer; returns null when the token is missing/invalid/expired
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the customer or null.</returns>
    public async Task<Customer> GetCustomerFromTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parts = token.Split('.');
        if (parts.Length != 2)
            return null;

        //verify signature (constant-time comparison)
        var expectedSignature = ComputeSignature(parts[0]);
        byte[] providedSignature;
        try
        {
            providedSignature = Base64UrlDecode(parts[1]);
        }
        catch
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
            return null;

        TokenPayload payload;
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            payload = JsonSerializer.Deserialize<TokenPayload>(payloadJson);
        }
        catch
        {
            return null;
        }

        if (payload == null)
            return null;

        //check expiration
        if (payload.e < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return null;

        if (!Guid.TryParseExact(payload.g, "N", out var customerGuid))
            return null;

        var customer = await _customerService.GetCustomerByGuidAsync(customerGuid);
        if (customer == null || customer.Deleted || !customer.Active)
            return null;

        return customer;
    }

    #endregion
}
