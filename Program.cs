using System.Globalization;
using GittBilSmsCore.Data;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<GittBilSmsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
// Add localization services and specify the Resources folder
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
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
    RequestCultureProviders =
    [
        new QueryStringRequestCultureProvider()
    ]
};

// Add the Request Localization middleware
app.UseRequestLocalization(requestLocalizationOptions);

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
//app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();