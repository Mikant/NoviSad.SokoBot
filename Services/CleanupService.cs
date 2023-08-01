using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
                var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

                var trainCount0 = await dbContext.Trains.CountAsync(stoppingToken);
                var passengerCount0 = await dbContext.Passengers.CountAsync(stoppingToken);

                service.Cleanup(dbContext);

                await dbContext.SaveChangesAsync(stoppingToken);

                var trainCount1 = await dbContext.Trains.CountAsync(stoppingToken);
                var passengerCount1 = await dbContext.Passengers.CountAsync(stoppingToken);

                var trainDelta = trainCount0 - trainCount1;
                var passengerDelta = passengerCount0 - passengerCount1;

                if (trainDelta > 0)
                {
                    _logger.LogInformation("Trains were cleaned up, count: {count}", trainDelta);
                }

                if (passengerDelta > 0)
                {
                    _logger.LogInformation("Passengers were cleaned up, count: {count}", passengerDelta);
                }

            } catch (Exception e) when (e is not OperationCanceledException) {
                _logger.LogError(e, "Cleanup iteration has failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
