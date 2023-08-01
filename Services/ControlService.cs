using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using NoviSad.SokoBot.Data;
using NoviSad.SokoBot.Data.Entities;
using NoviSad.SokoBot.Tools;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;

namespace NoviSad.SokoBot.Services;

public class ControlService {
    private static readonly InlineKeyboardButton CancelButton = InlineKeyboardButton.WithCallbackData("Отмена", Serializer.SerializeRequestContext(RequestContext.Empty with { Cancel = true }));

    private static readonly TimeSpan[] UserTimeSlots = { TimeSpan.FromHours(4), TimeSpan.FromHours(12), TimeSpan.FromHours(20) };

    private readonly ILogger<ControlService> _logger;
    private readonly ISystemClock _systemClock;
    private readonly ITelegramBotClient _botClient;
    private readonly TrainService _trainService;
    private readonly IServiceProvider _serviceProvider;

    public ControlService(
        ILogger<ControlService> logger,
        ISystemClock systemClock,
        ITelegramBotClient botClient,
        IServiceProvider serviceProvider,
        TrainService trainService
    ) {
        _logger = logger;
        _systemClock = systemClock;
        _botClient = botClient;
        _serviceProvider = serviceProvider;
        _trainService = trainService;
    }

    public async Task Handle(Update update, CancellationToken cancellationToken) {
        var handler = update switch {
            { Message: { } message } => BotOnMessageReceived(message, cancellationToken),
            { EditedMessage: { } message } => BotOnMessageReceived(message, cancellationToken),
            { CallbackQuery: { } callbackQuery } => BotOnCallbackQueryReceived(callbackQuery, cancellationToken),
            { InlineQuery: { } inlineQuery } => BotOnInlineQueryReceived(inlineQuery, cancellationToken),
            _ => UnknownUpdateHandlerAsync(update, cancellationToken)
        };

        await handler;
    }

    public async Task NotifyNewPassengerIsOnboard(TelegramUser user, TrainSlot slot, CancellationToken cancellationToken) {
        var query = new TrainQuery(slot.Train.TrainNumber, slot.Train.DepartureTime);

        foreach (var passenger in slot.Passengers) {
            if (passenger == user)
                continue;

            await StartInlineQuery(
                new ChatId(passenger.Username).Identifier.Value,
                $"Новый попутчик на {slot.Train.DepartureTime:HH:mm}: {user.Nickname}",
                Serializer.SerializeTrainQuery(query),
                cancellationToken
            );
        }
    }

    private async Task BotOnMessageReceived(Message message, CancellationToken cancellationToken) {
        _logger.LogInformation("Receive message type: {MessageType}", message.Type);
        if (message.Text == null)
            return;

        var action = message.Text.Split(' ')[0] switch {
            "/start" => RequestDirection(_botClient, message.Chat.Id, cancellationToken),
            _ => Usage(_botClient, message, cancellationToken)
        };
        Message sentMessage = await action;
        _logger.LogInformation("The message was sent with id: {SentMessageId}", sentMessage.MessageId);

        static async Task<Message> Usage(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken) {
            const string Usage = @"Usage:
/start";

            return await botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: Usage,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
        }
    }

    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken) {
        _logger.LogInformation("Received inline keyboard callback from: {CallbackQueryId}", callbackQuery.Id);

        try {
            await _botClient.AnswerCallbackQueryAsync(
                callbackQueryId: callbackQuery.Id,
                cancellationToken: cancellationToken);
        } catch {
            // ignored
        }

        if (callbackQuery.Data == null)
            return;

        var response = Serializer.DeserializeRequestContext(callbackQuery.Data);
        if (response == null)
            return;

        if (response.Cancel == true)
            return;

        if (callbackQuery.Data != null) {
            try {
                if (!response.Direction.HasValue) {
                    // noop

                } else if (!response.SearchStart.HasValue && !response.SearchEnd.HasValue) {
                    await RequestTimeSlot(_botClient, callbackQuery.Message.Chat.Id, response, cancellationToken);

                } else if (!response.TrainNumber.HasValue) {
                    await RequestTrain(_botClient, callbackQuery.Message.Chat.Id, response, cancellationToken);

                } else {
                    using var scope = _serviceProvider.CreateScope();
                    await using var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

                    var user = new TelegramUser(callbackQuery.From.Username);

                    var slot = await _trainService.FindTrain(dbContext, response.TrainNumber.Value, response.DepartureTime.Value, cancellationToken);
                    if (!slot.Passengers.Contains(user)) {
                        slot = await _trainService.AddPassenger(dbContext, response.TrainNumber.Value, response.DepartureTime.Value, user, cancellationToken);
                    } else {
                        slot = await _trainService.RemovePassenger(dbContext, response.TrainNumber.Value, response.DepartureTime.Value, user, cancellationToken);
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);

                    await NotifyNewPassengerIsOnboard(user, slot, cancellationToken);

                    var query = new TrainQuery(response.TrainNumber, response.DepartureTime);

                    await StartInlineQuery(
                        callbackQuery.Message.Chat.Id,
                        $"Поезд на {response.DepartureTime:HH:mm}",
                        Serializer.SerializeTrainQuery(query),
                        cancellationToken
                    );
                }
            } catch (Exception e) {
                _logger.LogError(e, "An exception occurred");
            }
        }
    }

    private async Task<Message> RequestDirection(ITelegramBotClient botClient, long chatId, CancellationToken cancellationToken) {
        var buttons = new List<InlineKeyboardButton> {
            InlineKeyboardButton.WithCallbackData("Нови Сад", Serializer.SerializeRequestContext(RequestContext.Empty with { Direction = TrainDirection.NoviSadToBelgrade })),
            InlineKeyboardButton.WithCallbackData("Београд Центар", Serializer.SerializeRequestContext(RequestContext.Empty with { Direction = TrainDirection.BelgradeToNoviSad })),
            CancelButton
        };

        return await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Выбери станцию отправления",
            replyMarkup: ToLinearMarkup(buttons),
            cancellationToken: cancellationToken
        );
    }

    private async Task<Message> RequestTimeSlot(ITelegramBotClient botClient, long chatId, RequestContext requestContext, CancellationToken cancellationToken) {
        var cetCurrentTime = TimeZoneHelper.ToCentralEuropeanTime(_systemClock.UtcNow);
        var slotStartIndex = Array.BinarySearch(UserTimeSlots, cetCurrentTime.TimeOfDay);
        if (slotStartIndex < 0) {
            slotStartIndex = ~slotStartIndex - 1;

            if (slotStartIndex < 0) {
                slotStartIndex = UserTimeSlots.Length - 1;
            }
        }

        var buttons = new List<InlineKeyboardButton>();
        for (int i = 0; i < UserTimeSlots.Length; i++) {
            var index0 = (slotStartIndex + i) % UserTimeSlots.Length;
            var index1 = (slotStartIndex + i + 1) % UserTimeSlots.Length;

            var time0 = UserTimeSlots[index0];
            var time1 = UserTimeSlots[index1];

            var cetDate = DateOnly.FromDateTime(cetCurrentTime.DateTime);
            var cetDate0 = cetDate;
            var cetDate1 = cetDate;
            if (cetCurrentTime.TimeOfDay < time0)
                cetDate0 = cetDate0.AddDays(-1);
            if (cetCurrentTime.TimeOfDay > time1)
                cetDate1 = cetDate1.AddDays(1);

            var offset0 = new DateTimeOffset(cetDate0.ToDateTime(TimeOnly.FromTimeSpan(time0), DateTimeKind.Unspecified), cetCurrentTime.Offset);
            var offset1 = new DateTimeOffset(cetDate1.ToDateTime(TimeOnly.FromTimeSpan(time1), DateTimeKind.Unspecified), cetCurrentTime.Offset);

            var text = $"{offset0:HH:mm} - {offset1:HH:mm}";
            var context = requestContext with { SearchStart = offset0, SearchEnd = offset1 };

            buttons.Add(InlineKeyboardButton.WithCallbackData(text, Serializer.SerializeRequestContext(context)));
        }

        buttons.Add(CancelButton);

        return await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Выберите время",
            replyMarkup: ToLinearMarkup(buttons),
            cancellationToken: cancellationToken
        );
    }

    private async Task<Message> RequestTrain(ITelegramBotClient botClient, long chatId, RequestContext requestContext, CancellationToken cancellationToken) {
        using var scope = _serviceProvider.CreateScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var searchStart = requestContext.SearchStart.Value;
        var searchEnd = requestContext.SearchEnd.Value;

        var trains = await _trainService.FindTrains(dbContext, searchStart, searchEnd, cancellationToken);

        var buttons = trains
            .Select(x => {
                var passengers = x.Passengers.Count == 0 ? "-" : x.Passengers.Count.ToString();
                return InlineKeyboardButton.WithCallbackData($"{x.Train.DepartureTime:HH:mm} {passengers} {x.Train.Tag,10}", Serializer.SerializeRequestContext(requestContext with { TrainNumber = x.Train.TrainNumber, DepartureTime = x.Train.DepartureTime }));
            })
            .Append(CancelButton);

        return await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Выберите поезд",
            replyMarkup: ToLinearMarkup(buttons),
            cancellationToken: cancellationToken
        );
    }

    private async Task<Message> StartInlineQuery(long chatId, string text, string query, CancellationToken cancellationToken) {
        InlineKeyboardMarkup inlineKeyboard = new(
            InlineKeyboardButton.WithSwitchInlineQueryCurrentChat("Посмотреть", query));

        return await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: text,
            replyMarkup: inlineKeyboard,
            cancellationToken: cancellationToken);
    }

    private async Task BotOnInlineQueryReceived(InlineQuery inlineQuery, CancellationToken cancellationToken) {
        _logger.LogInformation("Received inline query from: {InlineQueryFromId}", inlineQuery.From.Id);

        var refreshQuery = Serializer.DeserializeTrainQuery(inlineQuery.Query);
        if (refreshQuery != null && refreshQuery.TrainNumber.HasValue && refreshQuery.DepartureTime.HasValue) {
            using var scope = _serviceProvider.CreateScope();
            await using var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

            var slot = await _trainService.FindTrain(dbContext, refreshQuery.TrainNumber.Value, refreshQuery.DepartureTime.Value, cancellationToken);
            if (slot == null)
                return;

            InlineQueryResult[] results = {
                new InlineQueryResultArticle(
                    id: "1",
                    title: "Пассажиры",
                    inputMessageContent: new InputTextMessageContent(string.Join('\n', slot.Passengers.Select(x => x.Username))))
            };

            await _botClient.AnswerInlineQueryAsync(
                inlineQueryId: inlineQuery.Id,
                results: results,
                cacheTime: 0,
                isPersonal: true,
                cancellationToken: cancellationToken);
        }
    }

    private static InlineKeyboardMarkup ToLinearMarkup(IEnumerable<InlineKeyboardButton> buttons) {
        return new InlineKeyboardMarkup(buttons.Select(x => new[] { x }).ToArray());
    }

    private Task UnknownUpdateHandlerAsync(Update update, CancellationToken cancellationToken) {
        _logger.LogInformation("Unknown update type: {UpdateType}", update.Type);
        return Task.CompletedTask;
    }
}
