using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoviSad.SokoBot.Data.Converters;
using NoviSad.SokoBot.Data.Dto;

namespace NoviSad.SokoBot.Data.Configuration;

public class TrainConfiguration : IEntityTypeConfiguration<TrainDto> {
    public void Configure(EntityTypeBuilder<TrainDto> builder) {
        builder.ToTable("train");

        builder.HasKey(x => x.Id);

        builder
            .HasIndex(x => new { x.TrainNumber, x.DepartureTime })
            .IsUnique();

        builder.HasIndex(x => x.DepartureTime);
        builder.HasIndex(x => x.ArrivalTime);

        builder
            .Property(x => x.Id)
            .HasColumnName("id")
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder.Property(x => x.TrainNumber)
            .HasColumnName("number")
            .IsRequired();

        builder
            .Property(x => x.Direction)
            .HasColumnName("direction")
            .IsRequired();

        builder
            .Property(x => x.DepartureTime)
            .HasColumnName("departure_time")
            .HasConversion<DateTimeOffsetToUtcMinutesConverter>()
            .IsRequired();

        builder
            .Property(x => x.ArrivalTime)
            .HasColumnName("arrival_time")
            .HasConversion<DateTimeOffsetToUtcMinutesConverter>()
            .IsRequired();

        builder
            .Property(x => x.Tag)
            .HasColumnName("tag");
    }
}
