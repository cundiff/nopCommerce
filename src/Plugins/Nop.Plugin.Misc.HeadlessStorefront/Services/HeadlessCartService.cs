using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.HeadlessStorefront.Models;
using Nop.Services.Catalog;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Seo;
using Nop.Services.Helpers;
using Nop.Services.Stores;

namespace Nop.Plugin.Misc.HeadlessStorefront.Services;

public class HeadlessCartService : IHeadlessCartService
{
  private readonly ICurrencyService _currencyService;
  private readonly ILocalizationService _localizationService;
  private readonly IOrderTotalCalculationService _orderTotalCalculationService;
  private readonly IPictureService _pictureService;
  private readonly IPriceCalculationService _priceCalculationService;
  private readonly IProductService _productService;
  private readonly IShoppingCartService _shoppingCartService;
  private readonly IStoreContext _storeContext;
  private readonly IUrlRecordService _urlRecordService;
  private readonly IWebHelper _webHelper;
  private readonly IWorkContext _workContext;
  private readonly MediaSettings _mediaSettings;

  public HeadlessCartService(
    ICurrencyService currencyService,
    ILocalizationService localizationService,
    IOrderTotalCalculationService orderTotalCalculationService,
    IPictureService pictureService,
    IPriceCalculationService priceCalculationService,
    IProductService productService,
    IShoppingCartService shoppingCartService,
    IStoreContext storeContext,
    IUrlRecordService urlRecordService,
    IWebHelper webHelper,
    IWorkContext workContext,
    MediaSettings mediaSettings)
  {
    _currencyService = currencyService;
    _localizationService = localizationService;
    _orderTotalCalculationService = orderTotalCalculationService;
    _pictureService = pictureService;
    _priceCalculationService = priceCalculationService;
    _productService = productService;
    _shoppingCartService = shoppingCartService;
    _storeContext = storeContext;
    _urlRecordService = urlRecordService;
    _webHelper = webHelper;
    _workContext = workContext;
    _mediaSettings = mediaSettings;
  }

  public virtual async Task<HeadlessCartDto> GetCartAsync(Customer customer)
  {
    var store = await _storeContext.GetCurrentStoreAsync();
    var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);
    return await MapCartAsync(customer, cart, store.Id);
  }

  public virtual async Task<HeadlessCartDto> AddItemAsync(Customer customer, int productId, int quantity, string attributesXml = null)
  {
    var store = await _storeContext.GetCurrentStoreAsync();
    var product = await _productService.GetProductByIdAsync(productId);
    if (product == null)
      throw new InvalidOperationException("Product not found");

    var warnings = await _shoppingCartService.AddToCartAsync(
      customer,
      product,
      ShoppingCartType.ShoppingCart,
      store.Id,
      attributesXml,
      quantity: quantity);

    if (warnings.Any())
      throw new InvalidOperationException(string.Join("; ", warnings));

    return await GetCartAsync(customer);
  }

  public virtual async Task<HeadlessCartDto> UpdateItemAsync(Customer customer, int lineId, int quantity)
  {
    if (quantity <= 0)
      return await RemoveItemAsync(customer, lineId);

    var warnings = await _shoppingCartService.UpdateShoppingCartItemAsync(
      customer,
      lineId,
      attributesXml: null,
      customerEnteredPrice: decimal.Zero,
      rentalStartDate: null,
      rentalEndDate: null,
      quantity: quantity);

    if (warnings.Any())
      throw new InvalidOperationException(string.Join("; ", warnings));

    return await GetCartAsync(customer);
  }

  public virtual async Task<HeadlessCartDto> RemoveItemAsync(Customer customer, int lineId)
  {
    await _shoppingCartService.DeleteShoppingCartItemAsync(lineId);
    return await GetCartAsync(customer);
  }

  protected virtual async Task<HeadlessCartDto> MapCartAsync(Customer customer, IList<ShoppingCartItem> cart, int storeId)
  {
    var currency = await _workContext.GetWorkingCurrencyAsync();
    var storeLocation = _webHelper.GetStoreLocation();
    var lines = new List<HeadlessCartLineDto>();
    var totalQuantity = 0;

    foreach (var item in cart)
    {
      var product = await _productService.GetProductByIdAsync(item.ProductId);
      if (product == null)
        continue;

      var (subTotal, _, _, _) = await _shoppingCartService.GetSubTotalAsync(item, true);
      var subTotalConverted = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(subTotal, currency);
      var name = await _localizationService.GetLocalizedAsync(product, x => x.Name);
      var handle = await _urlRecordService.GetSeNameAsync(product);
      var pictures = await _pictureService.GetPicturesByProductIdAsync(product.Id, 1);
      string imageUrl;
      if (pictures.Any())
        imageUrl = await _pictureService.GetPictureUrlAsync(pictures.First(), _mediaSettings.ProductThumbPictureSize, true, storeLocation);
      else
        imageUrl = await _pictureService.GetDefaultPictureUrlAsync(_mediaSettings.ProductThumbPictureSize, PictureType.Entity, storeLocation);

      var merchandiseId = item.ProductId.ToString();
      var lineMoney = FormatMoney(subTotalConverted, currency.CurrencyCode);

      lines.Add(new HeadlessCartLineDto(
        Id: item.Id.ToString(),
        Quantity: item.Quantity,
        Cost: new HeadlessLineCostDto(lineMoney),
        Merchandise: new HeadlessCartMerchandiseDto(
          Id: merchandiseId,
          Title: name,
          SelectedOptions: new List<HeadlessSelectedOptionDto>(),
          Product: new HeadlessCartProductDto(product.Id.ToString(), handle, name, new HeadlessImageDto(imageUrl, name, 0, 0)))));

      totalQuantity += item.Quantity;
    }

    var (cartTotal, _, _, _, _, _) = await _orderTotalCalculationService.GetShoppingCartTotalAsync(cart);
    var total = cartTotal ?? 0;
    var totalConverted = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(total, currency);
    var money = FormatMoney(totalConverted, currency.CurrencyCode);

    return new HeadlessCartDto(
      Id: customer.CustomerGuid.ToString(),
      CheckoutUrl: string.Empty,
      Cost: new HeadlessCartCostDto(money, money, FormatMoney(0, currency.CurrencyCode)),
      Lines: lines,
      TotalQuantity: totalQuantity);
  }

  protected virtual HeadlessMoneyDto FormatMoney(decimal amount, string currencyCode)
  {
    return new HeadlessMoneyDto(amount.ToString("0.00"), currencyCode.ToUpperInvariant());
  }
}
