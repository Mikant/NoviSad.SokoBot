using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoviSad.SokoBot.Data;

namespace NoviSad.SokoBot.Services;

public class CleanupService : BackgroundService {
    private readonly ILogger<CleanupService> _logger;
    private readonly IServiceProvider _serviceProvider;

    public CleanupService(IServiceProvider serviceProvider, ILogger<CleanupService> logger) {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<TrainService>();
                var context = scope.ServiceProvider.GetRequiredService<BotDbContext>();

                service.Cleanup(context);

                await context.SaveChangesAsync(stoppingToken);

            } catch (Exception e) when (e is not OperationCanceledException) {
                _logger.LogError(e, "Cleanup iteration has failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
