using Nop.Plugin.Misc.HeadlessStorefront.Models;

namespace Nop.Plugin.Misc.HeadlessStorefront.Services;

public interface IHeadlessCatalogService
{
  Task<IList<HeadlessCollectionDto>> GetCollectionsAsync();

  Task<HeadlessCollectionDto> GetCollectionByHandleAsync(string handle);

  Task<IList<HeadlessProductDto>> GetProductsAsync(string query, string sortKey, bool reverse, int? categoryId = null);

  Task<HeadlessProductDto> GetProductByHandleAsync(string handle);

  Task<int?> ResolveCategoryIdByHandleAsync(string handle);
}
