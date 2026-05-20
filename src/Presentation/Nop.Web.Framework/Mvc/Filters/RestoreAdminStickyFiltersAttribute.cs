using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Nop.Core;
using Nop.Data;
using Nop.Services.Admin;

namespace Nop.Web.Framework.Mvc.Filters;

/// <summary>
/// Represents a filter attribute that restores admin sticky filters for list pages
/// </summary>
public sealed class RestoreAdminStickyFiltersAttribute : TypeFilterAttribute
{
    #region Ctor

    /// <summary>
    /// Create instance of the filter attribute
    /// </summary>
    public RestoreAdminStickyFiltersAttribute() : base(typeof(RestoreAdminStickyFiltersFilter))
    {
    }

    #endregion

    #region Nested filter

    /// <summary>
    /// Represents a filter that restores admin sticky filters
    /// </summary>
    private class RestoreAdminStickyFiltersFilter : IAsyncActionFilter
    {
        #region Fields

        protected readonly IAdminFilterPreferenceService _adminFilterPreferenceService;
        protected readonly IWorkContext _workContext;

        #endregion

        #region Ctor

        public RestoreAdminStickyFiltersFilter(IAdminFilterPreferenceService adminFilterPreferenceService,
            IWorkContext workContext)
        {
            _adminFilterPreferenceService = adminFilterPreferenceService;
            _workContext = workContext;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Called asynchronously before the action, after model binding is complete.
        /// </summary>
        /// <param name="context">A context for action filters</param>
        /// <param name="next">A delegate invoked to execute the next action filter or the action itself</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var resultContext = await next();

            if (!DataSettingsManager.IsDatabaseInstalled())
                return;

            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
                return;

            if (resultContext.Result is not ViewResult viewResult || viewResult.Model == null)
                return;

            if (!await _adminFilterPreferenceService.IsEnabledAsync())
                return;

            var controller = context.RouteData.Values["controller"]?.ToString();
            var action = context.RouteData.Values["action"]?.ToString();

            if (string.IsNullOrEmpty(controller) || string.IsNullOrEmpty(action))
                return;

            var customer = await _workContext.GetCurrentCustomerAsync();
            await _adminFilterPreferenceService.ApplyToViewModelAsync(customer, controller, action, viewResult.Model);
        }

        #endregion
    }

    #endregion
}
