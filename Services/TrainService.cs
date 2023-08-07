using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using NoviSad.SokoBot.Data;
using NoviSad.SokoBot.Data.Dto;
using NoviSad.SokoBot.Data.Entities;
using NoviSad.SokoBot.Tools;

namespace NoviSad.SokoBot.Services;

public class TrainService {
    private readonly ILogger<TrainService> _logger;
    private readonly ISystemClock _systemClock;

    public TrainService(
        ILogger<TrainService> logger,
        ISystemClock systemClock
    ) {
        _logger = logger;
        _systemClock = systemClock;
    }

    public async Task<TrainSlot?> AddPassenger(BotDbContext dbContext, int trainNumber, DateTimeOffset departureTime, TelegramUser user, CancellationToken cancellationToken) {
        _logger.LogInformation(
            "Adding a passenger to train, nickname: {nickname}, trainNumber: {trainNumber}, departureTime: {departureTime}",
            user.Nickname,
            trainNumber,
            departureTime
        );

        var train = await GetTrain(dbContext, trainNumber, departureTime, cancellationToken);
        if (train == null) {
            _logger.LogError("Train is not found, number: {number}, departureDate: {departureDate}", trainNumber, departureTime);
            return null;
        }

        var passenger = await GetOrCreatePassenger(dbContext, user, cancellationToken);

        train.Passengers.Add(passenger);

        return new TrainSlot(
            Convert(train),
            train.Passengers.Select(Convert).ToList()
        );
    }

    public async Task<TrainSlot?> RemovePassenger(BotDbContext dbContext, int trainNumber, DateTimeOffset departureTime, TelegramUser user, CancellationToken cancellationToken) {
        _logger.LogInformation(
            "Removing a passenger from train, nickname: {nickname}, trainNumber: {trainNumber}, departureDate: {departureDate}",
            user.Nickname,
            trainNumber,
            departureTime
        );

        var train = await GetTrain(dbContext, trainNumber, departureTime, cancellationToken);
        if (train == null) {
            _logger.LogError("Train is not found, trainNumber: {trainNumber}, departureDate: {departureDate}", trainNumber, departureTime);
            return null;
        }

        var passenger = await dbContext.Passengers.Where(x => x.Nickname == user.Nickname).FirstOrDefaultAsync(cancellationToken);
        if (passenger == null) {
            _logger.LogWarning("Passenger is not found, nickname: {nickname}", user.Nickname);
            return null;
        }

        if (!train.Passengers.Remove(passenger)) {
            _logger.LogError("Passenger was not found in train, nickname: {nickname}, trainNumber: {trainNumber}, departureTime: {departureTime}",
                user.Nickname,
                trainNumber,
                departureTime
            );
        }

        if (!passenger.Trains.Any()) {
            _logger.LogInformation("Removing passenger from store, nickname: {nickname}", user.Nickname);
            dbContext.Passengers.Remove(passenger);
        }

        return new TrainSlot(
            Convert(train),
            train.Passengers.Select(Convert).ToList()
        );
    }

    private static async Task<TrainDto?> GetTrain(BotDbContext dbContext, int trainNumber, DateTimeOffset localDepartureDate, CancellationToken cancellationToken) {
        return await dbContext.Trains
            .Include(x => x.Passengers)
            .Where(x => x.TrainNumber == trainNumber && x.DepartureTime == localDepartureDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<TrainSlot?> FindTrain(BotDbContext dbContext, int trainNumber, DateTimeOffset localDepartureDate, CancellationToken cancellationToken) {
        var train = await GetTrain(dbContext, trainNumber, localDepartureDate, cancellationToken);
        if (train == null)
            return null;

        return new TrainSlot(
            Convert(train),
            train.Passengers.Select(Convert).ToList()
        );
    }

    private static async Task<IReadOnlyCollection<TrainDto>> GetTrains(BotDbContext dbContext, TrainDirection direction, DateOnly date, CancellationToken cancellationToken) {
        var utcStartTime = TimeZoneHelper.ToCentralEuropeanTime(date).UtcDateTime;
        var utcEndTime = utcStartTime.AddDays(1);
        return await dbContext.Trains
            .Where(x => x.Direction == direction && utcStartTime <= x.DepartureTime && x.DepartureTime < utcEndTime)
            .ToListAsync(cancellationToken);
    }

    private async Task<PassengerDto> GetOrCreatePassenger(BotDbContext dbContext, TelegramUser user, CancellationToken cancellationToken) {
        var passenger = await dbContext.Passengers.Where(x => x.Nickname == user.Nickname).FirstOrDefaultAsync(cancellationToken);
        if (passenger == null) {
            _logger.LogInformation("New passenger is created, nickname: {nickname}", user.Nickname);
            passenger = new PassengerDto { Nickname = user.Nickname, ChatId = user.ChatId };
            await dbContext.Passengers.AddAsync(passenger, cancellationToken);
        } else {
            _logger.LogDebug("Passenger is found in store, nickname: {nickname}", user.Nickname);
        }

        return passenger;
    }

    public async Task<IReadOnlyList<TrainSlot>> FindTrains(BotDbContext dbContext, TrainDirection? direction, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) {
        var trains = await dbContext.Trains
            .Include(x => x.Passengers)
            .Where(x => x.Direction == direction && from <= x.ArrivalTime && x.DepartureTime < to)
            .OrderBy(x => x.DepartureTime)
            .ToListAsync(cancellationToken);

        return trains
            .ConvertAll(x => new TrainSlot(
                Convert(x),
                x.Passengers.Select(Convert).ToList()
            ));
    }

    public async Task UpdateTimetable(BotDbContext dbContext, TrainDirection direction, DateOnly date, CancellationToken cancellationToken) {
        _logger.LogInformation(
            "Updating timetable for {date}, direction: {direction}",
            date,
            direction
        );

        var externalTrains = await TrainTimetableLoader.Load(direction, date, cancellationToken);
        var externalTrainsDict = new Dictionary<int, TrainTimetableRecord>();
        foreach (var train in externalTrains) {
            if (externalTrainsDict.ContainsKey(train.TrainNumber)) {
                _logger.LogError("Duplicated trains found in timetable, trainNumber: {trainNumber}, date: {date}", train.TrainNumber, date);
                continue;
            }

            externalTrainsDict.Add(train.TrainNumber, train);
        }

        _logger.LogDebug("Timetable is loaded, count: {count}", externalTrainsDict.Count);

        var internalTrains = await GetTrains(dbContext, direction, date, cancellationToken);
        var internalTrainsDict = new Dictionary<int, TrainDto>();
        foreach (var train in internalTrains) {
            if (internalTrainsDict.ContainsKey(train.TrainNumber)) {
                _logger.LogError("Duplicated trains found in store, trainNumber: {trainNumber}, date: {date}", train.TrainNumber, date);
                continue;
            }

            internalTrainsDict.Add(train.TrainNumber, train);
        }

        var canceledTrains = new List<TrainDto>();
        foreach (var train in internalTrains) {
            if (!externalTrainsDict.ContainsKey(train.TrainNumber)) {
                canceledTrains.Add(train);
            }
        }

        if (canceledTrains.Count > 0)
            _logger.LogDebug("Canceled trains found, count: {count}", canceledTrains.Count);

        foreach (var train in canceledTrains) {
            train.Passengers.Clear();
        }

        dbContext.Trains.RemoveRange(canceledTrains);

        foreach (var externalTrain in externalTrainsDict.Values) {
            if (externalTrain.ArrivalTime < _systemClock.UtcNow)
                continue;

            var internalTrain = internalTrainsDict.GetValueOrDefault(externalTrain.TrainNumber);
            if (internalTrain != null) {
                if (internalTrain.DepartureTime != externalTrain.DepartureTime || internalTrain.ArrivalTime != externalTrain.ArrivalTime || internalTrain.Direction != direction) {
                    _logger.LogInformation(
                        "Train was rescheduled, trainNumber: {trainNumber}, departureDate: {departureDate}",
                        externalTrain.TrainNumber,
                        date
                    );

                    internalTrain.Passengers.Clear();

                    // here we could've notified the passengers, but that seems too luxurious for me

                    internalTrain.Direction = direction;
                    internalTrain.DepartureTime = externalTrain.DepartureTime;
                    internalTrain.ArrivalTime = externalTrain.ArrivalTime;
                }

            } else {
                await dbContext.Trains.AddAsync(new TrainDto {
                    TrainNumber = externalTrain.TrainNumber,
                    Direction = direction,
                    DepartureTime = externalTrain.DepartureTime,
                    ArrivalTime = externalTrain.ArrivalTime,
                    Tag = externalTrain.Tag,
                }, cancellationToken);
            }
        }
    }

    public void Cleanup(BotDbContext dbContext) {
        var arrivedTrains = dbContext.Trains.Where(x => x.ArrivalTime < _systemClock.UtcNow);
        dbContext.Trains.RemoveRange(arrivedTrains);

        var nonboardedPassenger = dbContext.Passengers.Where(x => x.Trains.Count == 0);
        dbContext.Passengers.RemoveRange(nonboardedPassenger);
    }

    private static TelegramUser Convert(PassengerDto passenger) {
        return new TelegramUser(passenger.Nickname, passenger.ChatId);
    }

    private static TrainTimetableRecord Convert(TrainDto train) {
        return new TrainTimetableRecord(train.TrainNumber, train.Direction, train.DepartureTime, train.ArrivalTime, train.Tag);
    }
}
