using System;
using Microsoft.AspNetCore.Mvc;
using NoviSad.SokoBot.Filters;
using NoviSad.SokoBot.Services;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace NoviSad.SokoBot.Controllers;

[BotAuth]
public class BotController : ControllerBase {
    private readonly ILogger<BotController> _logger;

    public BotController(ILogger<BotController> logger) {
        _logger = logger;
    }

    [HttpPost("bot")]
    public async Task<IActionResult> Update(
        [FromBody]Update update,
        [FromServices]ControlService service,
        CancellationToken cancellationToken
    ) {
        try {
            await service.Handle(update, cancellationToken);

        } catch (Exception e) {
            _logger.LogError(e, "An exception occurred");
        }

        return Ok();
    }
}
