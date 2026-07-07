using FluentValidation;
using Nop.Plugin.Misc.Shopify.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.Misc.Shopify.Validators;

/// <summary>
/// Represents configuration model validator
/// </summary>
public class ConfigurationModelValidator : BaseNopValidator<ConfigurationModel>
{
    public ConfigurationModelValidator(ILocalizationService localizationService)
    {
        RuleFor(model => model.ShopDomain)
            .NotEmpty()
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.Shopify.Fields.ShopDomain.Required"));

        RuleFor(model => model.AccessToken)
            .NotEmpty()
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.Shopify.Fields.AccessToken.Required"));

        RuleFor(model => model.AutoSyncPeriod)
            .GreaterThan(0)
            .When(model => model.AutoSyncEnabled)
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.Shopify.Fields.AutoSyncPeriod.Invalid"));
    }
}
