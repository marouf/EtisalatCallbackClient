using EtisalatSaasCallback.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EtisalatSaasCallback.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IMongoDbService _mongoDbService;
    private readonly ILogger<HealthController> _logger;

    public HealthController(IMongoDbService mongoDbService, ILogger<HealthController> logger)
    {
        _mongoDbService = mongoDbService;
        _logger = logger;
    }

    /// <summary>
    /// Basic health check endpoint
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            service = "Etisalat SaaS Callback Service"
        });
    }

    /// <summary>
    /// Detailed health check with MongoDB connectivity
    /// </summary>
    [HttpGet("detailed")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDetailed()
    {
        var mongoStatus = "healthy";
        try
        {
            await _mongoDbService.GetByStatusAsync(Models.ProvisioningStatus.Pending, 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MongoDB health check failed");
            mongoStatus = "unhealthy";
        }

        var isHealthy = mongoStatus == "healthy";

        return isHealthy
            ? Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                components = new
                {
                    mongodb = mongoStatus
                }
            })
            : StatusCode(503, new
            {
                status = "unhealthy",
                timestamp = DateTime.UtcNow,
                components = new
                {
                    mongodb = mongoStatus
                }
            });
    }
}
