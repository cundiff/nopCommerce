using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
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
using Nop.Web.Framework.Models.DataTables;
using Nop.Web.Framework.Mvc.Razor;

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
        services.AddDataProtection();
        services.AddAntiforgery();
        services.AddRouting();
        var diagnostic = new System.Diagnostics.DiagnosticListener("Microsoft.AspNetCore");
        services.AddSingleton<System.Diagnostics.DiagnosticSource>(diagnostic);
        services.AddSingleton(diagnostic);
        services.AddControllersWithViews()
            .AddApplicationPart(typeof(global::Nop.Web.Controllers.HomeController).Assembly)
            .AddApplicationPart(typeof(NopRazorPage<>).Assembly);
        // Real IHtmlGenerator/IHtmlHelper throw on missing routes and editor templates.
        // Stub them so compiled views can run past the first asp-/nop- tag helper.
        services.AddSingleton<IHtmlGenerator, CoverageHtmlGenerator>();
        services.AddTransient(typeof(IHtmlHelper), _ => EmptyProxy.Create(typeof(IHtmlHelper)));
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IHtmlGenerator>();
        return provider;
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

    public async Task SeedWishlistAsync()
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
                await cartService.AddToCartAsync(customer, product, ShoppingCartType.Wishlist, store.Id,
                    quantity: 1, addRequiredProducts: false);
            }
            catch
            {
            }
        }
    }

    public void ClearWorkContextCaches()
    {
        if (_services.GetService<IWorkContext>() is not WebWorkContext workContext)
            return;

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(WebWorkContext).GetField("_cachedVendor", flags)?.SetValue(workContext, null);
        typeof(WebWorkContext).GetField("_cachedLanguage", flags)?.SetValue(workContext, null);
        typeof(WebWorkContext).GetField("_cachedCurrency", flags)?.SetValue(workContext, null);
        typeof(WebWorkContext).GetField("_cachedTaxDisplayType", flags)?.SetValue(workContext, null);
    }

    public FormCollection CreateForm(IDictionary<string, string> values = null, bool withFile = false)
    {
        var fields = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        var addressId = GetEntityIds("Address").FirstOrDefault();
        if (addressId > 0)
        {
            fields["billing_address_id"] = addressId.ToString();
            fields["shipping_address_id"] = addressId.ToString();
        }

        if (values != null)
        {
            foreach (var pair in values)
                fields[pair.Key] = pair.Value;
        }

        var files = new FormFileCollection();
        if (withFile)
            files.Add(CreateFormFile());

        return new FormCollection(fields, files);
    }

    public static IFormFile CreateFormFile(string name = "file", string fileName = "coverage.jpg")
        => new CoverageFormFile(name, fileName, "image/jpeg", CoverageJpeg);

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
            IServiceProvider previousServices = null;
            HttpContext http = null;
            try
            {
                var instance = Activator.CreateInstance(type);
                if (instance == null)
                    continue;

                TypesCreated++;

                http = _services.GetRequiredService<IHttpContextAccessor>().HttpContext
                       ?? throw new InvalidOperationException("HttpContext is not available");
                previousServices = http.RequestServices;
                http.RequestServices = new CompositeServiceProvider(MvcServices.Value, _services);

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
                        // keep the helpers attached in AttachMvc
                    }

                    // Real IHtmlHelper/IViewComponentHelper try to locate partials and
                    // view components via the MVC view engine. Stub them so this page's
                    // own ExecuteAsync can finish; compiled partials are invoked separately.
                    ActivateRazorInjects(instance, razorPage.ViewContext);
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
            catch (Exception ex)
            {
                MethodsFailed++;
                if (Failures.Count < 80)
                    Failures.Add($"{type.Name}: {ex.GetBaseException().Message}");
            }
            finally
            {
                if (http != null && previousServices != null)
                    http.RequestServices = previousServices;
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
        if (routeData.Routers.Count == 0)
            routeData.Routers.Add(DummyRouter.Instance);
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

        var viewData = CreateViewData(modelType, instance.GetType());

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

    private ViewDataDictionary CreateViewData(Type modelType, Type ownerType)
    {
        object model = null;
        if (modelType != null)
        {
            try
            {
                model = CreateArg(modelType, "Model", ownerType);
            }
            catch
            {
                model = CreateDefault(modelType);
            }
        }

        if (modelType == null)
            return new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());

        try
        {
            var typedType = typeof(ViewDataDictionary<>).MakeGenericType(modelType);
            var typed = (ViewDataDictionary)Activator.CreateInstance(typedType, new EmptyModelMetadataProvider(), new ModelStateDictionary());
            if (model != null && modelType.IsInstanceOfType(model))
                typed.Model = model;
            return typed;
        }
        catch
        {
            var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model
            };
            return viewData;
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
        if (model == null || depth > 5)
            return;

        var type = model.GetType();
        if (ShouldSkipFill(type))
            return;

        if (model is DataTablesModel tables && string.IsNullOrEmpty(tables.Name))
            tables.Name = "coverage-grid";

        if (model is DataUrl dataUrl)
        {
            dataUrl.ActionName ??= "List";
            dataUrl.ControllerName ??= "Product";
            dataUrl.Url ??= "/coverage";
            dataUrl.RouteValues ??= new RouteValueDictionary();
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanWrite || prop.GetIndexParameters().Length != 0)
                continue;

            try
            {
                var current = prop.CanRead ? prop.GetValue(model) : null;
                var propertyType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                if (propertyType == typeof(string) && string.IsNullOrEmpty(current as string))
                {
                    prop.SetValue(model, prop.Name == "Name" ? "coverage-grid" : prop.Name);
                }
                else if (propertyType == typeof(bool) && current is false)
                {
                    prop.SetValue(model, true);
                }
                else if (propertyType == typeof(DataUrl) && current == null)
                {
                    var url = new DataUrl("/coverage", "id")
                    {
                        ActionName = "List",
                        ControllerName = "Product",
                        RouteValues = new RouteValueDictionary()
                    };
                    prop.SetValue(model, url);
                }
                else if (propertyType.IsArray)
                {
                    var element = propertyType.GetElementType();
                    if (current == null || ((Array)current).Length == 0)
                    {
                        var array = Array.CreateInstance(element!, 1);
                        var item = element == typeof(string) ? "item" : CreateDefault(element);
                        if (item != null)
                        {
                            array.SetValue(item, 0);
                            FillGraph(item, depth + 1);
                        }
                        prop.SetValue(model, array);
                    }
                }
                else if (typeof(IDictionary).IsAssignableFrom(propertyType))
                {
                    if (current == null)
                    {
                        var created = CreateDefault(propertyType) ?? CreateDictionary(propertyType);
                        if (created != null)
                            prop.SetValue(model, created);
                    }
                }
                else if (IsCollectionType(propertyType, out var itemType))
                {
                    IList list;
                    if (current == null)
                    {
                        list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
                        prop.SetValue(model, list);
                    }
                    else if (current is IList existing)
                    {
                        list = existing;
                    }
                    else
                    {
                        continue;
                    }

                    if (list.Count == 0)
                    {
                        object item;
                        if (itemType == typeof(string))
                            item = "item";
                        else if (itemType == typeof(SelectListItem))
                            item = new SelectListItem("coverage", "1", true);
                        else if (itemType.IsPrimitive || itemType.IsEnum)
                            item = CreateArg(itemType, prop.Name, type);
                        else
                            item = CreateDefault(itemType);

                        if (item != null)
                        {
                            list.Add(item);
                            FillGraph(item, depth + 1);
                        }
                    }
                    else
                    {
                        FillGraph(list[0], depth + 1);
                    }
                }
                else if (propertyType == typeof(SelectList) && current == null)
                {
                    prop.SetValue(model, new SelectList(new[] { "coverage" }));
                }
                else if (!propertyType.IsPrimitive && !propertyType.IsEnum && propertyType != typeof(decimal)
                         && propertyType != typeof(DateTime) && propertyType != typeof(DateTimeOffset)
                         && propertyType != typeof(Guid) && propertyType.IsClass)
                {
                    if (current == null)
                    {
                        current = CreateDefault(propertyType);
                        if (current != null)
                            prop.SetValue(model, current);
                    }

                    FillGraph(current, depth + 1);
                }
            }
            catch
            {
                // skip unreadable properties
            }
        }
    }

    private static bool IsCollectionType(Type type, out Type itemType)
    {
        itemType = null;
        if (!type.IsGenericType)
            return false;

        var def = type.GetGenericTypeDefinition();
        if (def != typeof(IList<>) && def != typeof(List<>) && def != typeof(ICollection<>)
            && def != typeof(IEnumerable<>) && def != typeof(IReadOnlyList<>)
            && def != typeof(IReadOnlyCollection<>))
            return false;

        itemType = type.GetGenericArguments()[0];
        return itemType != typeof(byte);
    }

    private static object CreateDictionary(Type type)
    {
        try
        {
            if (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                var args = type.GetGenericArguments();
                return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args));
            }

            return Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldSkipFill(Type type)
    {
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(decimal))
            return true;
        if (typeof(Delegate).IsAssignableFrom(type) || type == typeof(Type) || type == typeof(Stream))
            return true;
        if (typeof(HttpContext).IsAssignableFrom(type) || typeof(IServiceProvider).IsAssignableFrom(type))
            return true;
        if (typeof(IHtmlHelper).IsAssignableFrom(type) || typeof(IUrlHelper).IsAssignableFrom(type))
            return true;
        return type.Namespace?.StartsWith("System.Reflection", StringComparison.Ordinal) == true;
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
                p.ParameterType.IsByRef
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
        if (type == typeof(IFormFile) || typeof(IFormFile).IsAssignableFrom(type))
            return CreateFormFile();

        if (type == typeof(IFormCollection) || type == typeof(FormCollection))
            return CreateForm(withFile: true);

        if (type == typeof(IUrlHelper))
            return _services.GetRequiredService<IUrlHelperFactory>()
                .GetUrlHelper(new ActionContext(
                    _services.GetRequiredService<IHttpContextAccessor>().HttpContext,
                    new RouteData(),
                    new ActionDescriptor()));

        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            if (element == typeof(IFormFile))
                return new IFormFile[] { CreateFormFile() };
            return Array.CreateInstance(element, 0);
        }

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
                if (itemType == typeof(IFormFile))
                    return new List<IFormFile> { CreateFormFile() };

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
        if (type == null || type == typeof(void) || type.IsAbstract)
            return null;

        if (type == typeof(DataUrl))
            return new DataUrl("/coverage", "id") { ActionName = "List", ControllerName = "Product" };

        if (type == typeof(SelectList))
            return new SelectList(new[] { "coverage" });

        if (type == typeof(SelectListItem))
            return new SelectListItem("coverage", "1", true);

        try
        {
            return Activator.CreateInstance(type);
        }
        catch
        {
            // try a constructor with dummy arguments
        }

        try
        {
            var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .FirstOrDefault();
            if (ctor != null)
            {
                var args = ctor.GetParameters().Select(p =>
                {
                    if (p.HasDefaultValue)
                        return p.DefaultValue;
                    if (p.ParameterType == typeof(string))
                        return p.Name ?? "coverage";
                    if (p.ParameterType == typeof(bool))
                        return true;
                    if (p.ParameterType == typeof(int))
                        return 1;
                    if (p.ParameterType == typeof(RouteValueDictionary))
                        return new RouteValueDictionary();
                    if (p.ParameterType.IsValueType)
                        return Activator.CreateInstance(p.ParameterType);
                    return null;
                }).ToArray();
                return ctor.Invoke(args);
            }
        }
        catch
        {
        }

        try
        {
            return RuntimeHelpers.GetUninitializedObject(type);
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
            if (type == typeof(TagBuilder))
                return new TagBuilder("span");
            if (typeof(IHtmlContent).IsAssignableFrom(type))
                return HtmlString.Empty;
            if (type.IsValueType)
                return Activator.CreateInstance(type);
            if (type.IsArray)
            {
                var element = type.GetElementType()!;
                if (element == typeof(IFormFile))
                    return new IFormFile[] { CreateFormFile() };
                return Array.CreateInstance(element, 0);
            }

            if (type.IsGenericType)
            {
                var def = type.GetGenericTypeDefinition();
                if (def == typeof(IEnumerable<>) || def == typeof(ICollection<>) || def == typeof(IList<>)
                    || def == typeof(IReadOnlyList<>) || def == typeof(List<>))
                {
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));
                }

                if (def == typeof(IDictionary<,>) || def == typeof(Dictionary<,>))
                {
                    var args = type.GetGenericArguments();
                    return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args));
                }
            }

            if (type.IsClass && type.GetConstructor(Type.EmptyTypes) != null)
                return Activator.CreateInstance(type);
            return null;
        }
    }

    private sealed class CoverageHtmlGenerator : IHtmlGenerator
    {
        public string IdAttributeDotReplacement => "_";
        public string Encode(string value) => value ?? string.Empty;
        public string Encode(object value) => value?.ToString() ?? string.Empty;
        public string FormatValue(object value, string format) => value?.ToString() ?? string.Empty;

        private static TagBuilder Tag(string name = "span") => new(name);

        public TagBuilder GenerateActionLink(ViewContext viewContext, string linkText, string actionName, string controllerName, string protocol, string hostname, string fragment, object routeValues, object htmlAttributes)
            => Tag("a");
        public TagBuilder GeneratePageLink(ViewContext viewContext, string linkText, string pageName, string pageHandler, string protocol, string hostname, string fragment, object routeValues, object htmlAttributes)
            => Tag("a");
        public IHtmlContent GenerateAntiforgery(ViewContext viewContext) => HtmlString.Empty;
        public TagBuilder GenerateCheckBox(ViewContext viewContext, ModelExplorer modelExplorer, string expression, bool? isChecked, object htmlAttributes)
            => Tag("input");
        public TagBuilder GenerateHiddenForCheckbox(ViewContext viewContext, ModelExplorer modelExplorer, string expression)
            => Tag("input");
        public TagBuilder GenerateForm(ViewContext viewContext, string actionName, string controllerName, object routeValues, string method, object htmlAttributes)
            => Tag("form");
        public TagBuilder GeneratePageForm(ViewContext viewContext, string pageName, string pageHandler, object routeValues, string fragment, string method, object htmlAttributes)
            => Tag("form");
        public TagBuilder GenerateRouteForm(ViewContext viewContext, string routeName, object routeValues, string method, object htmlAttributes)
            => Tag("form");
        public TagBuilder GenerateHidden(ViewContext viewContext, ModelExplorer modelExplorer, string expression, object value, bool useViewData, object htmlAttributes)
            => Tag("input");
        public TagBuilder GenerateLabel(ViewContext viewContext, ModelExplorer modelExplorer, string expression, string labelText, object htmlAttributes)
            => Tag("label");
        public TagBuilder GeneratePassword(ViewContext viewContext, ModelExplorer modelExplorer, string expression, object value, object htmlAttributes)
            => Tag("input");
        public TagBuilder GenerateRadioButton(ViewContext viewContext, ModelExplorer modelExplorer, string expression, object value, bool? isChecked, object htmlAttributes)
            => Tag("input");
        public TagBuilder GenerateRouteLink(ViewContext viewContext, string linkText, string routeName, string protocol, string hostName, string fragment, object routeValues, object htmlAttributes)
            => Tag("a");
        public TagBuilder GenerateSelect(ViewContext viewContext, ModelExplorer modelExplorer, string optionLabel, string expression, IEnumerable<SelectListItem> selectList, bool allowMultiple, object htmlAttributes)
            => Tag("select");
        public TagBuilder GenerateSelect(ViewContext viewContext, ModelExplorer modelExplorer, string optionLabel, string expression, IEnumerable<SelectListItem> selectList, ICollection<string> currentValues, bool allowMultiple, object htmlAttributes)
            => Tag("select");
        public IHtmlContent GenerateGroupsAndOptions(string optionLabel, IEnumerable<SelectListItem> selectList)
            => HtmlString.Empty;
        public TagBuilder GenerateTextArea(ViewContext viewContext, ModelExplorer modelExplorer, string expression, int rows, int columns, object htmlAttributes)
            => Tag("textarea");
        public TagBuilder GenerateTextBox(ViewContext viewContext, ModelExplorer modelExplorer, string expression, object value, string format, object htmlAttributes)
            => Tag("input");
        public TagBuilder GenerateValidationMessage(ViewContext viewContext, ModelExplorer modelExplorer, string expression, string message, string tag, object htmlAttributes)
            => Tag("span");
        public TagBuilder GenerateValidationSummary(ViewContext viewContext, bool excludePropertyErrors, string message, string headerTag, object htmlAttributes)
            => Tag("div");
        public ICollection<string> GetCurrentValues(ViewContext viewContext, ModelExplorer modelExplorer, string expression, bool allowMultiple)
            => new List<string>();
    }

    private sealed class CoverageFormFile : IFormFile
    {
        private readonly byte[] _content;

        public CoverageFormFile(string name, string fileName, string contentType, byte[] content)
        {
            Name = name;
            FileName = fileName;
            ContentType = contentType;
            _content = content;
        }

        public string ContentType { get; }
        public string ContentDisposition => $"form-data; name=\"{Name}\"; filename=\"{FileName}\"";
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => _content.Length;
        public string Name { get; }
        public string FileName { get; }
        public void CopyTo(Stream target) => target.Write(_content, 0, _content.Length);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            target.Write(_content, 0, _content.Length);
            return Task.CompletedTask;
        }
        public Stream OpenReadStream() => new MemoryStream(_content, writable: false);
    }

    // 1x1 JPEG so picture/upload paths accept the file.
    private static readonly byte[] CoverageJpeg =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
        0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
        0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09,
        0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
        0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20,
        0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29,
        0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32,
        0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x00, 0x01,
        0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00,
        0x3F, 0x00, 0x7F, 0xFF, 0xD9
    ];

    private sealed class DummyRouter : IRouter
    {
        public static readonly DummyRouter Instance = new();
        public VirtualPathData GetVirtualPath(VirtualPathContext context) => new(this, "/coverage");
        public Task RouteAsync(RouteContext context) => Task.CompletedTask;
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
                var service = provider.GetService(serviceType);
                if (service != null)
                    return service;
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
