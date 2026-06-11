using EtisalatSaasCallback.Configuration;
using EtisalatSaasCallback.Models;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EtisalatSaasCallback.Services;

public interface IMongoDbService
{
    Task<ProvisioningRecord> CreateProvisioningRecordAsync(ProvisioningRecord record);
    Task<ProvisioningRecord?> GetByReferenceNumberAsync(string referenceNumber);
    Task<ProvisioningRecord?> GetBySubscriptionIdAsync(string subscriptionId);
    Task<List<ProvisioningRecord>> GetByStatusAsync(ProvisioningStatus status, int limit = 100);
    Task UpdateProvisioningRecordAsync(ProvisioningRecord record);

    Task<SubscriptionState?> GetSubscriptionStateAsync(string subscriptionId);
    Task<SubscriptionState> CreateOrUpdateSubscriptionStateAsync(SubscriptionState state);
    Task<bool> IsSubscriptionSuspendedAsync(string subscriptionId);
    Task<bool> IsSubscriptionCeasedAsync(string subscriptionId);
}

public class MongoDbService : IMongoDbService
{
    private readonly IMongoCollection<ProvisioningRecord> _provisioningCollection;
    private readonly IMongoCollection<SubscriptionState> _subscriptionCollection;
    private readonly ILogger<MongoDbService> _logger;

    public MongoDbService(IOptions<MongoDbSettings> settings, ILogger<MongoDbService> logger)
    {
        _logger = logger;
        var mongoClient = new MongoClient(settings.Value.ConnectionString);
        var database = mongoClient.GetDatabase(settings.Value.DatabaseName);

        _provisioningCollection = database.GetCollection<ProvisioningRecord>(settings.Value.ProvisioningCollection);
        _subscriptionCollection = database.GetCollection<SubscriptionState>(settings.Value.SubscriptionCollection);

        CreateIndexes();
    }

    private void CreateIndexes()
    {
        try
        {
            var provisioningIndexes = new[]
            {
                new CreateIndexModel<ProvisioningRecord>(
                    Builders<ProvisioningRecord>.IndexKeys.Ascending(x => x.ReferenceNumber),
                    new CreateIndexOptions { Unique = false }),
                new CreateIndexModel<ProvisioningRecord>(
                    Builders<ProvisioningRecord>.IndexKeys.Ascending(x => x.SubscriptionId)),
                new CreateIndexModel<ProvisioningRecord>(
                    Builders<ProvisioningRecord>.IndexKeys.Ascending(x => x.Status)),
                new CreateIndexModel<ProvisioningRecord>(
                    Builders<ProvisioningRecord>.IndexKeys.Descending(x => x.CreatedAt))
            };
            _provisioningCollection.Indexes.CreateMany(provisioningIndexes);

            var subscriptionIndexes = new[]
            {
                new CreateIndexModel<SubscriptionState>(
                    Builders<SubscriptionState>.IndexKeys.Ascending(x => x.SubscriptionId),
                    new CreateIndexOptions { Unique = true })
            };
            _subscriptionCollection.Indexes.CreateMany(subscriptionIndexes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create indexes, they may already exist");
        }
    }

    public async Task<ProvisioningRecord> CreateProvisioningRecordAsync(ProvisioningRecord record)
    {
        record.CreatedAt = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;
        await _provisioningCollection.InsertOneAsync(record);
        _logger.LogInformation("Created provisioning record for reference: {ReferenceNumber}", record.ReferenceNumber);
        return record;
    }

    public async Task<ProvisioningRecord?> GetByReferenceNumberAsync(string referenceNumber)
    {
        return await _provisioningCollection
            .Find(x => x.ReferenceNumber == referenceNumber)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<ProvisioningRecord?> GetBySubscriptionIdAsync(string subscriptionId)
    {
        return await _provisioningCollection
            .Find(x => x.SubscriptionId == subscriptionId)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<ProvisioningRecord>> GetByStatusAsync(ProvisioningStatus status, int limit = 100)
    {
        return await _provisioningCollection
            .Find(x => x.Status == status)
            .SortByDescending(x => x.CreatedAt)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task UpdateProvisioningRecordAsync(ProvisioningRecord record)
    {
        record.UpdatedAt = DateTime.UtcNow;
        await _provisioningCollection.ReplaceOneAsync(
            x => x.Id == record.Id,
            record);
        _logger.LogInformation("Updated provisioning record: {Id}", record.Id);
    }

    public async Task<SubscriptionState?> GetSubscriptionStateAsync(string subscriptionId)
    {
        return await _subscriptionCollection
            .Find(x => x.SubscriptionId == subscriptionId)
            .FirstOrDefaultAsync();
    }

    public async Task<SubscriptionState> CreateOrUpdateSubscriptionStateAsync(SubscriptionState state)
    {
        state.UpdatedAt = DateTime.UtcNow;

        var existing = await GetSubscriptionStateAsync(state.SubscriptionId);
        if (existing != null)
        {
            state.Id = existing.Id;
            state.CreatedAt = existing.CreatedAt;
            await _subscriptionCollection.ReplaceOneAsync(
                x => x.SubscriptionId == state.SubscriptionId,
                state);
        }
        else
        {
            state.Id = ObjectId.GenerateNewId().ToString();
            state.CreatedAt = DateTime.UtcNow;
            await _subscriptionCollection.InsertOneAsync(state);
        }

        _logger.LogInformation("Upserted subscription state for: {SubscriptionId}, State: {State}",
            state.SubscriptionId, state.State);
        return state;
    }

    public async Task<bool> IsSubscriptionSuspendedAsync(string subscriptionId)
    {
        var state = await GetSubscriptionStateAsync(subscriptionId);
        return state?.State == SubscriptionStateType.Suspended;
    }

    public async Task<bool> IsSubscriptionCeasedAsync(string subscriptionId)
    {
        var state = await GetSubscriptionStateAsync(subscriptionId);
        return state?.State is SubscriptionStateType.Ceased or SubscriptionStateType.Cancelled;
    }
}
