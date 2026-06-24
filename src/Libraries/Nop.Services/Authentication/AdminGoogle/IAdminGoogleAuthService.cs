namespace Nop.Services.Authentication.AdminGoogle;

/// <summary>
/// Admin Google authentication service interface
/// </summary>
public partial interface IAdminGoogleAuthService
{
    /// <summary>
    /// Initiates the admin Google authentication flow
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    Task InitiateAsync();

    /// <summary>
    /// Completes the admin Google authentication flow and returns stub profile data
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the authentication result, or null if the flow is invalid
    /// </returns>
    Task<AdminGoogleAuthResult> CompleteAsync();
}
