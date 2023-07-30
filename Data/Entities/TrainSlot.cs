using System.Collections.Generic;
using Microsoft;

namespace NoviSad.SokoBot.Data.Entities;

public class TrainSlot {
    public TrainTimetableRecord Train { get; }
    public IReadOnlyList<TelegramUser> Passengers { get; }

    public TrainSlot(TrainTimetableRecord train, IReadOnlyList<TelegramUser> passengers) {
        Requires.NotNull(train, nameof(train));
        Requires.NotNull(passengers, nameof(passengers));
        
        Train = train;
        Passengers = passengers;
    }
}
