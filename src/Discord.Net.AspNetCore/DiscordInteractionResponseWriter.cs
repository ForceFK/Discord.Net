using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Discord.AspNetCore;

internal sealed class DiscordInteractionResponseWriter
{
    private readonly HttpContext _httpContext;
    private readonly ILogger _logger;
    private int _responseCount;

    public DiscordInteractionResponseWriter(HttpContext httpContext, ILogger logger)
    {
        _httpContext = httpContext;
        _logger = logger;
    }

    public bool HasResponded => Volatile.Read(ref _responseCount) != 0;

    public async Task WriteAsync(string payload)
    {
        if (Interlocked.Increment(ref _responseCount) != 1)
        {
            _logger.LogError("A Discord interaction attempted to send more than one initial response.");
            throw new InvalidOperationException("Only one initial Discord interaction response may be sent per request.");
        }

        if (_httpContext.Response.HasStarted)
            throw new InvalidOperationException("The HTTP response has already started before the interaction response was written.");

        _httpContext.Response.StatusCode = StatusCodes.Status200OK;
        _httpContext.Response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(payload);
        _httpContext.Response.ContentLength = bytes.Length;
        await _httpContext.Response.Body.WriteAsync(bytes, _httpContext.RequestAborted).ConfigureAwait(false);
    }
}
