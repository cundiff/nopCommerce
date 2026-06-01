namespace Nop.Plugin.Misc.HeadlessStorefront.Models;

public record HeadlessMoneyDto(string Amount, string CurrencyCode);

public record HeadlessImageDto(string Url, string AltText, int Width, int Height);

public record HeadlessSeoDto(string Title, string Description);

public record HeadlessProductOptionDto(string Id, string Name, IList<string> Values);

public record HeadlessProductVariantDto(
  string Id,
  string Title,
  bool AvailableForSale,
  IList<HeadlessSelectedOptionDto> SelectedOptions,
  HeadlessMoneyDto Price);

public record HeadlessSelectedOptionDto(string Name, string Value);

public record HeadlessProductDto(
  string Id,
  string Handle,
  bool AvailableForSale,
  string Title,
  string Description,
  string DescriptionHtml,
  IList<HeadlessProductOptionDto> Options,
  HeadlessPriceRangeDto PriceRange,
  HeadlessImageDto FeaturedImage,
  IList<HeadlessImageDto> Images,
  HeadlessSeoDto Seo,
  IList<string> Tags,
  string UpdatedAt,
  IList<HeadlessProductVariantDto> Variants);

public record HeadlessPriceRangeDto(HeadlessMoneyDto MaxVariantPrice, HeadlessMoneyDto MinVariantPrice);

public record HeadlessCollectionDto(
  string Handle,
  string Title,
  string Description,
  HeadlessSeoDto Seo,
  string Path,
  string UpdatedAt);

public record HeadlessCartProductDto(string Id, string Handle, string Title, HeadlessImageDto FeaturedImage);

public record HeadlessCartLineDto(
  string Id,
  int Quantity,
  HeadlessLineCostDto Cost,
  HeadlessCartMerchandiseDto Merchandise);

public record HeadlessLineCostDto(HeadlessMoneyDto TotalAmount);

public record HeadlessCartMerchandiseDto(
  string Id,
  string Title,
  IList<HeadlessSelectedOptionDto> SelectedOptions,
  HeadlessCartProductDto Product);

public record HeadlessCartDto(
  string Id,
  string CheckoutUrl,
  HeadlessCartCostDto Cost,
  IList<HeadlessCartLineDto> Lines,
  int TotalQuantity);

public record HeadlessCartCostDto(
  HeadlessMoneyDto SubtotalAmount,
  HeadlessMoneyDto TotalAmount,
  HeadlessMoneyDto TotalTaxAmount);

public record HeadlessSessionResponse(string SessionToken);

public record HeadlessCheckoutResponse(string CheckoutUrl);

public record HeadlessAddCartItemRequest(int ProductId, int Quantity, string AttributesXml);

public record HeadlessUpdateCartItemRequest(int Quantity);
