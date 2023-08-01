using Microsoft.EntityFrameworkCore;
using NoviSad.SokoBot.Data.Dto;

namespace NoviSad.SokoBot.Data;

public class BotDbContext : DbContext {
    public DbSet<TrainDto> Trains => Set<TrainDto>();

    public DbSet<PassengerDto> Passengers => Set<PassengerDto>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
        optionsBuilder.UseSqlite("Data Source=db.sqlite");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BotDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
