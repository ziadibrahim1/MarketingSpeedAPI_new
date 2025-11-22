using MarketingSpeedAPI.Controllers;
using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Hubs;
using MarketingSpeedAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

 

var builder = WebApplication.CreateBuilder(args);

// 1️⃣ Database (مع تفعيل إعادة المحاولة + رفع الوقت)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 26)),
        mySqlOptions =>
        {
            // ⚡️ أهم شيء لمنع انهيار الاتصال
            mySqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null
            );

            // ⛔ منع timeout من EF Core
            mySqlOptions.CommandTimeout(60);
        }
    );
});

// 2️⃣ Services
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<EmailService>();
builder.Services.Configure<WasenderSettings>(builder.Configuration.GetSection("Wasender"));

// 3️⃣ Controllers
builder.Services.AddControllers();

// 4️⃣ Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// 5️⃣ Memory Cache
builder.Services.AddMemoryCache();

// 6️⃣ Hosted Services
builder.Services.AddHostedService<DeleteUnverifiedUsersJob>();

// 7️⃣ HttpClient
builder.Services.AddHttpClient("Wasender", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Wasender:BaseUrl"] ?? "https://www.wasenderapi.com");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {builder.Configuration["Wasender:ApiKey"]}");
});


builder.Services.Configure<MoyasarSettings>(
    builder.Configuration.GetSection("Moyasar"));

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 26))
    );
});




// 8️⃣ CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(_ => true));
});

// 9️⃣ Telegram Manager
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));
builder.Services.AddSingleton<Func<TelegramClientManager>>(sp =>
{
    return () =>
    {
        var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
        return new TelegramClientManager(opts.ApiId, opts.ApiHash, opts.BaseDataDir);
    };
});

builder.Services.AddSignalR();

// 🔟 Kestrel
builder.WebHost.UseKestrel(options =>
{
    options.ListenAnyIP(80);  // HTTP
});

// 1️⃣1️⃣ Build app
var app = builder.Build();

app.UseCors("AllowAll");
app.UseStaticFiles();
app.UseAuthorization();

// 1️⃣2️⃣ Map Controllers + Hubs
app.MapControllers();
app.MapHub<ChatHub>("/chathub");

// 1️⃣3️⃣ Developer mode
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.Run();
