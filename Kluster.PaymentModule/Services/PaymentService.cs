using System.Text.Json;
using ErrorOr;
using Kluster.PaymentModule.Data;
using Kluster.PaymentModule.ServiceErrors;
using Kluster.PaymentModule.Services.Contracts;
using Kluster.Shared.Constants;
using Kluster.Shared.Domain;
using Kluster.Shared.DTOs.Requests.Payments;
using Kluster.Shared.DTOs.Requests.Wallet;
using Kluster.Shared.DTOs.Responses.Payments;
using Kluster.Shared.Exceptions;
using Kluster.Shared.MessagingContracts.Commands.Payment;
using Kluster.Shared.MessagingContracts.Events;
using Kluster.Shared.MessagingContracts.Events.Invoices;
using Kluster.Shared.ServiceErrors;
using Kluster.Shared.SharedContracts.BusinessModule;
using Kluster.Shared.SharedContracts.PaymentModule;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Kluster.PaymentModule.Services;

public class PaymentService(
    PaymentModuleDbContext context,
    IPaystackService paystackService,
    IBus bus,
    IPayStackClient payStackClient,
    IWalletService walletService,
    ILogger<PaymentService> logger) : IPaymentService
{
    public async Task DeleteAllPaymentsLinkedToBusiness(DeletePaymentsForBusiness command)
    {
        var payments = await context.Payments
            .Where(x => x.BusinessId == command.BusinessId)
            .ToListAsync();

        context.RemoveRange(payments);
        await context.SaveChangesAsync();

        logger.LogInformation("Deleted {0} payments linked to business: {1}.", payments.Count, command.BusinessId);
    }

    public async Task DeleteAllPaymentsLinkedToClient(DeletePaymentsForClient command)
    {
        var payments = await context.Payments
            .Where(x => x.ClientId == command.ClientId)
            .ToListAsync();

        context.RemoveRange(payments);
        await context.SaveChangesAsync();

        logger.LogInformation("Deleted {0} payments linked to client: {1}.", payments.Count, command.ClientId);
    }

    public async Task DeleteAllPaymentsLinkedToInvoice(DeletePaymentsForInvoice command)
    {
        var payments = await context.Payments
            .Where(x => x.InvoiceId == command.InvoiceId)
            .ToListAsync();

        context.RemoveRange(payments);
        await context.SaveChangesAsync();

        logger.LogInformation("Deleted {0} payments linked to invoice: {1}.", payments.Count, command.InvoiceId);
    }

    public async Task<ErrorOr<PaymentDetailsResponse>> GetPaymentDetails(string invoiceNo)
    {
        var invoice = await context.Invoices.FirstOrDefaultAsync(x => x.InvoiceNo == invoiceNo);
        if (invoice is null)
        {
            logger.LogError("Invoice {0} not found.", invoiceNo);
            return SharedErrors<Invoice>.NotFound;
        }


        var payment = await context.Payments.FirstOrDefaultAsync(x => x.InvoiceId == invoiceNo);
        if (payment is null)
        {
            logger.LogError("Payment not found for invoice {0}.", invoiceNo);
            return SharedErrors<Payment>.NotFound;
        }

        // we only want incomplete payments
        if (payment.IsCompleted)
        {
            logger.LogError("Payment has already been made for invoice {0}.", invoiceNo);
            return Errors.Invoice.PaymentAlreadyCompleted;
        }

        return new PaymentDetailsResponse(invoice.ClientEmailAddress, invoice.Amount * 100);
    }

    public async Task CreatePayment(InvoiceCreatedEvent invoiceCreatedEvent)
    {
        if (await context.Payments.AnyAsync(x => x.InvoiceId == invoiceCreatedEvent.InvoiceId))
        {
            logger.LogError("Payment already exists for invoice: {0}.", invoiceCreatedEvent.InvoiceId);
            throw new PaymentAlreadyExistsException(
                $"Payment already exists for invoice: {invoiceCreatedEvent.InvoiceId}.");
        }

        var payment = PaymentModuleMapper.ToPayment(invoiceCreatedEvent);
        await context.AddAsync(payment);
        await context.SaveChangesAsync();
        logger.LogInformation("Created payment for: {0}.", invoiceCreatedEvent.InvoiceId);
    }

    public async Task<ErrorOr<Success>> ProcessPaymentNotification(PaystackNotification request, string ipAddress)
    {
        // temporary. in prod, use IP address here to verify source, then add to queue to complete transaction
        var isTransactionValid = await paystackService.VerifyTransaction(request.data.reference);
        if (!isTransactionValid)
        {
            logger.LogError("Payment notification not valid for reference: {0}.", request.data.reference);
            return Errors.Payment.NotValid;
        }

        await bus.Publish(new PaymentNotificationReceived(request.data.status, request.data.amount,
            request.data.reference));
        logger.LogInformation("Payment notification processed for reference: {0}.", request.data.reference);
        return Result.Success;
    }

    public async Task<ErrorOr<InvoicePaymentValidated>> IsPaystackTransactionNotificationValid(
        PaymentNotificationReceived contextMessage)
    {
        var notification = await payStackClient.VerifyTransaction(contextMessage.DataReference);
        if (notification is null)
        {
            logger.LogError("Could not find transaction with reference: {0}.", contextMessage.DataReference);
            return Errors.Payment.NotValid;
        }

        var invoice = await context.Invoices.FirstOrDefaultAsync(x => x.InvoiceNo == contextMessage.DataReference);
        if (invoice is null)
        {
            logger.LogError("Could not find invoice with reference: {0}.", contextMessage.DataReference);
            return SharedErrors<Invoice>.NotFound;
        }

        if ((decimal)notification.data.amount * 100 == invoice.Amount && notification.data.status == "success")
        {
            logger.LogInformation("Validated invoice payment with reference: {0}.", contextMessage.DataReference);
            return new InvoicePaymentValidated(invoice.InvoiceNo, notification.data.amount, notification.data.channel);
        }

        logger.LogError("Could not validate invoice with reference: {0}." +
                        "\nNotification: {1}", contextMessage.DataReference, JsonSerializer.Serialize(notification));
        return Errors.Payment.NotValid;
    }

    public async Task<ErrorOr<Success>> CompletePayment(InvoicePaymentValidated invoiceCreatedEvent)
    {
        var payment = await context.Payments.FirstOrDefaultAsync(c => c.InvoiceId == invoiceCreatedEvent.InvoiceId);
        if (payment is null)
        {
            logger.LogError("Could not find payment with InvoiceId: {0}.", invoiceCreatedEvent.InvoiceId);
            return SharedErrors<Payment>.NotFound;
        }

        if (payment.IsCompleted)
        {
            logger.LogInformation("Payment with InvoiceId: {0} is already completed.", invoiceCreatedEvent.InvoiceId);
            return Errors.Payment.AlreadyCompleted;
        }

        var invoice = await context.Invoices.FirstOrDefaultAsync(x => x.InvoiceNo == invoiceCreatedEvent.InvoiceId);
        if (invoice is null)
        {
            logger.LogError("Could not find invoice with InvoiceId: {0}.", invoiceCreatedEvent.InvoiceId);
            return SharedErrors<Invoice>.NotFound;
        }

        if (invoice.Status == InvoiceStatus.Paid.ToString())
        {
            logger.LogInformation("Invoice with InvoiceId: {0} is already paid.", invoiceCreatedEvent.InvoiceId);
            return Errors.Invoice.PaymentAlreadyCompleted;
        }

        var amountInNaira = (decimal)invoiceCreatedEvent.AmountInKobo / 100;
        logger.LogInformation(
            "Crediting wallet for BusinessId: {0} with amount: NGN{1}.", payment.BusinessId, amountInNaira);
        walletService.CreditWallet(new CreditWalletRequest(payment.BusinessId, amountInNaira));

        invoice.Status = InvoiceStatus.Paid.ToString();

        payment.PaymentChannel = invoiceCreatedEvent.PaymentChannel;
        payment.IsCompleted = true;
        payment.DateOfPayment = DateTime.UtcNow;

        context.Update(invoice);
        context.Update(payment);
        await context.SaveChangesAsync();

        logger.LogInformation("Payment completed for InvoiceId: {0}.", invoiceCreatedEvent.InvoiceId);
        return Result.Success;
    }
}