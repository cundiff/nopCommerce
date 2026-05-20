using AwesomeAssertions;
using Moq;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Services.Admin;
using Nop.Services.Common;
using NUnit.Framework;

namespace Nop.Tests.Nop.Services.Tests.Admin;

[TestFixture]
public class AdminFilterPreferenceServiceTests
{
    private Mock<IGenericAttributeService> _genericAttributeService;
    private AdminAreaSettings _adminAreaSettings;
    private AdminFilterPreferenceService _service;
    private Customer _customer;

    [SetUp]
    public void SetUp()
    {
        _genericAttributeService = new Mock<IGenericAttributeService>();
        _adminAreaSettings = new AdminAreaSettings { EnableStickyFilters = true };
        _service = new AdminFilterPreferenceService(_adminAreaSettings, _genericAttributeService.Object);
        _customer = new Customer { Id = 1, Email = "admin@test.com" };
    }

    [Test]
    public void BuildKey_ReturnsExpectedFormat()
    {
        AdminFilterPreferenceDefaults.BuildKey("Order", "List")
            .Should().Be("Admin.StickyFilters.Order.List");

        AdminFilterPreferenceDefaults.BuildKey("Order", "List", "NestedSearchModel")
            .Should().Be("Admin.StickyFilters.Order.List.NestedSearchModel");
    }

    [Test]
    public async Task SaveFromJsonAsync_SavesAttributeWhenEnabled()
    {
        const string key = "Admin.StickyFilters.Log.List";
        const string json = "{\"Message\":\"error\"}";

        await _service.SaveFromJsonAsync(_customer, key, json);

        _genericAttributeService.Verify(x => x.SaveAttributeAsync(_customer, key, json, 0), Times.Once);
    }

    [Test]
    public async Task SaveFromJsonAsync_DoesNotSaveWhenDisabled()
    {
        _adminAreaSettings.EnableStickyFilters = false;

        await _service.SaveFromJsonAsync(_customer, "Admin.StickyFilters.Log.List", "{\"Message\":\"error\"}");

        _genericAttributeService.Verify(
            x => x.SaveAttributeAsync(It.IsAny<Customer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    [Test]
    public async Task ApplyToSearchModelAsync_RestoresFilterProperties()
    {
        const string key = "Admin.StickyFilters.Log.List";
        var searchModel = new TestLogSearchModel
        {
            AvailableLogLevels = new List<string> { "should-not-change" },
            Message = string.Empty,
            LogLevelId = 0
        };

        _genericAttributeService
            .Setup(x => x.GetAttributeAsync<string>(_customer, key, 0, null))
            .ReturnsAsync("{\"Message\":\"payment failed\",\"LogLevelId\":10}");

        await _service.ApplyToSearchModelAsync(_customer, key, searchModel);

        searchModel.Message.Should().Be("payment failed");
        searchModel.LogLevelId.Should().Be(10);
        searchModel.AvailableLogLevels.Should().ContainSingle().Which.Should().Be("should-not-change");
    }

    [Test]
    public async Task ApplyToSearchModelAsync_RestoresMultiSelectList()
    {
        const string key = "Admin.StickyFilters.Customer.List";
        var searchModel = new TestCustomerSearchModel();

        _genericAttributeService
            .Setup(x => x.GetAttributeAsync<string>(_customer, key, 0, null))
            .ReturnsAsync("{\"SelectedCustomerRoleIds\":[3,5]}");

        await _service.ApplyToSearchModelAsync(_customer, key, searchModel);

        searchModel.SelectedCustomerRoleIds.Should().BeEquivalentTo(new[] { 3, 5 });
    }

    [Test]
    public async Task IsEnabledAsync_ReflectsAdminAreaSetting()
    {
        (await _service.IsEnabledAsync()).Should().BeTrue();

        _adminAreaSettings.EnableStickyFilters = false;

        (await _service.IsEnabledAsync()).Should().BeFalse();
    }

    private class TestLogSearchModel
    {
        public IList<string> AvailableLogLevels { get; set; } = new List<string>();
        public string Message { get; set; }
        public int LogLevelId { get; set; }
        public int Start { get; set; }
    }

    private class TestCustomerSearchModel
    {
        public IList<int> SelectedCustomerRoleIds { get; set; } = new List<int> { 1 };
    }
}
