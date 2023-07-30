using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoviSad.SokoBot.Services;
using NoviSad.SokoBot.Tools;
using Telegram.Bot;

namespace NoviSad.SokoBot;

static class Program {
    static void Main(string[] args) {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<BotConfiguration>(builder.Configuration.GetSection(nameof(BotConfiguration)));

        builder.Services
            .AddHttpClient("telegram_bot_client")
            .AddTypedClient<ITelegramBotClient>((httpClient, sp) => {
                var config = sp.GetRequiredService<IOptions<BotConfiguration>>();
                var options = new TelegramBotClientOptions(config.Value.BotToken);
                return new TelegramBotClient(options, httpClient);
            });

        builder.Services.AddHostedService<WebhookService>();

        builder.Services.AddScoped<BotService>();

        builder.Services
            .AddControllers()
            .AddNewtonsoftJson();

        var app = builder.Build();
        app.MapControllers();
        app.Run();
    }
}
