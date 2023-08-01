using System;
using System.Diagnostics;
using Microsoft;

namespace NoviSad.SokoBot.Data.Entities;

[DebuggerDisplay("{Nickname}")]
public class TelegramUser : IEquatable<TelegramUser> {
    public string Nickname { get; }

    public TelegramUser(string nickname) {
        Requires.NotNullOrWhiteSpace(nickname, nameof(nickname));
        Requires.Argument(nickname[0] != '@', nameof(nickname), "Nickname must not start with an '@'");

        Nickname = nickname;
    }

    public string Username => '@' + Nickname;

    private static bool Equals(TelegramUser? user0, TelegramUser? user1) {
        if (user0 is null ^ user1 is null)
            return false;
        if (user0 is null)
            return true;

        return user0.Nickname == user1.Nickname;
    }

    public bool Equals(TelegramUser? other) => Equals(this, other);

    public override bool Equals(object? obj) => obj is TelegramUser user && Equals(this, user);

    public static bool operator ==(TelegramUser user0, TelegramUser user1) => Equals(user0, user1);
    public static bool operator !=(TelegramUser user0, TelegramUser user1) => !Equals(user0, user1);

    public override int GetHashCode() => Nickname.GetHashCode();
}
