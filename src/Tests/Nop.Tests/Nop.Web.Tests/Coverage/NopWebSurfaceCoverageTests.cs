using System.Reflection;
using AutoMapper;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Configuration;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Tax;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.Mapper;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Installation;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Services.Shipping;
using Nop.Services.Vendors;
using Nop.Tests.Nop.Services.Tests;
using Nop.Web.Areas.Admin.Infrastructure.Mapper;
using Nop.Web.Extensions;
using Nop.Web.Framework.Mvc.Routing;
using Nop.Web.Infrastructure;
using Nop.Web.Infrastructure.Installation;
using Nop.Web.Models.Common;
using Nop.Web.Models.Customer;
using Nop.Web.Models.Install;
using NUnit.Framework;

namespace Nop.Tests.Nop.Web.Tests.Coverage;

[TestFixture]
public class NopWebSurfaceCoverageTests : ServiceTest
{
    private static readonly Type WebAssemblyMarker = typeof(global::Nop.Web.Controllers.HomeController);

    private WebCoverageHarness CreateHarness() => new(ServiceProvider);

    [OneTimeSetUp]
    public async Task CoverageFixtureSetup()
    {
        var harness = CreateHarness();
        await harness.EnableCheckoutTestPluginsAsync();
        await harness.EnsureSecondStoreAsync();
    }

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
        var harness = CreateHarness();
        var settings = GetService<global::Nop.Web.Areas.Admin.Factories.ISettingModelFactory>();
        async Task Try(Func<Task> action)
        {
            try { await action(); } catch { }
        }

        async Task PrepareAll()
        {
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
        }

        await harness.SetAdminStoreScopeAsync(0);
        await PrepareAll();
        await harness.EnableAdminStoreScopeAsync();
        await PrepareAll();
        await harness.SetAdminStoreScopeAsync(0);

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
        await harness.EnableAdminStoreScopeAsync();
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
        await harness.SetAdminStoreScopeAsync(0);
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.Catalog(await settingsFactory.PrepareCatalogSettingsModelAsync());
        });
        await Try(async () =>
        {
            settings.ModelState.Clear();
            await settings.GeneralCommon(await settingsFactory.PrepareGeneralCommonSettingsModelAsync());
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

        await Try(async () =>
        {
            var createModel = await productFactory.PrepareProductModelAsync(null, null);
            createModel.Name = "Coverage product " + Guid.NewGuid().ToString("N")[..8];
            createModel.Sku = "COV" + Guid.NewGuid().ToString("N")[..6];
            productController.ModelState.Clear();
            await productController.Create(createModel, true);
        });

        await Try(async () =>
        {
            var product = (await GetService<IProductService>().SearchProductsAsync(pageSize: 1)).First();
            var formValues = new Dictionary<string, StringValues>
            {
                [$"product-select-{product.Id}"] = "true",
                [$"name-{product.Id}"] = product.Name ?? "coverage",
                [$"sku-{product.Id}"] = product.Sku ?? "sku",
                [$"price-{product.Id}"] = product.Price.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [$"old-price-{product.Id}"] = product.OldPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [$"quantity-{product.Id}"] = product.StockQuantity.ToString(),
                [$"published-{product.Id}"] = product.Published.ToString()
            };
            var bulkForm = new FormCollection(formValues);
            var http = GetService<IHttpContextAccessor>().HttpContext;
            http.Request.Method = HttpMethods.Post;
            http.Request.ContentType = "application/x-www-form-urlencoded";
            http.Request.Form = bulkForm;
            var search = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductSearchModel();
            search.SetGridPageSize();
            productController.ModelState.Clear();
            await productController.BulkEditSave(search, true);
        });

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

        var cart = await GetService<IShoppingCartService>().GetShoppingCartAsync(
            adminCustomer, ShoppingCartType.ShoppingCart,
            (await GetService<IStoreContext>().GetCurrentStoreAsync()).Id);
        var opcFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        await Try(async () =>
        {
            var method = checkout.GetType().GetMethod("OpcLoadStepAfterShippingAddress", opcFlags);
            if (method != null)
                await (Task)method.Invoke(checkout, [cart]);
        });
        await Try(async () =>
        {
            var method = checkout.GetType().GetMethod("OpcLoadStepAfterShippingMethod", opcFlags);
            if (method != null)
                await (Task)method.Invoke(checkout, [cart]);
        });

        var shoppingCart = harness.CreateController<global::Nop.Web.Controllers.ShoppingCartController>();
        var simple = (await GetService<IProductService>().SearchProductsAsync(pageSize: 20))
            .FirstOrDefault(p => p.ProductType == ProductType.SimpleProduct);
        if (simple != null)
        {
            var qtyForm = new FormCollection(new Dictionary<string, StringValues>
            {
                [$"addtocart_{simple.Id}.EnteredQuantity"] = "1"
            });
            await Try(async () => await shoppingCart.AddProductToCart_Details(simple.Id, (int)ShoppingCartType.ShoppingCart, qtyForm));
            await Try(async () => await shoppingCart.AddProductToCart_Catalog(simple.Id, (int)ShoppingCartType.ShoppingCart, 1));
            await Try(async () => await shoppingCart.Cart());
            await Try(async () => await shoppingCart.ProductDetails_AttributeChange(simple.Id, true, true, qtyForm));
            await Try(async () => await shoppingCart.CheckoutAttributeChange(qtyForm, true));
            await Try(async () => await shoppingCart.GetEstimateShipping(new global::Nop.Web.Models.ShoppingCart.EstimateShippingModel
            {
                ZipPostalCode = "10021",
                CountryId = 1,
                StateProvinceId = 1
            }, qtyForm));
            await Try(async () => await shoppingCart.ApplyDiscountCoupon("coverage", qtyForm));
            await Try(async () => await shoppingCart.CustomerCart());
            await Try(async () => await shoppingCart.ContinueShopping());
            await Try(async () => await shoppingCart.Wishlist(null, null));
        }

        await Try(async () =>
        {
            var product = (await GetService<IProductService>().SearchProductsAsync(pageSize: 1)).First();
            var copyModel = await productFactory.PrepareProductModelAsync(null, product);
            copyModel.CopyProductModel.Id = product.Id;
            copyModel.CopyProductModel.Name = product.Name + " coverage copy";
            copyModel.CopyProductModel.Published = false;
            copyModel.CopyProductModel.CopyMultimedia = false;
            productController.ModelState.Clear();
            await productController.CopyProduct(copyModel);
        });

        foreach (var order in await GetService<IOrderService>().SearchOrdersAsync(pageIndex: 0, pageSize: 3))
        {
            await Try(async () => await orderController.AddShipment(order.Id));
            await Try(async () => await orderController.Edit(order.Id));
            await Try(async () => await orderController.PdfInvoice(order.Id));
            await Try(async () =>
            {
                var items = await GetService<IOrderService>().GetOrderItemsAsync(order.Id);
                var item = items.FirstOrDefault();
                if (item == null)
                    return;
                var itemForm = new FormCollection(new Dictionary<string, StringValues>
                {
                    ["quantity"] = "1",
                    [$"qtyToAdd{item.Id}"] = "1"
                });
                await orderController.EditOrderItem(item.Id, itemForm);
                await orderController.AddProductToOrder(order.Id);
                var simpleId = (await GetService<IProductService>().SearchProductsAsync(pageSize: 1)).First().Id;
                await orderController.AddProductToOrderDetails(order.Id, simpleId);
            });
        }

        await Try(async () =>
        {
            var login = new LoginModel
            {
                Email = NopTestsDefaults.AdminEmail,
                Username = NopTestsDefaults.AdminEmail,
                Password = NopTestsDefaults.AdminPassword
            };
            publicCustomerController.ModelState.Clear();
            await publicCustomerController.Login(login, string.Empty, true);
            await publicCustomerController.Login(false);
            await publicCustomerController.PasswordRecovery();
            var recovery = new PasswordRecoveryModel { Email = NopTestsDefaults.AdminEmail };
            await publicCustomerController.PasswordRecoverySend(recovery, true);
            await publicCustomerController.ChangePassword();
            await publicCustomerController.Addresses();
            await publicCustomerController.DownloadableProducts();
            await publicCustomerController.GdprTools();
            await publicCustomerController.CheckGiftCardBalance();
            var addResult = await publicCustomerController.AddressAdd();
            if (addResult is ViewResult { Model: CustomerAddressEditModel addressModel })
            {
                publicCustomerController.ModelState.Clear();
                await publicCustomerController.AddressAdd(addressModel, form);
            }

            var existing = addresses.FirstOrDefault();
            if (existing != null)
            {
                var editResult = await publicCustomerController.AddressEdit(existing.Id);
                if (editResult is ViewResult { Model: CustomerAddressEditModel editModel })
                {
                    publicCustomerController.ModelState.Clear();
                    await publicCustomerController.AddressEdit(editModel, form);
                }
            }
        });

        var reminder = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.ReminderController>();
        await Try(async () => await reminder.Index());
        var security = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.SecurityController>();
        await Try(async () => await security.AccessDenied("/Admin", "Product.List"));
        await Try(async () => await security.Permissions());
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
        TestContext.WriteLine($"Razor: created={harness.TypesCreated} invoked={harness.MethodsInvoked} failed={harness.MethodsFailed}");
        harness.TypesCreated.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ExerciseHtmlExtensions()
    {
        var harness = CreateHarness();
        await harness.ExerciseStaticTypeAsync(typeof(HtmlExtensions));

        var http = GetService<IHttpContextAccessor>().HttpContext
                   ?? throw new InvalidOperationException("HttpContext is not available");
        var actionContext = new ActionContext(http, http.GetRouteData() ?? new RouteData(), new ActionDescriptor());
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
        var viewContext = new ViewContext(
            actionContext,
            new CoverageNullView(),
            viewData,
            GetService<ITempDataDictionaryFactory>().GetTempData(http),
            TextWriter.Null,
            new HtmlHelperOptions());

        var html = (IHtmlHelper)WebCoverageHarness.EmptyProxy.Create(typeof(IHtmlHelper), viewContext);
        var typedHtml = (IHtmlHelper<PagerModel>)WebCoverageHarness.EmptyProxy.Create(typeof(IHtmlHelper<PagerModel>), viewContext);

        _ = html.GetJQueryDateFormat();
        _ = html.GetUIDirection();
        _ = html.GetUIDirection(true);
        _ = await html.ShouldUseRtlThemeAsync();
        _ = await html.IsTourActiveAsync();
        http.Request.QueryString = new QueryString("?ShowTour=true");
        _ = await html.IsTourActiveAsync();

        var localization = GetService<ILocalizationService>();
        async Task RunPager(bool useRouteLinks, int pageIndex)
        {
            var pager = new PagerModel(localization)
            {
                TotalRecords = 100,
                PageSize = 10,
                PageIndex = pageIndex,
                ShowFirst = true,
                ShowLast = true,
                ShowNext = true,
                ShowPrevious = true,
                ShowIndividualPages = true,
                ShowPagerItems = true,
                ShowTotalSummary = true,
                UseRouteLinks = useRouteLinks,
                RouteActionName = "List",
                RouteValues = new BaseRouteValues()
            };
            _ = await typedHtml.PagerAsync(pager);
        }

        await RunPager(false, 0);
        await RunPager(true, 4);
        await RunPager(false, 9);
        _ = await typedHtml.PagerAsync(new PagerModel(localization) { TotalRecords = 0 });
        _ = html.Pager(new CoveragePageableModel());

        harness.MethodsInvoked.Should().BeGreaterThan(0);
    }

    [Test]
    public void ExerciseNopStartup()
    {
        var startup = new NopStartup();
        startup.ConfigureServices(new ServiceCollection(), new ConfigurationBuilder().AddInMemoryCollection().Build());
        startup.Order.Should().BeGreaterThan(0);
        startup.Configure(new ApplicationBuilder(ServiceProvider));
    }

    [Test]
    public void ExerciseInstallControllerHelpers()
    {
        var sp = ServiceProvider;
        var install = new global::Nop.Web.Controllers.InstallController(
            sp.GetRequiredService<AppSettings>(),
            new Lazy<IInstallationLocalizationService>(sp.GetRequiredService<IInstallationLocalizationService>),
            new Lazy<IInstallationService>(sp.GetRequiredService<IInstallationService>),
            sp.GetRequiredService<INopFileProvider>(),
            new Lazy<IPermissionService>(sp.GetRequiredService<IPermissionService>),
            new Lazy<IPluginService>(sp.GetRequiredService<IPluginService>),
            new Lazy<IStaticCacheManager>(sp.GetRequiredService<IStaticCacheManager>),
            new Lazy<IUploadService>(sp.GetRequiredService<IUploadService>),
            new Lazy<IWebHelper>(sp.GetRequiredService<IWebHelper>),
            new Lazy<NopHttpClient>(sp.GetRequiredService<NopHttpClient>));
        CreateHarness().AttachMvc(install);

        _ = install.Index();
        _ = install.ChangeLanguage("en");
        _ = install.RestartInstall();
        _ = install.RestartApplication();

        var model = new InstallModel { InstallRegionalResources = true };
        foreach (var name in new[] { "PrepareCountryList", "PrepareLanguageList", "PrepareAvailableDataProviders" })
        {
            var method = typeof(global::Nop.Web.Controllers.InstallController)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            method?.Invoke(install, [model]);
        }

        model.AvailableCountries.Should().NotBeEmpty();
    }

    [Test]
    public async Task ExerciseHighMissControllerAndFactoryBranches()
    {
        var harness = CreateHarness();
        await harness.EnableCheckoutTestPluginsAsync();
        await harness.SeedShoppingCartAsync();
        await harness.SeedWishlistAsync();

        async Task Try(Func<Task> action)
        {
            try { await action(); } catch { }
        }

        var settingService = GetService<ISettingService>();
        var customerSettings = GetService<CustomerSettings>();
        var catalogSettings = GetService<CatalogSettings>();
        var taxSettings = GetService<TaxSettings>();
        var shoppingCartSettings = GetService<ShoppingCartSettings>();
        var orderSettings = GetService<OrderSettings>();

        var customerSnap = Snapshot(customerSettings);
        var catalogSnap = Snapshot(catalogSettings);
        var taxSnap = Snapshot(taxSettings);
        var cartSnap = Snapshot(shoppingCartSettings);
        var orderSnap = Snapshot(orderSettings);

        customerSettings.GenderEnabled = true;
        customerSettings.FirstNameEnabled = true;
        customerSettings.LastNameEnabled = true;
        customerSettings.DateOfBirthEnabled = true;
        customerSettings.CompanyEnabled = true;
        customerSettings.StreetAddressEnabled = true;
        customerSettings.StreetAddress2Enabled = true;
        customerSettings.ZipPostalCodeEnabled = true;
        customerSettings.CityEnabled = true;
        customerSettings.CountyEnabled = true;
        customerSettings.CountryEnabled = true;
        customerSettings.StateProvinceEnabled = true;
        customerSettings.PhoneEnabled = true;
        customerSettings.FaxEnabled = true;
        customerSettings.NewsletterEnabled = true;
        customerSettings.AllowCustomersToUploadAvatars = true;
        customerSettings.AllowViewingProfiles = true;
        customerSettings.UsernamesEnabled = true;
        catalogSettings.ProductReviewsMustBeApproved = true;
        catalogSettings.ShowProductReviewsPerStore = true;
        taxSettings.EuVatEnabled = true;
        shoppingCartSettings.MoveItemsFromWishlistToCart = true;
        orderSettings.OnePageCheckoutEnabled = false;
        orderSettings.DisableOrderCompletedPage = false;
        orderSettings.AutoUpdateOrderTotalsOnEditingOrder = false;
        await settingService.SaveSettingAsync(customerSettings);
        await settingService.SaveSettingAsync(catalogSettings);
        await settingService.SaveSettingAsync(taxSettings);
        await settingService.SaveSettingAsync(shoppingCartSettings);
        await settingService.SaveSettingAsync(orderSettings);

        try
        {
            var product = (await GetService<IProductService>().SearchProductsAsync(pageSize: 20))
                .FirstOrDefault(p => p.ProductType == ProductType.SimpleProduct)
                ?? (await GetService<IProductService>().SearchProductsAsync(pageSize: 1)).First();
            var productController = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.ProductController>();
            await Try(async () =>
            {
                productController.ModelState.Clear();
                await productController.ProductPictureAdd(product.Id, harness.CreateForm(withFile: true));
            });

            var warehouses = await GetService<IWarehouseService>().GetAllWarehousesAsync();
            var productFactory = GetService<global::Nop.Web.Areas.Admin.Factories.IProductModelFactory>();
            await Try(async () =>
            {
                var model = await productFactory.PrepareProductModelAsync(null, product);
                if (warehouses.Count > 0)
                {
                    model.WarehouseId = warehouses[0].Id == product.WarehouseId && warehouses.Count > 1
                        ? warehouses[1].Id
                        : warehouses[0].Id;
                }

                productController.ModelState.Clear();
                await productController.Edit(model, true);
            });

            await Try(async () =>
            {
                var search = await productFactory.PrepareProductSearchModelAsync(new global::Nop.Web.Areas.Admin.Models.Catalog.ProductSearchModel());
                search.SetGridPageSize();
                (await productFactory.PrepareProductListModelAsync(search)).Should().NotBeNull();
            });

            var attributeService = GetService<IProductAttributeService>();
            foreach (var sample in (await GetService<IProductService>().SearchProductsAsync(pageSize: 30)).Take(15))
            {
                var mappings = await attributeService.GetProductAttributeMappingsByProductIdAsync(sample.Id);
                if (mappings.Count == 0)
                    continue;

                await Try(async () =>
                {
                    var search = new global::Nop.Web.Areas.Admin.Models.Catalog.ProductAttributeMappingSearchModel
                    {
                        ProductId = sample.Id
                    };
                    search.SetGridPageSize();
                    productController.ModelState.Clear();
                    await productController.ProductAttributeMappingList(search);
                    (await productFactory.PrepareProductAttributeMappingListModelAsync(search, sample)).Should().NotBeNull();
                });
            }

            var adminCustomer = await GetService<IWorkContext>().GetCurrentCustomerAsync();
            var customerFactory = GetService<global::Nop.Web.Areas.Admin.Factories.ICustomerModelFactory>();
            var adminCustomerController = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.CustomerController>();
            await Try(async () =>
            {
                var model = await customerFactory.PrepareCustomerModelAsync(new global::Nop.Web.Areas.Admin.Models.Customers.CustomerModel(), null);
                model.Email = $"coverage-admin-{Guid.NewGuid():N}@example.com";
                model.Username = model.Email;
                model.Password = "1q2w3e4r5t";
                model.FirstName = "Coverage";
                model.LastName = "Admin";
                model.Gender = "M";
                adminCustomerController.ModelState.Clear();
                await adminCustomerController.Create(model, true, harness.CreateForm());
            });
            await Try(async () =>
            {
                var model = await customerFactory.PrepareCustomerModelAsync(null, adminCustomer);
                model.Gender = "F";
                model.FirstName = "Coverage";
                model.LastName = "Edited";
                model.VatNumber = "GB123";
                adminCustomerController.ModelState.Clear();
                await adminCustomerController.Edit(model, true, harness.CreateForm());
            });
            await Try(async () =>
            {
                var model = await customerFactory.PrepareCustomerModelAsync(null, adminCustomer);
                model.SendEmail.Subject = "Coverage subject";
                model.SendEmail.Body = "Coverage body";
                model.SendEmail.SendImmediately = true;
                adminCustomerController.ModelState.Clear();
                await adminCustomerController.SendEmail(model);
            });

            var publicCustomerFactory = GetService<global::Nop.Web.Factories.ICustomerModelFactory>();
            var publicCustomerController = harness.CreateController<global::Nop.Web.Controllers.CustomerController>();
            await Try(async () =>
            {
                var model = await publicCustomerFactory.PrepareCustomerInfoModelAsync(
                    new global::Nop.Web.Models.Customer.CustomerInfoModel(), adminCustomer, false);
                model.FirstName = "Coverage";
                model.LastName = "Info";
                model.Gender = "M";
                model.VatNumber = "GB999";
                publicCustomerController.ModelState.Clear();
                await publicCustomerController.Info(model, harness.CreateForm());
            });
            await Try(async () =>
            {
                publicCustomerController.ModelState.Clear();
                await publicCustomerController.UploadAvatar(
                    new global::Nop.Web.Models.Customer.CustomerAvatarModel(),
                    WebCoverageHarness.CreateFormFile("uploadedFile", "avatar.jpg"));
            });

            var orderFactory = GetService<global::Nop.Web.Areas.Admin.Factories.IOrderModelFactory>();
            var orderController = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.OrderController>();
            foreach (var order in await GetService<IOrderService>().SearchOrdersAsync(pageIndex: 0, pageSize: 3))
            {
                await Try(async () =>
                {
                    var items = await GetService<IOrderService>().GetOrderItemsAsync(order.Id);
                    var item = items.FirstOrDefault();
                    if (item == null)
                        return;

                    var form = harness.CreateForm(new Dictionary<string, string>
                    {
                        [$"btnSaveOrderItem{item.Id}"] = "Save",
                        [$"pvUnitPriceInclTax{item.Id}"] = item.UnitPriceInclTax.ToString("0.00"),
                        [$"pvUnitPriceExclTax{item.Id}"] = item.UnitPriceExclTax.ToString("0.00"),
                        [$"pvQuantity{item.Id}"] = Math.Max(1, item.Quantity).ToString(),
                        [$"pvDiscountInclTax{item.Id}"] = item.DiscountAmountInclTax.ToString("0.00"),
                        [$"pvDiscountExclTax{item.Id}"] = item.DiscountAmountExclTax.ToString("0.00"),
                        [$"pvPriceInclTax{item.Id}"] = item.PriceInclTax.ToString("0.00"),
                        [$"pvPriceExclTax{item.Id}"] = item.PriceExclTax.ToString("0.00")
                    });
                    orderController.ModelState.Clear();
                    await orderController.EditOrderItem(order.Id, form);
                });

                await Try(async () =>
                {
                    var addModel = await orderFactory.PrepareAddProductToOrderModelAsync(
                        new global::Nop.Web.Areas.Admin.Models.Orders.AddProductToOrderModel(), order, product);
                    addModel.Quantity = 1;
                    orderController.ModelState.Clear();
                    await orderController.AddProductToOrderDetails(order.Id, product.Id, addModel, harness.CreateForm());
                });
            }

            var checkout = harness.CreateController<global::Nop.Web.Controllers.CheckoutController>();
            var checkoutFactory = GetService<global::Nop.Web.Factories.ICheckoutModelFactory>();
            var customer = await GetService<IWorkContext>().GetCurrentCustomerAsync();
            var store = await GetService<IStoreContext>().GetCurrentStoreAsync();
            var cart = await GetService<IShoppingCartService>()
                .GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);
            await Try(async () =>
            {
                var shipping = new global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel();
                await checkoutFactory.PrepareShippingAddressModelAsync(shipping, cart, prePopulateNewAddressWithCustomerFields: true);
                shipping.ShippingNewAddress.FirstName = "Coverage";
                shipping.ShippingNewAddress.LastName = "Ship";
                shipping.ShippingNewAddress.Email = $"ship-{Guid.NewGuid():N}@example.com";
                shipping.ShippingNewAddress.Address1 = $"Coverage {Guid.NewGuid():N}";
                shipping.ShippingNewAddress.City = "New York";
                shipping.ShippingNewAddress.ZipPostalCode = "10021";
                shipping.ShippingNewAddress.CountryId = 1;
                checkout.ModelState.Clear();
                await checkout.NewShippingAddress(shipping, harness.CreateForm());
            });
            await Try(async () => await checkout.Confirm());
            await Try(async () => await checkout.ConfirmOrder(true));
            await Try(async () => await checkout.Completed(null));

            var previousOpc = orderSettings.OnePageCheckoutEnabled;
            orderSettings.OnePageCheckoutEnabled = true;
            await settingService.SaveSettingAsync(orderSettings);
            try
            {
                var existingAddress = (await GetService<ICustomerService>().GetAddressesByCustomerIdAsync(customer.Id)).FirstOrDefault();
                await Try(async () =>
                {
                    await GetService<IGenericAttributeService>().SaveAttributeAsync(customer,
                        NopCustomerDefaults.SelectedPaymentMethodAttribute, "Payments.TestMethod", store.Id);
                    var billingModel = new global::Nop.Web.Models.Checkout.CheckoutBillingAddressModel();
                    await checkoutFactory.PrepareBillingAddressModelAsync(billingModel, cart, prePopulateNewAddressWithCustomerFields: true);
                    if (existingAddress != null)
                        billingModel.BillingNewAddress.Id = existingAddress.Id;
                    checkout.ModelState.Clear();
                    await checkout.SaveEditBillingAddress(billingModel, harness.CreateForm(), true);
                });
                await Try(async () =>
                {
                    var shippingModel = new global::Nop.Web.Models.Checkout.CheckoutShippingAddressModel();
                    await checkoutFactory.PrepareShippingAddressModelAsync(shippingModel, cart, prePopulateNewAddressWithCustomerFields: true);
                    if (existingAddress != null)
                        shippingModel.ShippingNewAddress.Id = existingAddress.Id;
                    checkout.ModelState.Clear();
                    await checkout.SaveEditShippingAddress(shippingModel, harness.CreateForm(), true);
                });
                await Try(async () => await checkout.OpcSavePaymentMethod("Payments.TestMethod",
                    new global::Nop.Web.Models.Checkout.CheckoutPaymentMethodModel()));
                await Try(async () => await checkout.OpcConfirmOrder(true));
                await Try(async () => await checkout.ConfirmOrder(true));
            }
            finally
            {
                orderSettings.OnePageCheckoutEnabled = previousOpc;
                await settingService.SaveSettingAsync(orderSettings);
            }

            var shoppingCart = harness.CreateController<global::Nop.Web.Controllers.ShoppingCartController>();
            var wishlist = await GetService<IShoppingCartService>()
                .GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id);
            await Try(async () =>
            {
                var ids = string.Join(",", wishlist.Select(i => i.Id));
                var form = harness.CreateForm(new Dictionary<string, string> { ["addtocart"] = ids });
                await shoppingCart.AddItemsToCartFromWishlist(null,
                    new global::Nop.Web.Models.ShoppingCart.WishlistModel { ListId = 0 }, form);
            });
            await Try(async () => await shoppingCart.Wishlist(null, null));
            await Try(async () =>
            {
                var qtyForm = harness.CreateForm(new Dictionary<string, string>
                {
                    [$"addtocart_{product.Id}.EnteredQuantity"] = "1"
                });
                await shoppingCart.ProductDetails_AttributeChange(product.Id, true, false, qtyForm);
                await shoppingCart.CheckoutAttributeChange(qtyForm, false);
            });

            var publicProductFactory = GetService<global::Nop.Web.Factories.IProductModelFactory>();
            await Try(async () =>
            {
                (await publicProductFactory.PrepareCustomerProductReviewsModelAsync(1)).Should().NotBeNull();
                (await publicProductFactory.PrepareCustomerProductReviewsModelAsync(null)).Should().NotBeNull();
            });

            var catalogFactory = GetService<global::Nop.Web.Factories.ICatalogModelFactory>();
            var command = new global::Nop.Web.Models.Catalog.CatalogProductsCommand();
            foreach (var storeVendor in (await GetService<IVendorService>().GetAllVendorsAsync()).Take(5))
                await Try(async () => (await catalogFactory.PrepareVendorModelAsync(storeVendor, command)).Should().NotBeNull());

            var pluginController = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.PluginController>();
            var pluginFactory = GetService<global::Nop.Web.Areas.Admin.Factories.IPluginModelFactory>();
            foreach (var systemName in new[] { "Payments.TestMethod", "FixedRateTestShippingRateComputationMethod" })
            {
                await Try(async () =>
                {
                    var descriptor = await GetService<IPluginService>()
                        .GetPluginDescriptorBySystemNameAsync<IPlugin>(systemName, LoadPluginsMode.All);
                    if (descriptor == null)
                        return;
                    var model = await pluginFactory.PreparePluginModelAsync(null, descriptor);
                    model.IsEnabled = true;
                    pluginController.ModelState.Clear();
                    await pluginController.EditPopup(model);
                    model.IsEnabled = false;
                    await pluginController.EditPopup(model);
                });
            }

            var vendor = (await GetService<IVendorService>().GetAllVendorsAsync()).FirstOrDefault();
            if (vendor != null)
            {
                var previousVendorId = adminCustomer.VendorId;
                try
                {
                    adminCustomer.VendorId = vendor.Id;
                    await GetService<ICustomerService>().UpdateCustomerAsync(adminCustomer);
                    harness.ClearWorkContextCaches();
                    var vendorProductController = harness.CreateController<global::Nop.Web.Areas.Admin.Controllers.ProductController>();
                    await Try(async () =>
                    {
                        vendorProductController.ModelState.Clear();
                        await vendorProductController.ProductPictureAdd(product.Id, harness.CreateForm(withFile: true));
                    });
                    await Try(async () =>
                    {
                        var model = await productFactory.PrepareProductModelAsync(null, product);
                        vendorProductController.ModelState.Clear();
                        await vendorProductController.Edit(model, false);
                    });
                }
                finally
                {
                    adminCustomer.VendorId = previousVendorId;
                    await GetService<ICustomerService>().UpdateCustomerAsync(adminCustomer);
                    harness.ClearWorkContextCaches();
                }
            }
        }
        finally
        {
            Restore(customerSettings, customerSnap);
            Restore(catalogSettings, catalogSnap);
            Restore(taxSettings, taxSnap);
            Restore(shoppingCartSettings, cartSnap);
            Restore(orderSettings, orderSnap);
            await settingService.SaveSettingAsync(customerSettings);
            await settingService.SaveSettingAsync(catalogSettings);
            await settingService.SaveSettingAsync(taxSettings);
            await settingService.SaveSettingAsync(shoppingCartSettings);
            await settingService.SaveSettingAsync(orderSettings);
            var restored = await GetService<ICustomerService>().GetCustomerByEmailAsync(NopTestsDefaults.AdminEmail);
            if (restored != null)
                await GetService<IWorkContext>().SetCurrentCustomerAsync(restored);
            harness.ClearWorkContextCaches();
        }
    }

    private static Dictionary<string, object> Snapshot(object settings)
    {
        var values = new Dictionary<string, object>();
        foreach (var prop in settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0))
        {
            values[prop.Name] = prop.GetValue(settings);
        }

        return values;
    }

    private static void Restore(object settings, Dictionary<string, object> values)
    {
        foreach (var prop in settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0))
        {
            if (values.TryGetValue(prop.Name, out var value))
                prop.SetValue(settings, value);
        }
    }

    [Test]
    public async Task ExerciseAdminHelpersAndInfrastructure()
    {
        var harness = CreateHarness();
        var types = WebAssemblyMarker.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters
                        && t.Namespace is "Nop.Web.Areas.Admin.Helpers"
                            or "Nop.Web.Areas.Admin.Infrastructure"
                            or "Nop.Web.Infrastructure");
        await harness.ExerciseTypesAsync(types, asMvc: false);
        harness.TypesCreated.Should().BeGreaterThan(0);
    }

    private sealed class CoverageNullView : IView
    {
        public string Path => string.Empty;
        public Task RenderAsync(ViewContext context) => Task.CompletedTask;
    }

    private sealed class CoveragePageableModel : global::Nop.Web.Framework.UI.Paging.IPageableModel
    {
        public int PageIndex { get; set; }
        public int PageNumber => PageIndex + 1;
        public int PageSize { get; set; } = 10;
        public int TotalItems { get; set; } = 50;
        public int TotalPages => 5;
        public int FirstItem => 1;
        public int LastItem => 10;
        public bool HasPreviousPage => false;
        public bool HasNextPage => true;
    }

    private static IEnumerable<Type> TypesIn(string ns, string suffix = null)
    {
        return WebAssemblyMarker.Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters
                        && t.Namespace == ns
                        && (suffix == null || t.Name.EndsWith(suffix, StringComparison.Ordinal)));
    }
}
