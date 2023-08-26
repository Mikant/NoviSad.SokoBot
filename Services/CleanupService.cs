using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using NoviSad.SokoBot.Data;

namespace NoviSad.SokoBot.Services;

public class CleanupService : BackgroundService {
    private readonly ILogger<CleanupService> _logger;
    private readonly ISystemClock _systemClock;
    private readonly IServiceProvider _serviceProvider;

    public CleanupService(ILogger<CleanupService> logger, ISystemClock systemClock, IServiceProvider serviceProvider) {
        _logger = logger;
        _systemClock = systemClock;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

                var arrivedTrains = await dbContext.Trains
                    .Where(x => x.ArrivalTime < _systemClock.UtcNow)
                    .ToListAsync(stoppingToken);
                dbContext.Trains.RemoveRange(arrivedTrains);

                var offboardedPassengers = await dbContext.Passengers
                    .Where(x => x.Trains.Count == 0)
                    .ToListAsync(stoppingToken);
                dbContext.Passengers.RemoveRange(offboardedPassengers);

                await dbContext.SaveChangesAsync(stoppingToken);

                if (arrivedTrains.Count > 0)
                {
                    _logger.LogInformation("Trains were cleaned up, count: {count}, trains: {trains}", arrivedTrains.Count, string.Join(", ", arrivedTrains.Select(x => x.TrainNumber)));
                }

                if (offboardedPassengers.Count > 0)
                {
                    _logger.LogInformation("Passengers were cleaned up, count: {count}, passengers: {passengers}", offboardedPassengers.Count, string.Join(", ", offboardedPassengers.Select(x => x.Nickname)));
                }

            } catch (Exception e) when (e is not OperationCanceledException) {
                _logger.LogError(e, "Cleanup iteration has failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
