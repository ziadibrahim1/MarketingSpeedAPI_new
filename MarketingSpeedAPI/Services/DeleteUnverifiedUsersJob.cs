using MarketingSpeedAPI.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public class DeleteUnverifiedUsersJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;

    public DeleteUnverifiedUsersJob(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cutoff = DateTime.UtcNow.AddHours(-24); // عمر الصلاحية
            var toDelete = db.Users
                .Where(u => !u.is_email_verified && u.created_at < cutoff);

            db.Users.RemoveRange(toDelete);
            await db.SaveChangesAsync();

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken); // كل ساعة
        }
    }

   
}
