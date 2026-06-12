using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EtisalatSaasCallback.Configuration;
using EtisalatSaasCallback.Models;
using Microsoft.Extensions.Options;

namespace EtisalatSaasCallback.Services;

public interface IEtisalatCallbackClient
{
    Task<IsvProvisioningStatusResponse> SendProvisioningStatusAsync(IsvProvisioningStatusRequest request);
}

public class EtisalatCallbackClient : IEtisalatCallbackClient
{
    private readonly HttpClient _httpClient;
    private readonly EtisalatSettings _settings;
    private readonly IMongoDbService _mongoDbService;
    private readonly ILogger<EtisalatCallbackClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public EtisalatCallbackClient(
        HttpClient httpClient,
        IOptions<EtisalatSettings> settings,
        IMongoDbService mongoDbService,
        ILogger<EtisalatCallbackClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _mongoDbService = mongoDbService;
        _logger = logger;

        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.Username}:{_settings.Password}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EtisalatSaasCallback/1.0");
    }

    public async Task<IsvProvisioningStatusResponse> SendProvisioningStatusAsync(IsvProvisioningStatusRequest request)
    {
        // Service attributes are driven entirely by configuration (empty by default).
        request.ServiceAttribute = _settings.ServiceAttributes
            .Select(a => new ServiceAttribute { Name = a.Name, Value = a.Value })
            .ToList();

        var wrapper = new IsvProvisioningStatusRequestWrapper
        {
            IsvProvisioningStatusRequest = request
        };

        var json = JsonSerializer.Serialize(wrapper, JsonOptions);
        var fullUrl = new Uri(_httpClient.BaseAddress!, _settings.IsvProvisioningStatusEndpoint).ToString();

        var record = new ProvisioningRecord
        {
            ReferenceNumber = request.ReferenceNumber,
            SubscriptionId = request.SubscriptionId,
            BillingDate = request.BillingDate,
            Action = request.Action,
            ServiceAttributes = request.ServiceAttribute,
            Direction = CallbackDirection.Outbound,
            Status = ProvisioningStatus.Pending,
            RequestUrl = fullUrl,
            RequestBody = json
        };

        try
        {
            await _mongoDbService.CreateProvisioningRecordAsync(record);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "Sending provisioning status callback to {Url}. Reference: {ReferenceNumber}, Action: {Action}, Body: {Body}",
                fullUrl, request.ReferenceNumber, request.Action, json);

            var response = await _httpClient.PostAsync(_settings.IsvProvisioningStatusEndpoint, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            record.ResponseBody = responseBody;

            _logger.LogInformation("Etisalat response: {StatusCode} - {Body}",
                response.StatusCode, responseBody);

            if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(responseBody) || responseBody.TrimStart().StartsWith('<'))
            {
                _logger.LogError("Etisalat API returned non-JSON response. Status: {StatusCode}, Body: {Body}",
                    response.StatusCode, responseBody);

                record.Status = ProvisioningStatus.Failed;
                record.ResponseCode = ((int)response.StatusCode).ToString();
                record.ResponseDescription = $"Non-JSON response: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}";
                await _mongoDbService.UpdateProvisioningRecordAsync(record);

                return new IsvProvisioningStatusResponse
                {
                    ReferenceNumber = request.ReferenceNumber,
                    ResponseCode = ErrorCodes.InternalError,
                    Status = 1,
                    Description = $"API returned HTTP {response.StatusCode}"
                };
            }

            var responseWrapper = JsonSerializer.Deserialize<IsvProvisioningStatusResponseWrapper>(
                responseBody, JsonOptions);

            var result = responseWrapper?.IsvProvisioningStatusResponse ?? new IsvProvisioningStatusResponse
            {
                ReferenceNumber = request.ReferenceNumber,
                ResponseCode = ErrorCodes.InternalError,
                Status = 1,
                Description = "Failed to parse response"
            };

            record.ResponseCode = result.ResponseCode;
            record.ResponseDescription = result.Description;
            record.Status = result.ResponseCode == ErrorCodes.Success
                ? ProvisioningStatus.Success
                : ProvisioningStatus.Failed;

            await _mongoDbService.UpdateProvisioningRecordAsync(record);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error sending callback for reference: {ReferenceNumber}",
                request.ReferenceNumber);

            record.Status = ProvisioningStatus.Failed;
            record.LastError = ex.Message;
            record.RetryCount++;
            await _mongoDbService.UpdateProvisioningRecordAsync(record);

            return new IsvProvisioningStatusResponse
            {
                ReferenceNumber = request.ReferenceNumber,
                ResponseCode = ErrorCodes.InternalError,
                Status = 1,
                Description = $"HTTP Error: {ex.Message}"
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout sending callback for reference: {ReferenceNumber}",
                request.ReferenceNumber);

            record.Status = ProvisioningStatus.Failed;
            record.LastError = "Request timeout";
            record.RetryCount++;
            await _mongoDbService.UpdateProvisioningRecordAsync(record);

            return new IsvProvisioningStatusResponse
            {
                ReferenceNumber = request.ReferenceNumber,
                ResponseCode = ErrorCodes.InternalError,
                Status = 1,
                Description = "Request timeout"
            };
        }
    }
}
