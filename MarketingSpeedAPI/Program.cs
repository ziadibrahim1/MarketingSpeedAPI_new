using MarketingSpeedAPI.Controllers;
using MarketingSpeedAPI.Data;
using MarketingSpeedAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});
builder.Services.Configure<TelegramOptions>(
    builder.Configuration.GetSection("Telegram"));

// نضيف الـ Manager نفسه
builder.Services.AddSingleton<TelegramClientManager>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
    return new TelegramClientManager(opts.ApiId, opts.ApiHash, opts.BaseDataDir);
});
var app = builder.Build();

// 9️⃣ Middleware
app.UseCors("AllowAll");
app.UseHttpsRedirection();
app.UseAuthorization();

// 10️⃣ Map Controllers
app.MapControllers();

// 11️⃣ Run
app.Run();
