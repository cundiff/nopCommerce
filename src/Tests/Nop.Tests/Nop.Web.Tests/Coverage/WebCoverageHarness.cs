using System.Collections;
using System.Reflection;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Stores;
using Nop.Core.Events;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Services.Stores;
using Nop.Web.Framework;
using Nop.Web.Framework.Models;

namespace Nop.Tests.Nop.Web.Tests.Coverage;

/// <summary>
/// Invokes Nop.Web surface methods with sample-data arguments so coverable lines run.
/// Destructive methods (Delete/Import/Uninstall/ConfirmOrder) are skipped.
/// Save-style actions are first invoked with invalid ModelState, then GET→POST pairs
/// re-run the same actions with a factory-prepared model and valid ModelState.
/// </summary>
public sealed class WebCoverageHarness
{
    private static readonly Lazy<IServiceProvider> MvcServices = new(CreateMvcServices);

    private static IServiceProvider CreateMvcServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddMvc();
        return services.BuildServiceProvider();
    }

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RoundTripTimeout = TimeSpan.FromSeconds(90);

    private static readonly Dictionary<string, Type> EntityTypes =
        typeof(global::Nop.Core.Domain.Catalog.Product).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseEntity).IsAssignableFrom(t))
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private readonly IServiceProvider _services;
    private readonly Dictionary<Type, BaseEntity> _entities = [];
    private readonly Dictionary<Type, IList<BaseEntity>> _entityLists = [];

    public WebCoverageHarness(IServiceProvider services)
    {
        _services = services;
    }

    public int TypesCreated { get; private set; }
    public int MethodsInvoked { get; private set; }
    public int MethodsFailed { get; private set; }
    public int TypesFailed { get; private set; }
    public int ValidPosts { get; private set; }
    public List<string> Failures { get; } = [];

    public T CreateController<T>() where T : Controller
    {
        var instance = ActivatorUtilities.CreateInstance<T>(_services);
        AttachMvc(instance);
        TypesCreated++;
        return instance;
    }

    public async Task EnsureSecondStoreAsync()
    {
        var storeService = _services.GetRequiredService<IStoreService>();
        var stores = await storeService.GetAllStoresAsync();
        if (stores.Count >= 2)
            return;

        var source = stores.First();
        await storeService.InsertStoreAsync(new Store
        {
            Name = "Coverage Store",
            Url = "http://coverage.local/",
            Hosts = "coverage.local",
            SslEnabled = source.SslEnabled,
            DefaultLanguageId = source.DefaultLanguageId,
            DisplayOrder = 99,
            CompanyName = source.CompanyName ?? "Coverage",
            CompanyAddress = source.CompanyAddress,
            CompanyPhoneNumber = source.CompanyPhoneNumber,
            CompanyVat = source.CompanyVat,
            DefaultTitle = source.DefaultTitle,
            DefaultMetaDescription = source.DefaultMetaDescription,
            DefaultMetaKeywords = source.DefaultMetaKeywords,
            HomepageTitle = source.HomepageTitle,
            HomepageDescription = source.HomepageDescription
        });
    }

    public async Task SetAdminStoreScopeAsync(int storeId)
    {
        var customer = await _services.GetRequiredService<IWorkContext>().GetCurrentCustomerAsync();
        await _services.GetRequiredService<IGenericAttributeService>()
            .SaveAttributeAsync(customer, NopCustomerDefaults.AdminAreaStoreScopeConfigurationAttribute, storeId);

        if (_services.GetService<IStoreContext>() is WebStoreContext webStore)
        {
            typeof(WebStoreContext)
                .GetField("_cachedActiveStoreScopeConfiguration", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(webStore, null);
            typeof(WebStoreContext)
                .GetField("_cachedStore", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(webStore, null);
        }
    }

    public async Task EnableAdminStoreScopeAsync()
    {
        await EnsureSecondStoreAsync();
        var stores = await _services.GetRequiredService<IStoreService>().GetAllStoresAsync();
        await SetAdminStoreScopeAsync(stores.Last().Id);
    }

    public async Task EnableCheckoutTestPluginsAsync()
    {
        var shipping = _services.GetRequiredService<ShippingSettings>();
        shipping.ActiveShippingRateComputationMethodSystemNames ??= [];
        if (!shipping.ActiveShippingRateComputationMethodSystemNames
                .Contains("FixedRateTestShippingRateComputationMethod", StringComparer.OrdinalIgnoreCase))
        {
            shipping.ActiveShippingRateComputationMethodSystemNames.Add("FixedRateTestShippingRateComputationMethod");
        }

        var payment = _services.GetRequiredService<PaymentSettings>();
        payment.ActivePaymentMethodSystemNames ??= [];
        if (!payment.ActivePaymentMethodSystemNames.Contains("Payments.TestMethod", StringComparer.OrdinalIgnoreCase))
            payment.ActivePaymentMethodSystemNames.Add("Payments.TestMethod");

        var settings = _services.GetRequiredService<ISettingService>();
        await settings.SaveSettingAsync(shipping);
        await settings.SaveSettingAsync(payment);
    }

    public async Task SeedShoppingCartAsync()
    {
        var workContext = _services.GetRequiredService<IWorkContext>();
        var customer = await workContext.GetCurrentCustomerAsync();
        var store = await _services.GetRequiredService<IStoreContext>().GetCurrentStoreAsync();
        var cartService = _services.GetRequiredService<IShoppingCartService>();
        var products = await _services.GetRequiredService<IProductService>().SearchProductsAsync(pageSize: 20);

        foreach (var product in products)
        {
            try
            {
                await cartService.AddToCartAsync(customer, product, ShoppingCartType.ShoppingCart, store.Id,
                    quantity: 1, addRequiredProducts: false);
            }
            catch
            {
                // attribute-required products are skipped
            }
        }
    }

    public async Task ExerciseEditRoundTripsAsync(IEnumerable<Type> controllerTypes)
        => await ExerciseGetPostPairsAsync(controllerTypes);

    public async Task ExerciseGetPostPairsAsync(IEnumerable<Type> controllerTypes)
    {
        foreach (var type in controllerTypes)
        {
            object controller;
            try
            {
                controller = ActivatorUtilities.CreateInstance(_services, type);
                AttachMvc(controller);
                TypesCreated++;
            }
            catch (Exception ex)
            {
                TypesFailed++;
                Failures.Add($"{type.Name}: create {ex.GetBaseException().Message}");
                continue;
            }

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.DeclaringType == type)
                .ToList();

            foreach (var group in methods.GroupBy(m => m.Name))
            {
                if (ShouldSkipMethodName(group.Key) || IsCreateLike(group.Key))
                    continue;

                var posts = group.Where(m =>
                        m.GetParameters().Any(p => p.ParameterType.Name.EndsWith("Model", StringComparison.Ordinal)
                                                   && !p.ParameterType.Name.Contains("Search", StringComparison.Ordinal)))
                    .ToList();
                if (posts.Count == 0)
                    continue;

                var gets = group.Where(m =>
                {
                    var ps = m.GetParameters();
                    return ps.Length == 0
                           || (ps.Length == 1 && (ps[0].ParameterType == typeof(int) || ps[0].ParameterType == typeof(int?)));
                }).ToList();

                foreach (var post in posts)
                {
                    var modelParam = post.GetParameters().First(p => p.ParameterType.Name.EndsWith("Model", StringComparison.Ordinal));
                    object prepared = null;

                    foreach (var get in gets)
                    {
                        var getArgs = get.GetParameters().Select(p =>
                            p.ParameterType == typeof(int) || p.ParameterType == typeof(int?)
                                ? (object)ResolveId(p.Name, type, modelParam.ParameterType)
                                : CreateArg(p.ParameterType, p.Name, type)).ToArray();

                        var (ok, result) = await TryInvokeAsync(controller, get, getArgs, RoundTripTimeout);
                        if (!ok)
                            continue;

                        MethodsInvoked++;
                        prepared = (result as ViewResult)?.Model;
                        if (prepared != null && modelParam.ParameterType.IsInstanceOfType(prepared))
                            break;
                        prepared = null;
                    }

                    if (prepared == null)
                    {
                        prepared = CreateArg(modelParam.ParameterType, modelParam.Name, type);
                        var idProp = modelParam.ParameterType.GetProperty("Id");
                        if (idProp?.CanWrite == true && idProp.PropertyType == typeof(int))
                        {
                            var id = ResolveId("id", type, modelParam.ParameterType);
                            if (id > 0)
                                idProp.SetValue(prepared, id);
                        }
                    }

                    if (prepared == null)
                        continue;

                    if (controller is Controller mvc)
                        mvc.ModelState.Clear();

                    var postArgs = post.GetParameters().Select(p =>
                    {
                        if (p.Name == "continueEditing")
                            return (object)true;
                        if (p.ParameterType.IsInstanceOfType(prepared) || p.ParameterType.IsAssignableFrom(prepared.GetType()))
                            return prepared;
                        return CreateArg(p.ParameterType, p.Name, type);
                    }).ToArray();

                    var (posted, _) = await TryInvokeAsync(controller, post, postArgs, RoundTripTimeout);
                    if (posted)
                    {
                        MethodsInvoked++;
                        ValidPosts++;
                    }
                    else
                    {
                        MethodsFailed++;
                    }

                    if (controller is Controller clear)
                        clear.ModelState.Clear();
                }
            }
        }
    }

    public async Task ExerciseTypesAsync(IEnumerable<Type> types, bool asMvc)
    {
        foreach (var type in types)
            await ExerciseTypeAsync(type, asMvc);
    }

    public async Task ExerciseTypeAsync(Type type, bool asMvc)
    {
        object instance;
        try
        {
            instance = ActivatorUtilities.CreateInstance(_services, type);
            TypesCreated++;
        }
        catch (Exception ex)
        {
            TypesFailed++;
            Failures.Add($"{type.FullName}: create {ex.GetBaseException().Message}");
            return;
        }

        if (asMvc)
            AttachMvc(instance);

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName
                        && m.DeclaringType == type
                        && !m.IsAbstract
                        && !m.Name.Contains('<', StringComparison.Ordinal)
                        && !ShouldSkipMethod(m));

        foreach (var method in methods)
        {
            await InvokeMethodAsync(instance, method, boolOverrides: false, nullEntities: false, extraEntity: null);

            if (method.GetParameters().Any(p => p.ParameterType == typeof(bool) || p.ParameterType == typeof(bool?)))
                await InvokeMethodAsync(instance, method, boolOverrides: true, nullEntities: false, extraEntity: null);

            if (method.GetParameters().Any(p => typeof(BaseEntity).IsAssignableFrom(p.ParameterType)))
                await InvokeMethodAsync(instance, method, boolOverrides: false, nullEntities: true, extraEntity: null);

            if (!asMvc && method.Name.StartsWith("Prepare", StringComparison.Ordinal))
            {
                var entityParam = method.GetParameters()
                    .FirstOrDefault(p => typeof(BaseEntity).IsAssignableFrom(p.ParameterType));
                if (entityParam != null)
                {
                    foreach (var extra in GetEntities(entityParam.ParameterType, 4))
                        await InvokeMethodAsync(instance, method, boolOverrides: false, nullEntities: false, extraEntity: extra);
                }
            }
        }
    }

    public async Task ExerciseCheckoutFlowAsync()
    {
        await EnableCheckoutTestPluginsAsync();
        await SeedShoppingCartAsync();

        var checkoutType = typeof(global::Nop.Web.Controllers.CheckoutController);
        object checkout;
        try
        {
            checkout = ActivatorUtilities.CreateInstance(_services, checkoutType);
            AttachMvc(checkout);
            TypesCreated++;
        }
        catch (Exception ex)
        {
            TypesFailed++;
            Failures.Add($"CheckoutController: create {ex.GetBaseException().Message}");
            return;
        }

        var customer = await _services.GetRequiredService<IWorkContext>().GetCurrentCustomerAsync();
        var addresses = await _services.GetRequiredService<ICustomerService>().GetAddressesByCustomerIdAsync(customer.Id);
        var addressId = addresses.FirstOrDefault()?.Id ?? GetEntityIds("Address").FirstOrDefault();

        var formValues = new Dictionary<string, StringValues>();
        if (addressId > 0)
        {
            formValues["billing_address_id"] = addressId.ToString();
            formValues["shipping_address_id"] = addressId.ToString();
        }

        var form = new FormCollection(formValues);

        async Task Call(string name, params object[] args)
        {
            var method = checkoutType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
            if (method == null)
                return;
            var (ok, _) = await TryInvokeAsync(checkout, method, args, RoundTripTimeout);
            if (ok)
                MethodsInvoked++;
            else
                MethodsFailed++;
        }

        await Call("Index");
        await Call("OnePageCheckout");
        await Call("BillingAddress", form);
        if (addressId > 0)
            await Call("SelectBillingAddress", addressId, true);

        var billingModel = CreateArg(typeof(global::Nop.Web.Models.Checkout.CheckoutBillingAddressModel), "model", checkoutType);
        await Call("OpcSaveBilling", billingModel, form);
        await Call("NewBillingAddress", billingModel, form);
        await Call("SaveEditBillingAddress", billingModel, form, true);

        await Call("ShippingAddress");
        if (addressId > 0)
            await Call("SelectShippingAddress", addressId);

        var shippingModel = CreateArg(typeof(global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel), "model", checkoutType);
        await Call("OpcSaveShipping", shippingModel, form);
        await Call("NewShippingAddress", shippingModel, form);
        await Call("SaveEditShippingAddress", shippingModel, form, true);

        var shippingOption = "Shipping option 1___FixedRateTestShippingRateComputationMethod";
        try
        {
            var checkoutFactory = _services.GetRequiredService<global::Nop.Web.Factories.ICheckoutModelFactory>();
            var address = await _services.GetRequiredService<ICustomerService>().GetCustomerShippingAddressAsync(customer)
                          ?? addresses.FirstOrDefault();
            var cart = await _services.GetRequiredService<IShoppingCartService>()
                .GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart,
                    (await _services.GetRequiredService<IStoreContext>().GetCurrentStoreAsync()).Id);
            var shippingMethods = await checkoutFactory.PrepareShippingMethodModelAsync(cart, address);
            var selected = shippingMethods?.ShippingMethods?.FirstOrDefault();
            if (selected != null)
                shippingOption = $"{selected.Name}___{selected.ShippingRateComputationMethodSystemName}";
        }
        catch
        {
            // keep the test plugin option string
        }

        var orderSettings = _services.GetRequiredService<OrderSettings>();
        var previousOpc = orderSettings.OnePageCheckoutEnabled;
        orderSettings.OnePageCheckoutEnabled = false;
        try
        {
            await Call("ShippingMethod");
            await Call("SelectShippingMethod", shippingOption, form);
            await Call("PaymentMethod");
            var paymentModel = CreateArg(typeof(global::Nop.Web.Models.Checkout.CheckoutPaymentMethodModel), "model", checkoutType);
            await Call("SelectPaymentMethod", "Payments.TestMethod", paymentModel);
            await Call("PaymentInfo");
            await Call("EnterPaymentInfo", form);
            await Call("Confirm");
        }
        finally
        {
            orderSettings.OnePageCheckoutEnabled = previousOpc;
        }

        await Call("OpcSaveShippingMethod", shippingOption, form);
        await Call("PaymentMethod");
        var opcPayment = CreateArg(typeof(global::Nop.Web.Models.Checkout.CheckoutPaymentMethodModel), "model", checkoutType);
        await Call("SelectPaymentMethod", "Payments.TestMethod", opcPayment);
        await Call("OpcSavePaymentMethod", "Payments.TestMethod", opcPayment);
        await Call("PaymentInfo");
        await Call("EnterPaymentInfo", form);
        await Call("OpcSavePaymentInfo", form);
        await Call("Confirm");
        await Call("Completed", (int?)null);
        await Call("GetAddressById", addressId);
        await Call("OpcCompleteRedirectionPayment");
    }

    private async Task InvokeMethodAsync(object instance, MethodInfo method, bool boolOverrides, bool nullEntities, BaseEntity extraEntity)
    {
        try
        {
            if (instance is Controller controller && IsLikelyMutating(method))
                controller.ModelState.AddModelError("_coverage", "do not persist");

            var args = method.GetParameters().Select(p =>
            {
                if (nullEntities && typeof(BaseEntity).IsAssignableFrom(p.ParameterType))
                    return null;
                if (extraEntity != null && p.ParameterType.IsInstanceOfType(extraEntity))
                    return extraEntity;
                if (boolOverrides && (p.ParameterType == typeof(bool) || p.ParameterType == typeof(bool?)))
                    return true;
                return CreateArg(p.ParameterType, p.Name, instance.GetType());
            }).ToArray();

            var (ok, _) = await TryInvokeAsync(instance, method, args, DefaultTimeout);
            if (ok)
                MethodsInvoked++;
            else
            {
                MethodsFailed++;
                if (Failures.Count < 80)
                    Failures.Add($"{instance.GetType().Name}.{method.Name}: invoke failed or timed out");
            }
        }
        catch (Exception ex)
        {
            MethodsFailed++;
            var message = ex.GetBaseException().Message;
            if (Failures.Count < 80)
                Failures.Add($"{instance.GetType().Name}.{method.Name}: {message}");
        }
        finally
        {
            if (instance is Controller controller)
                controller.ModelState.Clear();
        }
    }

    public async Task ExerciseRazorPagesAsync(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            try
            {
                var instance = Activator.CreateInstance(type);
                if (instance == null)
                    continue;

                TypesCreated++;

                var http = _services.GetRequiredService<IHttpContextAccessor>().HttpContext
                           ?? throw new InvalidOperationException("HttpContext is not available");
                var previousServices = http.RequestServices;
                http.RequestServices = new CompositeServiceProvider(MvcServices.Value, _services);
                try
                {
                    AttachMvc(instance);
                    FillRazorModelGraph(instance);

                    if (instance is RazorPageBase razorPage)
                    {
                        razorPage.Layout = null;
                        razorPage.HtmlEncoder ??= HtmlEncoder.Default;
                        try
                        {
                            MvcServices.Value.GetService<IRazorPageActivator>()
                                ?.Activate(razorPage, razorPage.ViewContext);
                        }
                        catch
                        {
                            // continue with manually attached helpers
                        }
                    }

                    var execute = type.GetMethod("ExecuteAsync", BindingFlags.Public | BindingFlags.Instance);
                    if (execute == null)
                        continue;

                    try
                    {
                        var raw = execute.Invoke(instance, null);
                        if (raw is Task task)
                        {
                            var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(3)));
                            if (finished != task)
                            {
                                MethodsFailed++;
                                continue;
                            }

                            await task;
                        }

                        MethodsInvoked++;
                    }
                    catch (Exception ex)
                    {
                        MethodsFailed++;
                        if (Failures.Count < 40)
                            Failures.Add($"{type.Name}: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
                    }
                }
                finally
                {
                    http.RequestServices = previousServices;
                }
            }
            catch (Exception ex)
            {
                MethodsFailed++;
                if (Failures.Count < 80)
                    Failures.Add($"{type.Name}: {ex.GetBaseException().Message}");
            }
        }
    }

    public void ExerciseModelProperties(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            object instance;
            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch
            {
                try
                {
                    instance = ActivatorUtilities.CreateInstance(_services, type);
                }
                catch
                {
                    TypesFailed++;
                    continue;
                }
            }

            if (instance == null)
                continue;

            TypesCreated++;

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                try
                {
                    if (prop.CanWrite && prop.GetIndexParameters().Length == 0)
                    {
                        var value = CreateArg(prop.PropertyType, prop.Name, type);
                        prop.SetValue(instance, value);
                    }

                    if (prop.CanRead && prop.GetIndexParameters().Length == 0)
                        _ = prop.GetValue(instance);

                    MethodsInvoked++;
                }
                catch
                {
                    MethodsFailed++;
                }
            }
        }
    }

    public async Task ExerciseStaticTypeAsync(Type type)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(m => !m.IsSpecialName && !m.IsGenericMethodDefinition))
        {
            try
            {
                var args = method.GetParameters().Select(p => CreateArg(p.ParameterType, p.Name, type)).ToArray();
                var (ok, _) = await TryInvokeAsync(null, method, args, DefaultTimeout);
                if (ok)
                    MethodsInvoked++;
                else
                    MethodsFailed++;
            }
            catch (Exception ex)
            {
                MethodsFailed++;
                if (Failures.Count < 80)
                    Failures.Add($"{type.Name}.{method.Name}: {ex.GetBaseException().Message}");
            }
        }
    }

    public async Task ExerciseValidatorsAsync(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            object validator;
            try
            {
                validator = ActivatorUtilities.CreateInstance(_services, type);
                TypesCreated++;
            }
            catch (Exception ex)
            {
                TypesFailed++;
                Failures.Add($"{type.FullName}: create {ex.GetBaseException().Message}");
                continue;
            }

            var modelType = type.BaseType?.GetGenericArguments().FirstOrDefault();
            if (modelType == null)
                continue;

            try
            {
                var model = CreateArg(modelType, "model", type);
                var validate = validator.GetType().GetMethods()
                    .FirstOrDefault(m => m.Name == "Validate" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == modelType);
                validate?.Invoke(validator, [model]);
                MethodsInvoked++;
            }
            catch (Exception ex)
            {
                MethodsFailed++;
                if (Failures.Count < 80)
                    Failures.Add($"{type.Name}.Validate: {ex.GetBaseException().Message}");
            }

            await Task.CompletedTask;
        }
    }

    public void AttachMvc(object instance)
    {
        var http = _services.GetRequiredService<IHttpContextAccessor>().HttpContext
                   ?? throw new InvalidOperationException("HttpContext is not available");
        if (instance is RazorPageBase)
            http.RequestServices = new CompositeServiceProvider(MvcServices.Value, _services);
        else
            http.RequestServices = _services;

        var routeData = http.GetRouteData() ?? new RouteData();
        var actionContext = new ActionContext(http, routeData, new ControllerActionDescriptor());
        var url = _services.GetRequiredService<IUrlHelperFactory>().GetUrlHelper(actionContext);
        var tempData = _services.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);

        Type modelType = null;
        for (var t = instance.GetType(); t != null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(RazorPage<>))
            {
                modelType = t.GetGenericArguments()[0];
                break;
            }
        }

        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
        if (modelType != null)
        {
            try
            {
                viewData.Model = CreateArg(modelType, "Model", instance.GetType());
            }
            catch
            {
                // compiled views still execute as far as they can with an empty model
            }
        }

        var viewContext = new ViewContext(
            actionContext,
            NullView.Instance,
            viewData,
            tempData,
            TextWriter.Null,
            new HtmlHelperOptions());

        if (instance is Controller controller)
        {
            controller.ControllerContext = new ControllerContext(actionContext);
            controller.Url = url;
            controller.TempData = tempData;
            controller.ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), controller.ModelState);
        }

        if (instance is ViewComponent component)
        {
            component.ViewComponentContext = new ViewComponentContext
            {
                ViewContext = viewContext
            };
        }

        if (instance is RazorPageBase razorPage)
        {
            razorPage.ViewContext = viewContext;
            razorPage.Layout = null;
            razorPage.HtmlEncoder ??= HtmlEncoder.Default;
            ActivateRazorInjects(instance, viewContext);
        }
    }

    private void ActivateRazorInjects(object instance, ViewContext viewContext)
    {
        for (var type = instance.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (prop.GetIndexParameters().Length != 0)
                    continue;

                var setter = prop.GetSetMethod(true);
                if (setter == null)
                    continue;

                object value = null;
                var propertyType = prop.PropertyType;
                try
                {
                    if (prop.Name is "Html" or "Component" or "Json"
                        || (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(IHtmlHelper<>)))
                    {
                        value = EmptyProxy.Create(propertyType, viewContext);
                    }
                    else if (propertyType == typeof(IUrlHelper) || prop.Name == "Url")
                    {
                        value = _services.GetRequiredService<IUrlHelperFactory>().GetUrlHelper(viewContext);
                    }
                    else if (propertyType.IsInterface)
                    {
                        value = _services.GetService(propertyType) ?? EmptyProxy.Create(propertyType, viewContext);
                    }
                    else
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if (value == null)
                    continue;

                try
                {
                    setter.Invoke(instance, [value]);
                }
                catch
                {
                    // compiled pages expose a subset of these
                }

                if (value is IViewContextAware aware)
                {
                    try
                    {
                        aware.Contextualize(viewContext);
                    }
                    catch
                    {
                        // optional
                    }
                }
            }
        }
    }

    private void FillRazorModelGraph(object page)
    {
        try
        {
            var modelProp = page.GetType().GetProperty("Model", BindingFlags.Public | BindingFlags.Instance);
            var model = modelProp?.GetValue(page) ?? (page as RazorPageBase)?.ViewContext?.ViewData?.Model;
            FillGraph(model, 0);
        }
        catch
        {
            // dummy models still let ExecuteAsync start
        }
    }

    private void FillGraph(object model, int depth)
    {
        if (model == null || depth > 3)
            return;

        var type = model.GetType();
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum)
            return;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanWrite || prop.GetIndexParameters().Length != 0)
                continue;

            try
            {
                var current = prop.CanRead ? prop.GetValue(model) : null;
                if (prop.PropertyType == typeof(string) && current == null)
                {
                    prop.SetValue(model, prop.Name);
                }
                else if (typeof(BaseNopModel).IsAssignableFrom(prop.PropertyType))
                {
                    if (current == null)
                    {
                        current = CreateDefault(prop.PropertyType);
                        if (current != null)
                            prop.SetValue(model, current);
                    }

                    FillGraph(current, depth + 1);
                }
                else if (current is IEnumerable enumerable && current is not string)
                {
                    foreach (var item in enumerable)
                    {
                        FillGraph(item, depth + 1);
                        break;
                    }
                }
            }
            catch
            {
                // skip unreadable properties
            }
        }
    }

    private static bool ShouldSkipMethodName(string name)
    {
        return name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Import", StringComparison.OrdinalIgnoreCase)
               || name.Contains("ClearCache", StringComparison.OrdinalIgnoreCase)
               || name.Contains("GenerateAll", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Rss", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Restart", StringComparison.OrdinalIgnoreCase)
               || name.Contains("ConfirmOrder", StringComparison.OrdinalIgnoreCase)
               || name.Equals("OpcConfirmOrder", StringComparison.Ordinal)
               || name.Contains("ChangeEncryptionKey", StringComparison.OrdinalIgnoreCase)
               || name.Contains("UploadPlugin", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Install", StringComparison.OrdinalIgnoreCase)
               || name.Contains("ReloadList", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCreateLike(string name)
    {
        return name.Contains("Create", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Register", StringComparison.Ordinal)
               || name.Equals("AddProductToCart_Catalog", StringComparison.Ordinal)
               || name.Equals("AddProductToCart_Details", StringComparison.Ordinal);
    }

    private static bool ShouldSkipMethod(MethodInfo method)
    {
        var name = method.Name;
        if (ShouldSkipMethodName(name))
            return true;

        if (name.Equals("Index", StringComparison.Ordinal) && method.GetParameters().Length > 0 && method.DeclaringType?.Name == "InstallController")
            return true;

        if (method.GetParameters().Any(p =>
                typeof(IFormFile).IsAssignableFrom(p.ParameterType)
                || p.ParameterType.IsByRef
                || p.ParameterType == typeof(CancellationToken)))
            return true;

        return false;
    }

    private static bool IsLikelyMutating(MethodInfo method)
    {
        var name = method.Name;
        if (method.GetParameters().Any(p => p.Name == "continueEditing"))
            return true;

        if (name.Contains("Save", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Update", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Copy", StringComparison.OrdinalIgnoreCase)
            || (name.Contains("Create", StringComparison.OrdinalIgnoreCase) && method.GetParameters().Length > 0)
            || (name.Contains("Add", StringComparison.OrdinalIgnoreCase) && method.GetParameters().Any(p =>
                p.ParameterType.Name.EndsWith("Model", StringComparison.Ordinal) &&
                !p.ParameterType.Name.Contains("Search", StringComparison.Ordinal))))
            return true;

        return method.GetParameters().Any(p => typeof(IFormCollection).IsAssignableFrom(p.ParameterType));
    }

    private int ResolveId(string parameterName, Type ownerType, Type modelType = null)
    {
        if (modelType != null)
        {
            var fromModel = EntityNameFromModel(modelType);
            var ids = GetEntityIds(fromModel);
            if (ids.Count > 0)
                return ids[0];
        }

        if (!string.IsNullOrEmpty(parameterName) && parameterName.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
            && !parameterName.Equals("id", StringComparison.OrdinalIgnoreCase))
        {
            var entityName = parameterName[..^2];
            var ids = GetEntityIds(entityName);
            if (ids.Count > 0)
                return ids[0];
        }

        if (ownerType?.Name.EndsWith("Controller", StringComparison.Ordinal) == true)
        {
            var ids = GetEntityIds(ownerType.Name.Replace("Controller", string.Empty));
            if (ids.Count > 0)
                return ids[0];
        }

        var products = GetEntityIds("Product");
        return products.Count > 0 ? products[0] : 1;
    }

    private static string EntityNameFromModel(Type modelType)
    {
        var name = modelType.Name;
        if (name.EndsWith("SearchModel", StringComparison.Ordinal))
            return name.Replace("SearchModel", string.Empty);
        if (name.EndsWith("ListModel", StringComparison.Ordinal))
            return name.Replace("ListModel", string.Empty);
        if (name.EndsWith("Model", StringComparison.Ordinal))
            return name[..^5];
        return name;
    }

    private object CreateArg(Type type, string name, Type ownerType)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string))
            return name?.Contains("email", StringComparison.OrdinalIgnoreCase) == true ? "admin@yourStore.com" : "test";
        if (type == typeof(bool))
        {
            if (string.Equals(name, "ShipToSameAddress", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "continueEditing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "isEditable", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "excludeProperties", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
        {
            var id = ResolveId(name, ownerType);
            return Convert.ChangeType(id, type);
        }

        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
            return Convert.ChangeType(1, type);
        if (type == typeof(Guid))
            return Guid.Empty;
        if (type == typeof(DateTime))
            return DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
        if (type == typeof(DateTimeOffset))
            return DateTimeOffset.Now;
        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(0);
        if (type == typeof(CancellationToken))
            return CancellationToken.None;
        if (type == typeof(StringValues))
            return new StringValues("1");
        if (type == typeof(IFormCollection) || type == typeof(FormCollection))
        {
            var values = new Dictionary<string, StringValues>();
            var addressId = GetEntityIds("Address").FirstOrDefault();
            if (addressId > 0)
            {
                values["billing_address_id"] = addressId.ToString();
                values["shipping_address_id"] = addressId.ToString();
            }

            return new FormCollection(values);
        }

        if (type == typeof(IUrlHelper))
            return _services.GetRequiredService<IUrlHelperFactory>()
                .GetUrlHelper(new ActionContext(
                    _services.GetRequiredService<IHttpContextAccessor>().HttpContext,
                    new RouteData(),
                    new ActionDescriptor()));

        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType()!, 0);

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(EntityInsertedEvent<>) || def == typeof(EntityUpdatedEvent<>) || def == typeof(EntityDeletedEvent<>))
            {
                var entityType = type.GetGenericArguments()[0];
                var entity = GetEntity(entityType) ?? CreateDefault(entityType);
                return Activator.CreateInstance(type, entity);
            }

            if (def == typeof(IEnumerable<>) || def == typeof(ICollection<>) || def == typeof(IList<>) || def == typeof(List<>) || def == typeof(IReadOnlyList<>))
            {
                var itemType = type.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(itemType);
                var list = (IList)Activator.CreateInstance(listType)!;
                if (itemType == typeof(int))
                    list.Add(ResolveId(name, ownerType));
                else if (typeof(BaseEntity).IsAssignableFrom(itemType))
                {
                    foreach (var entity in GetEntities(itemType, 8))
                        list.Add(entity);
                }

                return list;
            }
        }

        if (typeof(BaseEntity).IsAssignableFrom(type))
            return GetEntity(type) ?? CreateDefault(type);

        if (type.IsInterface || type.IsAbstract)
            return _services.GetService(type);

        if (typeof(BaseNopModel).IsAssignableFrom(type) || type.IsClass)
        {
            var created = CreateDefault(type);
            PopulateModel(created, type, ownerType);
            return created;
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private void PopulateModel(object model, Type type, Type ownerType)
    {
        if (model == null)
            return;

        try
        {
            var idProp = type.GetProperty("Id");
            if (idProp?.CanWrite == true && idProp.PropertyType == typeof(int))
            {
                var id = ResolveId("id", ownerType, type);
                if (id > 0)
                    idProp.SetValue(model, id);
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite || prop.GetIndexParameters().Length != 0)
                    continue;

                if (prop.Name.EndsWith("Id", StringComparison.Ordinal) && prop.PropertyType == typeof(int) && prop.Name != "Id")
                {
                    var id = ResolveId(prop.Name, ownerType);
                    if (id > 0)
                        prop.SetValue(model, id);
                }
                else if ((prop.Name.Contains("StartDate", StringComparison.Ordinal) || prop.Name == "From")
                         && (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?)))
                {
                    prop.SetValue(model, DateTime.SpecifyKind(DateTime.Now.AddYears(-1), DateTimeKind.Unspecified));
                }
                else if ((prop.Name.Contains("EndDate", StringComparison.Ordinal) || prop.Name == "To")
                         && (prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?)))
                {
                    prop.SetValue(model, DateTime.SpecifyKind(DateTime.Now.AddDays(1), DateTimeKind.Unspecified));
                }
                else if (prop.Name == "ShipToSameAddress" && prop.PropertyType == typeof(bool))
                {
                    prop.SetValue(model, true);
                }
                else if (prop.Name is "PageSize" or "Page" && prop.PropertyType == typeof(int))
                {
                    prop.SetValue(model, prop.Name == "PageSize" ? 15 : 1);
                }
            }

            if (model is BaseSearchModel searchModel)
                searchModel.SetGridPageSize();
        }
        catch
        {
            // best-effort model fill
        }
    }

    private IList<int> GetEntityIds(string entityName)
    {
        if (string.IsNullOrWhiteSpace(entityName) || !EntityTypes.TryGetValue(entityName, out var entityType))
            return [];

        return GetEntities(entityType, 8).Select(e => e.Id).ToList();
    }

    private BaseEntity GetEntity(Type type)
    {
        if (_entities.TryGetValue(type, out var cached))
            return cached;

        var list = GetEntities(type, 8);
        return list.Count > 0 ? list[0] : null;
    }

    private IList<BaseEntity> GetEntities(Type type, int take)
    {
        if (_entityLists.TryGetValue(type, out var cached))
            return cached.Take(take).ToList();

        var list = new List<BaseEntity>();
        try
        {
            var repoType = typeof(IRepository<>).MakeGenericType(type);
            var repo = _services.GetService(repoType);
            if (repo != null)
            {
                var table = repo.GetType().GetProperty("Table")?.GetValue(repo);
                if (table is IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item is BaseEntity entity)
                            list.Add(entity);
                        if (list.Count >= 20)
                            break;
                    }
                }
            }
        }
        catch
        {
            // entity type may not be mapped
        }

        _entityLists[type] = list;
        if (list.Count > 0)
            _entities[type] = list[0];

        return list.Take(take).ToList();
    }

    private static object CreateDefault(Type type)
    {
        try
        {
            return Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(bool ok, object result)> TryInvokeAsync(object instance, MethodInfo method, object[] args, TimeSpan timeout)
    {
        try
        {
            var raw = method.Invoke(instance, args);
            if (raw is Task task)
            {
                var finished = await Task.WhenAny(task, Task.Delay(timeout));
                if (finished != task)
                    return (false, null);

                await task;
                if (task.GetType().IsGenericType)
                {
                    var resultProp = task.GetType().GetProperty("Result");
                    return (true, resultProp?.GetValue(task));
                }

                return (true, null);
            }

            return (true, raw);
        }
        catch
        {
            return (false, null);
        }
    }

    public class EmptyProxy : DispatchProxy
    {
        public ViewContext ViewContext { get; set; }

        public static object Create(Type interfaceType, ViewContext viewContext = null)
        {
            if (interfaceType == null || !interfaceType.IsInterface)
                return null;

            var proxyType = typeof(DispatchProxy);
            var create = proxyType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(DispatchProxy.Create) && m.GetGenericArguments().Length == 2);
            var proxy = create.MakeGenericMethod(interfaceType, typeof(EmptyProxy)).Invoke(null, null);
            if (proxy is EmptyProxy empty)
                empty.ViewContext = viewContext;
            return proxy;
        }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod == null)
                return null;

            var name = targetMethod.Name;
            if (name == "get_ViewContext" || name == "get_ActionContext")
                return ViewContext;
            if (name == "get_ViewData")
                return ViewContext?.ViewData;
            if (name == "get_ViewBag")
                return ViewContext?.ViewBag;
            if (name == "get_TempData")
                return ViewContext?.TempData;
            if (name == "get_HttpContext")
                return ViewContext?.HttpContext;
            if (name == "Contextualize" && args is { Length: 1 } && args[0] is ViewContext vc)
            {
                ViewContext = vc;
                return null;
            }

            var returnType = targetMethod.ReturnType;
            if (returnType == typeof(void))
                return null;
            if (returnType == typeof(Task))
                return Task.CompletedTask;
            if (returnType == typeof(string))
                return string.Empty;
            if (typeof(IHtmlContent).IsAssignableFrom(returnType))
                return HtmlString.Empty;
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var inner = returnType.GetGenericArguments()[0];
                var value = DefaultValue(inner);
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [value]);
            }

            return DefaultValue(returnType);
        }

        private static object DefaultValue(Type type)
        {
            if (type == typeof(string))
                return string.Empty;
            if (typeof(IHtmlContent).IsAssignableFrom(type))
                return HtmlString.Empty;
            if (type.IsValueType)
                return Activator.CreateInstance(type);
            if (type.IsArray)
                return Array.CreateInstance(type.GetElementType()!, 0);
            if (type.IsClass && type.GetConstructor(Type.EmptyTypes) != null)
                return Activator.CreateInstance(type);
            return null;
        }
    }

    private sealed class CompositeServiceProvider : IServiceProvider
    {
        private readonly IServiceProvider[] _providers;

        public CompositeServiceProvider(params IServiceProvider[] providers)
        {
            _providers = providers;
        }

        public object GetService(Type serviceType)
        {
            foreach (var provider in _providers)
            {
                try
                {
                    var service = provider.GetService(serviceType);
                    if (service != null)
                        return service;
                }
                catch
                {
                    // try the next provider
                }
            }

            return null;
        }
    }

    private sealed class NullView : IView
    {
        public static readonly NullView Instance = new();
        public string Path => string.Empty;
        public Task RenderAsync(ViewContext context) => Task.CompletedTask;
    }
}
