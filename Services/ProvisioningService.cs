using EtisalatSaasCallback.Models;

namespace EtisalatSaasCallback.Services;

public interface IProvisioningService
{
    Task<IsvProvisioningStatusResponse> ProcessInboundCallbackAsync(
        IsvProvisioningStatusRequest request, string? sourceIp);
    Task<IsvProvisioningStatusResponse> SendOutboundCallbackAsync(IsvProvisioningStatusRequest request);
}

public class ProvisioningService : IProvisioningService
{
    private readonly IMongoDbService _mongoDbService;
    private readonly IEtisalatCallbackClient _etisalatClient;
    private readonly ILogger<ProvisioningService> _logger;

    public ProvisioningService(
        IMongoDbService mongoDbService,
        IEtisalatCallbackClient etisalatClient,
        ILogger<ProvisioningService> logger)
    {
        _mongoDbService = mongoDbService;
        _etisalatClient = etisalatClient;
        _logger = logger;
    }

    public async Task<IsvProvisioningStatusResponse> ProcessInboundCallbackAsync(
        IsvProvisioningStatusRequest request, string? sourceIp)
    {
        _logger.LogInformation(
            "Processing inbound callback. Reference: {ReferenceNumber}, Action: {Action}, SourceIP: {SourceIp}",
            request.ReferenceNumber, request.Action, sourceIp);

        var validationResult = await ValidateRequestAsync(request);
        if (validationResult != null)
        {
            return validationResult;
        }

        var record = new ProvisioningRecord
        {
            ReferenceNumber = request.ReferenceNumber,
            SubscriptionId = request.SubscriptionId,
            BillingDate = request.BillingDate,
            Action = request.Action,
            ServiceAttributes = request.ServiceAttribute,
            Direction = CallbackDirection.Inbound,
            SourceIp = sourceIp,
            Status = ProvisioningStatus.Success
        };

        await _mongoDbService.CreateProvisioningRecordAsync(record);
        await UpdateSubscriptionStateAsync(request);

        _logger.LogInformation(
            "Successfully processed inbound callback for reference: {ReferenceNumber}",
            request.ReferenceNumber);

        return CreateSuccessResponse(request.ReferenceNumber);
    }

    public async Task<IsvProvisioningStatusResponse> SendOutboundCallbackAsync(IsvProvisioningStatusRequest request)
    {
        _logger.LogInformation(
            "Sending outbound callback. Reference: {ReferenceNumber}, Action: {Action}",
            request.ReferenceNumber, request.Action);

        if (!ProvisioningAction.IsValid(request.Action))
        {
            return CreateErrorResponse(request.ReferenceNumber, ErrorCodes.InvalidAction);
        }

        return await _etisalatClient.SendProvisioningStatusAsync(request);
    }

    private async Task<IsvProvisioningStatusResponse?> ValidateRequestAsync(IsvProvisioningStatusRequest request)
    {
        if (!ProvisioningAction.IsValid(request.Action))
        {
            return CreateErrorResponse(request.ReferenceNumber, ErrorCodes.InvalidAction);
        }

        var subscriptionState = await _mongoDbService.GetSubscriptionStateAsync(request.SubscriptionId);

        if (subscriptionState != null)
        {
            if (subscriptionState.State is SubscriptionStateType.Ceased or SubscriptionStateType.Cancelled)
            {
                _logger.LogWarning(
                    "Rejecting callback for ceased subscription: {SubscriptionId}",
                    request.SubscriptionId);
                return CreateErrorResponse(request.ReferenceNumber, ErrorCodes.AccountCeasedNoCallback);
            }

            if (subscriptionState.State == SubscriptionStateType.Suspended &&
                request.Action.Equals(ProvisioningAction.Accepted, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Rejecting ACCEPTED callback for suspended subscription: {SubscriptionId}",
                    request.SubscriptionId);
                return CreateErrorResponse(request.ReferenceNumber, ErrorCodes.AccountSuspendedCannotAccept);
            }
        }

        return null;
    }

    private async Task UpdateSubscriptionStateAsync(IsvProvisioningStatusRequest request)
    {
        var state = await _mongoDbService.GetSubscriptionStateAsync(request.SubscriptionId)
            ?? new SubscriptionState
            {
                SubscriptionId = request.SubscriptionId,
                CreatedAt = DateTime.UtcNow
            };

        state.State = request.Action.ToUpperInvariant() switch
        {
            ProvisioningAction.Accepted => SubscriptionStateType.Active,
            ProvisioningAction.Rejected => SubscriptionStateType.Rejected,
            ProvisioningAction.Expired => SubscriptionStateType.Expired,
            _ => state.State
        };

        if (request.Action.Equals(ProvisioningAction.Accepted, StringComparison.OrdinalIgnoreCase))
        {
            state.ActivatedAt = DateTime.UtcNow;
        }

        await _mongoDbService.CreateOrUpdateSubscriptionStateAsync(state);
    }

    private static IsvProvisioningStatusResponse CreateSuccessResponse(string referenceNumber)
    {
        return new IsvProvisioningStatusResponse
        {
            ReferenceNumber = referenceNumber,
            ResponseCode = ErrorCodes.Success,
            Status = 0,
            Description = ErrorCodes.GetDescription(ErrorCodes.Success),
            ResponseAttributes = []
        };
    }

    private static IsvProvisioningStatusResponse CreateErrorResponse(string referenceNumber, string errorCode)
    {
        return new IsvProvisioningStatusResponse
        {
            ReferenceNumber = referenceNumber,
            ResponseCode = errorCode,
            Status = 1,
            Description = ErrorCodes.GetDescription(errorCode),
            ResponseAttributes = []
        };
    }
}
