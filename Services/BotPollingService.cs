// Services/BotPollingService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace GittBilSmsCore.Services
{
    public class BotPollingService : BackgroundService
    {
        private readonly ITelegramBotClient _bot;
        private readonly IServiceProvider _sp;

        public BotPollingService(ITelegramBotClient bot, IServiceProvider sp)
        {
            _bot = bot;
            _sp = sp;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() // all
            };

            _bot.StartReceiving(
                async (client, update, token) =>
                {
                    try
                    {
                        using var scope = _sp.CreateScope(); // 🔑 new scope per update
                        var svc = scope.ServiceProvider.GetRequiredService<TelegramMessageService>();
                        Console.WriteLine($"[Update] type={update.Type}");

                        await svc.HandleUpdateAsync(update, token);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UpdateError] {ex}");
                    }
                },
                async (client, ex, token) =>
                {
                    Console.WriteLine($"[PollingError] {ex}");
                    await Task.CompletedTask;
                },
                receiverOptions,
                stoppingToken
            );

            // v22: method name is GetMe(...)
            var me = await _bot.GetMe(stoppingToken);
            Console.WriteLine($"🤖 Bot @{me.Username} running (polling).");
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}
