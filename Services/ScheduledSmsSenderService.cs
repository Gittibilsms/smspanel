using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using Microsoft.EntityFrameworkCore;
using GittBilSmsCore.Helpers;
public class ScheduledSmsSenderService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ScheduledSmsSenderService> _logger;
    private static readonly TimeZoneInfo TurkeyTimeZone =
TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");

    public ScheduledSmsSenderService(
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<ScheduledSmsSenderService> logger)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScheduledOrdersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in scheduled SMS processor.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Run every minute
        }
    }

    private async Task ProcessScheduledOrdersAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GittBilSmsDbContext>();

        var turkeyZone = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        var nowInTurkey = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TurkeyTimeZone);
        var orders = await db.Orders
            .Include(o => o.Company)
            .Include(o => o.Api)
            .Where(o => o.CurrentStatus == "Scheduled" && o.ScheduledSendDate <= nowInTurkey)
            .ToListAsync();

        foreach (var order in orders)
        {
            try
            {
                var api = order.Api;
                var client = _httpClientFactory.CreateClient();
                var contentType = api.ContentType;
                var orderFolderPath = Path.Combine("D:\\home\\data", "orders", order.OrderId.ToString());
                var recipientsFile = Path.Combine(orderFolderPath, "recipients.txt");

                if (!File.Exists(recipientsFile))
                {
                    _logger.LogWarning("Recipients file not found for OrderId={OrderId}", order.OrderId);
                    continue;
                }

                var numbers = await File.ReadAllLinesAsync(recipientsFile);

                string requestBody = contentType switch
                {
                    "application/json" => JsonConvert.SerializeObject(new
                    {
                        Username = api.Username,
                        Password = api.Password,
                        Text = order.MessageText,
                        To = numbers
                    }),
                    "text/xml" => $@"<?xml version=""1.0"" encoding=""UTF-8""?><SendSms><Username>{api.Username}</Username><Password>{api.Password}</Password><From>{api.Originator}</From><Text>{order.MessageText}</Text><To>{string.Join(",", numbers)}</To></SendSms>",
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    _logger.LogWarning("Unsupported content type for OrderId={OrderId}", order.OrderId);
                    continue;
                }

                var request = new HttpRequestMessage(HttpMethod.Post, api.ApiUrl)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, contentType)
                };

                var response = await client.SendAsync(request);
                var result = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    order.CurrentStatus = "Failed";
                    order.ApiErrorResponse = $"HTTP {(int)response.StatusCode} - {result}";
                    order.Actions.Add(new OrderAction
                    {
                        ActionName = "Sending failed",
                        Message = order.ApiErrorResponse,
                        CreatedAt = nowInTurkey
                    });
                    // ✅ FIX: Refund the scheduled deduction
                    if (order.TotalPrice > 0 && order.Returned == false && order.Company != null)
                    {
                        order.Company.CreditLimit += order.TotalPrice.Value;
                        order.Refundable = true;
                        order.Returned = true;
                        order.ReturnDate = nowInTurkey;

                        db.BalanceHistory.Add(new BalanceHistory
                        {
                            CompanyId = order.CompanyId,
                            Amount = order.TotalPrice.Value,
                            Action = "Refund on Failed (Scheduled)",
                            OrderId = order.OrderId,
                            CreatedAt = nowInTurkey,
                            CreatedByUserId = 1
                        });
                        db.CreditTransactions.Add(new CreditTransaction
                        {
                            CompanyId = order.CompanyId,
                            TransactionType = "Order_Cancellation",
                            Credit = order.TotalPrice.Value,
                            Currency = "TRY",
                            TransactionDate = nowInTurkey,
                            Note = $"Sipariş iadesi (Scheduled HTTP Error) - Order #{order.OrderId}",
                            UnitPrice = 0,
                            TotalPrice = 0
                        });
                    }
                    _logger.LogError("Send failed for OrderId={OrderId}: {Error}", order.OrderId, order.ApiErrorResponse);
                }
                else
                {
                    dynamic json = JsonConvert.DeserializeObject(result);
                    string status = (string)(json?.Status ?? "");

                    if (status == "OK")
                    {
                        order.SmsOrderId = (string)(json?.MessageId ?? "unknown");
                        order.CurrentStatus = "Sent";
                        order.StartedAt = nowInTurkey;
                        order.CompletedAt = nowInTurkey;
                        order.ReportLock = true;

                        order.Actions.Add(new OrderAction
                        {
                            ActionName = "Scheduled sent",
                            Message = $"MessageId: {order.SmsOrderId}",
                            CreatedAt = nowInTurkey
                        });

                        _logger.LogInformation("Scheduled SMS sent for OrderId={OrderId}", order.OrderId);
                    }
                    else
                    {
                        // API returned 200 but logical error - refund
                        order.CurrentStatus = "Failed";
                        order.ApiErrorResponse = $"Status: {status}, Full Response: {result}";
                        order.Actions.Add(new OrderAction
                        {
                            ActionName = "Sending failed",
                            Message = order.ApiErrorResponse,
                            CreatedAt = nowInTurkey
                        });

                        // ✅ Refund
                        if (order.TotalPrice > 0 && order.Returned == false && order.Company != null)
                        {
                            order.Company.CreditLimit += order.TotalPrice.Value;
                            order.Refundable = true;
                            order.Returned = true;
                            order.ReturnDate = nowInTurkey;

                            db.BalanceHistory.Add(new BalanceHistory
                            {
                                CompanyId = order.CompanyId,
                                Amount = order.TotalPrice.Value,
                                Action = "Refund on Failed (Scheduled)",
                                OrderId = order.OrderId,
                                CreatedAt = nowInTurkey,
                                CreatedByUserId = 1
                            });
                            db.CreditTransactions.Add(new CreditTransaction
                            {
                                CompanyId = order.CompanyId,
                                TransactionType = "Order_Cancellation",
                                Credit = order.TotalPrice.Value,
                                Currency = "TRY",
                                TransactionDate = nowInTurkey,
                                Note = $"Sipariş iadesi (Scheduled API Status Error) - Order #{order.OrderId}",
                                UnitPrice = 0,
                                TotalPrice = 0
                            });
                        }

                        _logger.LogError("Scheduled SMS API status error for OrderId={OrderId}: {Status}", order.OrderId, status);
                    }
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scheduled SMS for OrderId={OrderId}", order.OrderId);
            }
        }
    }
}
