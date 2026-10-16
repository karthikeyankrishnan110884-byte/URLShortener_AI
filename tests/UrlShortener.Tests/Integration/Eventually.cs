namespace UrlShortener.Tests.Integration;

/// <summary>Polls an eventually-consistent read (async analytics) instead of sleeping for a fixed time.</summary>
internal static class Eventually
{
    public static async Task<T> GetAsync<T>(Func<Task<T>> read, Func<T, bool> until, int timeoutMs = 5_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var value = await read();
            if (until(value) || DateTime.UtcNow > deadline)
            {
                return value;
            }

            await Task.Delay(20);
        }
    }
}
