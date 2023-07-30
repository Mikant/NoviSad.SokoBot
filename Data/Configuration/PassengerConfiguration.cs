using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NoviSad.SokoBot.Data.Dto;

namespace NoviSad.SokoBot.Data.Configuration;

public class PassengerConfiguration : IEntityTypeConfiguration<PassengerDto> {
    public void Configure(EntityTypeBuilder<PassengerDto> builder) {
        builder.ToTable("passenger");

        builder.HasKey(x => x.Id);

        builder
            .HasIndex(x => x.Nickname)
            .IsUnique();

        builder
            .Property(x => x.Id)
            .HasColumnName("id")
            .IsRequired()
            .ValueGeneratedOnAdd();

        builder
            .Property(x => x.Nickname)
            .HasColumnName("nickname")
            .IsRequired();
    }
}
