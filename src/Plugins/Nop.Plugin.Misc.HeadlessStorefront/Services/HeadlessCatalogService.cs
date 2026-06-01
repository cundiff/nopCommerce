using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Plugin.Misc.HeadlessStorefront.Models;
using Nop.Services.Catalog;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Seo;
using Nop.Services.Helpers;
using Nop.Services.Stores;

namespace Nop.Plugin.Misc.HeadlessStorefront.Services;

public class HeadlessCatalogService : IHeadlessCatalogService
{
  private readonly ICategoryService _categoryService;
  private readonly ICurrencyService _currencyService;
  private readonly ILocalizationService _localizationService;
  private readonly IPictureService _pictureService;
  private readonly IPriceCalculationService _priceCalculationService;
  private readonly IProductService _productService;
  private readonly IStoreContext _storeContext;
  private readonly IUrlRecordService _urlRecordService;
  private readonly IWebHelper _webHelper;
  private readonly IWorkContext _workContext;
  private readonly MediaSettings _mediaSettings;

  public HeadlessCatalogService(
    ICategoryService categoryService,
    ICurrencyService currencyService,
    ILocalizationService localizationService,
    IPictureService pictureService,
    IPriceCalculationService priceCalculationService,
    IProductService productService,
    IStoreContext storeContext,
    IUrlRecordService urlRecordService,
    IWebHelper webHelper,
    IWorkContext workContext,
    MediaSettings mediaSettings)
  {
    _categoryService = categoryService;
    _currencyService = currencyService;
    _localizationService = localizationService;
    _pictureService = pictureService;
    _priceCalculationService = priceCalculationService;
    _productService = productService;
    _storeContext = storeContext;
    _urlRecordService = urlRecordService;
    _webHelper = webHelper;
    _workContext = workContext;
    _mediaSettings = mediaSettings;
  }

  public virtual async Task<IList<HeadlessCollectionDto>> GetCollectionsAsync()
  {
    var store = await _storeContext.GetCurrentStoreAsync();
    var categories = await _categoryService.GetAllCategoriesAsync(store.Id);
    var result = new List<HeadlessCollectionDto>();

    foreach (var category in categories.Where(c => c.ParentCategoryId == 0 && c.Published))
    {
      var dto = await MapCategoryAsync(category);
      if (dto != null)
        result.Add(dto);
    }

    return result;
  }

  public virtual async Task<HeadlessCollectionDto> GetCollectionByHandleAsync(string handle)
  {
    var categoryId = await ResolveCategoryIdByHandleAsync(handle);
    if (categoryId == null)
      return null;

    var category = await _categoryService.GetCategoryByIdAsync(categoryId.Value);
    return category == null ? null : await MapCategoryAsync(category);
  }

  public virtual async Task<int?> ResolveCategoryIdByHandleAsync(string handle)
  {
    var urlRecord = await _urlRecordService.GetBySlugAsync(handle);
    if (urlRecord == null || !urlRecord.EntityName.Equals(nameof(Category), StringComparison.InvariantCultureIgnoreCase))
      return null;

    return urlRecord.EntityId;
  }

  public virtual async Task<IList<HeadlessProductDto>> GetProductsAsync(string query, string sortKey, bool reverse, int? categoryId = null)
  {
    var store = await _storeContext.GetCurrentStoreAsync();
    var customer = await _workContext.GetCurrentCustomerAsync();
    var orderBy = MapSortKey(sortKey);
    if (reverse && orderBy is ProductSortingEnum.NameAsc or ProductSortingEnum.PriceAsc)
      orderBy = orderBy == ProductSortingEnum.NameAsc ? ProductSortingEnum.NameDesc : ProductSortingEnum.PriceDesc;
    else if (reverse && orderBy == ProductSortingEnum.CreatedOn)
      orderBy = ProductSortingEnum.CreatedOn;

    IList<int> categoryIds = null;
    if (categoryId.HasValue)
      categoryIds = new List<int> { categoryId.Value };

    var products = await _productService.SearchProductsAsync(
      pageIndex: 0,
      pageSize: 100,
      categoryIds: categoryIds,
      storeId: store.Id,
      keywords: query,
      orderBy: orderBy,
      overridePublished: true);

    var result = new List<HeadlessProductDto>();
    foreach (var product in products)
    {
      var dto = await MapProductInternalAsync(product, customer, store.Id);
      if (dto != null)
        result.Add(dto);
    }

    if (reverse && orderBy == ProductSortingEnum.CreatedOn)
      result.Reverse();

    return result;
  }

  public virtual async Task<HeadlessProductDto> GetProductByHandleAsync(string handle)
  {
    var urlRecord = await _urlRecordService.GetBySlugAsync(handle);
    if (urlRecord == null || !urlRecord.EntityName.Equals(nameof(Product), StringComparison.InvariantCultureIgnoreCase))
      return null;

    var product = await _productService.GetProductByIdAsync(urlRecord.EntityId);
    if (product == null || !product.Published)
      return null;

    var store = await _storeContext.GetCurrentStoreAsync();
    var customer = await _workContext.GetCurrentCustomerAsync();
    return await MapProductInternalAsync(product, customer, store.Id);
  }

  protected virtual ProductSortingEnum MapSortKey(string sortKey)
  {
    return sortKey switch
    {
      "PRICE" => ProductSortingEnum.PriceAsc,
      "CREATED_AT" => ProductSortingEnum.CreatedOn,
      "BEST_SELLING" => ProductSortingEnum.Position,
      _ => ProductSortingEnum.Position
    };
  }

  protected virtual async Task<HeadlessCollectionDto> MapCategoryAsync(Category category)
  {
    var handle = await _urlRecordService.GetSeNameAsync(category);
    var description = await _localizationService.GetLocalizedAsync(category, x => x.Description);
    var name = await _localizationService.GetLocalizedAsync(category, x => x.Name);
    var metaTitle = await _localizationService.GetLocalizedAsync(category, x => x.MetaTitle);
    var metaDescription = await _localizationService.GetLocalizedAsync(category, x => x.MetaDescription);

    return new HeadlessCollectionDto(
      Handle: handle,
      Title: name,
      Description: description ?? string.Empty,
      Seo: new HeadlessSeoDto(metaTitle ?? name, metaDescription ?? description ?? string.Empty),
      Path: $"/search/{handle}",
      UpdatedAt: category.UpdatedOnUtc.ToString("O"));
  }

  protected virtual async Task<HeadlessProductDto> MapProductInternalAsync(Product product, Customer customer, int storeId)
  {
    if (product.ProductType == ProductType.Grouped)
      return null;

    var handle = await _urlRecordService.GetSeNameAsync(product);
    var name = await _localizationService.GetLocalizedAsync(product, x => x.Name);
    var description = await _localizationService.GetLocalizedAsync(product, x => x.FullDescription);
    var shortDescription = await _localizationService.GetLocalizedAsync(product, x => x.ShortDescription);
    var metaTitle = await _localizationService.GetLocalizedAsync(product, x => x.MetaTitle);
    var metaDescription = await _localizationService.GetLocalizedAsync(product, x => x.MetaDescription);
    var storeLocation = _webHelper.GetStoreLocation();

    var currency = await _workContext.GetWorkingCurrencyAsync();
    var (_, finalPrice, _, _) = await _priceCalculationService.GetFinalPriceAsync(product, customer, storeId);
    var price = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(finalPrice, currency);
    var money = FormatMoney(price, currency.CurrencyCode);

    var pictures = await _pictureService.GetPicturesByProductIdAsync(product.Id);
    var images = new List<HeadlessImageDto>();
    foreach (var picture in pictures)
    {
      var (url, _) = await _pictureService.GetPictureUrlAsync(picture, _mediaSettings.ProductDetailsPictureSize, true, storeLocation);
      images.Add(new HeadlessImageDto(url, $"{name}", _mediaSettings.ProductDetailsPictureSize, _mediaSettings.ProductDetailsPictureSize));
    }

    if (!images.Any())
    {
      var defaultUrl = await _pictureService.GetDefaultPictureUrlAsync(_mediaSettings.ProductDetailsPictureSize, PictureType.Entity, storeLocation);
      images.Add(new HeadlessImageDto(defaultUrl, name, 0, 0));
    }

    var variantId = product.Id.ToString();
    var variant = new HeadlessProductVariantDto(
      Id: variantId,
      Title: "Default Title",
      AvailableForSale: !product.DisableBuyButton && product.Published,
      SelectedOptions: new List<HeadlessSelectedOptionDto>(),
      Price: money);

    return new HeadlessProductDto(
      Id: product.Id.ToString(),
      Handle: handle,
      AvailableForSale: variant.AvailableForSale,
      Title: name,
      Description: shortDescription ?? string.Empty,
      DescriptionHtml: description ?? shortDescription ?? string.Empty,
      Options: new List<HeadlessProductOptionDto>(),
      PriceRange: new HeadlessPriceRangeDto(money, money),
      FeaturedImage: images.First(),
      Images: images,
      Seo: new HeadlessSeoDto(metaTitle ?? name, metaDescription ?? shortDescription ?? string.Empty),
      Tags: new List<string>(),
      UpdatedAt: product.UpdatedOnUtc.ToString("O"),
      Variants: new List<HeadlessProductVariantDto> { variant });
  }

  protected virtual HeadlessMoneyDto FormatMoney(decimal amount, string currencyCode)
  {
    return new HeadlessMoneyDto(amount.ToString("0.00"), currencyCode.ToUpperInvariant());
  }
}
