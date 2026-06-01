using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Topics;
using Nop.Core.Events;
using Nop.Services.Events;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Listens for catalog/content changes and triggers storefront cache revalidation webhooks
/// </summary>
public class EventConsumer :
    IConsumer<EntityInsertedEvent<Product>>,
    IConsumer<EntityUpdatedEvent<Product>>,
    IConsumer<EntityDeletedEvent<Product>>,
    IConsumer<EntityInsertedEvent<Category>>,
    IConsumer<EntityUpdatedEvent<Category>>,
    IConsumer<EntityDeletedEvent<Category>>,
    IConsumer<EntityInsertedEvent<ProductCategory>>,
    IConsumer<EntityDeletedEvent<ProductCategory>>,
    IConsumer<EntityInsertedEvent<Topic>>,
    IConsumer<EntityUpdatedEvent<Topic>>,
    IConsumer<EntityDeletedEvent<Topic>>
{
    #region Fields

    protected readonly WebhookNotificationService _webhookNotificationService;

    #endregion

    #region Ctor

    public EventConsumer(WebhookNotificationService webhookNotificationService)
    {
        _webhookNotificationService = webhookNotificationService;
    }

    #endregion

    #region Methods

    public async Task HandleEventAsync(EntityInsertedEvent<Product> eventMessage) =>
        await _webhookNotificationService.NotifyAsync(HeadlessApiDefaults.ProductsTopic);

    public async Task HandleEventAsync(EntityUpdatedEvent<Product> eventMessage) =>
        await _webhookNotificationService.NotifyAsync(HeadlessApiDefaults.ProductsTopic);

    public async Task HandleEventAsync(EntityDeletedEvent<Product> eventMessage) =>
        await _webhookNotificationService.NotifyAsync(HeadlessApiDefaults.ProductsTopic);

    public async Task HandleEventAsync(EntityInsertedEvent<Category> eventMessage) =>
        await _webhookNotificationService.NotifyAsync(HeadlessApiDefaults.CollectionsTopic);

    public async Task HandleEventAsync(EntityUpdatedEvent<Category> eventMessage) =>
        await _webhookNotificationService.NotifyAsync(HeadlessApiDefaults.CollectionsTopic);

    public async Task HandleEventAsync(EntityDeletedEvent<Category> eventMessage) =>
        await _webhookNotificationService.NotifyAsync(HeadlessApiDefaults.CollectionsTopic);

    public async Task HandleEventAsync(EntityInsertedEvent<ProductCategory> eventMessage) =>
        await _webhookNotificationService.NotifyAsync(HeadlessApiDefaults.CollectionsTopic);

    public async Task HandleEventAsync(EntityDeletedEvent<ProductCategory> eventMessage) =>
        await _webhookNotificationService.NotifyAsync(HeadlessApiDefaults.CollectionsTopic);

    public async Task HandleEventAsync(EntityInsertedEvent<Topic> eventMessage) =>
        await _webhookNotificationService.NotifyAsync(HeadlessApiDefaults.CollectionsTopic);

    public async Task HandleEventAsync(EntityUpdatedEvent<Topic> eventMessage) =>
        await _webhookNotificationService.NotifyAsync(HeadlessApiDefaults.CollectionsTopic);

    public async Task HandleEventAsync(EntityDeletedEvent<Topic> eventMessage) =>
        await _webhookNotificationService.NotifyAsync(HeadlessApiDefaults.CollectionsTopic);

    #endregion
}
