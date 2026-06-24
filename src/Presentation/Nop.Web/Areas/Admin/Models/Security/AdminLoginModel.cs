namespace Nop.Web.Areas.Admin.Models.Security;

/// <summary>
/// Represents an admin login model
/// </summary>
public partial record AdminLoginModel
{
    /// <summary>
    /// Gets or sets the return URL
    /// </summary>
    public string ReturnUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Google sign-in is enabled
    /// </summary>
    public bool IsGoogleEnabled { get; set; }
}
