using AwesomeAssertions;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Services.Orders;
using NUnit.Framework;

namespace Nop.Tests.Nop.Services.Tests.Orders;

[TestFixture]
public class CustomWishlistServiceTests : BaseNopTest
{
    private readonly List<CustomWishlist> _customWishlists = new();
    private readonly List<WishlistShare> _wishlistShares = new();

    private IRepository<Customer> _customerRepository;
    private IRepository<CustomWishlist> _customWishlistRepository;
    private IRepository<WishlistShare> _wishlistShareRepository;
    private ICustomWishlistService _customWishlistService;
    private Customer _customer;

    [SetUp]
    public async Task SetUp()
    {
        _customerRepository = GetService<IRepository<Customer>>();
        _customWishlistRepository = GetService<IRepository<CustomWishlist>>();
        _wishlistShareRepository = GetService<IRepository<WishlistShare>>();
        _customWishlistService = GetService<ICustomWishlistService>();
        _customer = await GetService<IWorkContext>().GetCurrentCustomerAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var wishlistShare in _wishlistShares.Where(share => share.Id > 0).ToList())
        {
            await _wishlistShareRepository.DeleteAsync(wishlistShare);
            _wishlistShares.Remove(wishlistShare);
        }

        foreach (var customWishlist in _customWishlists.Where(wishlist => wishlist.Id > 0).ToList())
        {
            await _customWishlistRepository.DeleteAsync(customWishlist);
            _customWishlists.Remove(customWishlist);
        }
    }

    [Test]
    public async Task CanGenerateDefaultWishlistShare()
    {
        var before = DateTime.UtcNow;
        var wishlistShare = await _customWishlistService.GenerateWishlistShareAsync(_customer.Id, null, 7);
        var after = DateTime.UtcNow;
        _wishlistShares.Add(wishlistShare);

        wishlistShare.ShareGuid.Should().NotBe(Guid.Empty);
        wishlistShare.CustomerId.Should().Be(_customer.Id);
        wishlistShare.CustomWishlistId.Should().BeNull();
        wishlistShare.CreatedOnUtc.Should().BeOnOrAfter(before);
        wishlistShare.CreatedOnUtc.Should().BeOnOrBefore(after);
        wishlistShare.ExpiresOnUtc.Should().BeOnOrAfter(before.AddDays(7));
        wishlistShare.ExpiresOnUtc.Should().BeOnOrBefore(after.AddDays(7));
    }

    [Test]
    public async Task CanGenerateOwnedCustomWishlistShare()
    {
        var customWishlist = await CreateCustomWishlistAsync(_customer.Id);

        var wishlistShare = await _customWishlistService.GenerateWishlistShareAsync(_customer.Id, customWishlist.Id, 30);
        _wishlistShares.Add(wishlistShare);

        wishlistShare.CustomerId.Should().Be(_customer.Id);
        wishlistShare.CustomWishlistId.Should().Be(customWishlist.Id);
        wishlistShare.ExpiresOnUtc.Should().BeAfter(wishlistShare.CreatedOnUtc.AddDays(29));
    }

    [Test]
    public async Task CannotGenerateWishlistShareWithUnsupportedExpiration()
    {
        Func<Task> action = async () => await _customWishlistService.GenerateWishlistShareAsync(_customer.Id, null, 14);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public async Task CannotGenerateShareForAnotherCustomersCustomWishlist()
    {
        var anotherCustomer = (await _customerRepository.Table.ToListAsync()).First(customer => customer.Id != _customer.Id);
        var customWishlist = await CreateCustomWishlistAsync(anotherCustomer.Id);

        Func<Task> action = async () => await _customWishlistService.GenerateWishlistShareAsync(_customer.Id, customWishlist.Id, 7);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task CanResolveActiveWishlistShareByGuid()
    {
        var wishlistShare = await _customWishlistService.GenerateWishlistShareAsync(_customer.Id, null, 90);
        _wishlistShares.Add(wishlistShare);

        var resolvedShare = await _customWishlistService.GetWishlistShareByGuidAsync(wishlistShare.ShareGuid);

        resolvedShare.Should().NotBeNull();
        resolvedShare.Id.Should().Be(wishlistShare.Id);
    }

    [Test]
    public async Task CannotResolveExpiredWishlistShareAsActive()
    {
        var expiredWishlistShare = new WishlistShare
        {
            ShareGuid = Guid.NewGuid(),
            CustomerId = _customer.Id,
            CreatedOnUtc = DateTime.UtcNow.AddDays(-10),
            ExpiresOnUtc = DateTime.UtcNow.AddDays(-1)
        };
        await _wishlistShareRepository.InsertAsync(expiredWishlistShare);
        _wishlistShares.Add(expiredWishlistShare);

        var activeShare = await _customWishlistService.GetWishlistShareByGuidAsync(expiredWishlistShare.ShareGuid);
        var anyShare = await _customWishlistService.GetWishlistShareByGuidAsync(expiredWishlistShare.ShareGuid, false);

        activeShare.Should().BeNull();
        anyShare.Should().NotBeNull();
        anyShare.Id.Should().Be(expiredWishlistShare.Id);
    }

    private async Task<CustomWishlist> CreateCustomWishlistAsync(int customerId)
    {
        var customWishlist = new CustomWishlist
        {
            CustomerId = customerId,
            Name = $"Test wishlist {Guid.NewGuid():N}",
            CreatedOnUtc = DateTime.UtcNow
        };

        await _customWishlistRepository.InsertAsync(customWishlist);
        _customWishlists.Add(customWishlist);

        return customWishlist;
    }
}
