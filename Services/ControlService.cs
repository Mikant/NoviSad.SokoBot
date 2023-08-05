using System;
using System.Collections.Generic;
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
    private static readonly TimeSpan[] TimeCutoffs = { TimeSpan.FromHours(4), TimeSpan.FromHours(12), TimeSpan.FromHours(20) };

    private readonly ILogger<ControlService> _logger;
    private readonly ISystemClock _systemClock;
    private readonly ITelegramBotClient _botClient;
    private readonly TrainService _trainService;
    private readonly IServiceProvider _serviceProvider;

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
            { EditedMessage: { } message } => OnMessageReceived(message, cancellationToken),
            { CallbackQuery: { } callbackQuery } => BotOnCallbackQueryReceived(callbackQuery, cancellationToken),
            _ => Task.CompletedTask
        };
    }

    private async Task OnMessageReceived(Message message, CancellationToken cancellationToken) {
        _logger.LogInformation("Received message of type: {MessageType}", message.Type);
        if (message.Text == null)
            return;

        var chatId = message.Chat.Id;

        var action = message.Text.Split(' ')[0] switch {
            "/start" => Start(chatId, false, cancellationToken),
            "/spectate" => Start(chatId, true, cancellationToken),
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

    private async Task Start(long chatId, bool spectate, CancellationToken cancellationToken) {
        if (spectate) {
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "В этом режиме сесть или сойти с поезда не получится, но можно потыкать-посмотреть",
                cancellationToken: cancellationToken
            );
        }

        await RequestDirection(chatId, RequestContext.Empty with { Spectate = spectate }, cancellationToken);
    }

    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken) {
        _logger.LogInformation("Received inline keyboard callback from: {CallbackQueryId}", callbackQuery.Id);

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

            var chatId = callbackQuery.Message.Chat.Id;

            var requestContext = Serializer.DeserializeRequestContext(callbackQuery.Data);
            if (requestContext != null) {
                if (callbackQuery.Data != null) {
                    try {
                        if (!requestContext.Direction.HasValue) {
                            // noop

                        } else if (!requestContext.SearchStart.HasValue || !requestContext.SearchEnd.HasValue) {
                            await RequestTimeSpan(chatId, requestContext, cancellationToken);

                        } else if (!requestContext.TrainNumber.HasValue || !requestContext.DepartureTime.HasValue) {
                            await RequestTrain(chatId, requestContext, cancellationToken);

                        } else {
                            await using var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

                            var username = callbackQuery.From.Username;
                            if (string.IsNullOrEmpty(username)) {
                                _logger.LogError("Empty username");
                                return;
                            }

                            var user = new TelegramUser(username);

                            var slot = await _trainService.FindTrain(dbContext, requestContext.TrainNumber.Value, requestContext.DepartureTime.Value, cancellationToken);
                            if (slot == null) {
                                _logger.LogError("Train is not found");
                                await NotifyTrainIsNotFound(callbackQuery.Id, cancellationToken);
                                return;
                            }

                            var spectate = requestContext.Spectate == true;
                            var onboarding = !slot.Passengers.Contains(user);

                            if (!spectate) {
                                if (onboarding)
                                    slot = await _trainService.AddPassenger(dbContext, requestContext.TrainNumber.Value, requestContext.DepartureTime.Value, user, cancellationToken);
                                else
                                    slot = await _trainService.RemovePassenger(dbContext, requestContext.TrainNumber.Value, requestContext.DepartureTime.Value, user, cancellationToken);

                                if (slot == null) {
                                    _logger.LogError("Train is not modified");
                                    await NotifyTrainIsNotFound(callbackQuery.Id, cancellationToken);
                                    return;
                                }

                                await dbContext.SaveChangesAsync(cancellationToken);
                            }

                            if (!spectate)
                                await Notify(callbackQuery.Id, onboarding ? "Сели в поезд" : "Сошли с поезда", cancellationToken);

                            if (onboarding && !spectate)
                                await NotifyNewPassengerIsOnboard(user, slot, cancellationToken);

                            if (spectate || onboarding) {
                                await ShowTrackerMessage(
                                    chatId,
                                    slot,
                                    cancellationToken
                                );
                            }
                        }
                    } catch (Exception e) {
                        _logger.LogError(e, "An exception occurred");
                    }
                }
            } else {
                var trainQuery = Serializer.DeserializeTrainQuery(callbackQuery.Data);
                if (trainQuery != null) {
                    await using var dbContext = scope.ServiceProvider.GetRequiredService<BotDbContext>();

                    var slot = await _trainService.FindTrain(dbContext, trainQuery.TrainNumber, trainQuery.DepartureTime, cancellationToken);
                    if (slot == null)
                        return;

                    await ShowTrackerMessage(
                        chatId: chatId,
                        slot,
                        cancellationToken: cancellationToken
                    );
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
            InlineKeyboardButton.WithCallbackData("Нови Сад", Serializer.SerializeRequestContext(requestContext with { Direction = TrainDirection.NoviSadToBelgrade })),
            InlineKeyboardButton.WithCallbackData("Београд Центар", Serializer.SerializeRequestContext(requestContext with { Direction = TrainDirection.BelgradeToNoviSad })),
        };

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Станция отправления:",
            replyMarkup: ToLinearMarkup(buttons),
            cancellationToken: cancellationToken
        );
    }

    private async Task RequestTimeSpan(long chatId, RequestContext requestContext, CancellationToken cancellationToken) {
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

            buttons.Add(InlineKeyboardButton.WithCallbackData(text, Serializer.SerializeRequestContext(context)));

            offset0 = offset1;
        }

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Время:",
            replyMarkup: ToLinearMarkup(buttons),
            cancellationToken: cancellationToken
        );
    }

    private async Task RequestTrain(long chatId, RequestContext requestContext, CancellationToken cancellationToken) {
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

                var passengersText = x.Passengers.Count == 0 ? "-" : $"{x.Passengers.Count} чел.";
                var text = $"{(IsSoko(x.Train.Tag) ? "𓅃" : "    ")}      {cetDepartureTime:HH:mm}      {passengersText,6}";

                return InlineKeyboardButton.WithCallbackData(text, Serializer.SerializeRequestContext(requestContext with { TrainNumber = x.Train.TrainNumber, DepartureTime = x.Train.DepartureTime }));
            });

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Поезд:",
            replyMarkup: ToLinearMarkup(buttons),
            cancellationToken: cancellationToken
        );
    }

    private async Task ShowTrackerMessage(long chatId, TrainSlot slot, CancellationToken cancellationToken) {
        var buttons = new[] {
            InlineKeyboardButton.WithCallbackData("Обновить", Serializer.SerializeTrainQuery(new TrainQuery(slot.Train.TrainNumber, slot.Train.DepartureTime)))
        };

        var cetDepartureTime = TimeZoneHelper.ToCentralEuropeanTime(slot.Train.DepartureTime);

        var messageBuilder = new StringBuilder();
        messageBuilder.AppendLine($"Поезд на {cetDepartureTime:HH:mm} {cetDepartureTime:dd/MM}");

        if (slot.Passengers.Count == 0) {
            messageBuilder.AppendLine("Нет пассажиров");

        } else {
            messageBuilder.AppendLine("Пассажиры:");
            foreach (var passenger in slot.Passengers) {
                messageBuilder.AppendLine(passenger.Username);
            }
        }

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: messageBuilder.ToString(),
            replyMarkup: ToLinearMarkup(buttons),
            cancellationToken: cancellationToken);
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

        foreach (var passenger in slot.Passengers) {
            if (passenger == user)
                continue;

            try {
                var identifier = new ChatId(passenger.Username).Identifier;
                if (identifier == null) {
                    _logger.LogError("Invalid chat id for username: {username}", passenger.Username);
                    continue;
                }

                var chatId = identifier.Value;

                var buttons = new[] {
                    InlineKeyboardButton.WithCallbackData("Посмотреть", Serializer.SerializeTrainQuery(new TrainQuery(slot.Train.TrainNumber, slot.Train.DepartureTime)))
                };

                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"Новый попутчик на {slot.Train.DepartureTime:HH:mm}: {user.Nickname}",
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
