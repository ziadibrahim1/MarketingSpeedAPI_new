using MarketingSpeedAPI.Controllers;
using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Hubs;
using MarketingSpeedAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

// 1️⃣ Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 26))
    )
);

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
builder.Services.AddSingleton<TelegramClientManager>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
    return new TelegramClientManager(opts.ApiId, opts.ApiHash, opts.BaseDataDir);
});

// 🔟 SignalR
builder.Services.AddSignalR();

// ✅ جلب الشهادة من Windows Certificate Store
var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
store.Open(OpenFlags.ReadOnly);
var certs = store.Certificates.Find(
    X509FindType.FindByThumbprint,
    "5ce000fd74f2c3a0242bdec24ffec213a44a2edc", // ضع هنا Thumbprint الشهادة
    validOnly: false
);

if (certs.Count == 0)
{
    throw new Exception("Certificate not found in LocalMachine\\My store!");
}

var certificate = certs[0];
store.Close();

// 🔹 تشغيل Kestrel مع HTTP و HTTPS
builder.WebHost.UseKestrel(options =>
{
    options.ListenAnyIP(80); // HTTP
    options.ListenAnyIP(443, listenOptions =>
    {
        listenOptions.UseHttps(certificate);
    });
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseAuthorization();

// 1️⃣2️⃣ Map Controllers + Hubs
app.MapControllers();
app.MapHub<ChatHub>("/chathub");

// 1️⃣3️⃣ Run
app.Run();
