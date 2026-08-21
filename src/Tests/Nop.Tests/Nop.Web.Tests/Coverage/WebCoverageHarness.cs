using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
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
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Attributes;
using Nop.Core.Domain.Blogs;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Discounts;
using Nop.Core.Domain.FilterLevels;
using Nop.Core.Domain.Gdpr;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Tax;
using Nop.Core.Domain.Vendors;
using Nop.Core.Events;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Services.Attributes;
using Nop.Services.Catalog;
using Nop.Services.Cms;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Discounts;
using Nop.Services.FilterLevels;
using Nop.Services.Gdpr;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Services.Shipping;
using Nop.Services.Shipping.Pickup;
using Nop.Services.Stores;
using Nop.Services.Vendors;
using Nop.Tests.Nop.Services.Tests.Payments;
using Nop.Tests.Nop.Web.Tests.Public;
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
        services.AddSingleton<IHtmlGenerator, CoverageHtmlGenerator>();
        services.AddSingleton<IAntiforgery, CoverageAntiforgery>();
        services.AddTransient(typeof(IHtmlHelper), _ => EmptyProxy.Create(typeof(IHtmlHelper)));
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IHtmlGenerator>();
        _ = provider.GetRequiredService<ITagHelperFactory>();
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
    public Type RazorControllerType { get; set; }

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

    public void EnsurePluginDescriptorsWritable()
    {
        var pluginsInfo = Singleton<IPluginsInfo>.Instance;
        if (pluginsInfo?.PluginDescriptors == null)
            return;

        if (pluginsInfo.PluginDescriptors.All(item =>
                !string.Equals(item.pluginDescriptor?.SystemName, "TestWidgetPlugin", StringComparison.OrdinalIgnoreCase)))
        {
            pluginsInfo.PluginDescriptors.Add((new PluginDescriptor
            {
                PluginType = typeof(TestWidgetPlugin),
                SystemName = "TestWidgetPlugin",
                FriendlyName = "Test widget plugin",
                Installed = true,
                ReferencedAssembly = typeof(TestWidgetPlugin).Assembly
            }, true));
        }

        foreach (var (descriptor, _) in pluginsInfo.PluginDescriptors)
        {
            if (descriptor == null)
                continue;

            descriptor.LimitedToStores ??= [];
            descriptor.LimitedToCustomerRoles ??= [];
            var directory = Path.Combine(Path.GetTempPath(), "nop-coverage-plugins",
                (descriptor.SystemName ?? "plugin").Replace('.', '_').Replace(Path.DirectorySeparatorChar, '_'));
            Directory.CreateDirectory(directory);
            var jsonPath = Path.Combine(directory, NopPluginDefaults.DescriptionFileName);
            if (!File.Exists(jsonPath))
                File.WriteAllText(jsonPath, "{}");
            descriptor.OriginalAssemblyFile = Path.Combine(directory, "plugin.dll");
        }
    }

    public async Task<Customer> EnsureRegisteredShopperAsync()
    {
        var customerService = _services.GetRequiredService<ICustomerService>();
        const string email = "coverage-shopper@example.com";
        var shopper = await customerService.GetCustomerByEmailAsync(email);
        if (shopper != null)
            return shopper;

        shopper = new Customer
        {
            Email = email,
            Username = email,
            Active = true,
            CreatedOnUtc = DateTime.UtcNow,
            LastActivityDateUtc = DateTime.UtcNow,
            RegisteredInStoreId = 1
        };
        await customerService.InsertCustomerAsync(shopper);
        await customerService.InsertCustomerPasswordAsync(new CustomerPassword
        {
            CustomerId = shopper.Id,
            PasswordFormat = PasswordFormat.Clear,
            Password = "1q2w3e4r5t",
            CreatedOnUtc = DateTime.UtcNow
        });
        var role = await customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.RegisteredRoleName);
        if (role != null)
        {
            await customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping
            {
                CustomerId = shopper.Id,
                CustomerRoleId = role.Id
            });
        }

        return shopper;
    }

    private static string StartLocalHttpResponder(int connections = 8)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < connections; i++)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    await using var stream = client.GetStream();
                    var buffer = new byte[2048];
                    _ = await stream.ReadAsync(buffer);
                    const string body = "ok";
                    var response =
                        $"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
                }
            }
            catch
            {
            }
            finally
            {
                listener.Stop();
            }
        });

        return $"http://127.0.0.1:{port}/coverage.mp4";
    }

    public async Task<Product> EnsurePlainProductAsync()
    {
        var productService = _services.GetRequiredService<IProductService>();
        var attributeService = _services.GetRequiredService<IProductAttributeService>();
        foreach (var candidate in await productService.SearchProductsAsync(pageSize: 80))
        {
            if (candidate.ProductType != ProductType.SimpleProduct)
                continue;
            if (candidate.CustomerEntersPrice || candidate.IsRental || candidate.OrderMinimumQuantity > 1)
                continue;
            if (!string.IsNullOrEmpty(candidate.AllowedQuantities))
                continue;
            if ((await attributeService.GetProductAttributeMappingsByProductIdAsync(candidate.Id)).Count > 0)
                continue;
            return candidate;
        }

        var source = (await productService.SearchProductsAsync(pageSize: 1)).First();
        var product = new Product
        {
            Name = "Coverage Plain Product",
            ShortDescription = source.ShortDescription,
            FullDescription = source.FullDescription,
            ProductType = ProductType.SimpleProduct,
            VisibleIndividually = true,
            ProductTemplateId = source.ProductTemplateId,
            AllowCustomerReviews = true,
            Published = true,
            Sku = $"COV{Guid.NewGuid():N}"[..12],
            Price = 19.99m,
            IsShipEnabled = true,
            ManageInventoryMethod = ManageInventoryMethod.DontManageStock,
            StockQuantity = 1000,
            OrderMinimumQuantity = 1,
            OrderMaximumQuantity = 10000,
            TaxCategoryId = source.TaxCategoryId,
            Weight = 1,
            Length = 1,
            Width = 1,
            Height = 1,
            CreatedOnUtc = DateTime.UtcNow,
            UpdatedOnUtc = DateTime.UtcNow,
            RecurringCycleLength = 100,
            RecurringTotalCycles = 10,
            RentalPriceLength = 1
        };
        await productService.InsertProductAsync(product);
        return product;
    }

    public async Task EnsurePlainProductInCartAsync()
    {
        var product = await EnsurePlainProductAsync();
        var customer = await _services.GetRequiredService<IWorkContext>().GetCurrentCustomerAsync();
        var store = await _services.GetRequiredService<IStoreContext>().GetCurrentStoreAsync();
        try
        {
            await _services.GetRequiredService<IShoppingCartService>()
                .AddToCartAsync(customer, product, ShoppingCartType.ShoppingCart, store.Id,
                    quantity: 1, addRequiredProducts: false);
        }
        catch
        {
            // already in the cart
        }
    }

    public async Task SeedCoverageAttributesAsync()
    {
        await SeedAttributeFamilyAsync<CheckoutAttribute, CheckoutAttributeValue>("Coverage checkout");
        await SeedAttributeFamilyAsync<CustomerAttribute, CustomerAttributeValue>("Coverage customer");
        await SeedAttributeFamilyAsync<AddressAttribute, AddressAttributeValue>("Coverage address");
        await SeedAttributeFamilyAsync<VendorAttribute, VendorAttributeValue>("Coverage vendor");

        var productService = _services.GetRequiredService<IProductService>();
        var attributeService = _services.GetRequiredService<IProductAttributeService>();
        var catalogAttributes = await attributeService.GetAllProductAttributesAsync();
        if (catalogAttributes.Count == 0)
            return;

        var host = (await productService.SearchProductsAsync(pageSize: 20))
            .FirstOrDefault(p => p.Name != "Coverage Plain Product")
            ?? (await productService.SearchProductsAsync(pageSize: 1)).First();

        var existing = await attributeService.GetProductAttributeMappingsByProductIdAsync(host.Id);
        if (!existing.Any(m => m.AttributeControlType == AttributeControlType.FileUpload))
        {
            await attributeService.InsertProductAttributeMappingAsync(new ProductAttributeMapping
            {
                ProductId = host.Id,
                ProductAttributeId = catalogAttributes[0].Id,
                AttributeControlType = AttributeControlType.FileUpload,
                IsRequired = false,
                DisplayOrder = 90,
                ValidationFileMaximumSize = 2048
            });
        }

        foreach (AttributeControlType control in Enum.GetValues<AttributeControlType>())
        {
            if (existing.Any(mapping => mapping.AttributeControlType == control))
                continue;
            await attributeService.InsertProductAttributeMappingAsync(new ProductAttributeMapping
            {
                ProductId = host.Id,
                ProductAttributeId = catalogAttributes[Math.Min((int)control, catalogAttributes.Count - 1)].Id,
                AttributeControlType = control,
                IsRequired = false,
                DisplayOrder = 70 + (int)control,
                ValidationFileMaximumSize = control == AttributeControlType.FileUpload ? 2048 : null
            });
            if (control is AttributeControlType.TextBox or AttributeControlType.MultilineTextbox
                or AttributeControlType.Datepicker or AttributeControlType.FileUpload)
                continue;
            var mapping = (await attributeService.GetProductAttributeMappingsByProductIdAsync(host.Id))
                .Last(m => m.AttributeControlType == control);
            await attributeService.InsertProductAttributeValueAsync(new ProductAttributeValue
            {
                ProductAttributeMappingId = mapping.Id,
                Name = "Coverage",
                IsPreSelected = true,
                DisplayOrder = 1,
                PriceAdjustment = 1,
                WeightAdjustment = 0.1m,
                CustomerEntersQty = control == AttributeControlType.Checkboxes
            });
        }
    }

    public async Task SeedAttributeFamilyAsync<TAttribute, TValue>(string namePrefix)
        where TAttribute : BaseAttribute, new()
        where TValue : BaseAttributeValue, new()
    {
        var service = _services.GetRequiredService<IAttributeService<TAttribute, TValue>>();
        var existing = await service.GetAllAttributesAsync();
        foreach (var required in existing.Where(attribute => attribute.IsRequired))
        {
            required.IsRequired = false;
            await service.UpdateAttributeAsync(required);
        }

        foreach (AttributeControlType control in Enum.GetValues<AttributeControlType>())
        {
            if (existing.Any(attribute => attribute.AttributeControlType == control &&
                                          (attribute.Name?.StartsWith(namePrefix, StringComparison.Ordinal) ?? false)))
                continue;

            var created = new TAttribute
            {
                Name = $"{namePrefix} {control}",
                AttributeControlType = control,
                IsRequired = false,
                DisplayOrder = 80 + (int)control
            };
            await service.InsertAttributeAsync(created);
            if (created.ShouldHaveValues)
            {
                await service.InsertAttributeValueAsync(new TValue
                {
                    AttributeId = created.Id,
                    Name = "Coverage",
                    IsPreSelected = true,
                    DisplayOrder = 1
                });
            }
        }
    }

    public async Task<(Dictionary<string, string> Values, List<(string name, string fileName)> Files)>
        BuildAttributeFormAsync<TAttribute, TValue>(string prefix)
        where TAttribute : BaseAttribute
        where TValue : BaseAttributeValue
    {
        var service = _services.GetRequiredService<IAttributeService<TAttribute, TValue>>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(string name, string fileName)>();
        foreach (var attribute in await service.GetAllAttributesAsync())
        {
            var controlId = $"{prefix}{attribute.Id}";
            switch (attribute.AttributeControlType)
            {
                case AttributeControlType.Checkboxes:
                case AttributeControlType.ReadonlyCheckboxes:
                    values[controlId] = string.Join(",",
                        (await service.GetAttributeValuesAsync(attribute.Id)).Select(value => value.Id));
                    break;
                case AttributeControlType.Datepicker:
                    values[$"{controlId}_day"] = "1";
                    values[$"{controlId}_month"] = "1";
                    values[$"{controlId}_year"] = "2026";
                    break;
                case AttributeControlType.TextBox:
                case AttributeControlType.MultilineTextbox:
                    values[controlId] = "coverage text";
                    break;
                case AttributeControlType.FileUpload:
                    files.Add((controlId, "coverage.jpg"));
                    break;
                default:
                    var selected = (await service.GetAttributeValuesAsync(attribute.Id)).FirstOrDefault();
                    if (selected != null)
                        values[controlId] = selected.Id.ToString();
                    break;
            }
        }

        return (values, files);
    }

    public async Task<IFormCollection> CreateCustomerAttributeFormAsync(IDictionary<string, string> extra = null)
    {
        var (values, files) = await BuildAttributeFormAsync<CustomerAttribute, CustomerAttributeValue>("customer_attribute_");
        if (extra != null)
        {
            foreach (var pair in extra)
                values[pair.Key] = pair.Value;
        }

        return CreateForm(values, true, files);
    }

    public async Task<IFormCollection> CreateAddressAttributeFormAsync(IDictionary<string, string> extra = null)
    {
        var (values, files) = await BuildAttributeFormAsync<AddressAttribute, AddressAttributeValue>("address_attribute_");
        if (extra != null)
        {
            foreach (var pair in extra)
                values[pair.Key] = pair.Value;
        }

        return CreateForm(values, true, files);
    }

    public void ApplyRequestForm(IDictionary<string, string> values = null, bool withFile = true,
        IEnumerable<(string name, string fileName)> extraFiles = null)
    {
        var http = _services.GetRequiredService<IHttpContextAccessor>().HttpContext;
        if (http == null)
            return;

        http.Request.ContentType = "multipart/form-data; boundary=----coverage";
        http.Request.QueryString = new QueryString("?q=coverage");
        try
        {
            http.Request.Form = CreateForm(values, withFile, extraFiles);
        }
        catch
        {
            // request form can already be read
        }
    }

    public async Task InvokeInstanceMethodAsync(object instance, string name, params object[] args)
    {
        var methods = instance.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.Name == name && !m.Name.Contains('<', StringComparison.Ordinal))
            .ToList();
        var method = methods.FirstOrDefault(m => m.GetParameters().Length == args.Length) ?? methods.FirstOrDefault();
        if (method == null)
            return;

        if (args.Length != method.GetParameters().Length)
        {
            var parameters = method.GetParameters();
            var filled = new object[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                filled[i] = i < args.Length ? args[i] : CreateArg(parameters[i].ParameterType, parameters[i].Name, instance.GetType());
            args = filled;
        }

        await TryInvokeAsync(instance, method, args, DefaultTimeout);
    }

    public async Task SeedShoppingCartAsync()
    {
        await EnsurePlainProductInCartAsync();

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
        typeof(WebWorkContext).GetField("_cachedCustomer", flags)?.SetValue(workContext, null);
        typeof(WebWorkContext).GetField("_cachedVendor", flags)?.SetValue(workContext, null);
        typeof(WebWorkContext).GetField("_cachedLanguage", flags)?.SetValue(workContext, null);
        typeof(WebWorkContext).GetField("_cachedCurrency", flags)?.SetValue(workContext, null);
        typeof(WebWorkContext).GetField("_cachedTaxDisplayType", flags)?.SetValue(workContext, null);
    }

    public FormCollection CreateForm(IDictionary<string, string> values = null, bool withFile = false,
        IEnumerable<(string name, string fileName)> extraFiles = null)
    {
        var fields = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        {
            ["q"] = "coverage"
        };
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
        if (extraFiles != null)
        {
            foreach (var (name, fileName) in extraFiles)
                files.Add(CreateFormFile(name, fileName));
        }

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
                if (ShouldSkipMethodName(group.Key))
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
        await SeedCoverageAttributesAsync();

        var orderSettings = _services.GetRequiredService<OrderSettings>();
        var shippingSettings = _services.GetRequiredService<ShippingSettings>();
        var previousOpc = orderSettings.OnePageCheckoutEnabled;
        var previousInterval = orderSettings.MinimumOrderPlacementInterval;
        var previousPickupPage = orderSettings.DisplayPickupInStoreOnShippingMethodPage;
        var previousShipToSame = shippingSettings.ShipToSameAddress;
        var previousAllowPickup = shippingSettings.AllowPickupInStore;

        orderSettings.OnePageCheckoutEnabled = false;
        orderSettings.MinimumOrderPlacementInterval = 0;
        orderSettings.DisplayPickupInStoreOnShippingMethodPage = true;
        shippingSettings.ShipToSameAddress = true;
        shippingSettings.AllowPickupInStore = true;
        var settings = _services.GetRequiredService<ISettingService>();
        await settings.SaveSettingAsync(orderSettings);
        await settings.SaveSettingAsync(shippingSettings);

        var checkoutType = typeof(global::Nop.Web.Controllers.CheckoutController);
        global::Nop.Web.Controllers.CheckoutController checkout;
        try
        {
            checkout = (global::Nop.Web.Controllers.CheckoutController)ActivatorUtilities.CreateInstance(_services, checkoutType);
            AttachMvc(checkout);
            TypesCreated++;
        }
        catch (Exception ex)
        {
            TypesFailed++;
            Failures.Add($"CheckoutController: create {ex.GetBaseException().Message}");
            return;
        }

        var workContext = _services.GetRequiredService<IWorkContext>();
        var customerService = _services.GetRequiredService<ICustomerService>();
        var cartService = _services.GetRequiredService<IShoppingCartService>();
        var store = await _services.GetRequiredService<IStoreContext>().GetCurrentStoreAsync();
        var customer = await workContext.GetCurrentCustomerAsync();
        var addresses = await customerService.GetAddressesByCustomerIdAsync(customer.Id);
        var addressId = addresses.FirstOrDefault()?.Id ?? GetEntityIds("Address").FirstOrDefault();
        var checkoutFactory = _services.GetRequiredService<global::Nop.Web.Factories.ICheckoutModelFactory>();
        var addressForm = await CreateAddressAttributeFormAsync(addressId > 0
            ? new Dictionary<string, string>
            {
                ["billing_address_id"] = addressId.ToString(),
                ["shipping_address_id"] = addressId.ToString(),
                ["nextstep"] = "next"
            }
            : new Dictionary<string, string> { ["nextstep"] = "next" });

        async Task Call(string name, params object[] args)
        {
            var method = checkoutType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
            if (method == null)
                return;
            checkout.ModelState.Clear();
            var (ok, _) = await TryInvokeAsync(checkout, method, args, RoundTripTimeout);
            if (ok)
                MethodsInvoked++;
            else
                MethodsFailed++;
        }

        async Task<IList<ShoppingCartItem>> Cart()
            => await cartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        async Task FillNewAddress(global::Nop.Web.Models.Common.AddressModel address, string suffix)
        {
            address.FirstName = "Coverage";
            address.LastName = suffix;
            address.Email = $"{suffix}-{Guid.NewGuid():N}@example.com";
            address.Address1 = $"Coverage {suffix} {Guid.NewGuid():N}";
            address.Address2 = "Suite 2";
            address.City = "New York";
            address.ZipPostalCode = "10021";
            address.County = "New York";
            address.CountryId = 1;
            address.StateProvinceId = 1;
            address.PhoneNumber = "5550001111";
            address.FaxNumber = "5550002222";
            address.Company = "Coverage Co";
        }

        try
        {
            await Call("Index");
            await Call("BillingAddress", addressForm);
            if (addressId > 0)
                await Call("SelectBillingAddress", addressId, true);

            var billing = new global::Nop.Web.Models.Checkout.CheckoutBillingAddressModel { ShipToSameAddress = true };
            await checkoutFactory.PrepareBillingAddressModelAsync(billing, await Cart(), prePopulateNewAddressWithCustomerFields: true);
            await FillNewAddress(billing.BillingNewAddress, "BillSame");
            checkout.ModelState.Clear();
            await Call("NewBillingAddress", billing, addressForm);

            await EnsurePlainProductInCartAsync();
            customer = await workContext.GetCurrentCustomerAsync();
            billing = new global::Nop.Web.Models.Checkout.CheckoutBillingAddressModel { ShipToSameAddress = false };
            await checkoutFactory.PrepareBillingAddressModelAsync(billing, await Cart(), prePopulateNewAddressWithCustomerFields: true);
            await FillNewAddress(billing.BillingNewAddress, "BillNew");
            checkout.ModelState.Clear();
            await Call("NewBillingAddress", billing, await CreateAddressAttributeFormAsync(new Dictionary<string, string>
            {
                ["billing_address_id"] = "0",
                ["nextstep"] = "next"
            }));

            await Call("ShippingAddress");
            if (addressId > 0)
                await Call("SelectShippingAddress", addressId);

            var shipping = new global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel();
            await checkoutFactory.PrepareShippingAddressModelAsync(shipping, await Cart(), prePopulateNewAddressWithCustomerFields: true);
            await FillNewAddress(shipping.ShippingNewAddress, "ShipNew");
            checkout.ModelState.Clear();
            await Call("NewShippingAddress", shipping, await CreateAddressAttributeFormAsync(new Dictionary<string, string>
            {
                ["shipping_address_id"] = "0",
                ["nextstep"] = "next",
                ["PickupInStore"] = "false"
            }));

            var pickupForm = await CreateAddressAttributeFormAsync(new Dictionary<string, string>
            {
                ["PickupInStore"] = "true",
                ["pickup-points-id"] = "pickup1___FixedRateTestShippingRateComputationMethod",
                ["nextstep"] = "next"
            });
            shipping = new global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel();
            await checkoutFactory.PrepareShippingAddressModelAsync(shipping, await Cart(), prePopulateNewAddressWithCustomerFields: true);
            await FillNewAddress(shipping.ShippingNewAddress, "ShipPickup");
            checkout.ModelState.Clear();
            await Call("NewShippingAddress", shipping, pickupForm);

            var shippingOption = "Shipping option 1___FixedRateTestShippingRateComputationMethod";
            try
            {
                var address = await customerService.GetCustomerShippingAddressAsync(customer) ?? addresses.FirstOrDefault();
                var shippingMethods = await checkoutFactory.PrepareShippingMethodModelAsync(await Cart(), address);
                var selected = shippingMethods?.ShippingMethods?.FirstOrDefault();
                if (selected != null)
                    shippingOption = $"{selected.Name}___{selected.ShippingRateComputationMethodSystemName}";
            }
            catch
            {
            }

            await Call("ShippingMethod");
            await Call("SelectShippingMethod", shippingOption, addressForm);
            await Call("SelectShippingMethod", shippingOption, pickupForm);
            await Call("PaymentMethod");
            var paymentModel = new global::Nop.Web.Models.Checkout.CheckoutPaymentMethodModel();
            await Call("SelectPaymentMethod", "Payments.TestMethod", paymentModel);
            await _services.GetRequiredService<IGenericAttributeService>().SaveAttributeAsync(customer,
                NopCustomerDefaults.SelectedPaymentMethodAttribute, "Payments.TestMethod", store.Id);
            await Call("PaymentInfo");
            await Call("EnterPaymentInfo", addressForm);
            await Call("Confirm");
            await Call("ConfirmOrder", true);

            await EnsurePlainProductInCartAsync();
            customer = await workContext.GetCurrentCustomerAsync();

            var extra = new Address
            {
                FirstName = "Delete",
                LastName = "Me",
                Email = $"del-{Guid.NewGuid():N}@example.com",
                Address1 = $"Delete {Guid.NewGuid():N}",
                City = "New York",
                ZipPostalCode = "10021",
                CountryId = 1,
                CreatedOnUtc = DateTime.UtcNow
            };
            var addressService = _services.GetRequiredService<IAddressService>();
            await addressService.InsertAddressAsync(extra);
            await customerService.InsertCustomerAddressAsync(customer, extra);
            await Call("DeleteEditBillingAddress", extra.Id, false);
            extra = new Address
            {
                FirstName = "Delete",
                LastName = "Ship",
                Email = $"del-ship-{Guid.NewGuid():N}@example.com",
                Address1 = $"DeleteShip {Guid.NewGuid():N}",
                City = "New York",
                ZipPostalCode = "10021",
                CountryId = 1,
                CreatedOnUtc = DateTime.UtcNow
            };
            await addressService.InsertAddressAsync(extra);
            await customerService.InsertCustomerAddressAsync(customer, extra);
            await Call("DeleteEditShippingAddress", extra.Id, true);

            orderSettings.OnePageCheckoutEnabled = true;
            await settings.SaveSettingAsync(orderSettings);
            await EnsurePlainProductInCartAsync();
            customer = await workContext.GetCurrentCustomerAsync();
            addresses = await customerService.GetAddressesByCustomerIdAsync(customer.Id);
            addressId = addresses.FirstOrDefault()?.Id ?? addressId;

            await Call("OnePageCheckout");
            var opcBilling = new global::Nop.Web.Models.Checkout.CheckoutBillingAddressModel { ShipToSameAddress = true };
            await checkoutFactory.PrepareBillingAddressModelAsync(opcBilling, await Cart(), prePopulateNewAddressWithCustomerFields: true);
            await Call("OpcSaveBilling", opcBilling, await CreateAddressAttributeFormAsync(new Dictionary<string, string>
            {
                ["billing_address_id"] = addressId.ToString()
            }));
            await FillNewAddress(opcBilling.BillingNewAddress, "OpcBill");
            opcBilling.ShipToSameAddress = false;
            await Call("OpcSaveBilling", opcBilling, await CreateAddressAttributeFormAsync(new Dictionary<string, string>
            {
                ["billing_address_id"] = "0"
            }));

            var opcShipping = new global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel();
            await checkoutFactory.PrepareShippingAddressModelAsync(opcShipping, await Cart(), prePopulateNewAddressWithCustomerFields: true);
            await Call("OpcSaveShipping", opcShipping, await CreateAddressAttributeFormAsync(new Dictionary<string, string>
            {
                ["shipping_address_id"] = addressId.ToString()
            }));
            await FillNewAddress(opcShipping.ShippingNewAddress, "OpcShip");
            await Call("OpcSaveShipping", opcShipping, await CreateAddressAttributeFormAsync(new Dictionary<string, string>
            {
                ["shipping_address_id"] = "0"
            }));

            await Call("OpcSaveShippingMethod", shippingOption, addressForm);
            await Call("OpcSavePaymentMethod", "Payments.TestMethod", paymentModel);
            await Call("OpcSavePaymentInfo", addressForm);
            await Call("OpcConfirmOrder", true);
            await Call("Completed", (int?)null);
            await Call("GetAddressById", addressId);
            await Call("OpcCompleteRedirectionPayment");
        }
        finally
        {
            orderSettings.OnePageCheckoutEnabled = previousOpc;
            orderSettings.MinimumOrderPlacementInterval = previousInterval;
            orderSettings.DisplayPickupInStoreOnShippingMethodPage = previousPickupPage;
            shippingSettings.ShipToSameAddress = previousShipToSame;
            shippingSettings.AllowPickupInStore = previousAllowPickup;
            await settings.SaveSettingAsync(orderSettings);
            await settings.SaveSettingAsync(shippingSettings);
            await EnsurePlainProductInCartAsync();
        }
    }

    public async Task ExerciseOnePageCheckoutGoldAsync()
    {
        await EnableCheckoutTestPluginsAsync();
        await EnsurePlainProductInCartAsync();

        var orderSettings = _services.GetRequiredService<OrderSettings>();
        var previousOpc = orderSettings.OnePageCheckoutEnabled;
        var previousInterval = orderSettings.MinimumOrderPlacementInterval;
        orderSettings.OnePageCheckoutEnabled = true;
        orderSettings.MinimumOrderPlacementInterval = 0;
        await _services.GetRequiredService<ISettingService>().SaveSettingAsync(orderSettings);

        try
        {
            var checkout = CreateController<global::Nop.Web.Controllers.CheckoutController>();
            var factory = _services.GetRequiredService<global::Nop.Web.Factories.ICheckoutModelFactory>();
            var customer = await _services.GetRequiredService<IWorkContext>().GetCurrentCustomerAsync();
            var store = await _services.GetRequiredService<IStoreContext>().GetCurrentStoreAsync();
            var cart = await _services.GetRequiredService<IShoppingCartService>()
                .GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);
            var addresses = await _services.GetRequiredService<ICustomerService>().GetAddressesByCustomerIdAsync(customer.Id);
            var addressId = addresses.FirstOrDefault()?.Id ?? 0;

            async Task Fill(global::Nop.Web.Models.Common.AddressModel address, string suffix)
            {
                address.FirstName = "Coverage";
                address.LastName = suffix;
                address.Email = $"{suffix}-{Guid.NewGuid():N}@example.com";
                address.Address1 = $"{suffix} {Guid.NewGuid():N}";
                address.City = "New York";
                address.ZipPostalCode = "10021";
                address.CountryId = 1;
                address.StateProvinceId = 1;
                address.PhoneNumber = "5550001111";
            }

            var emptyId = new FormCollection(new Dictionary<string, StringValues>
            {
                ["billing_address_id"] = "0",
                ["shipping_address_id"] = "0"
            });
            var existingId = new FormCollection(new Dictionary<string, StringValues>
            {
                ["billing_address_id"] = addressId.ToString(),
                ["shipping_address_id"] = addressId.ToString()
            });

            var billing = new global::Nop.Web.Models.Checkout.CheckoutBillingAddressModel { ShipToSameAddress = false };
            await factory.PrepareBillingAddressModelAsync(billing, cart, prePopulateNewAddressWithCustomerFields: true);
            await Fill(billing.BillingNewAddress, "OpcGoldBill");
            checkout.ModelState.Clear();
            await checkout.OpcSaveBilling(billing, emptyId);

            billing.ShipToSameAddress = true;
            checkout.ModelState.Clear();
            await checkout.OpcSaveBilling(billing, existingId);

            var shipping = new global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel();
            await factory.PrepareShippingAddressModelAsync(shipping, cart, prePopulateNewAddressWithCustomerFields: true);
            await Fill(shipping.ShippingNewAddress, "OpcGoldShip");
            checkout.ModelState.Clear();
            await checkout.OpcSaveShipping(shipping, emptyId);
            checkout.ModelState.Clear();
            await checkout.OpcSaveShipping(shipping, existingId);

            var option = "Shipping option 1___FixedRateTestShippingRateComputationMethod";
            try
            {
                var methods = await factory.PrepareShippingMethodModelAsync(cart, addresses.FirstOrDefault());
                var selected = methods.ShippingMethods.FirstOrDefault();
                if (selected != null)
                    option = $"{selected.Name}___{selected.ShippingRateComputationMethodSystemName}";
            }
            catch
            {
            }

            checkout.ModelState.Clear();
            await checkout.OpcSaveShippingMethod(option, existingId);
            await _services.GetRequiredService<IGenericAttributeService>().SaveAttributeAsync(customer,
                NopCustomerDefaults.SelectedPaymentMethodAttribute, "Payments.TestMethod", store.Id);
            checkout.ModelState.Clear();
            await checkout.OpcSavePaymentMethod("Payments.TestMethod",
                new global::Nop.Web.Models.Checkout.CheckoutPaymentMethodModel());
            checkout.ModelState.Clear();
            await checkout.OpcSavePaymentInfo(existingId);
            checkout.ModelState.Clear();
            await checkout.OpcConfirmOrder(true);
        }
        catch
        {
        }
        finally
        {
            orderSettings.OnePageCheckoutEnabled = previousOpc;
            orderSettings.MinimumOrderPlacementInterval = previousInterval;
            await _services.GetRequiredService<ISettingService>().SaveSettingAsync(orderSettings);
            await EnsurePlainProductInCartAsync();
        }
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

    public async Task ExerciseSkippedSafeInvokesAsync(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            object instance;
            try
            {
                instance = ActivatorUtilities.CreateInstance(_services, type);
                if (instance is Controller)
                    AttachMvc(instance);
                TypesCreated++;
            }
            catch
            {
                TypesFailed++;
                continue;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(m => !m.IsSpecialName && ShouldSkipMethodName(m.Name)
                                     && !m.Name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase)
                                     && !m.Name.Contains("Install", StringComparison.OrdinalIgnoreCase)
                                     && !m.Name.Contains("ConfirmOrder", StringComparison.OrdinalIgnoreCase)
                                     && !m.Name.Contains("ChangeEncryptionKey", StringComparison.OrdinalIgnoreCase)
                                     && !m.Name.Contains("Import", StringComparison.OrdinalIgnoreCase)
                                     && !m.Name.Contains("UploadPlugin", StringComparison.OrdinalIgnoreCase)
                                     && !m.Name.Contains("Restart", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var args = method.GetParameters().Select(p =>
                    {
                        if (p.ParameterType == typeof(int) || p.ParameterType == typeof(int?))
                            return 0;
                        if (p.ParameterType == typeof(ICollection<int>))
                            return new List<int> { 0 };
                        return CreateArg(p.ParameterType, p.Name, type);
                    }).ToArray();
                    var (ok, _) = await TryInvokeAsync(instance, method, args, DefaultTimeout);
                    if (ok)
                        MethodsInvoked++;
                    else
                        MethodsFailed++;
                }
                catch
                {
                    MethodsFailed++;
                }
            }
        }
    }

    public void ExerciseBulkEditDataHelpers()
    {
        var nested = typeof(global::Nop.Web.Areas.Admin.Controllers.ProductController)
            .GetNestedType("BulkEditData", BindingFlags.NonPublic | BindingFlags.Public);
        if (nested == null)
            return;

        var create = Activator.CreateInstance(nested, 1, 0);
        nested.GetProperty("IsSelected")?.SetValue(create, true);
        nested.GetProperty("Name")?.SetValue(create, "Coverage bulk");
        nested.GetProperty("Sku")?.SetValue(create, "COVBULK");
        nested.GetProperty("Price")?.SetValue(create, 10m);
        nested.GetProperty("OldPrice")?.SetValue(create, 12m);
        nested.GetProperty("Quantity")?.SetValue(create, 5);
        nested.GetProperty("IsPublished")?.SetValue(create, true);
        nested.GetMethod("NeedToCreate")?.Invoke(create, [true]);
        nested.GetMethod("NeedToCreate")?.Invoke(create, [false]);
        nested.GetMethod("CreateProduct")?.Invoke(create, [true]);
        nested.GetMethod("CreateProduct")?.Invoke(create, [false]);

        var update = Activator.CreateInstance(nested, 1, 0);
        nested.GetProperty("IsSelected")?.SetValue(update, true);
        nested.GetProperty("Name")?.SetValue(update, "Coverage updated");
        nested.GetProperty("Sku")?.SetValue(update, "COVUPD");
        nested.GetProperty("Price")?.SetValue(update, 15m);
        nested.GetProperty("OldPrice")?.SetValue(update, 20m);
        nested.GetProperty("Quantity")?.SetValue(update, 7);
        nested.GetProperty("IsPublished")?.SetValue(update, false);
        nested.GetProperty("Product")?.SetValue(update, new Product
        {
            Name = "Original",
            Sku = "OLD",
            Price = 1,
            OldPrice = 2,
            StockQuantity = 1,
            Published = true
        });
        nested.GetMethod("NeedToUpdate")?.Invoke(update, [true]);
        nested.GetMethod("NeedToUpdate")?.Invoke(update, [false]);
        nested.GetMethod("UpdateProduct")?.Invoke(update, [true]);
        nested.GetMethod("UpdateProduct")?.Invoke(update, [false]);
        MethodsInvoked += 8;
    }

    public async Task<IFormCollection> BuildProductAttributeFormAsync(int productId, IDictionary<string, string> extra = null)
    {
        var attributeService = _services.GetRequiredService<IProductAttributeService>();
        var values = extra != null
            ? new Dictionary<string, string>(extra, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(string name, string fileName)>();
        foreach (var mapping in await attributeService.GetProductAttributeMappingsByProductIdAsync(productId))
        {
            var controlId = $"product_attribute_{mapping.Id}";
            switch (mapping.AttributeControlType)
            {
                case AttributeControlType.Checkboxes:
                case AttributeControlType.ReadonlyCheckboxes:
                    values[controlId] = string.Join(",",
                        (await attributeService.GetProductAttributeValuesAsync(mapping.Id)).Select(value => value.Id));
                    break;
                case AttributeControlType.Datepicker:
                    values[$"{controlId}_day"] = "1";
                    values[$"{controlId}_month"] = "1";
                    values[$"{controlId}_year"] = "2026";
                    break;
                case AttributeControlType.TextBox:
                case AttributeControlType.MultilineTextbox:
                    values[controlId] = "coverage text";
                    break;
                case AttributeControlType.FileUpload:
                    files.Add((controlId, "coverage.jpg"));
                    break;
                default:
                    var selected = (await attributeService.GetProductAttributeValuesAsync(mapping.Id)).FirstOrDefault();
                    if (selected != null)
                    {
                        values[controlId] = selected.Id.ToString();
                        if (selected.CustomerEntersQty)
                            values[$"{controlId}_{selected.Id}_qty"] = "2";
                    }
                    break;
            }
        }

        return CreateForm(values, true, files);
    }

    public async Task ExerciseRemainingVolumeAsync()
    {
        await SeedCoverageAttributesAsync();
        await SeedShoppingCartAsync();

        async Task Try(Func<Task> action)
        {
            try { await action(); } catch { }
        }

        var productService = _services.GetRequiredService<IProductService>();
        var attributeService = _services.GetRequiredService<IProductAttributeService>();
        var productFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IProductModelFactory>();
        var publicProductFactory = _services.GetRequiredService<global::Nop.Web.Factories.IProductModelFactory>();
        var productController = CreateController<global::Nop.Web.Areas.Admin.Controllers.ProductController>();
        var publicProductController = CreateController<global::Nop.Web.Controllers.ProductController>();
        var shoppingCart = CreateController<global::Nop.Web.Controllers.ShoppingCartController>();
        var workContext = _services.GetRequiredService<IWorkContext>();
        var customer = await workContext.GetCurrentCustomerAsync();
        var store = await _services.GetRequiredService<IStoreContext>().GetCurrentStoreAsync();
        var categories = await _services.GetRequiredService<ICategoryService>().GetAllCategoriesAsync();
        var manufacturers = await _services.GetRequiredService<IManufacturerService>().GetAllManufacturersAsync();
        var discounts = await _services.GetRequiredService<IDiscountService>().GetAllDiscountsAsync(showHidden: true, isActive: null);
        var warehouses = await _services.GetRequiredService<IWarehouseService>().GetAllWarehousesAsync();
        Product host = null;
        foreach (var candidate in await productService.SearchProductsAsync(pageSize: 20))
        {
            if ((await attributeService.GetProductAttributeMappingsByProductIdAsync(candidate.Id)).Count == 0)
                continue;
            host = candidate;
            break;
        }

        host ??= (await productService.SearchProductsAsync(pageSize: 1)).First();

        async Task<Product> InsertSpecialAsync(string name, Action<Product> configure)
        {
            var product = new Product
            {
                Name = name,
                ProductType = ProductType.SimpleProduct,
                VisibleIndividually = true,
                Published = true,
                Sku = $"COV{Guid.NewGuid():N}"[..12],
                Price = 25m,
                IsShipEnabled = true,
                ManageInventoryMethod = ManageInventoryMethod.DontManageStock,
                StockQuantity = 50,
                OrderMinimumQuantity = 1,
                OrderMaximumQuantity = 10000,
                Weight = 1,
                Length = 1,
                Width = 1,
                Height = 1,
                CreatedOnUtc = DateTime.UtcNow,
                UpdatedOnUtc = DateTime.UtcNow,
                RecurringCycleLength = 30,
                RecurringTotalCycles = 12,
                RentalPriceLength = 1
            };
            configure(product);
            await productService.InsertProductAsync(product);
            return product;
        }

        var created = new List<Product>();
        await Try(async () => created.Add(await InsertSpecialAsync("Coverage downloadable", p =>
        {
            p.IsDownload = true;
            p.UnlimitedDownloads = true;
            p.HasSampleDownload = true;
            p.DownloadActivationType = DownloadActivationType.WhenOrderIsPaid;
        })));
        await Try(async () => created.Add(await InsertSpecialAsync("Coverage rental", p =>
        {
            p.IsRental = true;
            p.RentalPricePeriod = RentalPricePeriod.Days;
            p.RentalPriceLength = 3;
        })));
        await Try(async () => created.Add(await InsertSpecialAsync("Coverage gift card", p =>
        {
            p.IsGiftCard = true;
            p.GiftCardType = GiftCardType.Virtual;
            p.IsShipEnabled = false;
        })));
        await Try(async () => created.Add(await InsertSpecialAsync("Coverage recurring", p =>
        {
            p.IsRecurring = true;
            p.RecurringCyclePeriod = RecurringProductCyclePeriod.Months;
        })));
        await Try(async () => created.Add(await InsertSpecialAsync("Coverage grouped", p =>
        {
            p.ProductType = ProductType.GroupedProduct;
        })));
        await Try(async () => created.Add(await InsertSpecialAsync("Coverage enter price", p =>
        {
            p.CustomerEntersPrice = true;
            p.MinimumCustomerEnteredPrice = 5;
            p.MaximumCustomerEnteredPrice = 500;
        })));
        await Try(async () => created.Add(await InsertSpecialAsync("Coverage stock warehouse", p =>
        {
            p.ManageInventoryMethod = ManageInventoryMethod.ManageStock;
            p.UseMultipleWarehouses = true;
            p.StockQuantity = 20;
            p.WarehouseId = warehouses.FirstOrDefault()?.Id ?? 0;
            p.AllowBackInStockSubscriptions = true;
            p.BackorderMode = BackorderMode.NoBackorders;
            p.LowStockActivity = LowStockActivity.Nothing;
        })));
        await Try(async () => created.Add(await InsertSpecialAsync("Coverage required others", p =>
        {
            p.RequireOtherProducts = true;
            p.RequiredProductIds = host.Id.ToString();
            p.AutomaticallyAddRequiredProducts = true;
        })));

        foreach (var special in created)
        {
            await Try(async () =>
            {
                var model = await productFactory.PrepareProductModelAsync(null, special);
                model.SelectedCategoryIds = categories.Take(2).Select(c => c.Id).ToList();
                model.SelectedManufacturerIds = manufacturers.Take(2).Select(m => m.Id).ToList();
                model.SelectedDiscountIds = discounts.Take(2).Select(d => d.Id).ToList();
                model.SelectedProductTags = ["coverage-tag", special.Name];
                model.LastStockQuantity = special.StockQuantity;
                if (special.UseMultipleWarehouses)
                {
                    model.ManageInventoryMethodId = (int)ManageInventoryMethod.ManageStock;
                    model.UseMultipleWarehouses = true;
                    var warehouseForm = new Dictionary<string, string>();
                    foreach (var warehouse in warehouses)
                    {
                        warehouseForm[$"warehouse_qty_{warehouse.Id}"] = "4";
                        warehouseForm[$"warehouse_reserved_{warehouse.Id}"] = "1";
                        warehouseForm[$"warehouse_used_{warehouse.Id}"] = warehouse.Id.ToString();
                    }
                    ApplyRequestForm(warehouseForm);
                }

                productController.ModelState.Clear();
                await productController.Edit(model, true);
                model.LastStockQuantity = special.StockQuantity - 1;
                productController.ModelState.Clear();
                await productController.Edit(model, false);
            });
            await Try(async () => _ = await publicProductFactory.PrepareProductDetailsModelAsync(special));
            await Try(async () =>
            {
                var form = await BuildProductAttributeFormAsync(special.Id, new Dictionary<string, string>
                {
                    [$"addtocart_{special.Id}.EnteredQuantity"] = "1",
                    [$"rental_start_date_{special.Id}"] = DateTime.UtcNow.ToString("d"),
                    [$"rental_end_date_{special.Id}"] = DateTime.UtcNow.AddDays(3).ToString("d"),
                    [$"addtocart_{special.Id}.CustomerEnteredPrice"] = "15"
                });
                shoppingCart.ModelState.Clear();
                await shoppingCart.AddProductToCart_Details(special.Id, (int)ShoppingCartType.ShoppingCart, form);
                await shoppingCart.ProductDetails_AttributeChange(special.Id, true, true, form);
                publicProductController.ModelState.Clear();
                await publicProductController.EstimateShipping(new global::Nop.Web.Models.Catalog.ProductDetailsModel.ProductEstimateShippingModel
                {
                    ProductId = special.Id,
                    ZipPostalCode = "10021",
                    CountryId = 1,
                    StateProvinceId = 1
                }, form);
            });
        }

        await Try(async () =>
        {
            var create = await productFactory.PrepareProductModelAsync(null, null);
            create.Name = "Coverage mapped " + Guid.NewGuid().ToString("N")[..8];
            create.Sku = "COVM" + Guid.NewGuid().ToString("N")[..6];
            create.Price = 12m;
            create.Published = true;
            create.SelectedCategoryIds = categories.Take(2).Select(c => c.Id).ToList();
            create.SelectedManufacturerIds = manufacturers.Take(2).Select(m => m.Id).ToList();
            create.SelectedDiscountIds = discounts.Take(2).Select(d => d.Id).ToList();
            create.SelectedProductTags = ["coverage-create"];
            create.ManageInventoryMethodId = (int)ManageInventoryMethod.ManageStock;
            create.UseMultipleWarehouses = warehouses.Count > 0;
            create.StockQuantity = 15;
            var warehouseForm = new Dictionary<string, string>();
            foreach (var warehouse in warehouses)
            {
                warehouseForm[$"warehouse_qty_{warehouse.Id}"] = "3";
                warehouseForm[$"warehouse_reserved_{warehouse.Id}"] = "0";
                warehouseForm[$"warehouse_used_{warehouse.Id}"] = warehouse.Id.ToString();
            }
            ApplyRequestForm(warehouseForm);
            productController.ModelState.Clear();
            await productController.Create(create, true);
        });

        var attributed = host;
        foreach (var candidate in await productService.SearchProductsAsync(pageSize: 40))
        {
            if ((await attributeService.GetProductAttributeMappingsByProductIdAsync(candidate.Id)).Count == 0)
                continue;
            attributed = candidate;
            break;
        }
        var others = (await productService.SearchProductsAsync(pageSize: 10)).Where(p => p.Id != attributed.Id).Take(3).ToList();
        await Try(async () =>
        {
            productController.ModelState.Clear();
            await productController.RelatedProductAddPopup(new global::Nop.Web.Areas.Admin.Models.Catalog.AddRelatedProductModel
            {
                ProductId = attributed.Id,
                SelectedProductIds = others.Select(p => p.Id).ToList()
            });
            productController.ModelState.Clear();
            await productController.CrossSellProductAddPopup(new global::Nop.Web.Areas.Admin.Models.Catalog.AddCrossSellProductModel
            {
                ProductId = attributed.Id,
                SelectedProductIds = others.Select(p => p.Id).ToList()
            });
            productController.ModelState.Clear();
            await productController.AssociatedProductAddPopup(new global::Nop.Web.Areas.Admin.Models.Catalog.AddAssociatedProductModel
            {
                ProductId = attributed.Id,
                SelectedProductIds = others.Select(p => p.Id).ToList()
            });
        });

        await Try(async () =>
        {
            var tier = await productFactory.PrepareTierPriceModelAsync(
                new global::Nop.Web.Areas.Admin.Models.Catalog.TierPriceModel(), attributed, null);
            tier.ProductId = attributed.Id;
            tier.Quantity = 5;
            tier.Price = 9;
            productController.ModelState.Clear();
            await productController.TierPriceCreatePopup(attributed.Id);
            productController.ModelState.Clear();
            await productController.TierPriceCreatePopup(tier);
        });

        await Try(async () =>
        {
            var combo = await productFactory.PrepareProductAttributeCombinationModelAsync(
                new global::Nop.Web.Areas.Admin.Models.Catalog.ProductAttributeCombinationModel(), attributed, null);
            combo.ProductId = attributed.Id;
            combo.StockQuantity = 4;
            combo.AllowOutOfStockOrders = true;
            combo.Sku = "COVCOM";
            var form = await BuildProductAttributeFormAsync(attributed.Id);
            productController.ModelState.Clear();
            await productController.ProductAttributeCombinationCreatePopup(attributed.Id);
            productController.ModelState.Clear();
            await productController.ProductAttributeCombinationCreatePopup(attributed.Id, combo, form);
            await productController.ProductAttributeCombinationGeneratePopup(attributed.Id);
            await productController.ProductAttributeCombinationGeneratePopup(form, combo);
            await productController.GenerateAllAttributeCombinations(attributed.Id);
        });

        await Try(async () =>
        {
            var mappings = await attributeService.GetProductAttributeMappingsByProductIdAsync(attributed.Id);
            var mapping = mappings.FirstOrDefault(m => m.AttributeControlType == AttributeControlType.DropdownList)
                          ?? mappings.FirstOrDefault();
            if (mapping == null)
                return;
            var edit = await productFactory.PrepareProductAttributeMappingModelAsync(null, attributed, mapping);
            edit.ConditionModel.EnableCondition = true;
            edit.ConditionModel.SelectedProductAttributeId = mappings.First().Id;
            var form = await BuildProductAttributeFormAsync(attributed.Id);
            productController.ModelState.Clear();
            await productController.ProductAttributeMappingEdit(edit, true, form);
            var valueModel = await productFactory.PrepareProductAttributeValueModelAsync(
                new global::Nop.Web.Areas.Admin.Models.Catalog.ProductAttributeValueModel(), mapping, null);
            valueModel.Name = "Coverage value";
            valueModel.PriceAdjustment = 2;
            productController.ModelState.Clear();
            await productController.ProductAttributeValueCreatePopup(mapping.Id);
            productController.ModelState.Clear();
            await productController.ProductAttributeValueCreatePopup(valueModel);
        });

        await Try(async () =>
        {
            var search = await productFactory.PrepareProductSearchModelAsync(new global::Nop.Web.Areas.Admin.Models.Catalog.ProductSearchModel());
            search.SetGridPageSize();
            productController.ModelState.Clear();
            await productController.ExportXmlAll(search);
            await productController.ExportExcelAll(search);
            await productController.DownloadCatalogAsPdf(search);
            await productController.ExportXmlSelected(string.Join(",", (await productService.SearchProductsAsync(pageSize: 3)).Select(p => p.Id)));
            await productController.ExportExcelSelected(string.Join(",", (await productService.SearchProductsAsync(pageSize: 3)).Select(p => p.Id)));
        });

        await Try(async () =>
        {
            var tags = await _services.GetRequiredService<IProductTagService>().GetAllProductTagsAsync();
            var tag = tags.FirstOrDefault();
            if (tag == null)
                return;
            var tagModel = await productFactory.PrepareProductTagModelAsync(null, tag);
            productController.ModelState.Clear();
            await productController.EditProductTag(tag.Id);
            productController.ModelState.Clear();
            await productController.EditProductTag(tagModel, true);
            var tagged = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductTagProductSearchModel { ProductTagId = tag.Id };
            tagged.SetGridPageSize();
            await productController.TaggedProducts(tagged);
        });

        var cart = await _services.GetRequiredService<IShoppingCartService>()
            .GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);
        await Try(async () =>
        {
            var qty = new Dictionary<string, string>();
            foreach (var item in cart)
                qty[$"itemquantity{item.Id}"] = (item.Quantity + 1).ToString();
            var checkoutValues = (await BuildAttributeFormAsync<CheckoutAttribute, CheckoutAttributeValue>("checkout_attribute_")).Values;
            foreach (var pair in checkoutValues)
                qty[pair.Key] = pair.Value;
            shoppingCart.ModelState.Clear();
            await shoppingCart.UpdateCart(CreateForm(qty, true));
            shoppingCart.ModelState.Clear();
            await shoppingCart.StartCheckout(CreateForm(qty));
            shoppingCart.ModelState.Clear();
            await shoppingCart.ApplyGiftCard("coverage-gift", CreateForm(qty));
            shoppingCart.ModelState.Clear();
            await shoppingCart.RemoveDiscountCoupon(CreateForm(qty));
            shoppingCart.ModelState.Clear();
            await shoppingCart.RemoveGiftCardCode(CreateForm(qty));
            await shoppingCart.CheckoutAttributeChange(CreateForm(qty, true), true);
        });

        var adminCustomer = CreateController<global::Nop.Web.Areas.Admin.Controllers.CustomerController>();
        var customerFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.ICustomerModelFactory>();
        await Try(async () =>
        {
            var model = await customerFactory.PrepareCustomerModelAsync(null, customer);
            adminCustomer.ModelState.Clear();
            await adminCustomer.MarkVatNumberAsValid(model);
            await adminCustomer.MarkVatNumberAsInvalid(model);
            await adminCustomer.SendWelcomeMessage(model);
            await adminCustomer.ReSendActivationMessage(model);
            model.SendPm.Subject = "Coverage PM";
            model.SendPm.Message = "Coverage private message";
            await adminCustomer.SendPm(model);
            await adminCustomer.RewardPointsHistoryAdd(new global::Nop.Web.Areas.Admin.Models.Customers.AddRewardPointsToCustomerModel
            {
                CustomerId = customer.Id,
                Points = 10,
                StoreId = store.Id,
                Message = "Coverage points",
                ActivatePointsImmediately = false,
                ActivationDelay = 1,
                ActivationDelayPeriodId = 0,
                PointsValidity = 30
            });
            await adminCustomer.LoadCustomerStatistics("month");
            await adminCustomer.LoadCustomerStatistics("year");
            await adminCustomer.GdprExport(customer.Id);
        });
        await Try(async () =>
        {
            var addressModel = await customerFactory.PrepareCustomerAddressModelAsync(
                new global::Nop.Web.Areas.Admin.Models.Customers.CustomerAddressModel(), customer, null);
            addressModel.CustomerId = customer.Id;
            addressModel.Address.FirstName = "Coverage";
            addressModel.Address.LastName = "Addr";
            addressModel.Address.Email = $"addr-{Guid.NewGuid():N}@example.com";
            addressModel.Address.Address1 = "1 Coverage Way";
            addressModel.Address.City = "New York";
            addressModel.Address.ZipPostalCode = "10021";
            addressModel.Address.CountryId = 1;
            addressModel.Address.StateProvinceId = 1;
            adminCustomer.ModelState.Clear();
            await adminCustomer.AddressCreate(customer.Id);
            adminCustomer.ModelState.Clear();
            await adminCustomer.AddressCreate(addressModel, await CreateAddressAttributeFormAsync());
        });

        var orderController = CreateController<global::Nop.Web.Areas.Admin.Controllers.OrderController>();
        var orderFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IOrderModelFactory>();
        var orderService = _services.GetRequiredService<IOrderService>();
        foreach (var order in await orderService.SearchOrdersAsync(pageIndex: 0, pageSize: 4))
        {
            await Try(async () =>
            {
                await orderController.CancelOrder(order.Id);
                await orderController.CaptureOrder(order.Id);
                await orderController.MarkOrderAsPaid(order.Id);
                await orderController.RefundOrder(order.Id);
                await orderController.RefundOrderOffline(order.Id);
                await orderController.VoidOrder(order.Id);
                await orderController.VoidOrderOffline(order.Id);
                await orderController.OrderNoteAdd(order.Id, 0, false, "Coverage note");
                var search = await orderFactory.PrepareOrderSearchModelAsync(new global::Nop.Web.Areas.Admin.Models.Orders.OrderSearchModel());
                search.SetGridPageSize();
                await orderController.ExportXmlAll(search);
                await orderController.ExportExcelAll(search);
                await orderController.PdfInvoiceAll(search);
            });
            await Try(async () =>
            {
                var shipments = await _services.GetRequiredService<IShipmentService>().GetShipmentsByOrderIdAsync(order.Id);
                var shipment = shipments.FirstOrDefault();
                if (shipment == null)
                    return;
                await orderController.SetAsShipped(shipment.Id);
                await orderController.SetAsReadyForPickup(shipment.Id);
                await orderController.SetAsDelivered(shipment.Id);
                var shipmentModel = await orderFactory.PrepareShipmentModelAsync(
                    new global::Nop.Web.Areas.Admin.Models.Orders.ShipmentModel(), shipment, order);
                shipmentModel.TrackingNumber = "COV-SHIP";
                await orderController.SetTrackingNumber(shipmentModel);
                await orderController.SetShipmentAdminComment(shipmentModel);
                await orderController.PdfPackagingSlip(shipment.Id);
            });
        }

        await Try(async () =>
        {
            var settings = CreateController<global::Nop.Web.Areas.Admin.Controllers.SettingController>();
            var settingsFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.ISettingModelFactory>();
            settings.ModelState.Clear();
            await settings.ShoppingCart(await settingsFactory.PrepareShoppingCartSettingsModelAsync());
            settings.ModelState.Clear();
            await settings.AppSettings(await settingsFactory.PrepareAppSettingsModel());
            settings.ModelState.Clear();
            await settings.SettingAdd(new global::Nop.Web.Areas.Admin.Models.Settings.SettingModel
            {
                Name = "coverage.setting." + Guid.NewGuid().ToString("N")[..8],
                Value = "1",
                StoreId = 0
            });
        });

        await Try(async () =>
        {
            var acl = ActivatorUtilities.CreateInstance<global::Nop.Web.Areas.Admin.Infrastructure.AclEventConsumer>(_services);
            var productModel = await productFactory.PrepareProductModelAsync(null, host);
            await acl.HandleEventAsync(new global::Nop.Web.Framework.Events.ModelPreparedEvent<global::Nop.Web.Framework.Models.BaseNopModel>(productModel));
            await acl.HandleEventAsync(new global::Nop.Web.Framework.Events.ModelReceivedEvent<global::Nop.Web.Framework.Models.BaseNopModel>(productModel, new ModelStateDictionary()));
            await acl.HandleEventAsync(new EntityInsertedEvent<Product>(host));
            var category = categories.FirstOrDefault();
            if (category != null)
                await acl.HandleEventAsync(new EntityInsertedEvent<Category>(category));
        });

        await Try(async () =>
        {
            foreach (var sample in (await productService.SearchProductsAsync(pageSize: 20)).Take(8))
            {
                var form = await BuildProductAttributeFormAsync(sample.Id, new Dictionary<string, string>
                {
                    [$"addtocart_{sample.Id}.EnteredQuantity"] = "1"
                });
                await shoppingCart.ProductDetails_AttributeChange(sample.Id, true, true, form);
                await shoppingCart.AddProductToCart_Details(sample.Id, (int)ShoppingCartType.ShoppingCart, form);
                var reviews = await publicProductFactory.PrepareProductReviewsModelAsync(sample);
                reviews.AddProductReview.Title = "Coverage";
                reviews.AddProductReview.ReviewText = "Coverage review";
                reviews.AddProductReview.Rating = 4;
                publicProductController.ModelState.Clear();
                await publicProductController.ProductReviewsAdd(sample.Id, reviews, true);
            }
        });

        await Try(async () =>
        {
            var download = CreateController<global::Nop.Web.Controllers.DownloadController>();
            foreach (var special in created)
                await download.Sample(special.Id);
            await download.Sample(host.Id);
            foreach (var order in await _services.GetRequiredService<IOrderService>().SearchOrdersAsync(pageIndex: 0, pageSize: 5))
            {
                var items = await _services.GetRequiredService<IOrderService>().GetOrderItemsAsync(order.Id);
                foreach (var item in items.Take(2))
                {
                    await download.GetDownload(item.OrderItemGuid, true);
                    await download.GetDownload(item.OrderItemGuid, false);
                    await download.GetLicense(item.OrderItemGuid);
                }
            }
        });

        await Try(async () =>
        {
            var orders = CreateController<global::Nop.Web.Controllers.OrderController>();
            foreach (var order in await _services.GetRequiredService<IOrderService>().SearchOrdersAsync(pageIndex: 0, pageSize: 8))
            {
                await orders.Details(order.Id);
                await orders.PrintOrderDetails(order.Id);
                await orders.GetPdfInvoice(order.Id);
                await orders.ReOrder(order.Id);
                await orders.RePostPayment(order.Id);
            }

            await orders.CustomerOrders(1, OrderHistoryPeriods.All);
            await orders.CustomerOrders(1, OrderHistoryPeriods.Day);
            await orders.CustomerRecurringPayments();
            await orders.CustomerRewardPoints(1);
        });

        await EnsurePlainProductInCartAsync();
    }

    public async Task ExerciseFactoryPreparedAdminCrudAsync()
    {
        var assembly = typeof(global::Nop.Web.Controllers.HomeController).Assembly;
        var factoryInterfaces = assembly.GetTypes()
            .Where(t => t.IsInterface
                        && t.Namespace == "Nop.Web.Areas.Admin.Factories"
                        && t.Name.EndsWith("ModelFactory", StringComparison.Ordinal))
            .ToList();

        foreach (var iface in factoryInterfaces)
        {
            object factory;
            try
            {
                factory = _services.GetService(iface);
            }
            catch
            {
                continue;
            }

            if (factory == null)
                continue;
            if (iface.Name is "IProductModelFactory" or "IOrderModelFactory" or "ICustomerModelFactory"
                or "ISettingModelFactory" or "IShoppingCartModelFactory" or "ICheckoutModelFactory")
                continue;

            foreach (var prepare in iface.GetMethods().Where(method =>
                         method.Name.StartsWith("Prepare", StringComparison.Ordinal)
                         && method.Name.EndsWith("ModelAsync", StringComparison.Ordinal)
                         && !method.Name.Contains("List", StringComparison.Ordinal)
                         && !method.Name.Contains("Search", StringComparison.Ordinal)))
            {
                var parameters = prepare.GetParameters();
                var entityParam = parameters.FirstOrDefault(p => typeof(BaseEntity).IsAssignableFrom(p.ParameterType));
                var modelParam = parameters.FirstOrDefault(p => p.ParameterType.Name.EndsWith("Model", StringComparison.Ordinal));
                if (entityParam == null || modelParam == null)
                    continue;

                var entities = GetEntities(entityParam.ParameterType, 2);
                if (entities.Count == 0)
                    entities = [null];

                foreach (var entity in entities)
                {
                    object model;
                    try
                    {
                        var args = parameters.Select(p =>
                        {
                            if (p == entityParam)
                                return entity;
                            if (p == modelParam)
                                return null;
                            if (p.ParameterType == typeof(bool) || p.ParameterType == typeof(bool?))
                                return false;
                            return CreateArg(p.ParameterType, p.Name, iface);
                        }).ToArray();
                        model = await AwaitResult(prepare.Invoke(factory, args));
                    }
                    catch
                    {
                        continue;
                    }

                    if (model == null)
                        continue;

                    await InvokeMatchingControllerSaveAsync(model);
                }
            }
        }
    }

    private async Task InvokeMatchingControllerSaveAsync(object model)
    {
        var modelType = model.GetType();
        var modelName = modelType.Name;
        if (!modelName.EndsWith("Model", StringComparison.Ordinal))
            return;

        var entityName = modelName[..^5];
        var assembly = typeof(global::Nop.Web.Controllers.HomeController).Assembly;
        var controllerType = assembly.GetType($"Nop.Web.Areas.Admin.Controllers.{entityName}Controller")
                             ?? assembly.GetType($"Nop.Web.Controllers.{entityName}Controller");
        if (controllerType == null || controllerType.Name is "InstallController" or "ElFinderController"
            or "ProductController" or "OrderController" or "CustomerController" or "SettingController"
            or "CheckoutController" or "ShoppingCartController")
            return;

        object controller;
        try
        {
            controller = ActivatorUtilities.CreateInstance(_services, controllerType);
            if (controller is Controller)
                AttachMvc(controller);
            TypesCreated++;
        }
        catch
        {
            return;
        }

        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName
                             && method.GetParameters().Any(p => p.ParameterType == modelType || p.ParameterType.IsInstanceOfType(model))
                             && (method.Name.Contains("Edit", StringComparison.Ordinal)
                                 || method.Name.Contains("Create", StringComparison.Ordinal)
                                 || method.Name.Contains("Save", StringComparison.Ordinal)
                                 || method.Name.Contains("Update", StringComparison.Ordinal))
                             && !method.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                             && !method.Name.Contains("Import", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToList();

        foreach (var method in methods)
        {
            try
            {
                if (controller is Controller mvc)
                    mvc.ModelState.Clear();

                var nameProp = modelType.GetProperty("Name");
                if (nameProp?.CanWrite == true && nameProp.PropertyType == typeof(string)
                    && method.Name.Contains("Create", StringComparison.Ordinal))
                    nameProp.SetValue(model, "Coverage " + Guid.NewGuid().ToString("N")[..8]);

                var emailProp = modelType.GetProperty("Email");
                if (emailProp?.CanWrite == true && emailProp.PropertyType == typeof(string)
                    && method.Name.Contains("Create", StringComparison.Ordinal))
                    emailProp.SetValue(model, $"cov-{Guid.NewGuid():N}@example.com");

                var args = method.GetParameters().Select(p =>
                {
                    if (p.ParameterType == modelType || p.ParameterType.IsInstanceOfType(model))
                        return model;
                    if (p.Name == "continueEditing")
                        return (object)true;
                    if (typeof(IFormCollection).IsAssignableFrom(p.ParameterType))
                        return CreateForm();
                    return CreateArg(p.ParameterType, p.Name, controllerType);
                }).ToArray();

                var (ok, _) = await TryInvokeAsync(controller, method, args, TimeSpan.FromSeconds(12));
                if (ok)
                    MethodsInvoked++;
                else
                    MethodsFailed++;
            }
            catch
            {
                MethodsFailed++;
            }
        }
    }

    public async Task ExerciseCoverageTailsAsync()
    {
        await EnableCheckoutTestPluginsAsync();
        await SeedShoppingCartAsync();

        async Task Try(Func<Task> action)
        {
            try { await action(); } catch { }
        }

        var settingService = _services.GetRequiredService<ISettingService>();
        var orderSettings = _services.GetRequiredService<OrderSettings>();
        var customerSettings = _services.GetRequiredService<CustomerSettings>();
        var vendorSettings = _services.GetRequiredService<VendorSettings>();
        var privateMessages = _services.GetRequiredService<PrivateMessageSettings>();
        var gdprSettings = _services.GetRequiredService<global::Nop.Core.Domain.Gdpr.GdprSettings>();
        var sitemapSettings = _services.GetRequiredService<SitemapSettings>();
        var blogSettings = _services.GetRequiredService<BlogSettings>();
        var catalogSettings = _services.GetRequiredService<CatalogSettings>();

        var previousOpc = orderSettings.OnePageCheckoutEnabled;
        var previousReturnEnabled = orderSettings.ReturnRequestsEnabled;
        var previousReturnFiles = orderSettings.ReturnRequestsAllowFiles;
        var previousReturnDays = orderSettings.NumberOfDaysReturnRequestAvailable;
        var previousNewsletter = customerSettings.NewsletterEnabled;
        var previousVendorApply = vendorSettings.AllowCustomersToApplyForVendorAccount;
        var previousVendorEdit = vendorSettings.AllowVendorsToEditInfo;
        var previousVendorNotify = vendorSettings.NotifyStoreOwnerAboutVendorInformationChange;
        var previousPm = privateMessages.AllowPrivateMessages;
        var previousPmNotify = privateMessages.NotifyAboutPrivateMessages;
        var previousGdpr = gdprSettings.GdprEnabled;
        var previousGdprLog = gdprSettings.LogUserProfileChanges;
        var previousSitemapEnabled = sitemapSettings.SitemapEnabled;
        var previousSitemapBlog = sitemapSettings.SitemapIncludeBlogPosts;
        var previousSitemapCategories = sitemapSettings.SitemapIncludeCategories;
        var previousSitemapManufacturers = sitemapSettings.SitemapIncludeManufacturers;
        var previousSitemapProducts = sitemapSettings.SitemapIncludeProducts;
        var previousSitemapTags = sitemapSettings.SitemapIncludeProductTags;
        var previousSitemapTopics = sitemapSettings.SitemapIncludeTopics;
        var previousBlog = blogSettings.Enabled;
        var previousBackInStock = catalogSettings.MaximumBackInStockSubscriptions;
        var previousSkipPayment = TestPaymentMethod.TestSkipPaymentInfo;

        orderSettings.ReturnRequestsEnabled = true;
        orderSettings.ReturnRequestsAllowFiles = true;
        orderSettings.NumberOfDaysReturnRequestAvailable = 36500;
        orderSettings.OnePageCheckoutEnabled = true;
        orderSettings.MinimumOrderPlacementInterval = 0;
        customerSettings.NewsletterEnabled = true;
        vendorSettings.AllowCustomersToApplyForVendorAccount = true;
        vendorSettings.AllowVendorsToEditInfo = true;
        vendorSettings.NotifyStoreOwnerAboutVendorInformationChange = true;
        privateMessages.AllowPrivateMessages = true;
        privateMessages.NotifyAboutPrivateMessages = true;
        sitemapSettings.SitemapEnabled = true;
        sitemapSettings.SitemapIncludeBlogPosts = true;
        sitemapSettings.SitemapIncludeCategories = true;
        sitemapSettings.SitemapIncludeManufacturers = true;
        sitemapSettings.SitemapIncludeProducts = true;
        sitemapSettings.SitemapIncludeProductTags = true;
        sitemapSettings.SitemapIncludeTopics = true;
        blogSettings.Enabled = true;
        catalogSettings.MaximumBackInStockSubscriptions = 100;
        await settingService.SaveSettingAsync(orderSettings);
        await settingService.SaveSettingAsync(customerSettings);
        await settingService.SaveSettingAsync(vendorSettings);
        await settingService.SaveSettingAsync(privateMessages);
        await settingService.SaveSettingAsync(sitemapSettings);
        await settingService.SaveSettingAsync(blogSettings);
        await settingService.SaveSettingAsync(catalogSettings);

        var workContext = _services.GetRequiredService<IWorkContext>();
        var customerService = _services.GetRequiredService<ICustomerService>();
        var store = await _services.GetRequiredService<IStoreContext>().GetCurrentStoreAsync();
        var admin = await workContext.GetCurrentCustomerAsync();
        var previousVendorId = admin.VendorId;

        try
        {
            await Try(async () =>
            {
                var commonFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.ICommonModelFactory>();
                await commonFactory.PrepareSystemWarningModelsAsync();
                await InvokeInstanceMethodAsync(commonFactory, "PreparePluginsInstalledWarningModelAsync",
                    new List<global::Nop.Web.Areas.Admin.Models.Common.SystemWarningModel>());
                await InvokeInstanceMethodAsync(commonFactory, "PreparePluginsEnabledWarningModelAsync",
                    new List<global::Nop.Web.Areas.Admin.Models.Common.SystemWarningModel>());
                await InvokeInstanceMethodAsync(commonFactory, "PrepareIncompatibleWarningModelAsync",
                    new List<global::Nop.Web.Areas.Admin.Models.Common.SystemWarningModel>());
                await InvokeInstanceMethodAsync(commonFactory, "PreparePluginsCollisionsWarningModelAsync",
                    new List<global::Nop.Web.Areas.Admin.Models.Common.SystemWarningModel>());
            });

            await Try(async () =>
            {
                await _services.GetRequiredService<IStaticCacheManager>()
                    .RemoveByPrefixAsync("Nop.pres.sitemap");
                var sitemapFactory = _services.GetRequiredService<global::Nop.Web.Factories.ISitemapModelFactory>();
                await sitemapFactory.PrepareSitemapModelAsync(new global::Nop.Web.Models.Sitemap.SitemapPageModel
                {
                    PageNumber = 1
                });
            });

            await Try(async () =>
            {
                var reportFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IReportModelFactory>();
                var best = new global::Nop.Web.Areas.Admin.Models.Reports.BestCustomersReportSearchModel
                {
                    OrderBy = global::Nop.Services.Orders.OrderByEnum.OrderByTotalAmount,
                    StartDate = DateTime.UtcNow.AddYears(-2),
                    EndDate = DateTime.UtcNow
                };
                best.SetGridPageSize();
                await reportFactory.PrepareBestCustomersReportListModelAsync(best);
                best.OrderBy = global::Nop.Services.Orders.OrderByEnum.OrderByQuantity;
                await reportFactory.PrepareBestCustomersReportListModelAsync(best);

                var sales = new global::Nop.Web.Areas.Admin.Models.Reports.SalesSummarySearchModel
                {
                    StartDate = DateTime.UtcNow.AddYears(-2),
                    EndDate = DateTime.UtcNow
                };
                sales.SetGridPageSize();
                await reportFactory.PrepareSalesSummaryListModelAsync(sales);

                var orderFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IOrderModelFactory>();
                var brief = new global::Nop.Web.Areas.Admin.Models.Reports.BestsellerBriefSearchModel();
                brief.SetGridPageSize(5);
                await orderFactory.PrepareBestsellerBriefSearchModelAsync(brief);
                await orderFactory.PrepareBestsellerBriefListModelAsync(brief);
            });

            await Try(async () =>
            {
                var catalogFactory = _services.GetRequiredService<global::Nop.Web.Factories.ICatalogModelFactory>();
                await catalogFactory.PrepareSearchBoxModelAsync();
                var search = new global::Nop.Web.Models.Catalog.SearchModel { q = "computer" };
                await catalogFactory.PrepareSearchProductsModelAsync(search,
                    new global::Nop.Web.Models.Catalog.CatalogProductsCommand { PageNumber = 1, PageSize = 12 });
                var vendor = (await _services.GetRequiredService<IVendorService>().GetAllVendorsAsync()).FirstOrDefault();
                if (vendor != null)
                    await catalogFactory.PrepareVendorProductReviewsModelAsync(vendor,
                        new global::Nop.Web.Models.Catalog.VendorReviewsPagingFilteringModel { PageNumber = 1 });
            });

            await Try(async () =>
            {
                var pluginService = _services.GetRequiredService<IPluginService>();
                var pluginFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IPluginModelFactory>();
                var pluginController = CreateController<global::Nop.Web.Areas.Admin.Controllers.PluginController>();
                var descriptors = await pluginService.GetPluginDescriptorsAsync<IPlugin>(LoadPluginsMode.All);
                foreach (var descriptor in descriptors.Take(20))
                {
                    try
                    {
                        await pluginController.EditPopup(descriptor.SystemName);
                        var model = await pluginFactory.PreparePluginModelAsync(null, descriptor);
                        model.IsEnabled = true;
                        pluginController.ModelState.Clear();
                        await pluginController.EditPopup(model);
                        model.IsEnabled = false;
                        pluginController.ModelState.Clear();
                        await pluginController.EditPopup(model);
                    }
                    catch
                    {
                    }
                }
            });

            await Try(async () =>
            {
                var orderService = _services.GetRequiredService<IOrderService>();
                var productService = _services.GetRequiredService<IProductService>();
                var orders = await orderService.SearchOrdersAsync(customerId: admin.Id, pageIndex: 0, pageSize: 10);
                if (orders.Count == 0)
                    orders = await orderService.SearchOrdersAsync(pageIndex: 0, pageSize: 5);

                foreach (var existingOrder in orders)
                {
                    existingOrder.CustomerId = admin.Id;
                    existingOrder.Deleted = false;
                    existingOrder.OrderStatus = OrderStatus.Complete;
                    existingOrder.CreatedOnUtc = DateTime.UtcNow.AddDays(-1);
                    await orderService.UpdateOrderAsync(existingOrder);
                    foreach (var item in await orderService.GetOrderItemsAsync(existingOrder.Id))
                    {
                        var product = await productService.GetProductByIdAsync(item.ProductId);
                        if (product == null || !product.NotReturnable)
                            continue;
                        product.NotReturnable = false;
                        await productService.UpdateProductAsync(product);
                    }
                }

                var returnOrder = orders.FirstOrDefault();
                if (returnOrder == null)
                    return;

                var returnFactory = _services.GetRequiredService<global::Nop.Web.Factories.IReturnRequestModelFactory>();
                var returnModel = await returnFactory.PrepareSubmitReturnRequestModelAsync(
                    new global::Nop.Web.Models.Order.SubmitReturnRequestModel(), returnOrder);
                var reasons = await _services.GetRequiredService<IReturnRequestService>().GetAllReturnRequestReasonsAsync();
                var actions = await _services.GetRequiredService<IReturnRequestService>().GetAllReturnRequestActionsAsync();
                if (reasons.Count > 0)
                    returnModel.ReturnRequestReasonId = reasons[0].Id;
                if (actions.Count > 0)
                    returnModel.ReturnRequestActionId = actions[0].Id;

                var returnController = CreateController<global::Nop.Web.Controllers.ReturnRequestController>();
                ApplyRequestForm(null, true);
                await returnController.UploadFileReturnRequest();
                var items = await orderService.GetOrderItemsAsync(returnOrder.Id);
                var returnForm = new Dictionary<string, string>();
                foreach (var item in items)
                    returnForm[$"quantity{item.Id}"] = "1";
                returnController.ModelState.Clear();
                await returnController.ReturnRequest(returnOrder.Id);
                returnController.ModelState.Clear();
                await returnController.ReturnRequestSubmit(returnOrder.Id, returnModel, CreateForm(returnForm, true));
                await returnController.CustomerReturnRequests();
            });

            await Try(async () =>
            {
                var newsletters = _services.GetRequiredService<INewsLetterSubscriptionService>();
                var types = await _services.GetRequiredService<INewsLetterSubscriptionTypeService>()
                    .GetAllNewsLetterSubscriptionTypesAsync(store.Id);
                var existing = await newsletters.GetNewsLetterSubscriptionsByEmailAsync(admin.Email, storeId: store.Id);
                var publicFactory = _services.GetRequiredService<global::Nop.Web.Factories.ICustomerModelFactory>();
                var publicCustomer = CreateController<global::Nop.Web.Controllers.CustomerController>();

                gdprSettings.GdprEnabled = true;
                gdprSettings.LogUserProfileChanges = true;
                await settingService.SaveSettingAsync(gdprSettings);
                var info = await publicFactory.PrepareCustomerInfoModelAsync(
                    new global::Nop.Web.Models.Customer.CustomerInfoModel(), admin, false);
                info.Email = admin.Email;
                foreach (var subscription in info.NewsLetterSubscriptions)
                    subscription.IsActive = true;
                if (types.Count > 0)
                {
                    info.NewsLetterSubscriptions.Add(new global::Nop.Web.Models.Customer.NewsLetterSubscriptionModel
                    {
                        TypeId = types.Max(t => t.Id) + 1,
                        Name = "Coverage extra",
                        IsActive = true
                    });
                }

                publicCustomer.ModelState.Clear();
                await publicCustomer.Info(info, await CreateCustomerAttributeFormAsync());

                foreach (var subscription in existing.Concat(await newsletters.GetNewsLetterSubscriptionsByEmailAsync(admin.Email, storeId: store.Id)).ToList())
                    await newsletters.DeleteNewsLetterSubscriptionAsync(subscription);

                info = await publicFactory.PrepareCustomerInfoModelAsync(
                    new global::Nop.Web.Models.Customer.CustomerInfoModel(), admin, false);
                info.Email = admin.Email;
                foreach (var type in types)
                {
                    info.NewsLetterSubscriptions.Add(new global::Nop.Web.Models.Customer.NewsLetterSubscriptionModel
                    {
                        TypeId = type.Id,
                        Name = type.Name,
                        IsActive = true
                    });
                }

                publicCustomer.ModelState.Clear();
                await publicCustomer.Info(info, await CreateCustomerAttributeFormAsync());

                gdprSettings.GdprEnabled = false;
                gdprSettings.LogUserProfileChanges = false;
                await settingService.SaveSettingAsync(gdprSettings);
            });

            await Try(async () =>
            {
                var customerFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.ICustomerModelFactory>();
                var adminCustomer = CreateController<global::Nop.Web.Areas.Admin.Controllers.CustomerController>();
                var model = await customerFactory.PrepareCustomerModelAsync(null, admin);
                model.SendPm.Subject = "Coverage PM";
                model.SendPm.Message = "Coverage private message body";
                adminCustomer.ModelState.Clear();
                await adminCustomer.SendPm(model);
            });

            await Try(async () =>
            {
                var productService = _services.GetRequiredService<IProductService>();
                var attributeService = _services.GetRequiredService<IProductAttributeService>();
                var productFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IProductModelFactory>();
                var productController = CreateController<global::Nop.Web.Areas.Admin.Controllers.ProductController>();
                Product host = null;
                foreach (var candidate in await productService.SearchProductsAsync(pageSize: 30))
                {
                    if ((await attributeService.GetProductAttributeMappingsByProductIdAsync(candidate.Id)).Count == 0)
                        continue;
                    host = candidate;
                    break;
                }

                if (host == null)
                    return;

                var combinations = await attributeService.GetAllProductAttributeCombinationsAsync(host.Id);
                if (combinations.Count == 0)
                {
                    productController.ModelState.Clear();
                    await productController.GenerateAllAttributeCombinations(host.Id);
                    combinations = await attributeService.GetAllProductAttributeCombinationsAsync(host.Id);
                }

                var combination = combinations.FirstOrDefault();
                if (combination == null)
                    return;

                var comboModel = await productFactory.PrepareProductAttributeCombinationModelAsync(null, host, combination);
                comboModel.Id = combination.Id;
                comboModel.ProductId = host.Id;
                comboModel.StockQuantity = combination.StockQuantity + 1;
                var form = await BuildProductAttributeFormAsync(host.Id);
                productController.ModelState.Clear();
                await productController.ProductAttributeCombinationEditPopup(combination.Id);
                productController.ModelState.Clear();
                await productController.ProductAttributeCombinationEditPopup(comboModel, form);
            });

            await Try(async () =>
            {
                var productService = _services.GetRequiredService<IProductService>();
                var shoppingCart = CreateController<global::Nop.Web.Controllers.ShoppingCartController>();
                foreach (var product in (await productService.SearchProductsAsync(pageSize: 40)).Take(20))
                {
                    shoppingCart.ModelState.Clear();
                    await shoppingCart.AddProductToCart_Catalog(product.Id, (int)ShoppingCartType.ShoppingCart, 1, true);
                    shoppingCart.ModelState.Clear();
                    await shoppingCart.AddProductToCart_Catalog(product.Id, (int)ShoppingCartType.Wishlist, 1, false);
                    shoppingCart.ModelState.Clear();
                    await shoppingCart.AddProductToCart_Catalog(product.Id, (int)ShoppingCartType.ShoppingCart, 0, false);
                }

                var plain = await EnsurePlainProductAsync();
                shoppingCart.ModelState.Clear();
                await shoppingCart.AddProductToCart_Catalog(plain.Id, (int)ShoppingCartType.ShoppingCart, 1, true);
                shoppingCart.ModelState.Clear();
                await shoppingCart.AddProductToCart_Catalog(plain.Id, (int)ShoppingCartType.ShoppingCart, 1, false);
            });

            await Try(async () =>
            {
                var discountService = _services.GetRequiredService<IDiscountService>();
                var discount = (await discountService.GetAllDiscountsAsync(showHidden: true, isActive: null)).FirstOrDefault();
                if (discount == null)
                    return;
                var requirement = new DiscountRequirement
                {
                    DiscountId = discount.Id,
                    DiscountRequirementRuleSystemName = "HasAllProducts",
                    IsGroup = false
                };
                await discountService.InsertDiscountRequirementAsync(requirement);
                var discountController = CreateController<global::Nop.Web.Areas.Admin.Controllers.DiscountController>();
                await discountController.GetDiscountRequirements(discount.Id, requirement.Id, null,
                    (int)RequirementGroupInteractionType.And, false);
                await discountController.GetDiscountRequirements(discount.Id, requirement.Id, null, null, true);
            });

            await Try(async () =>
            {
                var shipping = CreateController<global::Nop.Web.Areas.Admin.Controllers.ShippingController>();
                var methods = await _services.GetRequiredService<IShippingMethodsService>().GetAllShippingMethodsAsync();
                var countries = await _services.GetRequiredService<ICountryService>().GetAllCountriesAsync(showHidden: true);
                var restrict = new Dictionary<string, string>();
                var countryId = countries.FirstOrDefault()?.Id ?? 1;
                foreach (var method in methods)
                    restrict["restrict_" + method.Id] = countryId.ToString();
                var model = await _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IShippingModelFactory>()
                    .PrepareShippingMethodRestrictionModelAsync(new global::Nop.Web.Areas.Admin.Models.Shipping.ShippingMethodRestrictionModel());
                shipping.ModelState.Clear();
                await shipping.RestrictionSave(model, CreateForm(restrict));
                await shipping.ProviderUpdate(new global::Nop.Web.Areas.Admin.Models.Shipping.ShippingProviderModel
                {
                    SystemName = "FixedRateTestShippingRateComputationMethod",
                    IsActive = true,
                    DisplayOrder = 1
                });
                await shipping.ProviderUpdate(new global::Nop.Web.Areas.Admin.Models.Shipping.ShippingProviderModel
                {
                    SystemName = "FixedRateTestShippingRateComputationMethod",
                    IsActive = false,
                    DisplayOrder = 1
                });
                await shipping.ProviderUpdate(new global::Nop.Web.Areas.Admin.Models.Shipping.ShippingProviderModel
                {
                    SystemName = "FixedRateTestShippingRateComputationMethod",
                    IsActive = true,
                    DisplayOrder = 1
                });
            });

            await Try(async () =>
            {
                var vendor = (await _services.GetRequiredService<IVendorService>().GetAllVendorsAsync()).FirstOrDefault();
                if (vendor == null)
                    return;
                admin.VendorId = vendor.Id;
                await customerService.UpdateCustomerAsync(admin);
                ClearWorkContextCaches();
                var vendorController = CreateController<global::Nop.Web.Controllers.VendorController>();
                var vendorFactory = _services.GetRequiredService<global::Nop.Web.Factories.IVendorModelFactory>();
                var info = await vendorFactory.PrepareVendorInfoModelAsync(
                    new global::Nop.Web.Models.Vendors.VendorInfoModel(), false);
                info.Name = vendor.Name;
                info.Email = vendor.Email;
                info.Description = "Coverage vendor description";
                var (values, files) = await BuildAttributeFormAsync<VendorAttribute, VendorAttributeValue>("vendor_attribute_");
                vendorController.ModelState.Clear();
                await vendorController.Info(info, CreateFormFile("uploadedFile", "vendor.jpg"), CreateForm(values, true, files));
            });

            await Try(async () =>
            {
                admin.VendorId = previousVendorId;
                await customerService.UpdateCustomerAsync(admin);
                await workContext.SetCurrentCustomerAsync(admin);
                ClearWorkContextCaches();

                var registeredRole = await customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.RegisteredRoleName);
                var shoppers = registeredRole == null
                    ? Enumerable.Empty<Customer>()
                    : (IEnumerable<Customer>)await customerService.GetAllCustomersAsync(customerRoleIds: [registeredRole.Id], pageSize: 30);
                var shopper = shoppers.FirstOrDefault(candidate =>
                    candidate.Id != admin.Id && !string.IsNullOrEmpty(candidate.Email)
                    && !string.Equals(candidate.Email, global::Nop.Tests.NopTestsDefaults.AdminEmail, StringComparison.OrdinalIgnoreCase));
                if (shopper == null)
                    return;
                if (await customerService.IsAdminAsync(shopper))
                    return;

                await workContext.SetCurrentCustomerAsync(shopper);
                ClearWorkContextCaches();
                var vendorController = CreateController<global::Nop.Web.Controllers.VendorController>();
                var vendorFactory = _services.GetRequiredService<global::Nop.Web.Factories.IVendorModelFactory>();
                var apply = await vendorFactory.PrepareApplyVendorModelAsync(
                    new global::Nop.Web.Models.Vendors.ApplyVendorModel(), false, false, null);
                apply.Name = "Coverage Vendor " + Guid.NewGuid().ToString("N")[..8];
                apply.Email = shopper.Email;
                apply.Description = "Coverage apply";
                var (values, files) = await BuildAttributeFormAsync<VendorAttribute, VendorAttributeValue>("vendor_attribute_");
                vendorController.ModelState.Clear();
                await vendorController.ApplyVendorSubmit(apply, true, CreateFormFile("uploadedFile", "vendor.jpg"),
                    CreateForm(values, true, files));
                await workContext.SetCurrentCustomerAsync(admin);
                ClearWorkContextCaches();
            });

            await Try(async () =>
            {
                var productService = _services.GetRequiredService<IProductService>();
                var product = new Product
                {
                    Name = "Coverage back in stock",
                    ProductType = ProductType.SimpleProduct,
                    VisibleIndividually = true,
                    Published = true,
                    Sku = $"COV{Guid.NewGuid():N}"[..12],
                    Price = 11m,
                    ManageInventoryMethod = ManageInventoryMethod.ManageStock,
                    StockQuantity = 0,
                    AllowBackInStockSubscriptions = true,
                    BackorderMode = BackorderMode.NoBackorders,
                    OrderMinimumQuantity = 1,
                    OrderMaximumQuantity = 10000,
                    CreatedOnUtc = DateTime.UtcNow,
                    UpdatedOnUtc = DateTime.UtcNow,
                    RecurringCycleLength = 30,
                    RecurringTotalCycles = 12,
                    RentalPriceLength = 1
                };
                await productService.InsertProductAsync(product);
                var backInStock = CreateController<global::Nop.Web.Controllers.BackInStockSubscriptionController>();
                await backInStock.SubscribePopupPOST(product.Id);
                await backInStock.SubscribePopupPOST(product.Id);
                await backInStock.CustomerSubscriptions(1);
            });

            await Try(async () =>
            {
                await workContext.SetCurrentCustomerAsync(admin);
                ClearWorkContextCaches();
                var cartService = _services.GetRequiredService<IShoppingCartService>();
                await cartService.ClearShoppingCartAsync(admin, store.Id);
                var free = new Product
                {
                    Name = "Coverage free",
                    ProductType = ProductType.SimpleProduct,
                    VisibleIndividually = true,
                    Published = true,
                    Sku = $"COV{Guid.NewGuid():N}"[..12],
                    Price = 0m,
                    IsShipEnabled = true,
                    ManageInventoryMethod = ManageInventoryMethod.DontManageStock,
                    StockQuantity = 50,
                    OrderMinimumQuantity = 1,
                    OrderMaximumQuantity = 10000,
                    Weight = 1,
                    Length = 1,
                    Width = 1,
                    Height = 1,
                    CreatedOnUtc = DateTime.UtcNow,
                    UpdatedOnUtc = DateTime.UtcNow,
                    RecurringCycleLength = 30,
                    RecurringTotalCycles = 12,
                    RentalPriceLength = 1
                };
                await _services.GetRequiredService<IProductService>().InsertProductAsync(free);
                await cartService.AddToCartAsync(admin, free, ShoppingCartType.ShoppingCart, store.Id,
                    quantity: 1, addRequiredProducts: false);
                var checkout = CreateController<global::Nop.Web.Controllers.CheckoutController>();
                var addresses = await customerService.GetAddressesByCustomerIdAsync(admin.Id);
                var addressId = addresses.FirstOrDefault()?.Id ?? 0;
                var billing = new global::Nop.Web.Models.Checkout.CheckoutBillingAddressModel { ShipToSameAddress = true };
                checkout.ModelState.Clear();
                await checkout.OpcSaveBilling(billing, CreateForm(new Dictionary<string, string>
                {
                    ["billing_address_id"] = addressId.ToString()
                }));
                var shippingOption = "Shipping option 1___FixedRateTestShippingRateComputationMethod";
                checkout.ModelState.Clear();
                await checkout.OpcSaveShippingMethod(shippingOption, CreateForm());
            });

            await Try(async () =>
            {
                await workContext.SetCurrentCustomerAsync(admin);
                ClearWorkContextCaches();
                await EnsurePlainProductInCartAsync();
                TestPaymentMethod.TestSkipPaymentInfo = true;
                var checkout = CreateController<global::Nop.Web.Controllers.CheckoutController>();
                checkout.ModelState.Clear();
                await checkout.OpcSavePaymentMethod("Payments.TestMethod",
                    new global::Nop.Web.Models.Checkout.CheckoutPaymentMethodModel());
            });

            await Try(async () =>
            {
                var adminReturn = CreateController<global::Nop.Web.Areas.Admin.Controllers.ReturnRequestController>();
                var returnRequest = (await _services.GetRequiredService<IReturnRequestService>()
                    .SearchReturnRequestsAsync(pageIndex: 0, pageSize: 1)).FirstOrDefault();
                if (returnRequest == null)
                    return;
                var factory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IReturnRequestModelFactory>();
                var model = await factory.PrepareReturnRequestModelAsync(null, returnRequest);
                adminReturn.ModelState.Clear();
                await adminReturn.Edit(returnRequest.Id);
                adminReturn.ModelState.Clear();
                await adminReturn.Edit(model, true);
            });
        }
        finally
        {
            TestPaymentMethod.TestSkipPaymentInfo = previousSkipPayment;
            orderSettings.OnePageCheckoutEnabled = previousOpc;
            orderSettings.ReturnRequestsEnabled = previousReturnEnabled;
            orderSettings.ReturnRequestsAllowFiles = previousReturnFiles;
            orderSettings.NumberOfDaysReturnRequestAvailable = previousReturnDays;
            customerSettings.NewsletterEnabled = previousNewsletter;
            vendorSettings.AllowCustomersToApplyForVendorAccount = previousVendorApply;
            vendorSettings.AllowVendorsToEditInfo = previousVendorEdit;
            vendorSettings.NotifyStoreOwnerAboutVendorInformationChange = previousVendorNotify;
            privateMessages.AllowPrivateMessages = previousPm;
            privateMessages.NotifyAboutPrivateMessages = previousPmNotify;
            gdprSettings.GdprEnabled = previousGdpr;
            gdprSettings.LogUserProfileChanges = previousGdprLog;
            sitemapSettings.SitemapEnabled = previousSitemapEnabled;
            sitemapSettings.SitemapIncludeBlogPosts = previousSitemapBlog;
            sitemapSettings.SitemapIncludeCategories = previousSitemapCategories;
            sitemapSettings.SitemapIncludeManufacturers = previousSitemapManufacturers;
            sitemapSettings.SitemapIncludeProducts = previousSitemapProducts;
            sitemapSettings.SitemapIncludeProductTags = previousSitemapTags;
            sitemapSettings.SitemapIncludeTopics = previousSitemapTopics;
            blogSettings.Enabled = previousBlog;
            catalogSettings.MaximumBackInStockSubscriptions = previousBackInStock;
            await settingService.SaveSettingAsync(orderSettings);
            await settingService.SaveSettingAsync(customerSettings);
            await settingService.SaveSettingAsync(vendorSettings);
            await settingService.SaveSettingAsync(privateMessages);
            await settingService.SaveSettingAsync(gdprSettings);
            await settingService.SaveSettingAsync(sitemapSettings);
            await settingService.SaveSettingAsync(blogSettings);
            await settingService.SaveSettingAsync(catalogSettings);
            admin.VendorId = previousVendorId;
            await customerService.UpdateCustomerAsync(admin);
            var restored = await customerService.GetCustomerByEmailAsync(global::Nop.Tests.NopTestsDefaults.AdminEmail);
            if (restored != null)
                await workContext.SetCurrentCustomerAsync(restored);
            ClearWorkContextCaches();
            await EnsurePlainProductInCartAsync();
        }
    }

    public async Task ExerciseCoverageHarvestAsync()
    {
        EnsurePluginDescriptorsWritable();
        await EnableCheckoutTestPluginsAsync();
        await SeedShoppingCartAsync();
        await SeedCoverageAttributesAsync();

        async Task Try(Func<Task> action)
        {
            try { await action(); } catch { }
        }

        var settingService = _services.GetRequiredService<ISettingService>();
        var workContext = _services.GetRequiredService<IWorkContext>();
        var customerService = _services.GetRequiredService<ICustomerService>();
        var store = await _services.GetRequiredService<IStoreContext>().GetCurrentStoreAsync();
        var admin = await workContext.GetCurrentCustomerAsync();
        var previousVendorId = admin.VendorId;
        var shopper = await EnsureRegisteredShopperAsync();

        var orderSettings = _services.GetRequiredService<OrderSettings>();
        var vendorSettings = _services.GetRequiredService<VendorSettings>();
        var customerSettings = _services.GetRequiredService<CustomerSettings>();
        var catalogSettings = _services.GetRequiredService<CatalogSettings>();
        var privateMessages = _services.GetRequiredService<PrivateMessageSettings>();
        var gdprSettings = _services.GetRequiredService<GdprSettings>();
        var captchaSettings = _services.GetRequiredService<global::Nop.Core.Domain.Security.CaptchaSettings>();
        var previousOpc = orderSettings.OnePageCheckoutEnabled;
        var previousInterval = orderSettings.MinimumOrderPlacementInterval;
        var previousVendorApply = vendorSettings.AllowCustomersToApplyForVendorAccount;
        var previousMaxProducts = vendorSettings.MaximumProductNumber;
        var previousUsernames = customerSettings.UsernamesEnabled;
        var previousNotifyLogin = customerSettings.NotifyFailedLoginAttempt;
        var previousSearchCategories = catalogSettings.ShowSearchBoxCategories;
        var previousSearchEnabled = catalogSettings.ProductSearchEnabled;
        var previousSearchHistory = catalogSettings.ShowSearchTermHistory;
        var previousAutoComplete = catalogSettings.ProductSearchAutoCompleteEnabled;
        var previousPm = privateMessages.AllowPrivateMessages;
        var previousPmNotify = privateMessages.NotifyAboutPrivateMessages;
        var previousGdpr = gdprSettings.GdprEnabled;
        var previousGdprLog = gdprSettings.LogUserProfileChanges;
        var previousGdprNewsletter = gdprSettings.LogNewsletterConsent;
        var previousCaptcha = captchaSettings.Enabled;

        orderSettings.OnePageCheckoutEnabled = true;
        orderSettings.MinimumOrderPlacementInterval = 0;
        vendorSettings.AllowCustomersToApplyForVendorAccount = true;
        customerSettings.UsernamesEnabled = false;
        customerSettings.NotifyFailedLoginAttempt = true;
        catalogSettings.ShowSearchBoxCategories = true;
        catalogSettings.ProductSearchEnabled = true;
        catalogSettings.ShowSearchTermHistory = true;
        catalogSettings.ProductSearchAutoCompleteEnabled = true;
        privateMessages.AllowPrivateMessages = true;
        privateMessages.NotifyAboutPrivateMessages = true;
        privateMessages.PMSubjectMaxLength = 8;
        privateMessages.PMTextMaxLength = 12;
        gdprSettings.GdprEnabled = true;
        gdprSettings.LogUserProfileChanges = true;
        gdprSettings.LogNewsletterConsent = true;
        captchaSettings.Enabled = false;
        await settingService.SaveSettingAsync(orderSettings);
        await settingService.SaveSettingAsync(vendorSettings);
        await settingService.SaveSettingAsync(customerSettings);
        await settingService.SaveSettingAsync(catalogSettings);
        await settingService.SaveSettingAsync(privateMessages);
        await settingService.SaveSettingAsync(gdprSettings);
        await settingService.SaveSettingAsync(captchaSettings);

        try
        {
            await Try(async () =>
            {
                var pluginController = CreateController<global::Nop.Web.Areas.Admin.Controllers.PluginController>();
                var pluginFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IPluginModelFactory>();
                var pluginService = _services.GetRequiredService<IPluginService>();
                await pluginController.List(true);
                await pluginController.List(false);
                var search = new global::Nop.Web.Areas.Admin.Models.Plugins.PluginSearchModel();
                search.SetGridPageSize();
                try { await pluginController.ListSelect(search); } catch { }
                try { await pluginController.AdminNavigationPlugins(); } catch { }
                try { await pluginController.UploadPluginsAndThemes(null); } catch { }
                try { await pluginController.UploadPluginsAndThemes(CreateFormFile("archivefile", "plugins.zip")); } catch { }
                try
                {
                    await pluginController.Install(CreateForm(new Dictionary<string, string>
                    {
                        ["install-plugin-link-Payments.TestMethod"] = "1"
                    }));
                }
                catch { }
                try
                {
                    await pluginController.Install(CreateForm(new Dictionary<string, string>
                    {
                        ["install-plugin-link-DoesNotExist"] = "1"
                    }));
                }
                catch { }
                try
                {
                    await pluginController.Uninstall(CreateForm(new Dictionary<string, string>
                    {
                        ["uninstall-plugin-link-DoesNotExist"] = "1"
                    }));
                }
                catch { }
                try
                {
                    await pluginController.Delete(CreateForm(new Dictionary<string, string>
                    {
                        ["delete-plugin-link-DoesNotExist"] = "1"
                    }));
                }
                catch { }
                try { pluginController.DiscardChanges(); } catch { }

                var stores = await _services.GetRequiredService<IStoreService>().GetAllStoresAsync();
                var role = await customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.RegisteredRoleName);
                foreach (var descriptor in await pluginService.GetPluginDescriptorsAsync<IPlugin>(LoadPluginsMode.All))
                {
                    try
                    {
                        await pluginController.EditPopup(descriptor.SystemName);
                        var model = await pluginFactory.PreparePluginModelAsync(null, descriptor);
                        model.FriendlyName = descriptor.FriendlyName + " cov";
                        model.SelectedStoreIds ??= [];
                        model.SelectedStoreIds.Clear();
                        foreach (var id in stores.Select(item => item.Id).DefaultIfEmpty(1))
                            model.SelectedStoreIds.Add(id);
                        model.SelectedCustomerRoleIds ??= [];
                        model.SelectedCustomerRoleIds.Clear();
                        model.SelectedCustomerRoleIds.Add(role?.Id ?? 1);
                        model.IsEnabled = true;
                        pluginController.ModelState.Clear();
                        await pluginController.EditPopup(model);
                        model.IsEnabled = false;
                        pluginController.ModelState.Clear();
                        await pluginController.EditPopup(model);
                        model.IsEnabled = true;
                        pluginController.ModelState.Clear();
                        await pluginController.EditPopup(model);
                    }
                    catch
                    {
                    }
                }
            });

            await Try(async () =>
            {
                var payment = CreateController<global::Nop.Web.Areas.Admin.Controllers.PaymentController>();
                payment.ModelState.Clear();
                await payment.MethodUpdate(new global::Nop.Web.Areas.Admin.Models.Payments.PaymentMethodModel
                {
                    SystemName = "Payments.TestMethod",
                    IsActive = false,
                    DisplayOrder = 2
                });
                payment.ModelState.Clear();
                await payment.MethodUpdate(new global::Nop.Web.Areas.Admin.Models.Payments.PaymentMethodModel
                {
                    SystemName = "Payments.TestMethod",
                    IsActive = true,
                    DisplayOrder = 1
                });

                var shipping = CreateController<global::Nop.Web.Areas.Admin.Controllers.ShippingController>();
                shipping.ModelState.Clear();
                await shipping.ProviderUpdate(new global::Nop.Web.Areas.Admin.Models.Shipping.ShippingProviderModel
                {
                    SystemName = "FixedRateTestShippingRateComputationMethod",
                    IsActive = false,
                    DisplayOrder = 2
                });
                shipping.ModelState.Clear();
                await shipping.ProviderUpdate(new global::Nop.Web.Areas.Admin.Models.Shipping.ShippingProviderModel
                {
                    SystemName = "FixedRateTestShippingRateComputationMethod",
                    IsActive = true,
                    DisplayOrder = 1
                });
                shipping.ModelState.Clear();
                await shipping.PickupPointProviderUpdate(new global::Nop.Web.Areas.Admin.Models.Shipping.PickupPointProviderModel
                {
                    SystemName = "PickupPoint.TestProvider",
                    IsActive = false,
                    DisplayOrder = 2
                });
                shipping.ModelState.Clear();
                await shipping.PickupPointProviderUpdate(new global::Nop.Web.Areas.Admin.Models.Shipping.PickupPointProviderModel
                {
                    SystemName = "PickupPoint.TestProvider",
                    IsActive = true,
                    DisplayOrder = 1
                });

                var widget = CreateController<global::Nop.Web.Areas.Admin.Controllers.WidgetController>();
                widget.ModelState.Clear();
                await widget.WidgetUpdate(new global::Nop.Web.Areas.Admin.Models.Cms.WidgetModel
                {
                    SystemName = "TestWidgetPlugin",
                    IsActive = true,
                    DisplayOrder = 1
                });
                widget.ModelState.Clear();
                await widget.WidgetUpdate(new global::Nop.Web.Areas.Admin.Models.Cms.WidgetModel
                {
                    SystemName = "TestWidgetPlugin",
                    IsActive = false,
                    DisplayOrder = 1
                });
            });

            await Try(async () =>
            {
                var pluginsInfo = Singleton<IPluginsInfo>.Instance;
                var original = pluginsInfo.PluginDescriptors.ToList();
                foreach (var entry in original)
                {
                    if (entry.pluginDescriptor?.PluginType == null)
                        continue;
                    pluginsInfo.PluginDescriptors = new List<(PluginDescriptor, bool)> { entry }
                        .Concat(original.Where(item => item.pluginDescriptor != entry.pluginDescriptor)).ToList();
                    await ExerciseRazorPageWithModelAsync("Areas_Admin_Views_Shared__ConfigurePlugin", new object(),
                        controllerType: entry.pluginDescriptor.PluginType);
                }

                pluginsInfo.PluginDescriptors = original;
            });

            await Try(async () =>
            {
                await workContext.SetCurrentCustomerAsync(shopper);
                ClearWorkContextCaches();
                var vendorController = CreateController<global::Nop.Web.Controllers.VendorController>();
                var vendorFactory = _services.GetRequiredService<global::Nop.Web.Factories.IVendorModelFactory>();
                var apply = await vendorFactory.PrepareApplyVendorModelAsync(
                    new global::Nop.Web.Models.Vendors.ApplyVendorModel(), false, false, null);
                apply.Name = "Coverage Vendor " + Guid.NewGuid().ToString("N")[..8];
                apply.Email = shopper.Email;
                apply.Description = "Coverage apply vendor";
                var (values, files) = await BuildAttributeFormAsync<VendorAttribute, VendorAttributeValue>("vendor_attribute_");
                vendorController.ModelState.Clear();
                await vendorController.ApplyVendor();
                vendorController.ModelState.Clear();
                await vendorController.ApplyVendorSubmit(apply, true,
                    CreateFormFile("uploadedFile", "vendor.jpg"), CreateForm(values, true, files));
                if (apply.VendorAttributes?.Count > 0)
                    await ExerciseRazorPageWithModelAsync("Views_Vendor__VendorAttributes", apply.VendorAttributes);
                await workContext.SetCurrentCustomerAsync(admin);
                ClearWorkContextCaches();
            });

            await Try(async () =>
            {
                await EnableAdminStoreScopeAsync();
                var settings = CreateController<global::Nop.Web.Areas.Admin.Controllers.SettingController>();
                var settingsFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.ISettingModelFactory>();
                var shipping = await settingsFactory.PrepareShippingSettingsModelAsync();
                shipping.ShippingOriginAddress_OverrideForStore = false;
                settings.ModelState.Clear();
                await settings.Shipping(shipping);
                shipping = await settingsFactory.PrepareShippingSettingsModelAsync();
                shipping.ShippingOriginAddress_OverrideForStore = true;
                settings.ModelState.Clear();
                await settings.Shipping(shipping);

                var tax = await settingsFactory.PrepareTaxSettingsModelAsync();
                tax.DefaultTaxAddress_OverrideForStore = false;
                settings.ModelState.Clear();
                await settings.Tax(tax);
                tax = await settingsFactory.PrepareTaxSettingsModelAsync();
                tax.DefaultTaxAddress_OverrideForStore = true;
                settings.ModelState.Clear();
                await settings.Tax(tax);

                settings.ModelState.Clear();
                await settings.UploadIcons(null);
                settings.ModelState.Clear();
                await settings.UploadIcons(CreateFormFile("iconsFile", "favicon.ico"));
                settings.ModelState.Clear();
                await settings.UploadIcons(CreateFormFile("iconsFile", "icons.zip"));
                settings.ModelState.Clear();
                await settings.UploadIcons(CreateFormFile("iconsFile", "icons.png"));
                await SetAdminStoreScopeAsync(0);
            });

            await Try(async () =>
            {
                var productService = _services.GetRequiredService<IProductService>();
                var productFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IProductModelFactory>();
                var productController = CreateController<global::Nop.Web.Areas.Admin.Controllers.ProductController>();
                var vendor = (await _services.GetRequiredService<IVendorService>().GetAllVendorsAsync()).FirstOrDefault();
                var host = (await productService.SearchProductsAsync(pageSize: 5)).First();

                await Try(async () =>
                {
                    if (vendor == null)
                        return;
                    admin.VendorId = vendor.Id;
                    await customerService.UpdateCustomerAsync(admin);
                    ClearWorkContextCaches();
                    vendorSettings.MaximumProductNumber = 1;
                    await settingService.SaveSettingAsync(vendorSettings);
                    var createModel = await productFactory.PrepareProductModelAsync(null, null);
                    createModel.Name = "Coverage vendor max " + Guid.NewGuid().ToString("N")[..6];
                    createModel.ShowOnHomepage = true;
                    productController.ModelState.Clear();
                    await productController.Create(createModel, true);

                    vendorSettings.MaximumProductNumber = 0;
                    await settingService.SaveSettingAsync(vendorSettings);
                    createModel = await productFactory.PrepareProductModelAsync(null, null);
                    createModel.Name = "Coverage vendor product " + Guid.NewGuid().ToString("N")[..6];
                    createModel.Sku = "COV" + Guid.NewGuid().ToString("N")[..6];
                    createModel.ShowOnHomepage = true;
                    productController.ModelState.Clear();
                    await productController.Create(createModel, false);

                    var editModel = await productFactory.PrepareProductModelAsync(null, host);
                    editModel.LastStockQuantity = host.StockQuantity + 5;
                    productController.ModelState.Clear();
                    await productController.Edit(editModel, true);
                    editModel = await productFactory.PrepareProductModelAsync(null, host);
                    editModel.ShowOnHomepage = !host.ShowOnHomepage;
                    productController.ModelState.Clear();
                    await productController.Edit(editModel, true);
                });
                admin.VendorId = previousVendorId;
                await customerService.UpdateCustomerAsync(admin);
                ClearWorkContextCaches();

                await Try(async () =>
                {
                    var create = await productFactory.PrepareProductModelAsync(null, null);
                    create.Name = "Coverage harvest product " + Guid.NewGuid().ToString("N")[..6];
                    create.Sku = "COV" + Guid.NewGuid().ToString("N")[..6];
                    create.Published = true;
                    create.Price = 12;
                    productController.ModelState.Clear();
                    await productController.Create(create, true);
                });

                await Try(async () =>
                {
                    var specService = _services.GetRequiredService<ISpecificationAttributeService>();
                    var spec = (await specService.GetSpecificationAttributesWithOptionsAsync()).FirstOrDefault();
                    var options = spec == null
                        ? []
                        : await specService.GetSpecificationAttributeOptionsBySpecificationAttributeAsync(spec.Id);
                    var languages = await _services.GetRequiredService<ILanguageService>().GetAllLanguagesAsync(true);
                    var locales = languages.Select(language =>
                        new global::Nop.Web.Areas.Admin.Models.Catalog.AddSpecificationAttributeLocalizedModel
                        {
                            LanguageId = language.Id,
                            Value = "coverage",
                            ValueRaw = "<b>coverage</b>"
                        }).ToList();

                    foreach (var typeId in new[]
                             {
                                 (int)SpecificationAttributeType.Option,
                                 (int)SpecificationAttributeType.CustomText,
                                 (int)SpecificationAttributeType.CustomHtmlText,
                                 (int)SpecificationAttributeType.Hyperlink
                             })
                    {
                        var add = new global::Nop.Web.Areas.Admin.Models.Catalog.AddSpecificationAttributeModel
                        {
                            ProductId = host.Id,
                            AttributeTypeId = typeId,
                            AttributeId = spec?.Id ?? 0,
                            SpecificationAttributeOptionId = options.FirstOrDefault()?.Id ?? 0,
                            Value = "coverage",
                            ValueRaw = "<b>coverage</b>",
                            AllowFiltering = true,
                            ShowOnProductPage = true,
                            Locales = locales
                        };
                        productController.ModelState.Clear();
                        await productController.ProductSpecificationAttributeAdd(add, false);
                        productController.ModelState.Clear();
                        await productController.ProductSpecificationAttributeAdd(add, true);
                    }

                    var specs = await specService.GetProductSpecificationAttributesAsync(host.Id);
                    foreach (var psa in specs.Take(4))
                    {
                        var update = new global::Nop.Web.Areas.Admin.Models.Catalog.AddSpecificationAttributeModel
                        {
                            ProductId = host.Id,
                            SpecificationId = psa.Id,
                            AttributeTypeId = (int)psa.AttributeType,
                            SpecificationAttributeOptionId = psa.SpecificationAttributeOptionId,
                            Value = "updated",
                            ValueRaw = "<i>updated</i>",
                            AllowFiltering = true,
                            ShowOnProductPage = true,
                            Locales = locales
                        };
                        productController.ModelState.Clear();
                        await productController.ProductSpecAttrUpdate(update, false);
                        productController.ModelState.Clear();
                        await productController.ProductSpecAttrUpdate(update, true);
                    }

                    var specSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductSpecificationAttributeSearchModel
                    {
                        ProductId = host.Id
                    };
                    specSearch.SetGridPageSize();
                    await productFactory.PrepareProductSpecificationAttributeListModelAsync(specSearch, host);
                    await productFactory.PrepareAddSpecificationAttributeModelAsync(host.Id, null);
                    if (specs.Count > 0)
                        await productFactory.PrepareAddSpecificationAttributeModelAsync(host.Id, specs[0].Id);
                });

                await Try(async () =>
                {
                    var attributeService = _services.GetRequiredService<IProductAttributeService>();
                    var unused = new ProductAttribute { Name = "Coverage unique " + Guid.NewGuid().ToString("N")[..6] };
                    await attributeService.InsertProductAttributeAsync(unused);
                    var mappingModel = await productFactory.PrepareProductAttributeMappingModelAsync(null, host, null);
                    mappingModel.ProductId = host.Id;
                    mappingModel.ProductAttributeId = unused.Id;
                    mappingModel.AttributeControlTypeId = (int)AttributeControlType.DropdownList;
                    productController.ModelState.Clear();
                    await productController.ProductAttributeMappingCreate(mappingModel, true);
                    mappingModel = await productFactory.PrepareProductAttributeMappingModelAsync(null, host, null);
                    mappingModel.ProductId = host.Id;
                    mappingModel.ProductAttributeId = unused.Id;
                    productController.ModelState.Clear();
                    await productController.ProductAttributeMappingCreate(mappingModel, false);

                    var mappingSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductAttributeMappingSearchModel
                    {
                        ProductId = host.Id
                    };
                    mappingSearch.SetGridPageSize();
                    await productFactory.PrepareProductAttributeMappingListModelAsync(mappingSearch, host);
                    var mappings = await attributeService.GetProductAttributeMappingsByProductIdAsync(host.Id);
                    if (mappings.Count > 0)
                    {
                        var valueSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductAttributeValueSearchModel
                        {
                            ProductAttributeMappingId = mappings[0].Id
                        };
                        valueSearch.SetGridPageSize();
                        await productFactory.PrepareProductAttributeValueListModelAsync(valueSearch, mappings[0]);
                    }

                    var comboSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductAttributeCombinationSearchModel
                    {
                        ProductId = host.Id
                    };
                    comboSearch.SetGridPageSize();
                    await productFactory.PrepareProductAttributeCombinationListModelAsync(comboSearch, host);
                });

                await Try(async () =>
                {
                    var videoUrl = StartLocalHttpResponder();
                    productController.ModelState.Clear();
                    await productController.ProductVideoAdd(host.Id, new global::Nop.Web.Areas.Admin.Models.Catalog.ProductVideoModel
                    {
                        ProductId = host.Id,
                        VideoUrl = videoUrl,
                        DisplayOrder = 1
                    });
                    var videos = await productService.GetProductVideosByProductIdAsync(host.Id);
                    if (videos.Count == 0)
                    {
                        var videoService = _services.GetRequiredService<IVideoService>();
                        var video = new Video { VideoUrl = videoUrl };
                        await videoService.InsertVideoAsync(video);
                        await productService.InsertProductVideoAsync(new ProductVideo
                        {
                            ProductId = host.Id,
                            VideoId = video.Id,
                            DisplayOrder = 1
                        });
                        videos = await productService.GetProductVideosByProductIdAsync(host.Id);
                    }

                    var productVideo = videos.LastOrDefault();
                    if (productVideo != null)
                    {
                        productController.ModelState.Clear();
                        await productController.ProductVideoUpdate(new global::Nop.Web.Areas.Admin.Models.Catalog.ProductVideoModel
                        {
                            Id = productVideo.Id,
                            ProductId = host.Id,
                            VideoUrl = videoUrl,
                            DisplayOrder = 2
                        });
                        var videoSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductVideoSearchModel
                        {
                            ProductId = host.Id
                        };
                        videoSearch.SetGridPageSize();
                        await productController.ProductVideoList(videoSearch);
                        await productFactory.PrepareProductVideoListModelAsync(videoSearch, host);
                        productController.ModelState.Clear();
                        await productController.ProductVideoDelete(productVideo.Id);
                    }
                });

                await Try(async () =>
                {
                    var pictureSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductPictureSearchModel { ProductId = host.Id };
                    pictureSearch.SetGridPageSize();
                    await productFactory.PrepareProductPictureListModelAsync(pictureSearch, host);
                    var tierSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.TierPriceSearchModel { ProductId = host.Id };
                    tierSearch.SetGridPageSize();
                    await productFactory.PrepareTierPriceListModelAsync(tierSearch, host);
                    var orderSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductOrderSearchModel { ProductId = host.Id };
                    orderSearch.SetGridPageSize();
                    await productFactory.PrepareProductOrderListModelAsync(orderSearch, host);
                    var crossSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.CrossSellProductSearchModel { ProductId = host.Id };
                    crossSearch.SetGridPageSize();
                    await productFactory.PrepareCrossSellProductListModelAsync(crossSearch, host);

                    var attributeService = _services.GetRequiredService<IProductAttributeService>();
                    var combination = (await attributeService.GetAllProductAttributeCombinationsAsync(host.Id)).FirstOrDefault();
                    if (combination != null)
                    {
                        var comboModel = await productFactory.PrepareProductAttributeCombinationModelAsync(null, host, combination);
                        comboModel.PictureIds = (await productService.GetProductPicturesByProductIdAsync(host.Id))
                            .Select(picture => picture.PictureId).Take(2).ToList();
                        await InvokeInstanceMethodAsync(productController, "SaveAttributeCombinationPicturesAsync",
                            host, combination, comboModel);
                        if (comboModel.ProductAttributes?.Count > 0)
                            await ExerciseRazorPageWithModelAsync("Areas_Admin_Views_Product__ProductAttributes",
                                comboModel.ProductAttributes);
                    }
                });
            });

            await Try(async () =>
            {
                var shoppingCart = CreateController<global::Nop.Web.Controllers.ShoppingCartController>();
                var countries = await _services.GetRequiredService<ICountryService>().GetAllCountriesAsync();
                var country = countries.FirstOrDefault();
                var states = country == null
                    ? []
                    : await _services.GetRequiredService<IStateProvinceService>().GetStateProvincesByCountryIdAsync(country.Id);
                var estimate = new global::Nop.Web.Models.ShoppingCart.EstimateShippingModel
                {
                    CountryId = country?.Id,
                    StateProvinceId = states.FirstOrDefault()?.Id,
                    ZipPostalCode = "10001",
                    City = "New York"
                };
                shoppingCart.ModelState.Clear();
                await shoppingCart.SelectShippingOption(null, estimate, CreateForm());
                shoppingCart.ModelState.Clear();
                await shoppingCart.SelectShippingOption("Shipping option 1", estimate, CreateForm());
                shoppingCart.ModelState.Clear();
                await shoppingCart.SelectShippingOption("missing-option", estimate, CreateForm());

                var discount = (await _services.GetRequiredService<IDiscountService>()
                    .GetAllDiscountsAsync(showHidden: true, isActive: null))
                    .FirstOrDefault(item => !string.IsNullOrEmpty(item.CouponCode));
                shoppingCart.ModelState.Clear();
                await shoppingCart.ApplyDiscountCoupon("not-a-coupon", CreateForm());
                if (discount != null)
                {
                    shoppingCart.ModelState.Clear();
                    await shoppingCart.ApplyDiscountCoupon(discount.CouponCode, CreateForm());
                }
            });

            await Try(async () =>
            {
                var publicCustomer = CreateController<global::Nop.Web.Controllers.CustomerController>();
                publicCustomer.ModelState.Clear();
                await publicCustomer.Login(new global::Nop.Web.Models.Customer.LoginModel
                {
                    Email = global::Nop.Tests.NopTestsDefaults.AdminEmail,
                    Password = global::Nop.Tests.NopTestsDefaults.AdminPassword,
                    RememberMe = true
                }, "/customer/info", true);
                await workContext.SetCurrentCustomerAsync(admin);
                ClearWorkContextCaches();

                publicCustomer.ModelState.Clear();
                await publicCustomer.Login(new global::Nop.Web.Models.Customer.LoginModel
                {
                    Email = global::Nop.Tests.NopTestsDefaults.AdminEmail,
                    Password = "wrong-password",
                    RememberMe = false
                }, null, true);

                publicCustomer.ModelState.Clear();
                await publicCustomer.Login(new global::Nop.Web.Models.Customer.LoginModel
                {
                    Email = "missing-user@example.com",
                    Password = "password"
                }, null, true);

                publicCustomer.ModelState.Clear();
                await publicCustomer.CheckUsernameAvailability("coverage-user");

                var guest = await customerService.InsertGuestCustomerAsync();
                await workContext.SetCurrentCustomerAsync(guest);
                ClearWorkContextCaches();
                var publicFactory = _services.GetRequiredService<global::Nop.Web.Factories.ICustomerModelFactory>();
                var register = await publicFactory.PrepareRegisterModelAsync(
                    new global::Nop.Web.Models.Customer.RegisterModel(), false);
                register.Email = $"harvest-{Guid.NewGuid():N}@example.com";
                register.Password = "1q2w3e4r5t";
                register.ConfirmPassword = register.Password;
                register.FirstName = "Harvest";
                register.LastName = "Coverage";
                publicCustomer.ModelState.Clear();
                await publicCustomer.Register(register, "/registerresult/1", true,
                    await CreateCustomerAttributeFormAsync());
                await workContext.SetCurrentCustomerAsync(admin);
                ClearWorkContextCaches();

                var gdpr = _services.GetRequiredService<IGdprService>();
                if (!(await gdpr.GetAllConsentsAsync()).Any(consent => consent.DisplayOnCustomerInfoPage))
                {
                    await gdpr.InsertConsentAsync(new GdprConsent
                    {
                        Message = "Harvest consent",
                        IsRequired = false,
                        RequiredMessage = "required",
                        DisplayDuringRegistration = true,
                        DisplayOnCustomerInfoPage = true,
                        DisplayOrder = 2
                    });
                }

                var info = await publicFactory.PrepareCustomerInfoModelAsync(
                    new global::Nop.Web.Models.Customer.CustomerInfoModel(), admin, false);
                var oldFirst = info.FirstName;
                info.FirstName = "Harvested";
                info.LastName = (info.LastName ?? "Admin") + "X";
                info.Email = admin.Email;
                info.Gender = info.Gender == "M" ? "F" : "M";
                info.Company = "Coverage Co";
                info.StreetAddress = "1 Coverage Way";
                info.City = "New York";
                info.ZipPostalCode = "10001";
                foreach (var subscription in info.NewsLetterSubscriptions)
                    subscription.IsActive = !subscription.IsActive;
                var consentFields = new Dictionary<string, string>();
                foreach (var consent in await gdpr.GetAllConsentsAsync())
                    consentFields[$"consent{consent.Id}"] = "on";
                publicCustomer.ModelState.Clear();
                await publicCustomer.Info(info, await CreateCustomerAttributeFormAsync(consentFields));
                info.FirstName = oldFirst;
                foreach (var subscription in info.NewsLetterSubscriptions)
                    subscription.IsActive = false;
                publicCustomer.ModelState.Clear();
                await publicCustomer.Info(info, await CreateCustomerAttributeFormAsync());

                if (info.CustomerAttributes?.Count > 0)
                    await ExerciseRazorPageWithModelAsync("Views_Customer__CustomerAttributes", info.CustomerAttributes);
            });

            await Try(async () =>
            {
                var pm = CreateController<global::Nop.Web.Controllers.PrivateMessagesController>();
                await pm.Index(1, "inbox");
                var send = new global::Nop.Web.Models.PrivateMessages.SendPrivateMessageModel
                {
                    ToCustomerId = shopper.Id,
                    Subject = "Coverage private subject",
                    Message = "Coverage private message body that is long"
                };
                pm.ModelState.Clear();
                await pm.SendPM(send);
                var messages = await customerService.GetAllPrivateMessagesAsync(store.Id, admin.Id, shopper.Id,
                    null, false, false, string.Empty, 0, 10);
                var message = messages.FirstOrDefault();
                if (message != null)
                {
                    await pm.ViewPM(message.Id);
                    await pm.SendPM(shopper.Id, message.Id);
                    var reply = new global::Nop.Web.Models.PrivateMessages.SendPrivateMessageModel
                    {
                        ToCustomerId = shopper.Id,
                        ReplyToMessageId = message.Id,
                        Subject = "Re: Coverage",
                        Message = "Reply body"
                    };
                    pm.ModelState.Clear();
                    await pm.SendPM(reply);
                    await pm.DeleteInboxPM(CreateForm(new Dictionary<string, string>
                    {
                        [$"pm{message.Id}"] = "on"
                    }));
                    await pm.DeleteSentPM(CreateForm(new Dictionary<string, string>
                    {
                        [$"pm{message.Id}"] = "on"
                    }));
                    await pm.MarkUnread(CreateForm(new Dictionary<string, string>
                    {
                        [$"pm{message.Id}"] = "on"
                    }));
                    await pm.DeletePM(message.Id);
                }
            });

            await Try(async () =>
            {
                await EnsurePlainProductInCartAsync();
                var generic = _services.GetRequiredService<IGenericAttributeService>();
                await generic.SaveAttributeAsync(admin, NopCustomerDefaults.SelectedPaymentMethodAttribute,
                    "Payments.TestMethod", store.Id);
                var processing = _services.GetRequiredService<IOrderProcessingService>();
                await processing.SetProcessPaymentRequestAsync(new ProcessPaymentRequest
                {
                    StoreId = store.Id,
                    CustomerId = admin.Id,
                    PaymentMethodSystemName = "Payments.TestMethod"
                });

                orderSettings.OnePageCheckoutEnabled = false;
                await settingService.SaveSettingAsync(orderSettings);
                var checkout = CreateController<global::Nop.Web.Controllers.CheckoutController>();
                checkout.ModelState.Clear();
                await checkout.ConfirmOrder(true);

                orderSettings.OnePageCheckoutEnabled = true;
                await settingService.SaveSettingAsync(orderSettings);
                await EnsurePlainProductInCartAsync();
                await processing.SetProcessPaymentRequestAsync(new ProcessPaymentRequest
                {
                    StoreId = store.Id,
                    CustomerId = admin.Id,
                    PaymentMethodSystemName = "Payments.TestMethod"
                });
                checkout.ModelState.Clear();
                await checkout.OpcConfirmOrder(true);

                var checkoutFactory = _services.GetRequiredService<global::Nop.Web.Factories.ICheckoutModelFactory>();
                var addresses = await customerService.GetAddressesByCustomerIdAsync(admin.Id);
                var cart = await _services.GetRequiredService<IShoppingCartService>()
                    .GetShoppingCartAsync(admin, ShoppingCartType.ShoppingCart, store.Id);
                var billing = new global::Nop.Web.Models.Checkout.CheckoutBillingAddressModel { ShipToSameAddress = true };
                await checkoutFactory.PrepareBillingAddressModelAsync(billing, cart,
                    prePopulateNewAddressWithCustomerFields: true);
                billing.BillingNewAddress ??= new global::Nop.Web.Models.Common.AddressModel();
                billing.BillingNewAddress.FirstName = "Harvest";
                billing.BillingNewAddress.LastName = "Buyer";
                billing.BillingNewAddress.Email = admin.Email;
                billing.BillingNewAddress.Address1 = "2 Coverage Ave";
                billing.BillingNewAddress.City = "New York";
                billing.BillingNewAddress.ZipPostalCode = "10002";
                billing.BillingNewAddress.CountryId = addresses.FirstOrDefault()?.CountryId ?? 1;
                checkout.ModelState.Clear();
                await checkout.OpcSaveBilling(billing, await CreateAddressAttributeFormAsync(new Dictionary<string, string>
                {
                    ["billing_address_id"] = "0"
                }));
                checkout.ModelState.Clear();
                await checkout.OpcSaveBilling(billing, await CreateAddressAttributeFormAsync(new Dictionary<string, string>
                {
                    ["billing_address_id"] = (addresses.FirstOrDefault()?.Id ?? 0).ToString()
                }));

                var addressAttrs = billing.BillingNewAddress.CustomAddressAttributes;
                if (addressAttrs?.Count > 0)
                    await ExerciseRazorPageWithModelAsync("Views_Shared__AddressAttributes", addressAttrs);
            });

            await Try(async () =>
            {
                var orders = await _services.GetRequiredService<IOrderService>().SearchOrdersAsync(pageIndex: 0, pageSize: 5);
                var orderController = CreateController<global::Nop.Web.Areas.Admin.Controllers.OrderController>();
                var ids = string.Join(",", orders.Select(order => order.Id));
                if (!string.IsNullOrEmpty(ids))
                {
                    await orderController.PdfInvoiceSelected(ids);
                    await orderController.ExportXmlSelected(ids);
                    await orderController.ExportExcelSelected(ids);
                    await orderController.PdfPackagingSlipSelected(ids);
                }

                var shipments = await _services.GetRequiredService<IShipmentService>().GetAllShipmentsAsync(pageIndex: 0, pageSize: 5);
                var shipmentIds = shipments.Select(shipment => shipment.Id).ToList();
                if (shipmentIds.Count > 0)
                {
                    await orderController.SetAsShippedSelected(shipmentIds);
                    await orderController.SetAsReadyForPickupSelected(shipmentIds);
                    await orderController.SetAsDeliveredSelected(shipmentIds);
                }
            });

            await Try(async () =>
            {
                var catalogFactory = _services.GetRequiredService<global::Nop.Web.Factories.ICatalogModelFactory>();
                await _services.GetRequiredService<IStaticCacheManager>().RemoveByPrefixAsync("Nop.pres.searchbox");
                await catalogFactory.PrepareSearchBoxModelAsync();
            });

            await Try(async () =>
            {
                var customerFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.ICustomerModelFactory>();
                var adminModel = await customerFactory.PrepareCustomerModelAsync(null, admin);
                if (adminModel.CustomerAttributes?.Count > 0)
                    await ExerciseRazorPageWithModelAsync("Areas_Admin_Views_Customer__CustomerAttributes",
                        adminModel.CustomerAttributes);

                var vendor = (await _services.GetRequiredService<IVendorService>().GetAllVendorsAsync()).FirstOrDefault();
                if (vendor != null)
                {
                    var vendorFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IVendorModelFactory>();
                    var vendorModel = await vendorFactory.PrepareVendorModelAsync(null, vendor);
                    if (vendorModel.VendorAttributes?.Count > 0)
                        await ExerciseRazorPageWithModelAsync("Areas_Admin_Views_Vendor__VendorAttributes",
                            vendorModel.VendorAttributes);
                }
            });

            await ExerciseHighMissGoldAsync(admin, shopper, store);
        }
        finally
        {
            orderSettings.OnePageCheckoutEnabled = previousOpc;
            orderSettings.MinimumOrderPlacementInterval = previousInterval;
            vendorSettings.AllowCustomersToApplyForVendorAccount = previousVendorApply;
            vendorSettings.MaximumProductNumber = previousMaxProducts;
            customerSettings.UsernamesEnabled = previousUsernames;
            customerSettings.NotifyFailedLoginAttempt = previousNotifyLogin;
            catalogSettings.ShowSearchBoxCategories = previousSearchCategories;
            catalogSettings.ProductSearchEnabled = previousSearchEnabled;
            catalogSettings.ShowSearchTermHistory = previousSearchHistory;
            catalogSettings.ProductSearchAutoCompleteEnabled = previousAutoComplete;
            privateMessages.AllowPrivateMessages = previousPm;
            privateMessages.NotifyAboutPrivateMessages = previousPmNotify;
            gdprSettings.GdprEnabled = previousGdpr;
            gdprSettings.LogUserProfileChanges = previousGdprLog;
            gdprSettings.LogNewsletterConsent = previousGdprNewsletter;
            captchaSettings.Enabled = previousCaptcha;
            await settingService.SaveSettingAsync(orderSettings);
            await settingService.SaveSettingAsync(vendorSettings);
            await settingService.SaveSettingAsync(customerSettings);
            await settingService.SaveSettingAsync(catalogSettings);
            await settingService.SaveSettingAsync(privateMessages);
            await settingService.SaveSettingAsync(gdprSettings);
            await settingService.SaveSettingAsync(captchaSettings);
            admin.VendorId = previousVendorId;
            await customerService.UpdateCustomerAsync(admin);
            var restored = await customerService.GetCustomerByEmailAsync(global::Nop.Tests.NopTestsDefaults.AdminEmail);
            if (restored != null)
                await workContext.SetCurrentCustomerAsync(restored);
            ClearWorkContextCaches();
            await SetAdminStoreScopeAsync(0);
            await EnsurePlainProductInCartAsync();
        }
    }

    private async Task ExerciseHighMissGoldAsync(Customer admin, Customer shopper, Store store)
    {
        async Task Try(Func<Task> action)
        {
            try { await action(); } catch { }
        }

        var settingService = _services.GetRequiredService<ISettingService>();
        var workContext = _services.GetRequiredService<IWorkContext>();
        var customerService = _services.GetRequiredService<ICustomerService>();
        var productService = _services.GetRequiredService<IProductService>();
        var pluginsInfo = Singleton<IPluginsInfo>.Instance;

        await Try(async () =>
        {
            pluginsInfo.IncompatiblePlugins ??= new Dictionary<string, PluginIncompatibleType>();
            pluginsInfo.IncompatiblePlugins["Coverage.Incompatible.Main"] = PluginIncompatibleType.MainAssemblyNotFound;
            pluginsInfo.IncompatiblePlugins["Coverage.Incompatible.Version"] = PluginIncompatibleType.NotCompatibleWithCurrentVersion;
            pluginsInfo.AssemblyLoadedCollision ??= [];
            var collision = new PluginLoadedAssemblyInfo("Coverage.Collision", new Version(13, 0, 0, 0));
            collision.References.Add(("Coverage.Plugin", new Version(12, 0, 0, 0)));
            pluginsInfo.AssemblyLoadedCollision.Add(collision);

            var directory = Path.Combine(Path.GetTempPath(), "nop-coverage-plugins", "NotInstalled_Harvest");
            Directory.CreateDirectory(directory);
            var jsonPath = Path.Combine(directory, NopPluginDefaults.DescriptionFileName);
            if (!File.Exists(jsonPath))
                File.WriteAllText(jsonPath, "{}");
            pluginsInfo.PluginDescriptors.Add((new PluginDescriptor
            {
                SystemName = "NotInstalled.Harvest.Plugin",
                FriendlyName = "Not installed harvest plugin",
                Group = "Misc",
                Installed = false,
                OriginalAssemblyFile = Path.Combine(directory, "plugin.dll"),
                AssemblyFileName = "NotInstalled.Harvest.dll",
                PluginType = typeof(TestWidgetPlugin)
            }, false));

            var commonFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.ICommonModelFactory>();
            var warnings = new List<global::Nop.Web.Areas.Admin.Models.Common.SystemWarningModel>();
            await commonFactory.PrepareSystemWarningModelsAsync();
            await InvokeInstanceMethodAsync(commonFactory, "PrepareIncompatibleWarningModelAsync", warnings);
            await InvokeInstanceMethodAsync(commonFactory, "PreparePluginsCollisionsWarningModelAsync", warnings);
            await InvokeInstanceMethodAsync(commonFactory, "PreparePluginsInstalledWarningModelAsync", warnings);
            await InvokeInstanceMethodAsync(commonFactory, "PreparePluginsEnabledWarningModelAsync", warnings);
        });

        await Try(async () =>
        {
            var currencySettings = _services.GetRequiredService<CurrencySettings>();
            var measureSettings = _services.GetRequiredService<MeasureSettings>();
            var catalogSettings = _services.GetRequiredService<CatalogSettings>();
            var storeService = _services.GetRequiredService<IStoreService>();
            var currencyService = _services.GetRequiredService<ICurrencyService>();
            var commonFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.ICommonModelFactory>();

            var prevExchange = currencySettings.PrimaryExchangeRateCurrencyId;
            var prevPrimary = currencySettings.PrimaryStoreCurrencyId;
            var prevWeight = measureSettings.BaseWeightId;
            var prevDimension = measureSettings.BaseDimensionId;
            var prevIgnoreAcl = catalogSettings.IgnoreAcl;
            var prevIgnoreStore = catalogSettings.IgnoreStoreLimitations;
            var currentStore = await storeService.GetStoreByIdAsync(store.Id) ?? store;
            var prevUrl = currentStore.Url;

            try
            {
                currencySettings.PrimaryExchangeRateCurrencyId = 0;
                currencySettings.PrimaryStoreCurrencyId = 0;
                measureSettings.BaseWeightId = 0;
                measureSettings.BaseDimensionId = 0;
                catalogSettings.IgnoreAcl = false;
                catalogSettings.IgnoreStoreLimitations = false;
                await settingService.SaveSettingAsync(currencySettings);
                await settingService.SaveSettingAsync(measureSettings);
                await settingService.SaveSettingAsync(catalogSettings);
                currentStore.Url = "http://coverage-mismatch.example/";
                await storeService.UpdateStoreAsync(currentStore);
                await commonFactory.PrepareSystemWarningModelsAsync();

                currencySettings.PrimaryExchangeRateCurrencyId = prevExchange;
                currencySettings.PrimaryStoreCurrencyId = prevPrimary;
                measureSettings.BaseWeightId = prevWeight;
                measureSettings.BaseDimensionId = prevDimension;
                await settingService.SaveSettingAsync(currencySettings);
                await settingService.SaveSettingAsync(measureSettings);
                var exchange = await currencyService.GetCurrencyByIdAsync(prevExchange);
                var prevRate = exchange?.Rate ?? 1;
                if (exchange != null)
                {
                    exchange.Rate = prevRate == 1 ? 2 : 1;
                    await currencyService.UpdateCurrencyAsync(exchange);
                    await commonFactory.PrepareSystemWarningModelsAsync();
                    exchange.Rate = prevRate;
                    await currencyService.UpdateCurrencyAsync(exchange);
                }

                var weight = await _services.GetRequiredService<IMeasureService>().GetMeasureWeightByIdAsync(prevWeight);
                if (weight != null && weight.Ratio == 1)
                {
                    weight.Ratio = 2;
                    await _services.GetRequiredService<IMeasureService>().UpdateMeasureWeightAsync(weight);
                    await commonFactory.PrepareSystemWarningModelsAsync();
                    weight.Ratio = 1;
                    await _services.GetRequiredService<IMeasureService>().UpdateMeasureWeightAsync(weight);
                }

                var dimension = await _services.GetRequiredService<IMeasureService>().GetMeasureDimensionByIdAsync(prevDimension);
                if (dimension != null && dimension.Ratio == 1)
                {
                    dimension.Ratio = 2;
                    await _services.GetRequiredService<IMeasureService>().UpdateMeasureDimensionAsync(dimension);
                    await commonFactory.PrepareSystemWarningModelsAsync();
                    dimension.Ratio = 1;
                    await _services.GetRequiredService<IMeasureService>().UpdateMeasureDimensionAsync(dimension);
                }
            }
            finally
            {
                currencySettings.PrimaryExchangeRateCurrencyId = prevExchange;
                currencySettings.PrimaryStoreCurrencyId = prevPrimary;
                measureSettings.BaseWeightId = prevWeight;
                measureSettings.BaseDimensionId = prevDimension;
                catalogSettings.IgnoreAcl = prevIgnoreAcl;
                catalogSettings.IgnoreStoreLimitations = prevIgnoreStore;
                currentStore.Url = prevUrl;
                await settingService.SaveSettingAsync(currencySettings);
                await settingService.SaveSettingAsync(measureSettings);
                await settingService.SaveSettingAsync(catalogSettings);
                await storeService.UpdateStoreAsync(currentStore);
            }
        });

        await Try(async () =>
        {
            var filterSettings = _services.GetRequiredService<FilterLevelSettings>();
            var previousEnabled = filterSettings.FilterLevelEnabled;
            var previousHome = filterSettings.DisplayOnHomePage;
            var previousDetails = filterSettings.DisplayOnProductDetailsPage;
            filterSettings.FilterLevelEnabled = true;
            filterSettings.DisplayOnHomePage = true;
            filterSettings.DisplayOnProductDetailsPage = true;
            await settingService.SaveSettingAsync(filterSettings);
            try
            {
                var filterService = _services.GetRequiredService<IFilterLevelValueService>();
                var filter = new FilterLevelValue
                {
                    FilterLevel1Value = "CoverageL1",
                    FilterLevel2Value = "CoverageL2",
                    FilterLevel3Value = "CoverageL3",
                    CreatedOnUtc = DateTime.UtcNow,
                    UpdatedOnUtc = DateTime.UtcNow
                };
                await filterService.InsertFilterLevelValueAsync(filter);
                var host = (await productService.SearchProductsAsync(pageSize: 1)).First();
                await filterService.InsertProductFilterLevelValueAsync(new FilterLevelValueProductMapping
                {
                    FilterLevelValueId = filter.Id,
                    ProductId = host.Id
                });

                var filterController = CreateController<global::Nop.Web.Areas.Admin.Controllers.FilterLevelValueController>();
                filterController.ModelState.Clear();
                _ = filterController.Create();
                var createModel = new global::Nop.Web.Areas.Admin.Models.Catalog.FilterLevelValueModel
                {
                    FilterLevel1Value = "HarvestL1",
                    FilterLevel2Value = "HarvestL2",
                    FilterLevel3Value = "HarvestL3",
                    FilterLevel1ValueEnabled = true,
                    FilterLevel2ValueEnabled = true,
                    FilterLevel3ValueEnabled = true
                };
                filterController.ModelState.Clear();
                await filterController.Create(createModel, true);
                filterController.ModelState.Clear();
                await filterController.Create(new global::Nop.Web.Areas.Admin.Models.Catalog.FilterLevelValueModel
                {
                    FilterLevel1Value = "CoverageL1",
                    FilterLevel2Value = "CoverageL2",
                    FilterLevel3Value = "CoverageL3"
                }, false);
                await filterController.Edit(filter.Id);
                var editModel = new global::Nop.Web.Areas.Admin.Models.Catalog.FilterLevelValueModel
                {
                    Id = filter.Id,
                    FilterLevel1Value = "CoverageL1X",
                    FilterLevel2Value = "CoverageL2",
                    FilterLevel3Value = "CoverageL3"
                };
                filterController.ModelState.Clear();
                await filterController.Edit(editModel, true);
                await filterController.ProductAddPopup(filter.Id);
                var addSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.AddProductToFilterLevelValueSearchModel();
                addSearch.SetGridPageSize();
                await filterController.ProductAddPopupList(addSearch);
                filterController.ModelState.Clear();
                await filterController.ProductAddPopup(new global::Nop.Web.Areas.Admin.Models.Catalog.AddProductToFilterLevelValueModel
                {
                    FilterLevelValueId = filter.Id,
                    SelectedProductIds = [host.Id]
                });

                var productFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IProductModelFactory>();
                var filterSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.FilterLevelValueSearchModel { ProductId = host.Id };
                filterSearch.SetGridPageSize();
                await productFactory.PrepareFilterLevelValueListModelAsync(filterSearch, host);

                var catalog = CreateController<global::Nop.Web.Controllers.CatalogController>();
                await catalog.GetFilterLevelValues();
                await catalog.GetFilterLevelValues("CoverageL1");
                await catalog.GetFilterLevelValues("CoverageL1", "CoverageL2");
                await catalog.GetFilterLevelValues("CoverageL1", "CoverageL2", "CoverageL3");
                await catalog.SearchByFilterLevelValues(new global::Nop.Web.Models.Catalog.SearchFilterLevelValueModel
                {
                    fl1id = "CoverageL1",
                    fl2id = "CoverageL2",
                    fl3id = "CoverageL3"
                }, new global::Nop.Web.Models.Catalog.CatalogProductsCommand());
            }
            finally
            {
                filterSettings.FilterLevelEnabled = previousEnabled;
                filterSettings.DisplayOnHomePage = previousHome;
                filterSettings.DisplayOnProductDetailsPage = previousDetails;
                await settingService.SaveSettingAsync(filterSettings);
            }
        });

        await Try(async () =>
        {
            var emailAccounts = _services.GetRequiredService<IEmailAccountService>();
            var emails = await emailAccounts.GetAllEmailAccountsAsync();
            var account = emails.FirstOrDefault();
            var emailController = CreateController<global::Nop.Web.Areas.Admin.Controllers.EmailAccountController>();
            if (account != null)
            {
                await emailController.Edit(account.Id);
                await emailController.MarkAsDefaultEmail(account.Id);
                var factory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IEmailAccountModelFactory>();
                var model = await factory.PrepareEmailAccountModelAsync(null, account);
                model.SendTestEmailTo = "not-an-email";
                emailController.ModelState.Clear();
                await emailController.SendTestEmail(model);
                model.SendTestEmailTo = "coverage-test@example.com";
                emailController.ModelState.Clear();
                await emailController.SendTestEmail(model);
                await emailController.AuthReturn(new global::Google.Apis.Auth.OAuth2.Responses.AuthorizationCodeResponseUrl());
                await emailController.AuthReturn(new global::Google.Apis.Auth.OAuth2.Responses.AuthorizationCodeResponseUrl
                {
                    State = account.Id.ToString(),
                    Error = "access_denied"
                });
                await emailController.AuthReturn(new global::Google.Apis.Auth.OAuth2.Responses.AuthorizationCodeResponseUrl
                {
                    State = account.Id.ToString(),
                    Code = string.Empty
                });
            }

            var create = new global::Nop.Web.Areas.Admin.Models.Messages.EmailAccountModel
            {
                Email = $"harvest-{Guid.NewGuid():N}@example.com",
                DisplayName = "Harvest mail",
                Host = "localhost",
                Port = 25,
                Username = "harvest",
                Password = "harvest"
            };
            emailController.ModelState.Clear();
            await emailController.Create(create, true);
        });

        await Try(async () =>
        {
            var productController = CreateController<global::Nop.Web.Areas.Admin.Controllers.ProductController>();
            var host = (await productService.SearchProductsAsync(pageSize: 1)).First();
            productController.ModelState.Clear();
            await productController.CopyProduct(new global::Nop.Web.Areas.Admin.Models.Catalog.ProductModel
            {
                CopyProductModel = new global::Nop.Web.Areas.Admin.Models.Catalog.CopyProductModel
                {
                    Id = host.Id,
                    Name = "Coverage copy " + Guid.NewGuid().ToString("N")[..6],
                    Published = true,
                    CopyMultimedia = true
                }
            });
            await productController.SkuReservedWarning(host.Id, host.Sku);
            await productController.SkuReservedWarning(0, "COV-UNIQUE-" + Guid.NewGuid().ToString("N")[..6]);
            await productController.Create();
            var create = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductModel
            {
                Name = "Harvest minimal " + Guid.NewGuid().ToString("N")[..6],
                Sku = "HMIN" + Guid.NewGuid().ToString("N")[..6],
                Price = 9,
                Published = true,
                ProductTypeId = (int)ProductType.SimpleProduct,
                VisibleIndividually = true,
                IsShipEnabled = true,
                StockQuantity = 10,
                OrderMinimumQuantity = 1,
                OrderMaximumQuantity = 10000,
                ManageInventoryMethodId = (int)ManageInventoryMethod.DontManageStock
            };
            productController.ModelState.Clear();
            await productController.Create(create, false);

            var specService = _services.GetRequiredService<ISpecificationAttributeService>();
            var specs = await specService.GetProductSpecificationAttributesAsync(host.Id);
            foreach (var psa in specs.Take(4))
            {
                var update = new global::Nop.Web.Areas.Admin.Models.Catalog.AddSpecificationAttributeModel
                {
                    ProductId = host.Id,
                    SpecificationId = psa.Id,
                    AttributeTypeId = (int)psa.AttributeType,
                    SpecificationAttributeOptionId = psa.SpecificationAttributeOptionId,
                    Value = "harvest",
                    ValueRaw = "<b>harvest</b>",
                    AllowFiltering = true,
                    ShowOnProductPage = true
                };
                productController.ModelState.Clear();
                await productController.ProductSpecAttrUpdate(update, false);
            }

            var attributeService = _services.GetRequiredService<IProductAttributeService>();
            var unused = new ProductAttribute { Name = "Harvest predefined " + Guid.NewGuid().ToString("N")[..6] };
            await attributeService.InsertProductAttributeAsync(unused);
            await attributeService.InsertPredefinedProductAttributeValueAsync(new PredefinedProductAttributeValue
            {
                ProductAttributeId = unused.Id,
                Name = "Harvest value",
                PriceAdjustment = 1,
                DisplayOrder = 1
            });
            var mappingModel = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductAttributeMappingModel
            {
                ProductId = host.Id,
                ProductAttributeId = unused.Id,
                AttributeControlTypeId = (int)AttributeControlType.DropdownList
            };
            productController.ModelState.Clear();
            await productController.ProductAttributeMappingCreate(mappingModel, false);

            var role = await customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.RegisteredRoleName);
            await productService.InsertTierPriceAsync(new TierPrice
            {
                ProductId = host.Id,
                StoreId = store.Id,
                CustomerRoleId = role?.Id,
                Quantity = 3,
                Price = 8
            });
            var productFactory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.IProductModelFactory>();
            var tierSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.TierPriceSearchModel { ProductId = host.Id };
            tierSearch.SetGridPageSize();
            await productFactory.PrepareTierPriceListModelAsync(tierSearch, host);
            var specSearch = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductSpecificationAttributeSearchModel { ProductId = host.Id };
            specSearch.SetGridPageSize();
            await productFactory.PrepareProductSpecificationAttributeListModelAsync(specSearch, host);
        });

        await Try(async () =>
        {
            var cartService = _services.GetRequiredService<IShoppingCartService>();
            var cart = await cartService.GetShoppingCartAsync(admin, ShoppingCartType.ShoppingCart, store.Id);
            foreach (var item in cart.ToList())
            {
                try { await cartService.DeleteShoppingCartItemAsync(item); }
                catch { }
            }

            await EnsurePlainProductInCartAsync();
            var orderSettings = _services.GetRequiredService<OrderSettings>();
            var previousOpc = orderSettings.OnePageCheckoutEnabled;
            orderSettings.OnePageCheckoutEnabled = false;
            await settingService.SaveSettingAsync(orderSettings);
            try
            {
                var checkout = CreateController<global::Nop.Web.Controllers.CheckoutController>();
                checkout.ModelState.Clear();
                await checkout.Index();
                checkout.ModelState.Clear();
                await checkout.BillingAddress(CreateForm());
            }
            finally
            {
                orderSettings.OnePageCheckoutEnabled = previousOpc;
                await settingService.SaveSettingAsync(orderSettings);
                await EnsurePlainProductInCartAsync();
            }
        });

        await Try(async () =>
        {
            var guest = await customerService.InsertGuestCustomerAsync();
            await workContext.SetCurrentCustomerAsync(guest);
            ClearWorkContextCaches();
            var publicCustomer = CreateController<global::Nop.Web.Controllers.CustomerController>();
            var publicFactory = _services.GetRequiredService<global::Nop.Web.Factories.ICustomerModelFactory>();
            var register = await publicFactory.PrepareRegisterModelAsync(
                new global::Nop.Web.Models.Customer.RegisterModel(), false);
            register.Email = $"harvest-nl-{Guid.NewGuid():N}@example.com";
            register.Password = "1q2w3e4r5t";
            register.ConfirmPassword = register.Password;
            register.FirstName = "Harvest";
            register.LastName = "News";
            foreach (var subscription in register.NewsLetterSubscriptions)
                subscription.IsActive = true;
            if (register.NewsLetterSubscriptions.Count == 0)
            {
                register.NewsLetterSubscriptions.Add(new global::Nop.Web.Models.Customer.NewsLetterSubscriptionModel
                {
                    TypeId = 1,
                    Name = "General",
                    IsActive = true
                });
            }

            var newsLetter = _services.GetRequiredService<INewsLetterSubscriptionService>();
            await newsLetter.InsertNewsLetterSubscriptionAsync(new NewsLetterSubscription
            {
                Email = register.Email,
                Active = true,
                StoreId = store.Id,
                TypeId = register.NewsLetterSubscriptions[0].TypeId,
                CreatedOnUtc = DateTime.UtcNow,
                NewsLetterSubscriptionGuid = Guid.NewGuid()
            });
            register.NewsLetterSubscriptions.Add(new global::Nop.Web.Models.Customer.NewsLetterSubscriptionModel
            {
                TypeId = register.NewsLetterSubscriptions[0].TypeId + 17,
                Name = "Extra",
                IsActive = true
            });
            publicCustomer.ModelState.Clear();
            await publicCustomer.Register(register, "/registerresult/1", true, await CreateCustomerAttributeFormAsync());
            await workContext.SetCurrentCustomerAsync(admin);
            ClearWorkContextCaches();
        });

        await Try(async () =>
        {
            var settings = CreateController<global::Nop.Web.Areas.Admin.Controllers.SettingController>();
            var factory = _services.GetRequiredService<global::Nop.Web.Areas.Admin.Factories.ISettingModelFactory>();
            await EnableAdminStoreScopeAsync();
            var customerUser = await factory.PrepareCustomerUserSettingsModelAsync();
            var previousRule = customerUser.CustomerSettings.UsernameValidationRule;
            customerUser.CustomerSettings.UsernameValidationEnabled = true;
            customerUser.CustomerSettings.UsernameValidationUseRegex = true;
            customerUser.CustomerSettings.UsernameValidationRule = "[invalid(";
            customerUser.CustomerSettings.PhoneNumberValidationEnabled = true;
            customerUser.CustomerSettings.PhoneNumberValidationUseRegex = true;
            customerUser.CustomerSettings.PhoneNumberValidationRule = "[invalid(";
            settings.ModelState.Clear();
            await settings.CustomerUser(customerUser);
            customerUser = await factory.PrepareCustomerUserSettingsModelAsync();
            customerUser.CustomerSettings.UsernameValidationRule = previousRule;
            settings.ModelState.Clear();
            await settings.CustomerUser(customerUser);
            await SetAdminStoreScopeAsync(0);
        });

        await Try(async () =>
        {
            var pluginController = CreateController<global::Nop.Web.Areas.Admin.Controllers.PluginController>();
            try
            {
                await pluginController.Uninstall(CreateForm(new Dictionary<string, string>
                {
                    ["uninstall-plugin-link-CurrencyExchange.TestProvider"] = "1"
                }));
            }
            catch { }
            try
            {
                await pluginController.Install(CreateForm(new Dictionary<string, string>
                {
                    ["install-plugin-link-CurrencyExchange.TestProvider"] = "1"
                }));
            }
            catch { }
            try
            {
                await pluginController.UninstallAndDeleteUnusedPlugins(["Does.Not.Exist"]);
            }
            catch { }
        });

        await Try(async () =>
        {
            var shipping = new global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel
            {
                DisplayPickupInStore = true,
                NewAddressPreselected = true,
                SelectedBillingAddress = 0
            };
            shipping.PickupPointsModel = new global::Nop.Web.Models.Checkout.CheckoutPickupPointsModel
            {
                AllowPickupInStore = true,
                PickupInStore = false,
                PickupInStoreOnly = false,
                DisplayPickupPointsOnMap = true,
                GoogleMapsApiKey = "coverage",
                PickupPoints =
                [
                    new global::Nop.Web.Models.Checkout.CheckoutPickupPointModel
                    {
                        Id = "1",
                        Name = "Warehouse",
                        Address = "1 Main",
                        City = "Town",
                        CountryName = "United States",
                        PickupFee = "$0.00",
                        ProviderSystemName = "PickupPoint.TestProvider",
                        OpeningHours = "9-5"
                    }
                ]
            };
            shipping.ExistingAddresses =
            [
                new global::Nop.Web.Models.Common.AddressModel
                {
                    Id = 1,
                    FirstName = "Ann",
                    LastName = "Admin",
                    Email = admin.Email,
                    PhoneEnabled = true,
                    PhoneNumber = "5550001111",
                    CityEnabled = true,
                    City = "New York",
                    StreetAddressEnabled = true,
                    Address1 = "1 Coverage"
                }
            ];
            shipping.InvalidExistingAddresses =
            [
                new global::Nop.Web.Models.Common.AddressModel { Id = 2, FirstName = "Bad", LastName = "Address" }
            ];
            await ExerciseRazorPageWithModelAsync("Views_Checkout_ShippingAddress", shipping);
            await ExerciseRazorPageWithModelAsync("Views_Checkout__PickupPoints", shipping.PickupPointsModel);

            var returns = new global::Nop.Web.Models.Order.SubmitReturnRequestModel
            {
                OrderId = 1,
                CustomOrderNumber = "1",
                Comments = "coverage",
                AllowFiles = true,
                Result = "Your return request has been submitted successfully.",
                Items =
                [
                    new global::Nop.Web.Models.Order.SubmitReturnRequestModel.OrderItemModel
                    {
                        Id = 1,
                        ProductId = 1,
                        ProductName = "Build your own computer",
                        ProductSeName = "build-your-own-computer",
                        AttributeInfo = "RAM: 2 GB",
                        Quantity = 2,
                        UnitPrice = "$1,200.00"
                    }
                ],
                AvailableReturnReasons = [new() { Id = 1, Name = "Damaged" }],
                AvailableReturnActions = [new() { Id = 1, Name = "Refund" }]
            };
            await ExerciseRazorPageWithModelAsync("Views_ReturnRequest_ReturnRequest", returns);

            var order = new global::Nop.Web.Areas.Admin.Models.Orders.OrderModel
            {
                AllowCustomersToSelectTaxDisplayType = true,
                TaxDisplayType = TaxDisplayType.IncludingTax,
                OrderSubtotalInclTax = "$10.00",
                OrderSubtotalExclTax = "$8.00",
                OrderSubTotalDiscountInclTax = "$1.00",
                OrderSubTotalDiscountExclTax = "$0.80",
                OrderShippingInclTax = "$2.00",
                OrderShippingExclTax = "$1.60",
                PaymentMethodAdditionalFeeInclTax = "$0.50",
                PaymentMethodAdditionalFeeExclTax = "$0.40",
                OrderTotalDiscount = "$0.10",
                OrderTotal = "$12.00",
                CanCancelOrder = true,
                CanCapture = true,
                CanMarkOrderAsPaid = true,
                CanRefund = true,
                CanRefundOffline = true,
                CanPartiallyRefund = true,
                CanPartiallyRefundOffline = true,
                CanVoid = true,
                CanVoidOffline = true,
                HasDownloadableProducts = true,
                CheckoutAttributeInfo = "Gift wrapping"
            };
            order.TaxRates.Add(new global::Nop.Web.Areas.Admin.Models.Orders.OrderModel.TaxRate { Rate = "10", Value = "$1.00" });
            order.GiftCards.Add(new global::Nop.Web.Areas.Admin.Models.Orders.OrderModel.GiftCard { CouponCode = "GIFT", Amount = "$5.00" });
            order.Items.Add(new global::Nop.Web.Areas.Admin.Models.Orders.OrderItemModel { ProductName = "Coverage", Quantity = 1 });
            await ExerciseRazorPageWithModelAsync("Areas_Admin_Views_Order__OrderDetails_Info", order);

            var tables = new DataTablesModel();
            EnrichDataTablesModel(tables);
            await ExerciseRazorPageWithModelAsync("Areas_Admin_Views_Shared__Table_Definition", tables);
            tables.Data = "[]";
            tables.UrlRead = new DataUrl("/coverage/list", true);
            await ExerciseRazorPageWithModelAsync("Areas_Admin_Views_Shared__Table_Definition", tables);
        });
    }

    private static async Task<object> AwaitResult(object raw)
    {
        if (raw is not Task task)
            return raw;

        await task;
        var taskType = task.GetType();
        return taskType.IsGenericType ? taskType.GetProperty("Result")?.GetValue(task) : null;
    }

    public async Task ExerciseRazorPageWithModelAsync(string typeNameFragment, object model,
        IDictionary<string, object> viewData = null, Type controllerType = null)
    {
        var type = typeof(global::Nop.Web.Controllers.HomeController).Assembly.GetTypes()
            .FirstOrDefault(candidate =>
                candidate.IsClass && !candidate.IsAbstract
                && candidate.GetMethod("ExecuteAsync") != null
                && (candidate.FullName?.Contains(typeNameFragment, StringComparison.Ordinal) == true
                    || candidate.Name.Contains(typeNameFragment, StringComparison.Ordinal)));
        if (type == null)
            return;

        var previousControllerType = RazorControllerType;
        if (controllerType != null)
            RazorControllerType = controllerType;

        IServiceProvider previousServices = null;
        HttpContext http = null;
        try
        {
            var instance = Activator.CreateInstance(type);
            if (instance == null)
                return;

            TypesCreated++;
            http = _services.GetRequiredService<IHttpContextAccessor>().HttpContext
                   ?? throw new InvalidOperationException("HttpContext is not available");
            previousServices = http.RequestServices;
            http.RequestServices = new CompositeServiceProvider(MvcServices.Value, _services);

            AttachMvc(instance);
            if (instance is RazorPageBase razorPage)
            {
                razorPage.Layout = null;
                razorPage.HtmlEncoder ??= HtmlEncoder.Default;
                BindTypedRazorModel(razorPage, model, viewData);
                try
                {
                    MvcServices.Value.GetService<IRazorPageActivator>()
                        ?.Activate(razorPage, razorPage.ViewContext);
                }
                catch
                {
                }

                FillGraph(model, 0);
                AttachRazorRuntime(razorPage);
                    ActivateRazorInjects(instance, razorPage.ViewContext);
                    razorPage.Layout = null;
                    RestoreRazorControllerDescriptor(razorPage);
                    BindTypedRazorModel(razorPage, model, viewData);
            }

            await ExecuteRazorAsync(instance, TimeSpan.FromSeconds(12));
            MethodsInvoked++;

            if (instance is RazorPageBase secondPage)
            {
                SetObjectBools(model, false);
                BindTypedRazorModel(secondPage, model, viewData);
                try
                {
                    await ExecuteRazorAsync(instance, TimeSpan.FromSeconds(6));
                    MethodsInvoked++;
                }
                catch
                {
                    MethodsFailed++;
                }
            }
        }
        catch
        {
            MethodsFailed++;
        }
        finally
        {
            RazorControllerType = previousControllerType;
            if (http != null && previousServices != null)
                http.RequestServices = previousServices;
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
                    var savedViewData = razorPage.ViewContext.ViewData;
                    try
                    {
                        MvcServices.Value.GetService<IRazorPageActivator>()
                            ?.Activate(razorPage, razorPage.ViewContext);
                    }
                    catch
                    {
                        // keep the helpers attached in AttachMvc
                    }

                    if (razorPage.ViewContext?.ViewData?.Model == null && savedViewData != null)
                        razorPage.ViewContext.ViewData = savedViewData;

                    AttachRazorRuntime(razorPage);

                    // Real IHtmlHelper/IViewComponentHelper try to locate partials and
                    // view components via the MVC view engine. Stub them so this page's
                    // own ExecuteAsync can finish; compiled partials are invoked separately.
                    ActivateRazorInjects(instance, razorPage.ViewContext);
                    FillRazorModelGraph(instance);
                    RestoreRazorControllerDescriptor(razorPage);
                    BindTypedRazorModel(razorPage, razorPage.ViewContext?.ViewData?.Model);
                }

                try
                {
                    await ExecuteRazorAsync(instance, TimeSpan.FromSeconds(12));
                    MethodsInvoked++;
                    if (instance is RazorPageBase polarityPage)
                    {
                        var polarityModel = polarityPage.ViewContext?.ViewData?.Model;
                        SetObjectBools(polarityModel, false);
                        BindTypedRazorModel(polarityPage, polarityModel);
                        try
                        {
                            await ExecuteRazorAsync(instance, TimeSpan.FromSeconds(6));
                            MethodsInvoked++;
                        }
                        catch
                        {
                        }
                    }
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
        var typeList = types.ToList();
        var settingService = _services.GetRequiredService<ISettingService>();
        var customerSettings = _services.GetRequiredService<CustomerSettings>();
        var addressSettings = _services.GetRequiredService<AddressSettings>();
        var taxSettings = _services.GetRequiredService<TaxSettings>();

        Dictionary<string, object> Snapshot(object settings)
        {
            var values = new Dictionary<string, object>();
            foreach (var prop in settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0))
                values[prop.Name] = prop.GetValue(settings);
            return values;
        }

        void Restore(object settings, Dictionary<string, object> values)
        {
            foreach (var prop in settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0))
            {
                if (values.TryGetValue(prop.Name, out var value))
                    prop.SetValue(settings, value);
            }
        }

        void ApplyFlags(object settings, bool enabled, bool required)
        {
            foreach (var prop in settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0))
            {
                if (prop.PropertyType == typeof(bool) && prop.Name.EndsWith("Enabled", StringComparison.Ordinal))
                    prop.SetValue(settings, enabled);
                if (prop.PropertyType == typeof(bool) && prop.Name.EndsWith("Required", StringComparison.Ordinal))
                    prop.SetValue(settings, required);
            }
        }

        async Task PersistAsync()
        {
            await settingService.SaveSettingAsync(customerSettings);
            await settingService.SaveSettingAsync(addressSettings);
            await settingService.SaveSettingAsync(taxSettings);
        }

        async Task ConstructAllAsync()
        {
            foreach (var type in typeList)
            {
                try
                {
                    var validator = ActivatorUtilities.CreateInstance(_services, type);
                    TypesCreated++;
                    var modelType = type.BaseType?.GetGenericArguments().FirstOrDefault();
                    if (modelType == null)
                        continue;
                    var model = CreateArg(modelType, "model", type);
                    var validate = validator.GetType().GetMethods()
                        .FirstOrDefault(m => m.Name == "Validate" && m.GetParameters().Length == 1
                                             && m.GetParameters()[0].ParameterType == modelType);
                    validate?.Invoke(validator, [model]);
                    MethodsInvoked++;
                }
                catch (Exception ex)
                {
                    TypesFailed++;
                    if (Failures.Count < 80)
                        Failures.Add($"{type.Name}: {ex.GetBaseException().Message}");
                }
            }
        }

        var snapCustomer = Snapshot(customerSettings);
        var snapAddress = Snapshot(addressSettings);
        var snapTax = Snapshot(taxSettings);
        try
        {
            ApplyFlags(customerSettings, true, true);
            ApplyFlags(addressSettings, true, true);
            ApplyFlags(taxSettings, true, true);
            customerSettings.EnteringEmailTwice = true;
            customerSettings.UsernamesEnabled = true;
            customerSettings.DateOfBirthMinimumAge = 18;
            taxSettings.EuVatEnabled = true;
            taxSettings.EuVatRequired = true;
            await PersistAsync();
            await ConstructAllAsync();

            ApplyFlags(customerSettings, true, false);
            ApplyFlags(addressSettings, true, false);
            ApplyFlags(taxSettings, true, false);
            customerSettings.EnteringEmailTwice = false;
            customerSettings.DateOfBirthMinimumAge = null;
            taxSettings.EuVatRequired = false;
            await PersistAsync();
            await ConstructAllAsync();
        }
        finally
        {
            Restore(customerSettings, snapCustomer);
            Restore(addressSettings, snapAddress);
            Restore(taxSettings, snapTax);
            await PersistAsync();
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

        http.Request.ContentType = "multipart/form-data; boundary=----coverage";
        http.Request.QueryString = new QueryString("?q=coverage");
        try
        {
            http.Request.Form = CreateForm(withFile: true);
        }
        catch
        {
            // request form can already be read
        }

        var routeData = http.GetRouteData() ?? new RouteData();
        if (routeData.Routers.Count == 0)
            routeData.Routers.Add(DummyRouter.Instance);
        var typeName = instance.GetType().Name;
        if (typeName.Contains("Views_", StringComparison.Ordinal) || typeName.Contains('_'))
        {
            var parts = typeName.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                routeData.Values["action"] ??= parts[^1];
                routeData.Values["controller"] ??= parts[^2];
            }
        }

        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerName = routeData.Values["controller"]?.ToString() ?? "Product",
            ActionName = routeData.Values["action"]?.ToString() ?? "List",
            ControllerTypeInfo = (RazorControllerType
                                   ?? typeof(global::Nop.Tests.Nop.Services.Tests.Payments.TestPaymentMethod)).GetTypeInfo()
        };
        var actionContext = new ActionContext(http, routeData, actionDescriptor);
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
            AttachRazorRuntime(razorPage);
            ActivateRazorInjects(instance, viewContext);
        }
    }

    private static void AttachRazorRuntime(RazorPageBase page)
    {
        var mvc = MvcServices.Value;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var razorType = typeof(RazorPageBase);
        razorType.GetField("_tagHelperFactory", flags)?.SetValue(page, mvc.GetService<ITagHelperFactory>());
        var bufferField = razorType.GetField("_bufferScope", flags);
        if (bufferField != null)
            bufferField.SetValue(page, mvc.GetService(bufferField.FieldType));
        page.DiagnosticSource ??= mvc.GetService<DiagnosticSource>();
        page.HtmlEncoder ??= HtmlEncoder.Default;
        foreach (var propertyName in new[] { "MetadataProvider", "ModelExpressionProvider", "Json" })
        {
            try
            {
                var prop = razorType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (prop?.GetValue(page) != null || prop?.SetMethod == null)
                    continue;
                var value = mvc.GetService(prop.PropertyType);
                if (value != null)
                    prop.SetValue(page, value);
            }
            catch
            {
            }
        }
        try
        {
            var url = mvc.GetService<IUrlHelperFactory>()?.GetUrlHelper(page.ViewContext)
                      ?? EmptyProxy.Create(typeof(IUrlHelper), page.ViewContext);
            razorType.GetField("_urlHelper", flags)?.SetValue(page, url);
        }
        catch
        {
        }
    }

    private ViewDataDictionary CreateViewData(Type modelType, Type ownerType)
    {
        object model = null;
        if (modelType == typeof(DataTablesModel))
            model = CreateCoverageTableModel();
        else if (modelType != null)
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
                        value = MvcServices.Value.GetService(propertyType)
                                ?? _services.GetService(propertyType)
                                ?? EmptyProxy.Create(propertyType, viewContext);
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

    private async Task ExecuteRazorAsync(object instance, TimeSpan timeout)
    {
        var execute = instance.GetType().GetMethod("ExecuteAsync", BindingFlags.Public | BindingFlags.Instance);
        if (execute == null)
            return;

        var raw = execute.Invoke(instance, null);
        if (raw is not Task task)
            return;

        var finished = await Task.WhenAny(task, Task.Delay(timeout));
        if (finished != task)
            throw new TimeoutException($"{instance.GetType().Name}.ExecuteAsync timed out");

        await task;
    }

    private void BindTypedRazorModel(RazorPageBase page, object model, IDictionary<string, object> extra = null)
    {
        Type modelType = null;
        Type razorGeneric = null;
        for (var type = page.GetType(); type != null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(RazorPage<>))
            {
                razorGeneric = type;
                modelType = type.GetGenericArguments()[0];
                break;
            }
        }

        if (page.ViewContext == null)
            return;

        if (model == null && modelType != null)
        {
            try
            {
                model = CreateArg(modelType, "Model", page.GetType());
            }
            catch
            {
                model = CreateDefault(modelType);
            }

            FillGraph(model, 0);
        }

        var source = page.ViewContext.ViewData
                     ?? new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
        var typed = source;
        if (modelType != null)
        {
            try
            {
                var typedType = typeof(ViewDataDictionary<>).MakeGenericType(modelType);
                object created;
                try
                {
                    created = Activator.CreateInstance(typedType, source);
                }
                catch
                {
                    created = Activator.CreateInstance(typedType, new EmptyModelMetadataProvider(), new ModelStateDictionary());
                    if (created is ViewDataDictionary copied)
                    {
                        foreach (var pair in source)
                            copied[pair.Key] = pair.Value;
                    }
                }

                typed = (ViewDataDictionary)created;
                if (model != null)
                {
                    try
                    {
                        typedType.GetProperty("Model")?.SetValue(typed, model);
                    }
                    catch
                    {
                        try { typed.Model = model; }
                        catch { }
                    }
                }
            }
            catch
            {
                if (model != null)
                {
                    try { source.Model = model; }
                    catch { }
                }

                typed = source;
            }
        }
        else if (model != null)
        {
            try { source.Model = model; }
            catch { }
        }

        if (extra != null)
        {
            foreach (var pair in extra)
                typed[pair.Key] = pair.Value;
        }

        page.ViewContext.ViewData = typed;
        if (razorGeneric == null)
            return;

        try
        {
            razorGeneric.GetProperty("ViewData")?.SetValue(page, typed);
        }
        catch
        {
        }
    }

    private void RestoreRazorControllerDescriptor(RazorPageBase page)
    {
        if (page.ViewContext == null)
            return;

        var type = RazorControllerType ?? typeof(TestPaymentMethod);
        if (page.ViewContext.ActionDescriptor is ControllerActionDescriptor existing)
        {
            existing.ControllerTypeInfo = type.GetTypeInfo();
            return;
        }

        page.ViewContext.ActionDescriptor = new ControllerActionDescriptor
        {
            ControllerName = "Configure",
            ActionName = "Configure",
            ControllerTypeInfo = type.GetTypeInfo()
        };
    }

    private static void SetObjectBools(object model, bool value, int depth = 0)
    {
        if (model == null || depth > 2)
            return;
        if (model is string or IDictionary)
            return;
        if (model is IEnumerable enumerable)
        {
            foreach (var item in enumerable.Cast<object>().Take(40))
                SetObjectBools(item, value, depth + 1);
            return;
        }

        foreach (var prop in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanWrite || prop.GetIndexParameters().Length != 0)
                continue;
            if (prop.PropertyType != typeof(bool) && prop.PropertyType != typeof(bool?))
                continue;
            try
            {
                prop.SetValue(model, value);
            }
            catch
            {
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

    private void PopulateAttributeControlItems(IList list, Type itemType = null)
    {
        if (list == null || list.IsReadOnly)
            return;

        itemType ??= list.GetType().IsGenericType
            ? list.GetType().GetGenericArguments()[0]
            : list.GetType().GetInterfaces()
                .FirstOrDefault(candidate => candidate.IsGenericType
                                             && candidate.GetGenericTypeDefinition() == typeof(IList<>))
                ?.GetGenericArguments()[0];
        if (itemType == null)
            return;

        var controlProp = itemType.GetProperty("AttributeControlType");
        if (controlProp?.PropertyType != typeof(AttributeControlType))
        {
            if (list.Count == 0)
            {
                var fallback = CreateDefault(itemType);
                if (fallback != null)
                    list.Add(fallback);
            }

            return;
        }

        foreach (AttributeControlType control in Enum.GetValues<AttributeControlType>())
        {
            if (list.Cast<object>().Any(existingItem => Equals(controlProp.GetValue(existingItem), control)))
                continue;

            var item = CreateDefault(itemType);
            if (item == null)
                continue;
            controlProp.SetValue(item, control);
            itemType.GetProperty("Id")?.SetValue(item, 200 + (int)control);
            itemType.GetProperty("Name")?.SetValue(item, control.ToString());
            itemType.GetProperty("TextPrompt")?.SetValue(item, control.ToString());
            itemType.GetProperty("Description")?.SetValue(item, "coverage");
            itemType.GetProperty("ControlId")?.SetValue(item, $"attribute_{(int)control}");
            FillAttributeValues(item);
            list.Add(item);
        }
    }

    private static void FillAttributeValues(object attribute)
    {
        var valuesProp = attribute.GetType().GetProperty("Values");
        if (valuesProp?.GetValue(attribute) is not IList values)
            return;

        var itemType = valuesProp.PropertyType.IsGenericType
            ? valuesProp.PropertyType.GetGenericArguments()[0]
            : values.GetType().GetGenericArguments().FirstOrDefault();
        if (itemType == null)
            return;

        while (values.Count < 2)
        {
            var value = Activator.CreateInstance(itemType);
            if (value == null)
                break;
            itemType.GetProperty("Name")?.SetValue(value, $"value{values.Count}");
            itemType.GetProperty("PriceAdjustment")?.SetValue(value, "+$1.00");
            itemType.GetProperty("PriceAdjustmentValue")?.SetValue(value, 1m);
            itemType.GetProperty("CustomerEntersQty")?.SetValue(value, true);
            itemType.GetProperty("Quantity")?.SetValue(value, 2);
            itemType.GetProperty("IsPreSelected")?.SetValue(value, values.Count == 0);
            itemType.GetProperty("ColorSquaresRgb")?.SetValue(value, "#ff0000");
            values.Add(value);
        }
    }

    private void FillGraph(object model, int depth)
    {
        if (model == null || depth > 5)
            return;

        var type = model.GetType();
        if (ShouldSkipFill(type))
            return;

        if (model is IList rootList && !rootList.IsReadOnly)
            PopulateAttributeControlItems(rootList);

        if (model is IEnumerable enumerable and not string and not IDictionary)
        {
            foreach (var item in enumerable.Cast<object>().Take(40))
                FillGraph(item, depth + 1);
        }

        if (model is DataTablesModel tables)
            EnrichDataTablesModel(tables);

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

                    var controlProp = itemType.GetProperty("AttributeControlType");
                    if (controlProp?.PropertyType == typeof(AttributeControlType))
                    {
                        PopulateAttributeControlItems(list, itemType);
                        foreach (var existingItem in list.Cast<object>().Take(40))
                            FillGraph(existingItem, depth + 1);
                    }
                    else if (list.Count == 0)
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

        if (model is global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel shipping)
        {
            shipping.DisplayPickupInStore = true;
            shipping.NewAddressPreselected = true;
            shipping.PickupPointsModel ??= new global::Nop.Web.Models.Checkout.CheckoutPickupPointsModel();
            shipping.PickupPointsModel.AllowPickupInStore = true;
            shipping.PickupPointsModel.PickupInStore = false;
            shipping.PickupPointsModel.PickupInStoreOnly = false;
            if (shipping.PickupPointsModel.PickupPoints.Count == 0)
            {
                shipping.PickupPointsModel.PickupPoints.Add(new global::Nop.Web.Models.Checkout.CheckoutPickupPointModel
                {
                    Id = "1",
                    Name = "Warehouse",
                    Address = "1 Main",
                    PickupFee = "$0.00"
                });
            }

            if (shipping.ExistingAddresses.Count == 0)
                shipping.ExistingAddresses.Add(new global::Nop.Web.Models.Common.AddressModel { Id = 1, FirstName = "Ann", LastName = "Admin", Email = "a@b.c", PhoneEnabled = true });
            if (shipping.InvalidExistingAddresses.Count == 0)
                shipping.InvalidExistingAddresses.Add(new global::Nop.Web.Models.Common.AddressModel { Id = 2, FirstName = "Bad" });
        }

        if (model is global::Nop.Web.Models.Order.SubmitReturnRequestModel returnRequest)
        {
            if (returnRequest.Items.Count == 0)
            {
                returnRequest.Items.Add(new global::Nop.Web.Models.Order.SubmitReturnRequestModel.OrderItemModel
                {
                    Id = 1,
                    ProductName = "Coverage",
                    ProductSeName = "coverage",
                    AttributeInfo = "Size: M",
                    Quantity = 2,
                    UnitPrice = "$1.00"
                });
            }

            returnRequest.AllowFiles = true;
            if (string.IsNullOrEmpty(returnRequest.Result))
                returnRequest.Result = "submitted";
            if (returnRequest.AvailableReturnReasons.Count == 0)
                returnRequest.AvailableReturnReasons.Add(new() { Id = 1, Name = "Damaged" });
            if (returnRequest.AvailableReturnActions.Count == 0)
                returnRequest.AvailableReturnActions.Add(new() { Id = 1, Name = "Refund" });
        }

        if (model is global::Nop.Web.Areas.Admin.Models.Orders.OrderModel order)
        {
            order.AllowCustomersToSelectTaxDisplayType = true;
            order.OrderSubtotalInclTax = "$10.00";
            order.OrderSubtotalExclTax = "$8.00";
            order.OrderSubTotalDiscountInclTax = "$1.00";
            order.OrderSubTotalDiscountExclTax = "$0.80";
            order.OrderShippingInclTax = "$2.00";
            order.OrderShippingExclTax = "$1.60";
            order.PaymentMethodAdditionalFeeInclTax = "$0.50";
            order.PaymentMethodAdditionalFeeExclTax = "$0.40";
            order.CanCancelOrder = true;
            order.CanCapture = true;
            order.CanRefund = true;
        }
    }

    private static void EnrichDataTablesModel(DataTablesModel tables)
    {
        if (string.IsNullOrEmpty(tables.Name))
            tables.Name = "coverage-grid";
        tables.UrlRead = new DataUrl("List", "Product", new RouteValueDictionary());
        tables.UrlDelete = new DataUrl("~/Admin/Product/Delete", true);
        tables.UrlUpdate = new DataUrl("/Admin/Product/Update");
        tables.HeaderCallback = "function(){}";
        tables.FooterCallback = "function(){}";
        tables.DrawCallback = "function(){}";
        tables.Processing = true;
        tables.ServerSide = true;
        tables.Paging = true;
        tables.Info = true;
        tables.RefreshButton = true;
        tables.DeferLoading = 10;
        tables.RowIdBasedOnField = "Name";
        tables.Length = 15;
        tables.LengthMenu = "10,15,20";
        tables.PrimaryKeyColumn ??= "Id";
        tables.Filters =
        [
            new FilterParameter("SearchName"),
            new FilterParameter("SearchSku", "SearchModel"),
            new FilterParameter("ProductId", "Id", true),
            new FilterParameter("Published", true),
            new FilterParameter("PageSize", 15),
            new FilterParameter("CreatedOn", typeof(DateTime?))
        ];
        tables.LayoutCollection =
        [
            new LayoutOption { Name = "topStart", OptionType = LayoutOptionType.Null },
            new LayoutOption { Name = "topEnd", OptionType = LayoutOptionType.String, Value = "search" },
            new LayoutOption { Name = "bottomStart", OptionType = LayoutOptionType.Object, Value = "{}" },
            new LayoutOption { Name = "bottom", OptionType = LayoutOptionType.Array, Value = new List<string> { "paging", "info" } }
        ];
        var actionUrl = new DataUrl("Edit", "Product", new RouteValueDictionary());
        var rawUrl = new DataUrl("/Admin/Product/Edit", "Id");
        tables.ColumnCollection =
        [
            new ColumnProperty("Id") { Title = "Id", IsMasterCheckBox = true, Width = "50", Visible = true, Render = new RenderCheckBox("checkbox_products") },
            new ColumnProperty("Select") { Title = string.Empty, IsMasterCheckBox = true, Width = "20" },
            new ColumnProperty("Name") { Title = "Name", Visible = true, Searchable = true, AutoWidth = true, Encode = true, ClassName = NopColumnClassDefaults.CenterAll, Render = new RenderLink(actionUrl) { Title = "Open" } },
            new ColumnProperty("Sku") { Title = "Sku", Render = new RenderLink(rawUrl) },
            new ColumnProperty("Published") { Title = "Published", Render = new RenderBoolean() },
            new ColumnProperty("Price") { Title = "Price", Render = new RenderCustom("function(data){ return data; }") },
            new ColumnProperty("Gtin") { Title = "Gtin", Render = new RenderButtonEdit(actionUrl) },
            new ColumnProperty("ManufacturerPartNumber") { Title = "MPN", Render = new RenderButtonView(actionUrl) },
            new ColumnProperty("DisplayOrder") { Title = "Order", Render = new RenderButtonsInlineEdit() },
            new ColumnProperty("PictureUrl") { Title = "Picture", Render = new RenderPicture("PictureUrl") },
            new ColumnProperty("CreatedOn") { Title = "Created", Render = new RenderDate() },
            new ColumnProperty("Delete") { Title = "Delete", Render = new RenderButtonRemove("delete") },
            new ColumnProperty("Custom") { Title = "Custom", Render = new RenderButtonCustom("btn", "Go") { Url = "/go", OnClickFunctionName = "go" } },
            new ColumnProperty("Child") { Title = "Child", Render = new RenderChildCaret() }
        ];
        tables.ChildTable = new DataTablesModel
        {
            Name = "coverage-child",
            IsChildTable = true,
            UrlRead = new DataUrl("/coverage/child"),
            UrlUpdate = new DataUrl("/coverage/child-update"),
            ColumnCollection = [new ColumnProperty("Sku") { Title = "Sku" }],
            Filters = [new FilterParameter("ProductId", "Id", true)]
        };
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

    private static DataTablesModel CreateCoverageTableModel()
    {
        return new DataTablesModel
        {
            Name = "coverage-grid",
            UrlRead = new DataUrl("List", "Product", new RouteValueDictionary()),
            UrlDelete = new DataUrl("/coverage/delete", "id"),
            UrlUpdate = new DataUrl("/coverage/update", true),
            SearchButtonId = "search",
            Length = 15,
            LengthMenu = "10,15,20",
            RefreshButton = true,
            ServerSide = true,
            Paging = true,
            Info = true,
            ColumnCollection =
            [
                new ColumnProperty("Name") { Title = "Name", Visible = true, Searchable = true, AutoWidth = true },
                new ColumnProperty("Id") { Title = "Id", IsMasterCheckBox = true, Width = "50" }
            ],
            Filters = [new FilterParameter("Name")]
        };
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
        private ViewDataDictionary _viewData;

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

        private ViewDataDictionary ViewData =>
            ViewContext?.ViewData
            ?? (_viewData ??= new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()));

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod == null)
                return null;

            var name = targetMethod.Name;
            if (name == "get_ViewContext" || name == "get_ActionContext")
                return ViewContext;
            if (name == "get_ViewData")
                return ViewData;
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

    private sealed class CoverageTagHelperFactory : ITagHelperFactory
    {
        public TTagHelper CreateTagHelper<TTagHelper>(ViewContext context) where TTagHelper : ITagHelper
        {
            var services = context?.HttpContext?.RequestServices;
            TTagHelper helper;
            try
            {
                helper = services != null
                    ? ActivatorUtilities.CreateInstance<TTagHelper>(services)
                    : (TTagHelper)Activator.CreateInstance(typeof(TTagHelper));
            }
            catch
            {
                helper = (TTagHelper)RuntimeHelpers.GetUninitializedObject(typeof(TTagHelper));
            }

            if (helper is IViewContextAware aware && context != null)
            {
                try { aware.Contextualize(context); }
                catch { }
            }

            return helper;
        }
    }

    private sealed class CoverageAntiforgery : IAntiforgery
    {
        private static AntiforgeryTokenSet Tokens()
            => new("coverage-token", "coverage-cookie", "__RequestVerificationToken", "RequestVerificationToken");

        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => Tokens();
        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => Tokens();
        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
        public void SetCookieTokenAndHeader(HttpContext httpContext) { }
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
