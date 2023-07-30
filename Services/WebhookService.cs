using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoviSad.SokoBot.Controllers;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace NoviSad.SokoBot.Services;

public class WebhookService : IHostedService {
    private readonly ILogger<WebhookService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly BotConfiguration _config;

    public WebhookService(
        ILogger<WebhookService> logger,
        IServiceProvider serviceProvider,
        IOptions<BotConfiguration> botOptions
    ) {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _config = botOptions.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken) {
        using var scope = _serviceProvider.CreateScope();

        var route = typeof(BotController)
            .GetMethod(nameof(BotController.Update), BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttribute<HttpMethodAttribute>()!
            .Template;

        var webhookUri = new Uri(new Uri(_config.Host), route);

        _logger.LogInformation("Setting webhook: " + webhookUri.AbsoluteUri);

        var botClient = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        await botClient.SetWebhookAsync(
            url: webhookUri.AbsoluteUri,
            allowedUpdates: Array.Empty<UpdateType>(),
            secretToken: _config.AuthToken,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Webhook is set");
    }

    public async Task StopAsync(CancellationToken cancellationToken) {
        using var scope = _serviceProvider.CreateScope();
        
        var botClient = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        await botClient.DeleteWebhookAsync(cancellationToken: cancellationToken);
    
        _logger.LogInformation("Webhook is removed");
    }
}
