using System.Collections.Generic;

namespace NoviSad.SokoBot.Data.Dto;

public class PassengerDto {
    public int Id { get; set; }

    public string Nickname { get; set; }

    public List<TrainDto> Trains { get; set; }
}
