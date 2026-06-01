using Nop.Services.Configuration;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Sends cache-revalidation webhooks to the storefront when catalog data changes
/// </summary>
public class WebhookNotificationService
{
    #region Fields

    protected readonly HttpClient _httpClient;
    protected readonly ILogger _logger;
    protected readonly ISettingService _settingService;

    #endregion

    #region Ctor

    public WebhookNotificationService(HttpClient httpClient,
        ILogger logger,
        ISettingService settingService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settingService = settingService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Notifies the storefront that entities of the given topic have changed
    /// </summary>
    /// <param name="topic">Topic value (see <see cref="HeadlessApiDefaults"/>)</param>
    public virtual async Task NotifyAsync(string topic)
    {
        var settings = await _settingService.LoadSettingAsync<HeadlessApiSettings>();
        if (!settings.EnableWebhooks || string.IsNullOrWhiteSpace(settings.RevalidationWebhookUrl))
            return;

        try
        {
            var separator = settings.RevalidationWebhookUrl.Contains('?') ? "&" : "?";
            var url = $"{settings.RevalidationWebhookUrl}{separator}secret={Uri.EscapeDataString(settings.RevalidationSecret ?? string.Empty)}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation(HeadlessApiDefaults.WebhookTopicHeaderName, topic);
            request.Content = new StringContent(string.Empty);

            await _httpClient.SendAsync(request);
        }
        catch (Exception exception)
        {
            await _logger.ErrorAsync($"Headless API: failed to send revalidation webhook for topic '{topic}'", exception);
        }
    }

    #endregion
}
