using System.Net;
using System.Net.Http.Json;

namespace Rockets.Api.Tests;

public static class RocketMessages
{
    public static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string channel, int messageNumber, DateTimeOffset messageTime, string messageType, object message)
    {
        var envelope = new
        {
            metadata = new { channel, messageNumber, messageTime, messageType },
            message
        };

        return client.PostAsJsonAsync("/messages", envelope);
    }

    public static async Task<RocketApiResponse?> WaitForStateAsync(
        HttpClient client, string channel, Func<RocketApiResponse, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/rockets/{channel}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var rocket = await response.Content.ReadFromJsonAsync<RocketApiResponse>();
                if (rocket is not null && predicate(rocket))
                {
                    return rocket;
                }
            }

            await Task.Delay(200);
        }

        return null;
    }
}
