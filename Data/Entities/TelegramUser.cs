using System.Diagnostics;
using Microsoft;

namespace NoviSad.SokoBot.Data.Entities;

[DebuggerDisplay("{Nickname}")]
public class TelegramUser {
    public string Nickname { get; }

    public TelegramUser(string nickname) {
        Requires.NotNullOrWhiteSpace(nickname, nameof(nickname));
        Requires.Argument(nickname[0] == '@', nameof(nickname), "Nickname must start with an '@'");

        Nickname = nickname;
    }
}
