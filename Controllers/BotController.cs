using Microsoft.AspNetCore.Mvc;
using NoviSad.SokoBot.Filters;
using NoviSad.SokoBot.Services;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace NoviSad.SokoBot.Controllers;

[BotAuth]
public class BotController : ControllerBase {
    [HttpPost("bot")]
    public async Task<IActionResult> Update(
        [FromBody]Update update,
        [FromServices]ControlService service,
        CancellationToken cancellationToken
    ) {
        await service.Handle(update, cancellationToken);
        return Ok();
    }
}
