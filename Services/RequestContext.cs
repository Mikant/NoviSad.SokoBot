using System;
using NoviSad.SokoBot.Data.Entities;

namespace NoviSad.SokoBot.Services;

public record RequestContext(
    bool Cancel,
    bool Spectate,
    TrainDirection? Direction,
    DateTimeOffset? SearchStart,
    DateTimeOffset? SearchEnd,
    int? TrainNumber,
    DateTimeOffset? DepartureTime,
    bool Leave) {
    public static RequestContext Empty { get; } = new(default, default, default, default, default, default, default, default);
}
