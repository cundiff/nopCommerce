using System.Globalization;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Stores;
using Nop.Core.Domain.Topics;
using Nop.Plugin.Misc.HeadlessApi.Models;
using Nop.Services.Catalog;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Seo;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Maps nopCommerce domain entities to the storefront DTOs consumed by Vercel Commerce
/// </summary>
public class HeadlessApiModelFactory
{
    #region Fields

    protected readonly ICategoryService _categoryService;
    protected readonly ICurrencyService _currencyService;
    protected readonly IPictureService _pictureService;
    protected readonly IPriceCalculationService _priceCalculationService;
    protected readonly IProductAttributeParser _productAttributeParser;
    protected readonly IProductAttributeService _productAttributeService;
    protected readonly IProductService _productService;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IStoreContext _storeContext;
    protected readonly IUrlRecordService _urlRecordService;
    protected readonly IWebHelper _webHelper;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public HeadlessApiModelFactory(ICategoryService categoryService,
        ICurrencyService currencyService,
        IPictureService pictureService,
        IPriceCalculationService priceCalculationService,
        IProductAttributeParser productAttributeParser,
        IProductAttributeService productAttributeService,
        IProductService productService,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        IUrlRecordService urlRecordService,
        IWebHelper webHelper,
        IWorkContext workContext)
    {
        _categoryService = categoryService;
        _currencyService = currencyService;
        _pictureService = pictureService;
        _priceCalculationService = priceCalculationService;
        _productAttributeParser = productAttributeParser;
        _productAttributeService = productAttributeService;
        _productService = productService;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _urlRecordService = urlRecordService;
        _webHelper = webHelper;
        _workContext = workContext;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Builds a Money DTO, converting a primary-store-currency amount to the working currency
    /// </summary>
    protected virtual async Task<MoneyDto> PrepareMoneyAsync(decimal primaryStoreAmount, Currency workingCurrency)
    {
        var converted = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(primaryStoreAmount, workingCurrency);
        return new MoneyDto
        {
            Amount = Math.Round(converted, 2).ToString("0.00", CultureInfo.InvariantCulture),
            CurrencyCode = workingCurrency.CurrencyCode
        };
    }

    /// <summary>
    /// Determines whether a product can currently be purchased
    /// </summary>
    protected static bool IsAvailableForSale(Product product)
    {
        return product is { Published: true, Deleted: false, DisableBuyButton: false };
    }

    /// <summary>
    /// Encodes a stable variant identifier understood by the cart endpoints
    /// </summary>
    public static string EncodeVariantId(int productId, int? combinationId = null)
    {
        return combinationId.HasValue ? $"{productId}:{combinationId.Value}" : productId.ToString();
    }

    /// <summary>
    /// Parses a variant identifier into a product id and an optional combination id
    /// </summary>
    public static bool TryParseVariantId(string variantId, out int productId, out int? combinationId)
    {
        productId = 0;
        combinationId = null;

        if (string.IsNullOrWhiteSpace(variantId))
            return false;

        var parts = variantId.Split(':');
        if (!int.TryParse(parts[0], out productId))
            return false;

        if (parts.Length > 1 && int.TryParse(parts[1], out var combo))
            combinationId = combo;

        return true;
    }

    #endregion

    #region Product

    public virtual async Task<ProductDto> PrepareProductDtoAsync(Product product)
    {
        if (product == null)
            return null;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var currency = await _workContext.GetWorkingCurrencyAsync();

        var handle = await _urlRecordService.GetSeNameAsync(product);

        var (_, finalPrice, _, _) = await _priceCalculationService.GetFinalPriceAsync(product, customer, store);
        var price = await PrepareMoneyAsync(finalPrice, currency);

        //images
        var pictures = await _productService.GetProductPicturesByProductIdAsync(product.Id);
        var images = new List<ImageDto>();
        foreach (var picture in pictures)
        {
            var url = await _pictureService.GetPictureUrlAsync(picture.PictureId);
            if (string.IsNullOrEmpty(url))
                continue;

            images.Add(new ImageDto { Url = url, AltText = product.Name, Width = 1000, Height = 1000 });
        }

        var featuredImage = images.FirstOrDefault();
        if (featuredImage == null)
        {
            var defaultUrl = await _pictureService.GetDefaultPictureUrlAsync();
            featuredImage = new ImageDto { Url = defaultUrl, AltText = product.Name, Width = 1000, Height = 1000 };
            images.Add(featuredImage);
        }

        //options & variants from product attributes / combinations
        var (options, variants) = await PrepareOptionsAndVariantsAsync(product, customer, store, currency, price);

        return new ProductDto
        {
            Id = product.Id.ToString(),
            Handle = handle,
            AvailableForSale = IsAvailableForSale(product),
            Title = product.Name,
            Description = product.ShortDescription ?? string.Empty,
            DescriptionHtml = product.FullDescription ?? string.Empty,
            Options = options,
            PriceRange = new PriceRangeDto { MaxVariantPrice = price, MinVariantPrice = price },
            Variants = variants,
            FeaturedImage = featuredImage,
            Images = images,
            Seo = new SeoDto
            {
                Title = string.IsNullOrEmpty(product.MetaTitle) ? product.Name : product.MetaTitle,
                Description = product.MetaDescription ?? string.Empty
            },
            Tags = new List<string>(),
            UpdatedAt = product.UpdatedOnUtc.ToString("o", CultureInfo.InvariantCulture)
        };
    }

    protected virtual async Task<(List<ProductOptionDto> options, List<ProductVariantDto> variants)> PrepareOptionsAndVariantsAsync(
        Product product, Customer customer, Store store, Currency currency, MoneyDto defaultPrice)
    {
        var options = new List<ProductOptionDto>();
        var variants = new List<ProductVariantDto>();

        var combinations = await _productAttributeService.GetAllProductAttributeCombinationsAsync(product.Id);

        if (combinations == null || !combinations.Any())
        {
            //a single, default variant
            variants.Add(new ProductVariantDto
            {
                Id = EncodeVariantId(product.Id),
                Title = "Default Title",
                AvailableForSale = IsAvailableForSale(product),
                SelectedOptions = new List<SelectedOptionDto>(),
                Price = defaultPrice
            });

            return (options, variants);
        }

        //build options from the product attribute mappings
        var mappings = await _productAttributeService.GetProductAttributeMappingsByProductIdAsync(product.Id);
        foreach (var mapping in mappings)
        {
            var attribute = await _productAttributeService.GetProductAttributeByIdAsync(mapping.ProductAttributeId);
            if (attribute == null)
                continue;

            var values = await _productAttributeService.GetProductAttributeValuesAsync(mapping.Id);
            options.Add(new ProductOptionDto
            {
                Id = mapping.Id.ToString(),
                Name = attribute.Name,
                Values = values.Select(v => v.Name).ToList()
            });
        }

        //build variants from the combinations
        foreach (var combination in combinations)
        {
            var selectedOptions = new List<SelectedOptionDto>();
            var attributeValues = await _productAttributeParser.ParseProductAttributeValuesAsync(combination.AttributesXml);
            foreach (var value in attributeValues)
            {
                var mapping = await _productAttributeService.GetProductAttributeMappingByIdAsync(value.ProductAttributeMappingId);
                if (mapping == null)
                    continue;

                var attribute = await _productAttributeService.GetProductAttributeByIdAsync(mapping.ProductAttributeId);
                if (attribute == null)
                    continue;

                selectedOptions.Add(new SelectedOptionDto { Name = attribute.Name, Value = value.Name });
            }

            var variantPrice = defaultPrice;
            if (combination.OverriddenPrice.HasValue)
                variantPrice = await PrepareMoneyAsync(combination.OverriddenPrice.Value, currency);

            var variantAvailable = IsAvailableForSale(product) &&
                (combination.AllowOutOfStockOrders || combination.StockQuantity > 0);

            variants.Add(new ProductVariantDto
            {
                Id = EncodeVariantId(product.Id, combination.Id),
                Title = selectedOptions.Any()
                    ? string.Join(" / ", selectedOptions.Select(o => o.Value))
                    : "Default Title",
                AvailableForSale = variantAvailable,
                SelectedOptions = selectedOptions,
                Price = variantPrice
            });
        }

        return (options, variants);
    }

    #endregion

    #region Category

    public virtual async Task<CollectionDto> PrepareCollectionDtoAsync(Category category)
    {
        if (category == null)
            return null;

        var handle = await _urlRecordService.GetSeNameAsync(category);

        return new CollectionDto
        {
            Handle = handle,
            Title = category.Name,
            Description = category.Description ?? string.Empty,
            Seo = new SeoDto
            {
                Title = string.IsNullOrEmpty(category.MetaTitle) ? category.Name : category.MetaTitle,
                Description = category.MetaDescription ?? string.Empty
            },
            UpdatedAt = category.UpdatedOnUtc.ToString("o", CultureInfo.InvariantCulture),
            Path = $"/search/{handle}"
        };
    }

    #endregion

    #region Topic (CMS pages)

    public virtual PageDto PreparePageDto(Topic topic)
    {
        if (topic == null)
            return null;

        var handle = topic.SystemName;

        return new PageDto
        {
            Id = topic.Id.ToString(),
            Title = string.IsNullOrEmpty(topic.Title) ? topic.SystemName : topic.Title,
            Handle = handle,
            Body = topic.Body ?? string.Empty,
            BodySummary = string.Empty,
            Seo = new SeoDto
            {
                Title = string.IsNullOrEmpty(topic.MetaTitle) ? topic.Title : topic.MetaTitle,
                Description = topic.MetaDescription ?? string.Empty
            },
            CreatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            UpdatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
    }

    #endregion

    #region Cart

    public virtual async Task<CartDto> PrepareCartDtoAsync(Customer customer, string cartToken)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var currency = await _workContext.GetWorkingCurrencyAsync();

        var cartItems = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        var lines = new List<CartItemDto>();
        decimal subtotal = 0;
        var totalQuantity = 0;

        foreach (var item in cartItems)
        {
            var product = await _productService.GetProductByIdAsync(item.ProductId);
            if (product == null)
                continue;

            var (_, itemSubTotal, _, _) = await _shoppingCartService.GetSubTotalAsync(item, true);
            subtotal += itemSubTotal;
            totalQuantity += item.Quantity;

            var handle = await _urlRecordService.GetSeNameAsync(product);

            var pictureUrl = await GetProductPictureUrlAsync(product);

            //selected options for display
            var selectedOptions = new List<SelectedOptionDto>();
            if (!string.IsNullOrEmpty(item.AttributesXml))
            {
                var values = await _productAttributeParser.ParseProductAttributeValuesAsync(item.AttributesXml);
                foreach (var value in values)
                {
                    var mapping = await _productAttributeService.GetProductAttributeMappingByIdAsync(value.ProductAttributeMappingId);
                    if (mapping == null)
                        continue;

                    var attribute = await _productAttributeService.GetProductAttributeByIdAsync(mapping.ProductAttributeId);
                    if (attribute == null)
                        continue;

                    selectedOptions.Add(new SelectedOptionDto { Name = attribute.Name, Value = value.Name });
                }
            }

            //resolve the variant id (combination) so the storefront can match it back
            int? combinationId = null;
            var combination = await _productAttributeParser.FindProductAttributeCombinationAsync(product, item.AttributesXml);
            if (combination != null)
                combinationId = combination.Id;

            lines.Add(new CartItemDto
            {
                Id = item.Id.ToString(),
                Quantity = item.Quantity,
                Cost = new CartItemCostDto { TotalAmount = await PrepareMoneyAsync(itemSubTotal, currency) },
                Merchandise = new CartMerchandiseDto
                {
                    Id = EncodeVariantId(product.Id, combinationId),
                    Title = selectedOptions.Any() ? string.Join(" / ", selectedOptions.Select(o => o.Value)) : "Default Title",
                    SelectedOptions = selectedOptions,
                    Product = new CartProductDto
                    {
                        Id = product.Id.ToString(),
                        Handle = handle,
                        Title = product.Name,
                        FeaturedImage = new ImageDto { Url = pictureUrl, AltText = product.Name, Width = 1000, Height = 1000 }
                    }
                }
            });
        }

        var checkoutUrl = $"{_webHelper.GetStoreLocation()}cart";

        return new CartDto
        {
            Id = cartToken,
            CheckoutUrl = checkoutUrl,
            TotalQuantity = totalQuantity,
            Lines = lines,
            Cost = new CartCostDto
            {
                SubtotalAmount = await PrepareMoneyAsync(subtotal, currency),
                TotalAmount = await PrepareMoneyAsync(subtotal, currency),
                TotalTaxAmount = await PrepareMoneyAsync(0, currency)
            }
        };
    }

    protected virtual async Task<string> GetProductPictureUrlAsync(Product product)
    {
        var pictures = await _productService.GetProductPicturesByProductIdAsync(product.Id);
        var first = pictures.FirstOrDefault();
        if (first != null)
        {
            var url = await _pictureService.GetPictureUrlAsync(first.PictureId);
            if (!string.IsNullOrEmpty(url))
                return url;
        }

        return await _pictureService.GetDefaultPictureUrlAsync();
    }

    #endregion
}
