using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using NoviSad.SokoBot.Data;
using NoviSad.SokoBot.Data.Entities;

namespace NoviSad.SokoBot.Services;

public class TimetableUpdateService : BackgroundService {
    private readonly ILogger<CleanupService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISystemClock _systemClock;

    public TimetableUpdateService(IServiceProvider serviceProvider, ILogger<CleanupService> logger, ISystemClock systemClock) {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _systemClock = systemClock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<TrainService>();
                var context = scope.ServiceProvider.GetRequiredService<BotDbContext>();

                var date = DateOnly.FromDateTime(_systemClock.UtcNow.UtcDateTime);
                
                foreach (var direction in Enum.GetValues<TrainDirection>()) {
                    for (int dayOffset = 0; dayOffset <= 1; dayOffset++) {
                        await service.UpdateTimetable(context, direction, date.AddDays(dayOffset), stoppingToken);
                    }
                }

                await context.SaveChangesAsync(stoppingToken);

                var count = await context.Trains.CountAsync(stoppingToken);

                _logger.LogInformation("Timetable was updated, count: {count}", count);

                await Task.Delay(TimeSpan.FromHours(12), stoppingToken);

            } catch (Exception e) when (e is not OperationCanceledException) {
                _logger.LogError(e, "Timetable update iteration has failed");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }
}
