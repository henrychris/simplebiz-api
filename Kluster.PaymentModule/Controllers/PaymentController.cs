using ErrorOr;
using Kluster.Shared.API;
using Kluster.Shared.DTOs.Requests.Payments;
using Kluster.Shared.DTOs.Responses.Payments;
using Kluster.Shared.Extensions;
using Kluster.Shared.SharedContracts.PaymentModule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kluster.PaymentModule.Controllers;

public class PaymentController(IPaymentService paymentService) : BaseController
{
    [AllowAnonymous]
    [HttpGet("request-payment details")]
    public async Task<IActionResult> GetPaymentDetails(string invoiceId)
    {
        // for endpoints with allowAnonymous, consider generating a token on frontend before sending requests?

        // just return 0 and log errors.
        // todo: add logs along all request paths.
        var paymentDetailsResult = await paymentService.GetPaymentDetails(invoiceId);

        // If successful, return the event data in an ApiResponse.
        // If an error occurs, return an error response using the ReturnErrorResponse method.
        return paymentDetailsResult.Match(
            _ => Ok(paymentDetailsResult.ToSuccessfulApiResponse()),
            ReturnErrorResponse);
    }

    private string GetRemoteIpAddress()
    {
        // Retrieve the remote IP address if needed
        // Example:
        // return HttpContext.Connection.RemoteIpAddress?.ToString();
        return "ipAddress"; // Replace this with the actual logic to fetch the IP address
    }

    [AllowAnonymous]
    [HttpPost("complete")]
    public async Task<IActionResult> ProcessPaymentNotification(PaystackNotification notification)
    {
        var paymentCompletionRequest =
            await paymentService.ProcessPaymentNotification(notification, GetRemoteIpAddress());
        return paymentCompletionRequest.Match(_ => Ok(), ReturnErrorResponse);
    }
}