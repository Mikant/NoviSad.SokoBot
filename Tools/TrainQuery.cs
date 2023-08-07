using System;

namespace NoviSad.SokoBot.Tools;

public record TrainQuery(int TrainNumber, DateTimeOffset DepartureTime) {
    public bool Leave { get; set; }
}
