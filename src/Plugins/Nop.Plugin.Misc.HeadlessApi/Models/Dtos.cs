namespace Nop.Plugin.Misc.HeadlessApi.Models;

/// <summary>
/// These DTOs mirror the storefront-facing domain types expected by Vercel Commerce
/// (see lib/types.ts on the storefront). They are serialized to camelCase JSON.
/// </summary>
public class MoneyDto
{
    public string Amount { get; set; } = "0";
    public string CurrencyCode { get; set; } = "USD";
}

public class ImageDto
{
    public string Url { get; set; }
    public string AltText { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class SeoDto
{
    public string Title { get; set; }
    public string Description { get; set; }
}

public class SelectedOptionDto
{
    public string Name { get; set; }
    public string Value { get; set; }
}

public class ProductOptionDto
{
    public string Id { get; set; }
    public string Name { get; set; }
    public List<string> Values { get; set; } = new();
}

public class ProductVariantDto
{
    public string Id { get; set; }
    public string Title { get; set; }
    public bool AvailableForSale { get; set; }
    public List<SelectedOptionDto> SelectedOptions { get; set; } = new();
    public MoneyDto Price { get; set; } = new();
}

public class PriceRangeDto
{
    public MoneyDto MaxVariantPrice { get; set; } = new();
    public MoneyDto MinVariantPrice { get; set; } = new();
}

public class ProductDto
{
    public string Id { get; set; }
    public string Handle { get; set; }
    public bool AvailableForSale { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string DescriptionHtml { get; set; }
    public List<ProductOptionDto> Options { get; set; } = new();
    public PriceRangeDto PriceRange { get; set; } = new();
    public List<ProductVariantDto> Variants { get; set; } = new();
    public ImageDto FeaturedImage { get; set; }
    public List<ImageDto> Images { get; set; } = new();
    public SeoDto Seo { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string UpdatedAt { get; set; }
}

public class CollectionDto
{
    public string Handle { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public SeoDto Seo { get; set; } = new();
    public string UpdatedAt { get; set; }
    public string Path { get; set; }
}

public class MenuDto
{
    public string Title { get; set; }
    public string Path { get; set; }
}

public class PageDto
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Handle { get; set; }
    public string Body { get; set; }
    public string BodySummary { get; set; }
    public SeoDto Seo { get; set; }
    public string CreatedAt { get; set; }
    public string UpdatedAt { get; set; }
}

public class CartProductDto
{
    public string Id { get; set; }
    public string Handle { get; set; }
    public string Title { get; set; }
    public ImageDto FeaturedImage { get; set; }
}

public class CartMerchandiseDto
{
    public string Id { get; set; }
    public string Title { get; set; }
    public List<SelectedOptionDto> SelectedOptions { get; set; } = new();
    public CartProductDto Product { get; set; } = new();
}

public class CartItemCostDto
{
    public MoneyDto TotalAmount { get; set; } = new();
}

public class CartItemDto
{
    public string Id { get; set; }
    public int Quantity { get; set; }
    public CartItemCostDto Cost { get; set; } = new();
    public CartMerchandiseDto Merchandise { get; set; } = new();
}

public class CartCostDto
{
    public MoneyDto SubtotalAmount { get; set; } = new();
    public MoneyDto TotalAmount { get; set; } = new();
    public MoneyDto TotalTaxAmount { get; set; } = new();
}

public class CartDto
{
    public string Id { get; set; }
    public string CheckoutUrl { get; set; }
    public CartCostDto Cost { get; set; } = new();
    public int TotalQuantity { get; set; }
    public List<CartItemDto> Lines { get; set; } = new();
}
