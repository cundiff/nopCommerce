using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Configuration;
using Nop.Services.Authentication.AdminGoogle;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Models.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Areas.Admin.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin(ignore: true)]
[AutoValidateAntiforgeryToken]
public partial class AdminLoginController : BaseController
{
    #region Fields

    protected readonly AppSettings _appSettings;
    protected readonly IAdminGoogleAuthService _adminGoogleAuthService;
    protected readonly ICustomerRegistrationService _customerRegistrationService;
    protected readonly ICustomerService _customerService;
    protected readonly ILocalizationService _localizationService;
    protected readonly INotificationService _notificationService;
    protected readonly IPermissionService _permissionService;
    protected readonly IWebHelper _webHelper;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public AdminLoginController(AppSettings appSettings,
        IAdminGoogleAuthService adminGoogleAuthService,
        ICustomerRegistrationService customerRegistrationService,
        ICustomerService customerService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        IPermissionService permissionService,
        IWebHelper webHelper,
        IWorkContext workContext)
    {
        _appSettings = appSettings;
        _adminGoogleAuthService = adminGoogleAuthService;
        _customerRegistrationService = customerRegistrationService;
        _customerService = customerService;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _permissionService = permissionService;
        _webHelper = webHelper;
        _workContext = workContext;
    }

    #endregion

    #region Utilities

    protected virtual string GetDefaultReturnUrl()
    {
        return Url.Action("Index", "Home", new { area = AreaNames.ADMIN });
    }

    protected virtual string NormalizeReturnUrl(string returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && _webHelper.CheckIsLocalUrl(returnUrl))
            return returnUrl;

        return GetDefaultReturnUrl();
    }

    protected virtual AdminLoginModel PrepareLoginModel(string returnUrl)
    {
        var config = _appSettings.Get<AdminGoogleAuthConfig>();

        return new AdminLoginModel
        {
            ReturnUrl = NormalizeReturnUrl(returnUrl),
            IsGoogleEnabled = config.Enabled
        };
    }

    protected virtual async Task<IActionResult> RedirectToLoginWithErrorAsync(string returnUrl, string resourceKey)
    {
        _notificationService.ErrorNotification(await _localizationService.GetResourceAsync(resourceKey));
        return RedirectToAction(nameof(Login), new { returnUrl });
    }

    #endregion

    #region Methods

    public virtual async Task<IActionResult> Login(string returnUrl)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (await _customerService.IsRegisteredAsync(customer) &&
            await _permissionService.AuthorizeAsync(StandardPermission.Security.ACCESS_ADMIN_PANEL, customer))
        {
            return Redirect(NormalizeReturnUrl(returnUrl));
        }

        var model = PrepareLoginModel(returnUrl);
        return View(model);
    }

    public virtual async Task<IActionResult> Google(string returnUrl)
    {
        var config = _appSettings.Get<AdminGoogleAuthConfig>();
        if (!config.Enabled)
            return await RedirectToLoginWithErrorAsync(returnUrl, "Admin.Login.GoogleDisabled");

        await _adminGoogleAuthService.InitiateAsync();

        return RedirectToAction(nameof(GoogleCallback), new { returnUrl = NormalizeReturnUrl(returnUrl) });
    }

    public virtual async Task<IActionResult> GoogleCallback(string returnUrl)
    {
        var authResult = await _adminGoogleAuthService.CompleteAsync();
        if (authResult == null)
            return await RedirectToLoginWithErrorAsync(returnUrl, "Admin.Login.GoogleCallbackInvalid");

        var customer = await _customerService.GetCustomerByEmailAsync(authResult.Email);
        if (customer == null || customer.Deleted || !customer.Active)
            return await RedirectToLoginWithErrorAsync(returnUrl, "Admin.Login.CustomerNotFound");

        if (!await _customerService.IsRegisteredAsync(customer))
            return await RedirectToLoginWithErrorAsync(returnUrl, "Admin.Login.CustomerNotFound");

        if (!await _permissionService.AuthorizeAsync(StandardPermission.Security.ACCESS_ADMIN_PANEL, customer))
            return await RedirectToLoginWithErrorAsync(returnUrl, "Admin.Login.NotAuthorized");

        return await _customerRegistrationService.SignInCustomerAsync(customer, NormalizeReturnUrl(returnUrl));
    }

    #endregion
}
