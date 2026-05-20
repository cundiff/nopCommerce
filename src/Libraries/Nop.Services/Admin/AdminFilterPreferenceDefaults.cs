namespace Nop.Services.Admin;

/// <summary>
/// Represents default values related to admin filter preferences
/// </summary>
public static partial class AdminFilterPreferenceDefaults
{
    /// <summary>
    /// Gets the prefix for sticky filter preference keys
    /// </summary>
    public static string KeyPrefix => "Admin.StickyFilters";

    /// <summary>
    /// Builds a sticky filter preference key
    /// </summary>
    /// <param name="controller">Controller name</param>
    /// <param name="action">Action name</param>
    /// <param name="suffix">Optional suffix for nested search models</param>
    /// <returns>Preference key</returns>
    public static string BuildKey(string controller, string action, string suffix = null)
    {
        var key = $"{KeyPrefix}.{controller}.{action}";
        return string.IsNullOrEmpty(suffix) ? key : $"{key}.{suffix}";
    }
}
