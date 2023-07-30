using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using System;

namespace NoviSad.SokoBot.Filters; 

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class BotAuthAttribute : TypeFilterAttribute {
    private const string AuthTokenHeaderName = @"X-Telegram-Bot-Api-Secret-Token";

    public BotAuthAttribute()
        : base(typeof(ValidateTelegramBotFilter)) {
    }

    private class ValidateTelegramBotFilter : IActionFilter {
        private readonly string _authToken;

        public ValidateTelegramBotFilter(IOptions<BotConfiguration> options) {
            _authToken = options.Value.AuthToken;
        }

        public void OnActionExecuted(ActionExecutedContext context) { }

        public void OnActionExecuting(ActionExecutingContext context) {
            if (!IsValidRequest(context.HttpContext.Request)) {
                context.Result = new ObjectResult($"\"{AuthTokenHeaderName}\" is invalid") {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
            }
        }

        private bool IsValidRequest(HttpRequest request) {
            if (!request.Headers.TryGetValue(AuthTokenHeaderName, out var token))
                return false;

            return string.Equals(token, _authToken, StringComparison.Ordinal);
        }
    }
}