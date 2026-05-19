using Nop.Core.Domain.Cms;
using Nop.Plugin.Widgets.WishlistSharing.Components;
using Nop.Services.Cms;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Widgets.WishlistSharing;

/// <summary>
/// Wishlist sharing widget plugin
/// </summary>
public class WishlistSharingPlugin : BasePlugin, IWidgetPlugin
{
    #region Fields

    protected readonly ILocalizationService _localizationService;
    protected readonly ISettingService _settingService;
    protected readonly WidgetSettings _widgetSettings;

    #endregion

    #region Ctor

    public WishlistSharingPlugin(ILocalizationService localizationService,
        ISettingService settingService,
        WidgetSettings widgetSettings)
    {
        _localizationService = localizationService;
        _settingService = settingService;
        _widgetSettings = widgetSettings;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets widget zones where this widget should be rendered
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the widget zones
    /// </returns>
    public Task<IList<string>> GetWidgetZonesAsync()
    {
        return Task.FromResult<IList<string>>(new List<string> { PublicWidgetZones.WishlistBottom });
    }

    /// <summary>
    /// Gets a type of a view component for displaying widget
    /// </summary>
    /// <param name="widgetZone">Name of the widget zone</param>
    /// <returns>View component type</returns>
    public Type GetWidgetViewComponent(string widgetZone)
    {
        ArgumentNullException.ThrowIfNull(widgetZone);

        if (widgetZone.Equals(PublicWidgetZones.WishlistBottom))
            return typeof(WishlistSharingViewComponent);

        return null;
    }

    /// <summary>
    /// Install plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        if (!_widgetSettings.ActiveWidgetSystemNames.Contains(WishlistSharingDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Add(WishlistSharingDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Widgets.WishlistSharing.Title"] = "Share your wishlist",
            ["Plugins.Widgets.WishlistSharing.Description"] = "Generate a public wishlist URL that expires automatically.",
            ["Plugins.Widgets.WishlistSharing.ExpirationDays"] = "Link expires in",
            ["Plugins.Widgets.WishlistSharing.ExpirationDays.Option"] = "{0} days",
            ["Plugins.Widgets.WishlistSharing.Generate"] = "Generate share link",
            ["Plugins.Widgets.WishlistSharing.GeneratedLink"] = "Shareable wishlist URL",
            ["Plugins.Widgets.WishlistSharing.Copy"] = "Copy",
            ["Plugins.Widgets.WishlistSharing.Copied"] = "Copied",
            ["Plugins.Widgets.WishlistSharing.Error"] = "Unable to generate a wishlist sharing link.",
            ["Plugins.Widgets.WishlistSharing.InvalidExpiration"] = "Select a supported expiration period.",
            ["Plugins.Widgets.WishlistSharing.EmptyWishlist"] = "Add items to your wishlist before sharing it."
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        if (_widgetSettings.ActiveWidgetSystemNames.Contains(WishlistSharingDefaults.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Remove(WishlistSharingDefaults.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Widgets.WishlistSharing");

        await base.UninstallAsync();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets a value indicating whether to hide this plugin on the widget list page in the admin area
    /// </summary>
    public bool HideInWidgetList => false;

    #endregion
}
