using Microsoft.AspNetCore.Http;
using Nop.Core.Configuration;
using Nop.Core.Http.Extensions;

namespace Nop.Services.Authentication.AdminGoogle;

/// <summary>
/// Stub implementation of admin Google authentication (no real Google API calls)
/// </summary>
public partial class StubAdminGoogleAuthService : IAdminGoogleAuthService
{
    #region Fields

    protected readonly AppSettings _appSettings;
    protected readonly IHttpContextAccessor _httpContextAccessor;

    #endregion

    #region Ctor

    public StubAdminGoogleAuthService(AppSettings appSettings,
        IHttpContextAccessor httpContextAccessor)
    {
        _appSettings = appSettings;
        _httpContextAccessor = httpContextAccessor;
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public virtual async Task InitiateAsync()
    {
        var session = _httpContextAccessor.HttpContext?.Session
                      ?? throw new InvalidOperationException("Session is not available");

        await session.SetAsync(AdminGoogleAuthDefaults.PendingSessionKey, true);
    }

    /// <inheritdoc />
    public virtual async Task<AdminGoogleAuthResult> CompleteAsync()
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null)
            return null;

        var isPending = await session.GetAsync<bool>(AdminGoogleAuthDefaults.PendingSessionKey);
        if (!isPending)
            return null;

        await session.RemoveAsync(AdminGoogleAuthDefaults.PendingSessionKey);

        var config = _appSettings.Get<AdminGoogleAuthConfig>();
        if (!config.Enabled)
            return null;

        return new AdminGoogleAuthResult
        {
            Email = config.StubEmail,
            ExternalId = config.StubExternalId,
            DisplayName = config.StubDisplayName
        };
    }

    #endregion
}
