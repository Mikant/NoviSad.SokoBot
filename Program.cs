using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoviSad.SokoBot.Data;
using NoviSad.SokoBot.Services;
using NoviSad.SokoBot.Tools;
using Telegram.Bot;

namespace NoviSad.SokoBot;

class Program {
    static void Main(string[] args) {
        using (var dbContext = new BotDbContext())
            dbContext.Database.Migrate();

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<BotConfiguration>(builder.Configuration.GetSection(nameof(BotConfiguration)));

        builder.Services
            .AddHttpClient("telegram_bot_client")
            .AddTypedClient<ITelegramBotClient>((httpClient, sp) => {
                var config = sp.GetRequiredService<IOptions<BotConfiguration>>();
                var options = new TelegramBotClientOptions(config.Value.BotToken);
                return new TelegramBotClient(options, httpClient);
            });

        builder.Services.AddSingleton<ISystemClock, SystemClock>();

        builder.Services
            .AddEntityFrameworkSqlite()
            .AddDbContext<BotDbContext>();

        builder.Services.AddHostedService<TimetableUpdateService>();
        builder.Services.AddHostedService<CleanupService>();
        builder.Services.AddHostedService<WebhookService>();

        builder.Services.AddScoped<ControlService>();
        builder.Services.AddScoped<TrainService>();

        builder.Services
            .AddControllers()
            .AddNewtonsoftJson();

        var app = builder.Build();
        app.MapControllers();
        app.Services.GetRequiredService<ILogger<Program>>().LogInformation("Starting application");
        app.Run();
    }
}
