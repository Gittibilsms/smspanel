using DocumentFormat.OpenXml.InkML;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.Services;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
public class TelegramMessageService
{
    private readonly ITelegramBotClient _bot;
    private readonly GittBilSmsDbContext _context;
    private readonly TelegramAuditService _audit;
    public TelegramMessageService(ITelegramBotClient bot, GittBilSmsDbContext db, TelegramAuditService audit)
    {
        _bot = bot; _context = db; _audit = audit;
    }
    public async Task HandleUpdateAsync(Update update, CancellationToken ct = default)
    {
        // Only handle text messages for now
        if (update.Type != UpdateType.Message || update.Message is null) return;

        var msg = update.Message;
        var text = msg.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // =========================
        // ADMIN REPLY: /send <userId> <message...>
        // =========================
        if (text.StartsWith("/send", StringComparison.OrdinalIgnoreCase))
        {
            // Validate sender is an admin (IsMainUser == true)
            var admin = await _context.Users.FirstOrDefaultAsync(u => u.TelegramUserId == msg.Chat.Id, ct);
            if (admin is null || admin.IsMainUser != true)
            {
                await _bot.SendMessage(msg.Chat.Id, "❌ Bu komutu kullanma yetkiniz yok.", cancellationToken: ct);
                return;
            }

            // Expect at least "/send 12345 hi there"
            var parts = text.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !int.TryParse(parts[1], out var targetUserId))
            {
                await _bot.SendMessage(msg.Chat.Id, "Usage:\n/send <UserId> <message>", cancellationToken: ct);
                return;
            }

            var messageToSend = parts[2].Trim();
            if (string.IsNullOrWhiteSpace(messageToSend))
            {
                await _bot.SendMessage(msg.Chat.Id, "⚠️ Mesaj boş bırakılamaz.", cancellationToken: ct);
                return;
            }

            var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
            if (targetUser is null)
            {
                await _bot.SendMessage(msg.Chat.Id, "❌ Hedef kullanıcı bulunamadı.", cancellationToken: ct);
                return;
            }
             
            if (targetUser.TelegramUserId is null)
            {
                await _bot.SendMessage(msg.Chat.Id, "ℹ️ Hedef kullanıcının Telegram bağlantısı yok.", cancellationToken: ct);
                return;
            }

            try
            {
                var sent = await _bot.SendMessage(chatId: targetUser.TelegramUserId.Value, text: messageToSend, cancellationToken: ct);

                _context.TelegramMessages.Add(new TelegramMessage
                {
                    Direction = MessageDirection.Outbound,
                    UserId = targetUser.Id,
                    TelegramMessageId = sent.MessageId,
                    ChatId = sent.Chat.Id,
                    Body = messageToSend,
                    Status = "Sent",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await _context.SaveChangesAsync(ct);

                await _audit.LogAsync("Message",
                    entityId: targetUser.Id.ToString(),
                    action: "AdminReply",
                    performedById: admin.Id,
                    new { toUserId = targetUser.Id, toChatId = targetUser.TelegramUserId, sourceAdminChatId = msg.Chat.Id },
                    ct);

                await _bot.SendMessage(msg.Chat.Id, $"✅ Kullanıcıya gönderildi #{targetUser.Id}.", cancellationToken: ct);
            }
            catch (Exception ex)
            {
                await _audit.LogAsync("Message",
                    entityId: "0",
                    action: "AdminReplyError",
                    performedById: admin.Id,
                    new { error = ex.Message, toUserId = targetUser.Id, toChatId = targetUser.TelegramUserId },
                    ct);

                await _bot.SendMessage(msg.Chat.Id, "❌ Mesaj gönderilemedi.", cancellationToken: ct);
            }

            return;
        }

        // =========================
        // LINKING FLOW: /start <payload>
        // =========================
        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var payload = parts.Length == 2 ? parts[1].Trim() : null;

            if (!string.IsNullOrWhiteSpace(payload) && int.TryParse(payload, out var userId))
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
                if (user is null)
                {
                    await _bot.SendMessage(msg.Chat.Id, "❌ Bu bağlantı için kullanıcı bulunamadı.", cancellationToken: ct);
                    return;
                }

                user.TelegramUserId = msg.Chat.Id;

                try
                {
                    await _context.SaveChangesAsync(ct);

                    await _audit.LogAsync("User", user.Id.ToString(), "LinkTelegram", null,
                        new { chatId = msg.Chat.Id, fromId = msg.From?.Id, payload }, ct);

                    await _bot.SendMessage(msg.Chat.Id, "✅ Hesabınız artık bağlandı.", cancellationToken: ct);
                }
                catch (DbUpdateException ex)
                {
                    var inner = ex.InnerException?.Message ?? ex.Message;
                    if (inner.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                        inner.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                    {
                        await _bot.SendMessage(msg.Chat.Id, "⚠️ Bu Telegram hesabı başka bir kullanıcıya bağlı.", cancellationToken: ct);
                    }
                    else
                    {
                        await _bot.SendMessage(msg.Chat.Id, "❌ Hesabınız bağlanırken hata oluştu. Lütfen daha sonra tekrar deneyin.", cancellationToken: ct);
                    }
                }
                return;
            }

            await _bot.SendMessage(msg.Chat.Id, "Hoş geldin! 🎉", cancellationToken: ct);
            return;
        }

        // =========================
        // NORMAL INBOUND MESSAGES
        // =========================
        var linkedUser = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramUserId == msg.Chat.Id, ct);

        if (linkedUser is null)
        {
            await _bot.SendMessage(msg.Chat.Id,
                "ℹ️ Telegram'ınız henüz bir hesaba bağlı değil. Lütfen bağlanmak için web sitesindeki bağlantıyı kullanın.",
                cancellationToken: ct);
            return;
        }

        var inbound = new TelegramMessage
        {
            Direction = MessageDirection.Inbound,
            UserId = linkedUser.Id,
            TelegramMessageId = msg.MessageId,
            ChatId = msg.Chat.Id,
            Body = text,
            Status = "Received",
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            _context.TelegramMessages.Add(inbound);
            await _context.SaveChangesAsync(ct);

            await _audit.LogAsync("Message", inbound.Id.ToString(), "Receive",
                linkedUser.Id, new { chatId = msg.Chat.Id, telegramMessageId = msg.MessageId, replyTo = msg.ReplyToMessage?.MessageId }, ct);

            // Notify admins of this company (if present)
            var linkedCompanyId = linkedUser.CompanyId ?? 0;
            if (linkedCompanyId != 0)
            {
                await NotifyAdminsAsync(
                    companyId: linkedCompanyId,
                    fromUserId: linkedUser.Id,
                    fromChatId: msg.Chat.Id,
                    telegramMessageId: msg.MessageId,
                    text: text,
                    ct: ct
                );
            }
 
        }
        catch (Exception ex)
        {
            await _audit.LogAsync("Message", "0", "ReceiveError", linkedUser.Id, new { error = ex.Message }, ct);            
        }
    }
    private async Task NotifyAdminsAsync(int companyId,int fromUserId,long fromChatId,int telegramMessageId,string text,CancellationToken ct = default)
    {

        var admins = await _context.Users
            .Where(u => u.CompanyId == companyId && u.IsMainUser == true && u.TelegramUserId != null)
            .Select(u => new { u.Id, u.TelegramUserId, u.FullName })
            .ToListAsync(ct);

        if (admins.Count == 0)
        {
            await _audit.LogAsync("Notify", companyId.ToString(), "NoAdminsToNotify", fromUserId,
                new { reason = "No main users with TelegramUserId" }, ct);
            return;
        }
        
        var header = $"📨 New reply from User #{fromUserId}";
        var preview = text?.Length > 300 ? text.Substring(0, 300) + "..." : text;
        var body = $@"{header}Message: {preview} Reply quickly:- /send {fromUserId} <your message here>";

        foreach (var admin in admins)
        {
            try
            {
               
                var sent = await _bot.SendMessage(chatId: admin.TelegramUserId!.Value,text: body,cancellationToken: ct);
                
                _context.TelegramMessages.Add(new TelegramMessage
                {
                    Direction = MessageDirection.Outbound,
                    UserId = admin.Id,                // who received the notification
                    TelegramMessageId = sent.MessageId,
                    ChatId = sent.Chat.Id,
                    Body = body,
                    Status = "Sent",
                    CreatedAtUtc = DateTime.UtcNow
                });
                await _context.SaveChangesAsync(ct);
                await _audit.LogAsync("Notify",entityId: admin.Id.ToString(),action: "AdminAlert",performedById: fromUserId,
                    new { toAdminChatId = admin.TelegramUserId, sourceUserId = fromUserId, sourceChatId = fromChatId, telegramMessageId },ct);
            }
            catch (Exception ex)
            {
                await _audit.LogAsync("Notify",
                    entityId: admin.Id.ToString(),
                    action: "AdminAlertError",
                    performedById: fromUserId,
                    new { error = ex.Message, toAdminChatId = admin.TelegramUserId, sourceUserId = fromUserId },
                    ct);
            }

            await Task.Delay(50, ct); // keep a tiny gap for rate limits
        }
    }
    public async Task SendToUsersAsync(int companyId, int performedByUserId, string text, string txtToSaveBody, CancellationToken ct = default)
    {
        try
        {
            var user = await _context.Users
                  .Where(u => u.CompanyId == companyId && u.IsMainUser == true)   // <-- filter by company + main user
                  .Select(u => new { u.Id, u.TelegramUserId })
                  .FirstOrDefaultAsync();
            if (user is null)
            {
                await _audit.LogAsync("User", "0", "SkipNoMainUser", performedByUserId,
                    new { reason = "No main user for company" });
                return;
            }
            if (user.TelegramUserId is null)
            {
                await _audit.LogAsync("User", user.Id.ToString(), "SkipNoTelegramId", performedByUserId,
                    new { reason = "TelegramUserId null" });
            }
            else
            {
                try
                {
                    var sent = await _bot.SendMessage(chatId: user.TelegramUserId.Value, text: text);

                    var telegrammsg = new TelegramMessage
                    {
                        Direction = MessageDirection.Outbound,
                        UserId = user.Id,
                        TelegramMessageId = sent.MessageId,
                        ChatId = sent.Chat.Id,
                        Body = txtToSaveBody,
                        Status = "Sent"
                    };
                    _context.TelegramMessages.Add(telegrammsg);
                    await _context.SaveChangesAsync(ct);

                    //await _audit.LogAsync("Message", telegrammsg.Id.ToString(), "Send", performedByUserId,
                    //    new { chatId = sent.Chat.Id, telegramMessageId = sent.MessageId });
                }
                catch (Exception ex)
                {
                    await _audit.LogAsync("User", user.Id.ToString(), "Error", performedByUserId, new { error = ex.Message });
                }
            }
        }
        catch (Exception ex)
        {
            await _audit.LogAsync("ERRON IN SEND TOUSERASYNC","0", "Error", performedByUserId, new { error = " ERRON IN SEND TOUSERASYNC" + ex.Message.ToString() });
        }
    }
}
