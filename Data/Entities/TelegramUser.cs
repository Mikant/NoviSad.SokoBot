using System;
using System.Diagnostics;
using Microsoft;
using Telegram.Bot.Types;

namespace NoviSad.SokoBot.Data.Entities;

[DebuggerDisplay("{Nickname}")]
public class TelegramUser : IEquatable<TelegramUser> {
    private readonly string _nickname;
    private readonly long _chatId;

    public TelegramUser(string nickname, long chatId) {
        Requires.NotNullOrWhiteSpace(nickname, nameof(nickname));
        Requires.Argument(nickname[0] != '@', nameof(nickname), "Nickname must not start with an '@'");
        Requires.Range(chatId > 0, nameof(chatId));

        _nickname = nickname;
        _chatId = chatId;
    }

    public string Nickname => _nickname;

    public string Username => '@' + _nickname;

    public long ChatId => _chatId;

    public static implicit operator ChatId(TelegramUser user) => new(user._chatId);

    private static bool Equals(TelegramUser? user0, TelegramUser? user1) {
        if (user0 is null ^ user1 is null)
            return false;
        if (user0 is null)
            return true;

        return user0._nickname == user1._nickname;
    }

    public bool Equals(TelegramUser? other) => Equals(this, other);

    public override bool Equals(object? obj) => obj is TelegramUser user && Equals(this, user);

    public static bool operator ==(TelegramUser user0, TelegramUser user1) => Equals(user0, user1);
    public static bool operator !=(TelegramUser user0, TelegramUser user1) => !Equals(user0, user1);

    public override int GetHashCode() => _nickname.GetHashCode();
}
