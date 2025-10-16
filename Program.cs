using GittBilSmsCore.Data;
using GittBilSmsCore.Models;
using GittBilSmsCore.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using System.Globalization;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);
var logPath = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? AppContext.BaseDirectory, "LogFiles", "GittBilSms", "sms-report-.txt");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
    // Add filter for SmsApiController
    .WriteTo.Logger(lc => lc
    .Filter.ByIncludingOnly(le =>
        le.Properties.ContainsKey("SourceContext") &&
        le.Properties["SourceContext"].ToString().Contains("ScheduledSmsSenderService"))
    .WriteTo.File(
        Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? AppContext.BaseDirectory, "LogFiles", "GittBilSms", "sms-scheduler-.txt"),
        rollingInterval: RollingInterval.Day
    )
   ).CreateLogger();
builder.Host.UseSerilog();
//builder.Logging.AddConsole();
builder.Services.AddDbContext<GittBilSmsDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));

    // Enable sensitive data logging for debugging
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
    }
});
builder.Services.AddIdentity<User, IdentityRole<int>>()
    .AddEntityFrameworkStores<GittBilSmsDbContext>()
    .AddDefaultTokenProviders();
builder.Services.AddScoped<INotificationService, NotificationService>();
// Add localization services and specify the Resources folder   
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(120);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
//builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));
//builder.Services.AddSingleton<ITelegramBotClient>(sp =>
//{
//    var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
//    return new TelegramBotClient(opts.BotToken);
//});
//builder.Services.AddScoped<TelegramAuditService>();
//builder.Services.AddScoped<TelegramMessageService>();
//builder.Services.AddHostedService<BotPollingService>();
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve;
        options.JsonSerializerOptions.WriteIndented = true;
    });

builder.Services.AddRazorPages()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});
builder.Services.AddSingleton<SmsReportBackgroundService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SmsReportBackgroundService>());
builder.Services.AddHostedService<ScheduledSmsSenderService>();
builder.Services.AddHttpClient();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
    });
builder.Services.AddSignalR();
var app = builder.Build();

// Define the supported cultures
var supportedCultures = new[] { new CultureInfo("tr"), new CultureInfo("en") };

// Configure the Request Localization options
var requestLocalizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("tr"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures,
    // Explicitly specifying the type for RequestCultureProviders
    RequestCultureProviders = new List<IRequestCultureProvider>
        {
            new QueryStringRequestCultureProvider(),
            new CookieRequestCultureProvider()
        }
};

// Add the Request Localization middleware
app.UseRequestLocalization(requestLocalizationOptions);

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
app.Use(async (context, next) =>
{
    var host = context.Request.Host.Value.ToLower();

    if (host.Contains("azurewebsites.net"))
    {
        var newUrl = "https://gittibilsms.com" + context.Request.Path + context.Request.QueryString;
        context.Response.Redirect(newUrl, permanent: true);
        return;
    }

    await next();
});
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapControllerRoute(
    name: "controllerOnly",
    pattern: "{controller}",
    defaults: new { action = "Index" });

app.MapRazorPages();
app.MapHub<GittBilSmsCore.Hubs.ChatHub>("/chathub");
app.Run();

