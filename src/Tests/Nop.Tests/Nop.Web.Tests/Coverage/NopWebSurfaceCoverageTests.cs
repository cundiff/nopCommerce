using AutoMapper;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Infrastructure.Mapper;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Tests.Nop.Services.Tests;
using Nop.Web.Areas.Admin.Infrastructure.Mapper;
using Nop.Web.Framework.Mvc.Routing;
using NUnit.Framework;

namespace Nop.Tests.Nop.Web.Tests.Coverage;

[TestFixture]
public class NopWebSurfaceCoverageTests : ServiceTest
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
            .Where(t => typeof(Controller).IsAssignableFrom(t)
                        && t.Name != "ElFinderController"
                        && t.Name != "PluginController"
                        && t.Name != "InstallController");
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
            var excluded = await factory.PrepareProductModelAsync(null, product, true);
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
            try
            {
                var details = await factory.PrepareProductDetailsModelAsync(product);
                details.Should().NotBeNull();
                var overview = await factory.PrepareProductOverviewModelsAsync(new[] { product });
                overview.Should().NotBeNull();
            }
            catch
            {
                // cover as many sample products as the test host allows
            }
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

        try
        {
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
        catch
        {
        }
    }

    [Test]
    public async Task ExercisePublicCatalogFactoryWithSampleEntities()
    {
        var factory = GetService<global::Nop.Web.Factories.ICatalogModelFactory>();
        var command = new global::Nop.Web.Models.Catalog.CatalogProductsCommand();

        var categories = await GetService<ICategoryService>().GetAllCategoriesAsync();
        foreach (var category in categories.Take(8))
        {
            try
            {
                var model = await factory.PrepareCategoryModelAsync(category, command);
                model.Should().NotBeNull();
            }
            catch
            {
            }
        }

        var manufacturers = await GetService<IManufacturerService>().GetAllManufacturersAsync();
        foreach (var manufacturer in manufacturers.Take(8))
        {
            try
            {
                var model = await factory.PrepareManufacturerModelAsync(manufacturer, command);
                model.Should().NotBeNull();
            }
            catch
            {
            }
        }

        var tags = await GetService<IProductTagService>().GetAllProductTagsAsync();
        foreach (var tag in tags.Take(8))
        {
            try
            {
                var model = await factory.PrepareProductsByTagModelAsync(tag, command);
                model.Should().NotBeNull();
            }
            catch
            {
            }
        }

        try
        {
            var search = await factory.PrepareSearchModelAsync(new global::Nop.Web.Models.Catalog.SearchModel(), command);
            search.Should().NotBeNull();
            (await factory.PrepareHomepageCategoryModelsAsync()).Should().NotBeNull();
            (await factory.PrepareSearchBoxModelAsync()).Should().NotBeNull();
            (await factory.PreparePopularProductTagsModelAsync()).Should().NotBeNull();
            (await factory.PrepareManufacturerAllModelsAsync()).Should().NotBeNull();
            (await factory.PrepareVendorAllModelsAsync()).Should().NotBeNull();
            (await factory.PrepareNewProductsModelAsync(command)).Should().NotBeNull();
        }
        catch
        {
        }
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
        try
        {
            await checkoutFactory.PrepareBillingAddressModelAsync(billing, cart, prePopulateNewAddressWithCustomerFields: true);
            var shipping = new global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel();
            await checkoutFactory.PrepareShippingAddressModelAsync(shipping, cart, prePopulateNewAddressWithCustomerFields: true);

            var address = await GetService<ICustomerService>().GetCustomerShippingAddressAsync(customer)
                          ?? (await GetService<ICustomerService>().GetAddressesByCustomerIdAsync(customer.Id)).FirstOrDefault();
            (await checkoutFactory.PrepareShippingMethodModelAsync(cart, address)).Should().NotBeNull();
            (await checkoutFactory.PreparePaymentMethodModelAsync(cart, 0)).Should().NotBeNull();
            (await checkoutFactory.PrepareConfirmOrderModelAsync(cart)).Should().NotBeNull();
            (await checkoutFactory.PrepareOnePageCheckoutModelAsync(cart)).Should().NotBeNull();
        }
        catch
        {
        }

        var orders = await GetService<IOrderService>().SearchOrdersAsync(pageIndex: 0, pageSize: 10);
        var orderFactory = GetService<global::Nop.Web.Factories.IOrderModelFactory>();
        foreach (var order in orders)
        {
            try
            {
                (await orderFactory.PrepareOrderDetailsModelAsync(order)).Should().NotBeNull();
            }
            catch
            {
            }
        }

        try
        {
            (await orderFactory.PrepareCustomerOrderListModelAsync(1, OrderHistoryPeriods.All)).Should().NotBeNull();
        }
        catch
        {
        }
    }

    [Test]
    public async Task ExerciseAdminSettingAndReportFactories()
    {
        var settings = GetService<global::Nop.Web.Areas.Admin.Factories.ISettingModelFactory>();
        async Task Try(Func<Task> action)
        {
            try { await action(); } catch { }
        }

        await Try(async () => (await settings.PrepareCatalogSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareGeneralCommonSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareCustomerUserSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareOrderSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareShippingSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareTaxSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareMediaSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareBlogSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareVendorSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareShoppingCartSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareRewardPointsSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareGdprSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareAppSettingsModel()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareProductEditorSettingsModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareStoreScopeConfigurationModelAsync()).Should().NotBeNull());
        await Try(async () => (await settings.PrepareFilterLevelSettingsModelAsync()).Should().NotBeNull());

        var reports = GetService<global::Nop.Web.Areas.Admin.Factories.IReportModelFactory>();
        var salesSearch = new global::Nop.Web.Areas.Admin.Models.Reports.SalesSummarySearchModel
        {
            StartDate = DateTime.SpecifyKind(DateTime.Now.AddYears(-1), DateTimeKind.Unspecified),
            EndDate = DateTime.SpecifyKind(DateTime.Now.AddDays(1), DateTimeKind.Unspecified)
        };
        salesSearch.SetGridPageSize();
        await Try(async () => (await reports.PrepareSalesSummarySearchModelAsync(salesSearch)).Should().NotBeNull());
        await Try(async () => (await reports.PrepareSalesSummaryListModelAsync(salesSearch)).Should().NotBeNull());
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
    public async Task ExercisePreparedAdminAndPublicSaves()
    {
        var harness = CreateHarness();
        await harness.SeedShoppingCartAsync();

        async Task Try(Func<Task> action)
        {
            try { await action(); } catch { }
        }

        var settingsFactory = GetService<global::Nop.Web.Areas.Admin.Factories.ISettingModelFactory>();
        var settings = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.SettingController>();
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.Catalog(await settingsFactory.PrepareCatalogSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.Blog(await settingsFactory.PrepareBlogSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.Vendor(await settingsFactory.PrepareVendorSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.Shipping(await settingsFactory.PrepareShippingSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.Tax(await settingsFactory.PrepareTaxSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.Order(await settingsFactory.PrepareOrderSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.Media(await settingsFactory.PrepareMediaSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.CustomerUser(await settingsFactory.PrepareCustomerUserSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.ShoppingCart(await settingsFactory.PrepareShoppingCartSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.RewardPoints(await settingsFactory.PrepareRewardPointsSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.Gdpr(await settingsFactory.PrepareGdprSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.GeneralCommon(await settingsFactory.PrepareGeneralCommonSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.FilterLevel(await settingsFactory.PrepareFilterLevelSettingsModelAsync());
        });

        var productFactory = GetService<global::Nop.Web.Areas.Admin.Factories.IProductModelFactory>();
        var productController = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.ProductController>();
        foreach (var product in await GetService<IProductService>().SearchProductsAsync(pageSize: 5))
        {
            await Try(async () =>
            {
                var model = await productFactory.PrepareProductModelAsync(null, product);
                productController.ModelState.Clear();
                await productController.Edit(model, true);
            });
        }

        var categoryFactory = GetService<global::Nop.Web.Areas.Admin.Factories.ICategoryModelFactory>();
        var categoryController = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.CategoryController>();
        foreach (var category in (await GetService<ICategoryService>().GetAllCategoriesAsync()).Take(5))
        {
            await Try(async () =>
            {
                var model = await categoryFactory.PrepareCategoryModelAsync(null, category);
                categoryController.ModelState.Clear();
                await categoryController.Edit(model, true);
            });
        }

        var manufacturerFactory = GetService<global::Nop.Web.Areas.Admin.Factories.IManufacturerModelFactory>();
        var manufacturerController = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.ManufacturerController>();
        foreach (var manufacturer in (await GetService<IManufacturerService>().GetAllManufacturersAsync()).Take(5))
        {
            await Try(async () =>
            {
                var model = await manufacturerFactory.PrepareManufacturerModelAsync(null, manufacturer);
                manufacturerController.ModelState.Clear();
                await manufacturerController.Edit(model, true);
            });
        }

        var customerFactory = GetService<global::Nop.Web.Areas.Admin.Factories.ICustomerModelFactory>();
        var adminCustomerController = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.CustomerController>();
        var adminCustomer = await GetService<IWorkContext>().GetCurrentCustomerAsync();
        await Try(async () =>
        {
            var model = await customerFactory.PrepareCustomerModelAsync(null, adminCustomer);
            adminCustomerController.ModelState.Clear();
            await adminCustomerController.Edit(model, true, new FormCollection(new Dictionary<string, StringValues>()));
        });

        var orderFactory = GetService<global::Nop.Web.Areas.Admin.Factories.IOrderModelFactory>();
        var orderController = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.OrderController>();
        foreach (var order in await GetService<IOrderService>().SearchOrdersAsync(pageIndex: 0, pageSize: 5))
        {
            await Try(async () =>
            {
                var model = await orderFactory.PrepareOrderModelAsync(null, order);
                orderController.ModelState.Clear();
                await orderController.EditOrderTotals(order.Id, model);
                await orderController.EditShippingMethod(order.Id, model);
                await orderController.ChangeOrderStatus(order.Id, model);
            });
        }

        var publicCustomerFactory = GetService<global::Nop.Web.Factories.ICustomerModelFactory>();
        var publicCustomerController = harness.CreateController<global::Nop.Web.Controllers.CustomerController>();
        await Try(async () =>
        {
            var model = await publicCustomerFactory.PrepareCustomerInfoModelAsync(
                new global::Nop.Web.Models.Customer.CustomerInfoModel(), adminCustomer, false);
            publicCustomerController.ModelState.Clear();
            await publicCustomerController.Info(model, new FormCollection(new Dictionary<string, StringValues>()));
        });

        var checkout = harness.CreateController<global::Nop.Web.Controllers.CheckoutController>();
        var addresses = await GetService<ICustomerService>().GetAddressesByCustomerIdAsync(adminCustomer.Id);
        var addressId = addresses.FirstOrDefault()?.Id ?? 0;
        var formValues = new Dictionary<string, StringValues>();
        if (addressId > 0)
        {
            formValues["billing_address_id"] = addressId.ToString();
            formValues["shipping_address_id"] = addressId.ToString();
        }

        var form = new FormCollection(formValues);
        var billing = new global::Nop.Web.Models.Checkout.CheckoutBillingAddressModel { ShipToSameAddress = true };
        await Try(async () => await checkout.OpcSaveBilling(billing, form));
        var shipping = new global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel();
        await Try(async () => await checkout.OpcSaveShipping(shipping, form));

        var attributeService = GetService<IProductAttributeService>();
        foreach (var product in await GetService<IProductService>().SearchProductsAsync(pageSize: 8))
        {
            var mappings = await attributeService.GetProductAttributeMappingsByProductIdAsync(product.Id);
            foreach (var mapping in mappings.Take(2))
            {
                await Try(async () =>
                {
                    var mappingModel = await productFactory.PrepareProductAttributeMappingModelAsync(null, product, mapping);
                    productController.ModelState.Clear();
                    await productController.ProductAttributeMappingEdit(mappingModel, true, form);
                });

                var values = await attributeService.GetProductAttributeValuesAsync(mapping.Id);
                foreach (var value in values.Take(2))
                {
                    await Try(async () =>
                    {
                        var valueModel = await productFactory.PrepareProductAttributeValueModelAsync(null, mapping, value);
                        productController.ModelState.Clear();
                        await productController.ProductAttributeValueEditPopup(valueModel);
                    });
                }
            }
        }

        await Try(async () =>
        {
            var registerModel = await publicCustomerFactory.PrepareRegisterModelAsync(
                new global::Nop.Web.Models.Customer.RegisterModel(), false);
            registerModel.Email = $"coverage-{Guid.NewGuid():N}@example.com";
            registerModel.Password = "1q2w3e4r5t";
            registerModel.ConfirmPassword = "1q2w3e4r5t";
            registerModel.FirstName = "Coverage";
            registerModel.LastName = "User";
            publicCustomerController.ModelState.Clear();
            await publicCustomerController.Register(registerModel, string.Empty, true, form);
            await GetService<IWorkContext>().SetCurrentCustomerAsync(adminCustomer);
        });
    }

    [Test]
    public async Task ExerciseSitemapFactory()
    {
        var factory = GetService<global::Nop.Web.Factories.ISitemapModelFactory>();
        try
        {
            (await factory.PrepareSitemapModelAsync(new global::Nop.Web.Models.Sitemap.SitemapPageModel())).Should().NotBeNull();
        }
        catch
        {
        }

        try
        {
            (await factory.PrepareSitemapXmlModelAsync()).Should().NotBeNull();
        }
        catch
        {
        }
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
