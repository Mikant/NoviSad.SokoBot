using System;
using System.Collections.Generic;
using NoviSad.SokoBot.Data.Entities;

namespace NoviSad.SokoBot.Data.Dto;

public class TrainDto {
    public int Id { get; set; }

    public int TrainNumber { get; set; }

    public DateOnly UtcDate { get; set; }

    public TrainDirection Direction { get; set; }

    public DateTimeOffset DepartureTime { get; set; }

    public DateTimeOffset ArrivalTime { get; set; }

    public string? Tag { get; set; }

    public List<PassengerDto> Passengers { get; set; }
}
