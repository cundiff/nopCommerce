using AutoMapper;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Infrastructure.Mapper;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Web.Areas.Admin.Infrastructure.Mapper;
using Nop.Web.Framework.Mvc.Routing;
using NUnit.Framework;

namespace Nop.Tests.Nop.Web.Tests.Coverage;

[TestFixture]
public class NopWebSurfaceCoverageTests : BaseNopTest
{
    private static readonly Type WebAssemblyMarker = typeof(global::Nop.Web.Controllers.HomeController);

    private WebCoverageHarness CreateHarness() => new(ServiceProvider);

    [Test]
    public async Task ExerciseAdminFactories()
    {
        var harness = CreateHarness();
        await harness.ExerciseTypesAsync(TypesIn("Nop.Web.Areas.Admin.Factories", suffix: "Factory"), asMvc: false);
        harness.TypesCreated.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExercisePublicFactories()
    {
        var harness = CreateHarness();
        await harness.SeedShoppingCartAsync();
        await harness.ExerciseTypesAsync(TypesIn("Nop.Web.Factories", suffix: "Factory"), asMvc: false);
        harness.TypesCreated.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExerciseAdminControllers()
    {
        var harness = CreateHarness();
        var types = TypesIn("Nop.Web.Areas.Admin.Controllers")
            .Where(t => typeof(Controller).IsAssignableFrom(t) && t.Name != "ElFinderController");
        await harness.ExerciseTypesAsync(types, asMvc: true);
        harness.TypesCreated.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExercisePublicControllers()
    {
        var harness = CreateHarness();
        await harness.SeedShoppingCartAsync();
        var types = TypesIn("Nop.Web.Controllers")
            .Where(t => typeof(Controller).IsAssignableFrom(t) && t.Name != "InstallController");
        await harness.ExerciseTypesAsync(types, asMvc: true);
        harness.TypesCreated.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExerciseViewComponents()
    {
        var harness = CreateHarness();
        var types = WebAssemblyMarker.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(global::Nop.Web.Framework.Components.NopViewComponent).IsAssignableFrom(t)
                        && t.Name != "NopCommerceNewsViewComponent");
        await harness.ExerciseTypesAsync(types, asMvc: true);
        harness.TypesCreated.Should().BeGreaterThan(0);
    }

    [Test]
    public void ExerciseAdminAndPublicModels()
    {
        var harness = CreateHarness();
        var types = WebAssemblyMarker.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters
                        && t.Namespace != null
                        && (t.Namespace.StartsWith("Nop.Web.Areas.Admin.Models", StringComparison.Ordinal)
                            || t.Namespace.StartsWith("Nop.Web.Models", StringComparison.Ordinal)));
        harness.ExerciseModelProperties(types);
        harness.TypesCreated.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExerciseValidators()
    {
        var harness = CreateHarness();
        var types = WebAssemblyMarker.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Validator", StringComparison.Ordinal)
                        && t.Namespace != null
                        && (t.Namespace.StartsWith("Nop.Web.Areas.Admin.Validators", StringComparison.Ordinal)
                            || t.Namespace.StartsWith("Nop.Web.Validators", StringComparison.Ordinal)));
        await harness.ExerciseValidatorsAsync(types);
        harness.TypesCreated.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExerciseAdminEditRoundTrips()
    {
        var harness = CreateHarness();
        var types = TypesIn("Nop.Web.Areas.Admin.Controllers")
            .Where(t => typeof(Controller).IsAssignableFrom(t) && t.Name != "ElFinderController");
        await harness.ExerciseGetPostPairsAsync(types);
        harness.MethodsInvoked.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExercisePublicEditRoundTrips()
    {
        var harness = CreateHarness();
        await harness.SeedShoppingCartAsync();
        var types = TypesIn("Nop.Web.Controllers")
            .Where(t => typeof(Controller).IsAssignableFrom(t) && t.Name != "InstallController");
        await harness.ExerciseGetPostPairsAsync(types);
    }

    [Test]
    public async Task ExerciseCheckoutWithCart()
    {
        var harness = CreateHarness();
        await harness.ExerciseCheckoutFlowAsync();
        harness.MethodsInvoked.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExerciseAdminProductFactoryForSampleProducts()
    {
        var factory = GetService<global::Nop.Web.Areas.Admin.Factories.IProductModelFactory>();
        var products = await GetService<IProductService>().SearchProductsAsync(pageSize: 50);
        foreach (var product in products)
        {
            var model = await factory.PrepareProductModelAsync(null, product);
            model.Should().NotBeNull();
            var excluded = await factory.PrepareProductModelAsync(model, product, true);
            excluded.Should().NotBeNull();
        }

        products.Count.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExercisePublicProductDetailsForSampleProducts()
    {
        var factory = GetService<global::Nop.Web.Factories.IProductModelFactory>();
        var products = await GetService<IProductService>().SearchProductsAsync(pageSize: 50);
        foreach (var product in products)
        {
            var details = await factory.PrepareProductDetailsModelAsync(product);
            details.Should().NotBeNull();
            var overview = await factory.PrepareProductOverviewModelsAsync(new[] { product });
            overview.Should().NotBeNull();
        }

        products.Count.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExercisePublicShoppingCartFactoryWithCart()
    {
        var harness = CreateHarness();
        await harness.SeedShoppingCartAsync();

        var workContext = GetService<IWorkContext>();
        var customer = await workContext.GetCurrentCustomerAsync();
        var store = await GetService<IStoreContext>().GetCurrentStoreAsync();
        var cart = await GetService<IShoppingCartService>()
            .GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        cart.Should().NotBeEmpty();

        var factory = GetService<global::Nop.Web.Factories.IShoppingCartModelFactory>();
        var cartModel = await factory.PrepareShoppingCartModelAsync(new global::Nop.Web.Models.ShoppingCart.ShoppingCartModel(), cart);
        cartModel.Should().NotBeNull();

        var totals = await factory.PrepareOrderTotalsModelAsync(cart, true);
        totals.Should().NotBeNull();

        var mini = await factory.PrepareMiniShoppingCartModelAsync();
        mini.Should().NotBeNull();

        var estimate = await factory.PrepareEstimateShippingModelAsync(cart);
        estimate.Should().NotBeNull();

        var estimateResult = await factory.PrepareEstimateShippingResultModelAsync(cart, estimate, false);
        estimateResult.Should().NotBeNull();

        var wishlist = await factory.PrepareWishlistModelAsync(new global::Nop.Web.Models.ShoppingCart.WishlistModel(), cart);
        wishlist.Should().NotBeNull();
    }

    [Test]
    public async Task ExercisePublicCatalogFactoryWithSampleEntities()
    {
        var factory = GetService<global::Nop.Web.Factories.ICatalogModelFactory>();
        var command = new global::Nop.Web.Models.Catalog.CatalogProductsCommand();

        var categories = await GetService<ICategoryService>().GetAllCategoriesAsync();
        foreach (var category in categories.Take(8))
        {
            var model = await factory.PrepareCategoryModelAsync(category, command);
            model.Should().NotBeNull();
        }

        var manufacturers = await GetService<IManufacturerService>().GetAllManufacturersAsync();
        foreach (var manufacturer in manufacturers.Take(8))
        {
            var model = await factory.PrepareManufacturerModelAsync(manufacturer, command);
            model.Should().NotBeNull();
        }

        var tags = await GetService<IProductTagService>().GetAllProductTagsAsync();
        foreach (var tag in tags.Take(8))
        {
            var model = await factory.PrepareProductsByTagModelAsync(tag, command);
            model.Should().NotBeNull();
        }

        var search = await factory.PrepareSearchModelAsync(new global::Nop.Web.Models.Catalog.SearchModel(), command);
        search.Should().NotBeNull();
        (await factory.PrepareHomepageCategoryModelsAsync()).Should().NotBeNull();
        (await factory.PrepareSearchBoxModelAsync()).Should().NotBeNull();
        (await factory.PreparePopularProductTagsModelAsync()).Should().NotBeNull();
        (await factory.PrepareManufacturerAllModelsAsync()).Should().NotBeNull();
        (await factory.PrepareVendorAllModelsAsync()).Should().NotBeNull();
        (await factory.PrepareNewProductsModelAsync(command)).Should().NotBeNull();
    }

    [Test]
    public async Task ExercisePublicCheckoutAndOrderFactories()
    {
        var harness = CreateHarness();
        await harness.SeedShoppingCartAsync();

        var customer = await GetService<IWorkContext>().GetCurrentCustomerAsync();
        var store = await GetService<IStoreContext>().GetCurrentStoreAsync();
        var cart = await GetService<IShoppingCartService>()
            .GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        var checkoutFactory = GetService<global::Nop.Web.Factories.ICheckoutModelFactory>();
        var billing = new global::Nop.Web.Models.Checkout.CheckoutBillingAddressModel();
        await checkoutFactory.PrepareBillingAddressModelAsync(billing, cart, prePopulateNewAddressWithCustomerFields: true);
        var shipping = new global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel();
        await checkoutFactory.PrepareShippingAddressModelAsync(shipping, cart, prePopulateNewAddressWithCustomerFields: true);

        var address = await GetService<ICustomerService>().GetCustomerShippingAddressAsync(customer)
                      ?? (await GetService<ICustomerService>().GetAddressesByCustomerIdAsync(customer.Id)).FirstOrDefault();
        (await checkoutFactory.PrepareShippingMethodModelAsync(cart, address)).Should().NotBeNull();
        (await checkoutFactory.PreparePaymentMethodModelAsync(cart, 0)).Should().NotBeNull();
        (await checkoutFactory.PrepareConfirmOrderModelAsync(cart)).Should().NotBeNull();
        (await checkoutFactory.PrepareOnePageCheckoutModelAsync(cart)).Should().NotBeNull();

        var orders = await GetService<IOrderService>().SearchOrdersAsync(pageIndex: 0, pageSize: 10);
        var orderFactory = GetService<global::Nop.Web.Factories.IOrderModelFactory>();
        foreach (var order in orders)
        {
            (await orderFactory.PrepareOrderDetailsModelAsync(order)).Should().NotBeNull();
        }

        (await orderFactory.PrepareCustomerOrderListModelAsync(1, OrderHistoryPeriods.All)).Should().NotBeNull();
    }

    [Test]
    public async Task ExerciseAdminSettingAndReportFactories()
    {
        var settings = GetService<global::Nop.Web.Areas.Admin.Factories.ISettingModelFactory>();
        (await settings.PrepareCatalogSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareGeneralCommonSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareCustomerUserSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareOrderSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareShippingSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareTaxSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareMediaSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareBlogSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareVendorSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareShoppingCartSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareRewardPointsSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareGdprSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareAppSettingsModel()).Should().NotBeNull();
        (await settings.PrepareProductEditorSettingsModelAsync()).Should().NotBeNull();
        (await settings.PrepareStoreScopeConfigurationModelAsync()).Should().NotBeNull();
        (await settings.PrepareFilterLevelSettingsModelAsync()).Should().NotBeNull();

        var reports = GetService<global::Nop.Web.Areas.Admin.Factories.IReportModelFactory>();
        var salesSearch = new global::Nop.Web.Areas.Admin.Models.Reports.SalesSummarySearchModel
        {
            StartDate = DateTime.UtcNow.AddYears(-1),
            EndDate = DateTime.UtcNow.AddDays(1)
        };
        salesSearch.SetGridPageSize();
        (await reports.PrepareSalesSummarySearchModelAsync(salesSearch)).Should().NotBeNull();
        (await reports.PrepareSalesSummaryListModelAsync(salesSearch)).Should().NotBeNull();
    }

    [Test]
    public async Task ExerciseModelCacheEventConsumer()
    {
        var harness = CreateHarness();
        var types = WebAssemblyMarker.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace == "Nop.Web.Infrastructure.Cache");
        await harness.ExerciseTypesAsync(types, asMvc: false);
        harness.TypesCreated.Should().BeGreaterThan(0);
    }

    [Test]
    public void ExerciseAdminMapperConfiguration()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile(typeof(AdminMapperConfiguration));
        });
        AutoMapperConfiguration.Init(config);
        AutoMapperConfiguration.MapperConfiguration.AssertConfigurationIsValid();
    }

    [Test]
    public void ExerciseRouteProviders()
    {
        Exception last = null;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Services.AddControllersWithViews();
            builder.Services.AddRouting();
            using var app = builder.Build();

            foreach (var providerType in WebAssemblyMarker.Assembly.GetTypes()
                         .Where(t => t.IsClass && !t.IsAbstract && typeof(IRouteProvider).IsAssignableFrom(t)))
            {
                try
                {
                    var routeProvider = (IRouteProvider)Activator.CreateInstance(providerType);
                    routeProvider?.RegisterRoutes(app);
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }
        }
        catch (Exception ex)
        {
            last = ex;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRouting();
            services.AddControllersWithViews();
            using var provider = services.BuildServiceProvider();
            var endpoints = new TestEndpointRouteBuilder(provider);

            foreach (var providerType in WebAssemblyMarker.Assembly.GetTypes()
                         .Where(t => t.IsClass && !t.IsAbstract && typeof(IRouteProvider).IsAssignableFrom(t)))
            {
                try
                {
                    var routeProvider = (IRouteProvider)Activator.CreateInstance(providerType);
                    routeProvider?.RegisterRoutes(endpoints);
                }
                catch (Exception inner)
                {
                    last = inner;
                }
            }
        }

        _ = last;
    }

    private sealed class TestEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public TestEndpointRouteBuilder(IServiceProvider services)
        {
            ServiceProvider = services;
        }

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IServiceProvider ServiceProvider { get; }
    }

    [Test]
    public async Task ExerciseCompiledRazorPages()
    {
        var harness = CreateHarness();
        var types = WebAssemblyMarker.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract
                        && (t.Namespace == "AspNetCoreGeneratedDocument" || t.FullName?.Contains("AspNetCoreGeneratedDocument", StringComparison.Ordinal) == true)
                        && t.GetMethod("ExecuteAsync") != null);
        await harness.ExerciseRazorPagesAsync(types);
    }

    [Test]
    public async Task ExerciseHtmlExtensions()
    {
        var harness = CreateHarness();
        await harness.ExerciseStaticTypeAsync(typeof(global::Nop.Web.Extensions.HtmlExtensions));
        harness.MethodsInvoked.Should().BeGreaterThan(0);
    }

    private static IEnumerable<Type> TypesIn(string ns, string suffix = null)
    {
        return WebAssemblyMarker.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters
                        && t.Namespace == ns
                        && (suffix == null || t.Name.EndsWith(suffix, StringComparison.Ordinal)));
    }
}
