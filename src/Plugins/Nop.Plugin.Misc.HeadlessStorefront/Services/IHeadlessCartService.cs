using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.HeadlessStorefront.Models;

namespace Nop.Plugin.Misc.HeadlessStorefront.Services;

public interface IHeadlessCartService
{
  Task<HeadlessCartDto> GetCartAsync(Customer customer);

  Task<HeadlessCartDto> AddItemAsync(Customer customer, int productId, int quantity, string attributesXml = null);

  Task<HeadlessCartDto> UpdateItemAsync(Customer customer, int lineId, int quantity);

  Task<HeadlessCartDto> RemoveItemAsync(Customer customer, int lineId);
}
