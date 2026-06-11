using EtisalatSaasCallback.Models;
using EtisalatSaasCallback.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EtisalatSaasCallback.Controllers;

[ApiController]
[Route("rest/xaas/v1")]
[Produces("application/json")]
public class CallbackController : ControllerBase
{
    private readonly IProvisioningService _provisioningService;
    private readonly ILogger<CallbackController> _logger;

    public CallbackController(
        IProvisioningService provisioningService,
        ILogger<CallbackController> logger)
    {
        _provisioningService = provisioningService;
        _logger = logger;
    }

    /// <summary>
    /// Receive ISV Provisioning Status callback (Inbound from Etisalat/ISV)
    /// </summary>
    /// <param name="request">The provisioning status request</param>
    /// <returns>Provisioning status response</returns>
    [HttpPost("isvProvisioningStatus")]
    [Authorize(AuthenticationSchemes = "BasicAuth")]
    [ProducesResponseType(typeof(IsvProvisioningStatusResponseWrapper), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IsvProvisioningStatusResponseWrapper), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IsvProvisioningStatusResponseWrapper>> ReceiveCallback(
        [FromBody] IsvProvisioningStatusRequestWrapper request)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            _logger.LogWarning("Validation failed: {Errors}", string.Join(", ", errors));

            return BadRequest(new IsvProvisioningStatusResponseWrapper
            {
                IsvProvisioningStatusResponse = new IsvProvisioningStatusResponse
                {
                    ReferenceNumber = request.IsvProvisioningStatusRequest?.ReferenceNumber ?? "UNKNOWN",
                    ResponseCode = ErrorCodes.InputValidationError,
                    Status = 1,
                    Description = $"Input validation error: {string.Join(", ", errors)}",
                    ResponseAttributes = []
                }
            });
        }

        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var response = await _provisioningService.ProcessInboundCallbackAsync(
            request.IsvProvisioningStatusRequest, sourceIp);

        return Ok(new IsvProvisioningStatusResponseWrapper
        {
            IsvProvisioningStatusResponse = response
        });
    }

    /// <summary>
    /// Send ISV Provisioning Status callback to Etisalat (Outbound)
    /// </summary>
    /// <param name="request">The provisioning status request</param>
    /// <returns>Provisioning status response from Etisalat</returns>
    [HttpPost("sendCallback")]
    [Authorize(AuthenticationSchemes = "BasicAuth")]
    [ProducesResponseType(typeof(IsvProvisioningStatusResponseWrapper), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IsvProvisioningStatusResponseWrapper), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IsvProvisioningStatusResponseWrapper>> SendCallback(
        [FromBody] IsvProvisioningStatusRequestWrapper request)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .ToList();

            return BadRequest(new IsvProvisioningStatusResponseWrapper
            {
                IsvProvisioningStatusResponse = new IsvProvisioningStatusResponse
                {
                    ReferenceNumber = request.IsvProvisioningStatusRequest?.ReferenceNumber ?? "UNKNOWN",
                    ResponseCode = ErrorCodes.InputValidationError,
                    Status = 1,
                    Description = $"Input validation error: {string.Join(", ", errors)}",
                    ResponseAttributes = []
                }
            });
        }

        var response = await _provisioningService.SendOutboundCallbackAsync(
            request.IsvProvisioningStatusRequest);

        return Ok(new IsvProvisioningStatusResponseWrapper
        {
            IsvProvisioningStatusResponse = response
        });
    }
}
