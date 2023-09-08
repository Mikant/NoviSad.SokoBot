using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using NoviSad.SokoBot.Data;
using NoviSad.SokoBot.Data.Entities;
using NoviSad.SokoBot.Tools;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace NoviSad.SokoBot.Services;

public class ControlService {
    private static readonly InlineKeyboardButton CancelButton = InlineKeyboardButton.WithCallbackData("◀️ Отмена", Serializer.Serialize(RequestContext.Empty with { Cancel = true }));

    private static readonly TimeSpan[] TimeCutoffs = { TimeSpan.FromHours(4), TimeSpan.FromHours(12), TimeSpan.FromHours(20) };

    private readonly ILogger<ControlService> _logger;
    private readonly ISystemClock _systemClock;
    private readonly ITelegramBotClient _botClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly TrainService _trainService;

    private bool _callbackIsAnswered;

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

    public Task Handle(Update update, CancellationToken cancellationToken) {
        return update switch {
            { Message: { } message } => OnMessageReceived(message, cancellationToken),
            { CallbackQuery: { } callbackQuery } => BotOnCallbackQueryReceived(callbackQuery, cancellationToken),
            _ => Task.CompletedTask
        };
    }

    private async Task OnMessageReceived(Message message, CancellationToken cancellationToken) {
        _logger.LogDebug("Received message of type: {MessageType}", message.Type);
        if (message.Text == null)
            return;

        var chatId = message.Chat.Id;

        var action = message.Text.Split(' ')[0] switch {
            "/start" => Start(message, false, cancellationToken),
            "/spectate" => Start(message, true, cancellationToken),
            _ => ShowUsage(chatId, cancellationToken)
        };

        await action;
    }

    private async Task<Message> ShowUsage(long chatId, CancellationToken cancellationToken) {
        const string Usage = @"Допустимые команды:
/start
/spectate";

        return await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: Usage,
            cancellationToken: cancellationToken);
    }

    private async Task Start(Message message, bool spectate, CancellationToken cancellationToken) {
        //await _botClient.DeleteMessageAsync(message.Chat.Id, message.MessageId, cancellationToken);

        _logger.LogInformation("Starting process for {user} (spectate: {spectate})", message.Chat.Username, spectate);

        if (spectate) {
            await _botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "В этом режиме сесть или сойти с поезда не получится, но можно потыкать-посмотреть",
                cancellationToken: cancellationToken
            );
        }

        await RequestDirection(message.Chat.Id, RequestContext.Empty with { Spectate = spectate }, cancellationToken);
    }

    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken) {
        _logger.LogDebug("Received inline keyboard callback from: {CallbackQueryId}", callbackQuery.Id);

        using var scope = _serviceProvider.CreateScope();

        try {
            if (callbackQuery.Message == null) {
                _logger.LogWarning("Callback query message is null");
                return;
            }

            if (callbackQuery.Data == null) {
                _logger.LogInformation("Callback query data is null");
                return;
            }

            var username = callbackQuery.From.Username;
            if (string.IsNullOrEmpty(username)) {
                _logger.LogError("Empty username");
                return;
            }

            var user = new TelegramUser(username, callbackQuery.Message.Chat.Id);

            var requestContext = Serializer.DeserializeRequestContext(callbackQuery.Data);
            if (requestContext != null) {
                try {
                    await _botClient.DeleteMessageAsync(user.ChatId, callbackQuery.Message.MessageId, cancellationToken);
                } catch (Exception e) {
                    _logger.LogError(e, "An exception occurred while deleting message. payload: {@payload}, context: {@context}", callbackQuery, requestContext);
                }

                if (requestContext.Cancel)
                    return;

                try {
                    if (!requestContext.Direction.HasValue) {
                        // noop

                    } else if (!requestContext.SearchStart.HasValue || !requestContext.SearchEnd.HasValue) {
                        await RequestTimeSpan(user, requestContext, cancellationToken);

                    } else if (!requestContext.TrainNumber.HasValue || !requestContext.DepartureTime.HasValue) {
                        await RequestTrain(user, requestContext, cancellationToken);

                    } else {
                        await using var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

                        var slot = await _trainService.FindTrain(dbContext, requestContext.TrainNumber.Value, requestContext.DepartureTime.Value, cancellationToken);
                        if (slot == null) {
                            _logger.LogInformation("Train is not found");
                            await NotifyTrainIsNotFound(callbackQuery.Id, cancellationToken);
                            return;
                        }

                        var spectate = requestContext.Spectate;
                        var userExisted = slot.Passengers.Contains(user);

                        if (!spectate) {
                            if (!userExisted && !requestContext.Leave)
                                slot = await _trainService.AddPassenger(dbContext, requestContext.TrainNumber.Value, requestContext.DepartureTime.Value, user, cancellationToken);
                            else if (userExisted && requestContext.Leave)
                                slot = await _trainService.RemovePassenger(dbContext, requestContext.TrainNumber.Value, requestContext.DepartureTime.Value, user, cancellationToken);

                            if (slot == null) {
                                _logger.LogError("Train is not modified");
                                await NotifyTrainIsNotFound(callbackQuery.Id, cancellationToken);
                                return;
                            }

                            await dbContext.SaveChangesAsync(cancellationToken);
                        }

                        if (!spectate) {
                            if (!userExisted && !requestContext.Leave) {
                                await Notify(callbackQuery.Id, "Сели в поезд", cancellationToken);
                                await NotifyNewPassengerIsOnboard(user, slot, cancellationToken);
                            } else if (userExisted && requestContext.Leave) {
                                await Notify(callbackQuery.Id, "Сошли с поезда", cancellationToken);
                            }
                        }

                        if (spectate || !userExisted && !requestContext.Leave) {
                            await ShowTrackerMessage(
                                user,
                                slot,
                                null,
                                cancellationToken
                            );
                        }
                    }
                } catch (Exception e) {
                    _logger.LogError(e, "An exception occurred");
                }
            } else {
                var trainQuery = Serializer.DeserializeTrainQuery(callbackQuery.Data);
                if (trainQuery != null) {
                    await using var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

                    var slot = await _trainService.FindTrain(dbContext, trainQuery.TrainNumber, trainQuery.DepartureTime, cancellationToken);
                    if (slot == null) {
                        _logger.LogInformation("Train is not found");
                        await NotifyTrainIsNotFound(callbackQuery.Id, cancellationToken);
                        try {
                            await _botClient.DeleteMessageAsync(user.ChatId, callbackQuery.Message.MessageId, cancellationToken);
                        } catch (Exception e) {
                            _logger.LogError(e, "An exception occurred while deleting message. payload: {@payload}, context: {@context}", callbackQuery, requestContext);
                        }
                        return;
                    }

                    if (trainQuery.Leave) {
                        try {
                            await _botClient.DeleteMessageAsync(user.ChatId, callbackQuery.Message.MessageId, cancellationToken);
                        } catch (Exception e) {
                            _logger.LogError(e, "An exception occurred while deleting message. payload: {@payload}, context: {@context}", callbackQuery, requestContext);
                        }

                        slot = await _trainService.RemovePassenger(dbContext, trainQuery.TrainNumber, trainQuery.DepartureTime, user, cancellationToken);

                        if (slot == null) {
                            _logger.LogError("Train is not modified");
                            await NotifyTrainIsNotFound(callbackQuery.Id, cancellationToken);
                            return;
                        }

                        await dbContext.SaveChangesAsync(cancellationToken);

                        await Notify(callbackQuery.Id, "Сошли с поезда", cancellationToken);

                    } else {
                        await ShowTrackerMessage(
                            user,
                            slot,
                            callbackQuery.Message.MessageId,
                            cancellationToken: cancellationToken
                        );
                    }
                }
            }

        } finally {
            if (!_callbackIsAnswered) {
                await Notify(
                    callbackQuery.Id,
                    null!,
                    cancellationToken);
            }
        }
    }

    private async Task RequestDirection(long chatId, RequestContext requestContext, CancellationToken cancellationToken) {
        var buttons = new List<InlineKeyboardButton> {
            InlineKeyboardButton.WithCallbackData("Нови Сад", Serializer.Serialize(requestContext with { Direction = TrainDirection.NoviSadToBelgrade })),
            InlineKeyboardButton.WithCallbackData("Београд Центар", Serializer.Serialize(requestContext with { Direction = TrainDirection.BelgradeToNoviSad })),
        };

        buttons.Add(CancelButton);

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Станция отправления:",
            replyMarkup: ToLinearMarkup(buttons),
            cancellationToken: cancellationToken
        );
    }

    private async Task RequestTimeSpan(TelegramUser user, RequestContext requestContext, CancellationToken cancellationToken) {
        var cetCurrentTime = TimeZoneHelper.ToCentralEuropeanTime(_systemClock.UtcNow);
        var slotStartIndex = Array.BinarySearch(TimeCutoffs, cetCurrentTime.TimeOfDay);
        if (slotStartIndex < 0) {
            slotStartIndex = ~slotStartIndex - 1;
            if (slotStartIndex < 0)
                slotStartIndex = TimeCutoffs.Length - 1;
        }

        var cetStartTime = TimeCutoffs[slotStartIndex % TimeCutoffs.Length];
        var cetStartDate = DateOnly.FromDateTime(cetCurrentTime.DateTime);
        if (cetCurrentTime.TimeOfDay < cetStartTime)
            cetStartDate = cetStartDate.AddDays(-1);
        var offset0 = new DateTimeOffset(cetStartDate.ToDateTime(TimeOnly.FromTimeSpan(cetStartTime), DateTimeKind.Unspecified), cetCurrentTime.Offset);

        var buttons = new List<InlineKeyboardButton>();
        for (int i = 0; i < TimeCutoffs.Length; i++) {
            var index0 = (slotStartIndex + i) % TimeCutoffs.Length;
            var index1 = (slotStartIndex + i + 1) % TimeCutoffs.Length;

            var time0 = TimeCutoffs[index0];
            var time1 = TimeCutoffs[index1];

            var duration = time1 - time0;
            if (duration < TimeSpan.Zero)
                duration = TimeSpan.FromDays(1) + duration;

            var offset1 = offset0 + duration;

            var text = $"{offset0:HH:mm} - {offset1:HH:mm}";
            var context = requestContext with { SearchStart = offset0, SearchEnd = offset1 };

            buttons.Add(InlineKeyboardButton.WithCallbackData(text, Serializer.Serialize(context)));

            offset0 = offset1;
        }

        buttons.Add(CancelButton);

        await _botClient.SendTextMessageAsync(
            chatId: user,
            text: "Время:",
            replyMarkup: ToLinearMarkup(buttons),
            cancellationToken: cancellationToken
        );
    }

    private async Task RequestTrain(TelegramUser user, RequestContext requestContext, CancellationToken cancellationToken) {
        if (!requestContext.SearchStart.HasValue || !requestContext.SearchEnd.HasValue) {
            _logger.LogError("Invalid search range");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var searchStart = requestContext.SearchStart.Value;
        var searchEnd = requestContext.SearchEnd.Value;

        var trains = await _trainService.FindTrains(dbContext, requestContext.Direction, searchStart, searchEnd, cancellationToken);

        var buttons = trains
            .Select(x => {
                var cetDepartureTime = TimeZoneHelper.ToCentralEuropeanTime(x.Train.DepartureTime);

                static bool IsSoko(string? tag) => tag == TrainTimetableLoader.SokoTag;

                var passengersText = x.Passengers.Count == 0 ? string.Empty : $"{x.Passengers.Count} 𓀠";

                var row = new List<InlineKeyboardButton>();

                if (!requestContext.Spectate && x.Passengers.Any(x => x.Nickname == user.Nickname)) {
                    var text = $"{(IsSoko(x.Train.Tag) ? "𓅃 " : string.Empty)}{cetDepartureTime:HH:mm} {passengersText}";

                    row.Add(InlineKeyboardButton.WithCallbackData(text, Serializer.Serialize(requestContext with { TrainNumber = x.Train.TrainNumber, DepartureTime = x.Train.DepartureTime, Leave = false })));
                    row.Add(InlineKeyboardButton.WithCallbackData("❌ Выйти", Serializer.Serialize(requestContext with { TrainNumber = x.Train.TrainNumber, DepartureTime = x.Train.DepartureTime, Leave = true })));
                } else {
                    var text = $"{(IsSoko(x.Train.Tag) ? "𓅃" : "    ")}        {cetDepartureTime:HH:mm}      {passengersText,6}";

                    row.Add(InlineKeyboardButton.WithCallbackData(text, Serializer.Serialize(requestContext with { TrainNumber = x.Train.TrainNumber, DepartureTime = x.Train.DepartureTime, Leave = false })));
                }

                return row;
            });

        buttons = buttons.Append(new List<InlineKeyboardButton> { CancelButton });

        await _botClient.SendTextMessageAsync(
            chatId: user,
            text: "Поезд:",
            replyMarkup: new InlineKeyboardMarkup(buttons),
            cancellationToken: cancellationToken
        );
    }

    private async Task ShowTrackerMessage(TelegramUser user, TrainSlot slot, int? updateMessageId, CancellationToken cancellationToken) {
        _logger.LogDebug("Tracker message to {user}", user.Username);

        var cetDepartureTime = TimeZoneHelper.ToCentralEuropeanTime(slot.Train.DepartureTime);

        var messageBuilder = new StringBuilder();
        messageBuilder.Append("Поезд ");
        messageBuilder.Append(slot.Train.Direction switch {
            TrainDirection.NoviSadToBelgrade => "Нови-Сад - Белград",
            TrainDirection.BelgradeToNoviSad => "Белград - Нови-Сад",
            _ => throw new InvalidEnumArgumentException(nameof(TrainDirection), (int)slot.Train.Direction, typeof(TrainDirection))
        });
        messageBuilder.AppendLine($" на {cetDepartureTime:HH:mm} {cetDepartureTime:dd/MM}");

        if (slot.Passengers.Count == 0) {
            messageBuilder.AppendLine("Нет пассажиров");

        } else {
            messageBuilder.AppendLine("Пассажиры:");
            foreach (var passenger in slot.Passengers) {
                messageBuilder.AppendLine(passenger.Username);
            }
        }

        var refreshButton = InlineKeyboardButton.WithCallbackData("🔄 Обновить", Serializer.Serialize(new TrainQuery(slot.Train.TrainNumber, slot.Train.DepartureTime)));

        InlineKeyboardMarkup markup;
        if (slot.Passengers.Contains(user)) {
            var exitButton = InlineKeyboardButton.WithCallbackData("❌ Выйти", Serializer.Serialize(new TrainQuery(slot.Train.TrainNumber, slot.Train.DepartureTime) { Leave = true }));

            markup = new InlineKeyboardMarkup(new[] { new[] { refreshButton, exitButton } });

        } else {
            markup = new InlineKeyboardMarkup(refreshButton);
        }

        if (updateMessageId.HasValue) {
            await _botClient.EditMessageTextAsync(
                chatId: user,
                messageId: updateMessageId.Value,
                text: messageBuilder.ToString(),
                cancellationToken: cancellationToken);
            await _botClient.EditMessageReplyMarkupAsync(
                chatId: user,
                messageId: updateMessageId.Value,
                replyMarkup: markup,
                cancellationToken: cancellationToken);

        } else {
            await _botClient.SendTextMessageAsync(
                chatId: user,
                text: messageBuilder.ToString(),
                replyMarkup: markup,
                cancellationToken: cancellationToken);
        }
    }

    private Task NotifyTrainIsNotFound(string callbackQueryId, CancellationToken cancellationToken) {
        return Notify(callbackQueryId, "Поезд не найден", cancellationToken);
    }

    private async Task Notify(string callbackQueryId, string text, CancellationToken cancellationToken) {
        await _botClient.AnswerCallbackQueryAsync(
            callbackQueryId: callbackQueryId,
            text: text,
            cancellationToken: cancellationToken
        );

        _callbackIsAnswered = true;
    }

    private async Task NotifyNewPassengerIsOnboard(TelegramUser user, TrainSlot slot, CancellationToken cancellationToken) {
        var query = new TrainQuery(slot.Train.TrainNumber, slot.Train.DepartureTime);

        _logger.LogDebug("Notifying existing passengers. user: {user}, passengers: {passengers}", user.Username, string.Join(", ", slot.Passengers.Select(x => x.Username)));

        foreach (var passenger in slot.Passengers) {
            if (passenger == user)
                continue;

            try {
                var buttons = new[] {
                    InlineKeyboardButton.WithCallbackData("Посмотреть", Serializer.Serialize(query))
                };

                var cetDepartureTime = TimeZoneHelper.ToCentralEuropeanTime(slot.Train.DepartureTime);

                await _botClient.SendTextMessageAsync(
                    chatId: passenger,
                    text: $"Новый попутчик на {cetDepartureTime:HH:mm}: {user.Username}",
                    replyMarkup: ToLinearMarkup(buttons),
                    cancellationToken: cancellationToken);

            } catch (Exception e) {
                _logger.LogError(e, "An exception occurred");
            }
        }
    }

    private static InlineKeyboardMarkup ToLinearMarkup(IEnumerable<InlineKeyboardButton> buttons) {
        return new InlineKeyboardMarkup(buttons.Select(x => new[] { x }).ToArray());
    }
}
