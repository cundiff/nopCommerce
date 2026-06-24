namespace Nop.Services.Authentication.AdminGoogle;

/// <summary>
/// Represents the result of a stubbed admin Google authentication
/// </summary>
public partial class AdminGoogleAuthResult
{
    /// <summary>
    /// Gets or sets the email address
    /// </summary>
    public string Email { get; set; }

    /// <summary>
    /// Gets or sets the external identifier
    /// </summary>
    public string ExternalId { get; set; }

    /// <summary>
    /// Gets or sets the display name
    /// </summary>
    public string DisplayName { get; set; }
}
