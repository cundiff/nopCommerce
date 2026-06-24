namespace Nop.Core.Configuration;

/// <summary>
/// Represents admin Google authentication configuration parameters
/// </summary>
public partial class AdminGoogleAuthConfig : IConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether admin Google sign-in is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the stub email returned by the Google auth stub (must match an admin customer)
    /// </summary>
    public string StubEmail { get; set; } = "admin@yourstore.com";

    /// <summary>
    /// Gets or sets the stub external identifier returned by the Google auth stub
    /// </summary>
    public string StubExternalId { get; set; } = "google-stub-admin-001";

    /// <summary>
    /// Gets or sets the stub display name returned by the Google auth stub
    /// </summary>
    public string StubDisplayName { get; set; } = "Admin User";
}
