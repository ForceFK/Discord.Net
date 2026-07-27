using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Discord.AspNetCore;

internal static class DiscordInteractionRequestReader
{
    public static async Task<byte[]> ReadAsync(HttpRequest request, long maximumSize, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximumSize)
            throw new BadHttpRequestException("The interaction request body is too large.", StatusCodes.Status413PayloadTooLarge);

        using var body = new MemoryStream(request.ContentLength is > 0 ? (int)request.ContentLength.Value : 0);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (body.Length + read > maximumSize)
                throw new BadHttpRequestException("The interaction request body is too large.", StatusCodes.Status413PayloadTooLarge);
            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return body.ToArray();
    }
}
