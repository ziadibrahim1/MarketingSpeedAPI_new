using MarketingSpeedAPI.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ------------------------
// 1️⃣ Configure Database
// ------------------------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        new MySqlServerVersion(new Version(8, 0, 26))
    )
);

// ------------------------
// 2️⃣ Configure Email Service
// ------------------------
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<EmailService>();

// ------------------------
// 3️⃣ Add Controllers
// ------------------------
builder.Services.AddControllers();

// ------------------------
// 4️⃣ Logging
// ------------------------
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// ------------------------
// 5️⃣ Memory Cache
// ------------------------
builder.Services.AddMemoryCache();

// ------------------------
// 6️⃣ Hosted Services
// ------------------------
builder.Services.AddHostedService<DeleteUnverifiedUsersJob>();

// ------------------------
// 7️⃣ HttpClient for API calls
// ------------------------
builder.Services.AddHttpClient("Wasender", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Wasender:BaseUrl"] ?? "https://www.wasenderapi.com");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {builder.Configuration["Wasender:ApiKey"]}");
});

// ------------------------
// 8️⃣ CORS Policy
// ------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ------------------------
// 9️⃣ Build App
// ------------------------
var app = builder.Build();

// ------------------------
// 10️⃣ Middleware
// ------------------------
app.UseCors("AllowAll");
app.UseHttpsRedirection();
app.UseAuthorization();

// ------------------------
// 11️⃣ Map Controllers
// ------------------------
app.MapControllers();

// ------------------------
// 12️⃣ Run App
// ------------------------
app.Run();
