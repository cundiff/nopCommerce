using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Admin;
using Nop.Services.Common;

namespace Nop.Web.Areas.Admin.Controllers;

public partial class PreferencesController : BaseAdminController
{
    #region Fields

    protected readonly IAdminFilterPreferenceService _adminFilterPreferenceService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public PreferencesController(IAdminFilterPreferenceService adminFilterPreferenceService,
        IGenericAttributeService genericAttributeService,
        IWorkContext workContext)
    {
        _adminFilterPreferenceService = adminFilterPreferenceService;
        _genericAttributeService = genericAttributeService;
        _workContext = workContext;
    }

    #endregion

    #region Methods

    [HttpPost]
    public virtual async Task<IActionResult> SavePreference(string name, bool value)
    {
        //permission validation is not required here
        ArgumentException.ThrowIfNullOrEmpty(name);

        await _genericAttributeService.SaveAttributeAsync(await _workContext.GetCurrentCustomerAsync(), name, value);

        return Json(new
        {
            Result = true
        });
    }

    [HttpPost]
    public virtual async Task<IActionResult> SaveFilterPreference(string key, string filtersJson)
    {
        //permission validation is not required here
        ArgumentException.ThrowIfNullOrEmpty(key);

        await _adminFilterPreferenceService.SaveFromJsonAsync(await _workContext.GetCurrentCustomerAsync(), key, filtersJson);

        return Json(new
        {
            Result = true
        });
    }

    #endregion
}