using MarketingSpeedAPI.Controllers;
using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Hubs;
using MarketingSpeedAPI.Models;
using MarketingSpeedAPI.Models.MarketingSpeedAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

#region Database (DbContext Pool + Retry + Timeout)
builder.Services.AddDbContextPool<AppDbContext>(options =>
{
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 26)),
        mySqlOptions =>
        {
            mySqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null
            );

            mySqlOptions.CommandTimeout(60);
        }
    );
});
#endregion

#region Services & Options
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<EmailService>();

builder.Services.Configure<WasenderSettings>(
    builder.Configuration.GetSection("Wasender"));

builder.Services.Configure<MoyasarSettings>(
    builder.Configuration.GetSection("Moyasar"));

builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection("Telegram"));
#endregion

#region Controllers
builder.Services.AddControllers();
#endregion

#region Logging (Safe for Production)
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddConsole();
}
#endregion

#region Memory Cache (With Limit)
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100 * 1024 * 1024; // 100 MB
});
#endregion

#region Hosted Services
builder.Services.AddHostedService<DeleteUnverifiedUsersJob>();
#endregion

#region HttpClient
builder.Services.AddHttpClient("Wasender", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Wasender:BaseUrl"] ??
        "https://www.wasenderapi.com");

    client.DefaultRequestHeaders.Add(
        "Authorization",
        $"Bearer {builder.Configuration["Wasender:ApiKey"]}");
});
#endregion

#region CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true));
});
#endregion

builder.Services.AddScoped<TelegramSessionService>();

builder.Services.AddHttpClient<TelegramClientManager>(c =>
{
    c.BaseAddress = new Uri("http://localhost:8001");
});


#region SignalR
builder.Services.AddSignalR();
#endregion

#region Kestrel
builder.WebHost.UseKestrel(options =>
{
    options.ListenAnyIP(80); // HTTP
});
#endregion

#region Build
var app = builder.Build();
#endregion

#region Middleware
app.UseCors("AllowAll");
app.UseStaticFiles();
app.UseAuthorization();
#endregion

#region Endpoints
app.MapControllers();
app.MapHub<ChatHub>("/chathub");
#endregion

#region Development
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
#endregion

app.Run();
