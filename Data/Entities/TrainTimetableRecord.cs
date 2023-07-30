using System;
using Microsoft;

namespace NoviSad.SokoBot.Data.Entities;

public class TrainTimetableRecord {
    public TrainTimetableRecord(int trainNumber, DateTimeOffset departureTime, DateTimeOffset arrivalTime, string? tag) {
        Requires.Range(trainNumber > 0, nameof(trainNumber));
        Requires.Range(departureTime > DateTimeOffset.MinValue, nameof(departureTime));
        Requires.Range(arrivalTime > DateTimeOffset.MinValue, nameof(arrivalTime));
        Requires.Argument(arrivalTime > departureTime, nameof(arrivalTime), "Arrival time must be greater than departure time");

        TrainNumber = trainNumber;

        DepartureTime = departureTime;
        ArrivalTime = arrivalTime;

        Tag = tag;
    }

    public int TrainNumber { get; }

    public DateTimeOffset DepartureTime { get; }
    public DateTimeOffset ArrivalTime { get; }

    public string? Tag { get; }
}
