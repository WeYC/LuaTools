using System.Net;
using System.Text;
using LuaToolsGui.Services;
using Xunit;

namespace LuaToolsGui.Tests;

/// <summary>
/// The auto-updater's GitHub requests must carry a User-Agent.
/// </summary>
/// <remarks>
/// This looks like a cosmetic detail and is not one. Velopack's <c>GitBase</c> supplies ONLY an Accept
/// header (plus Authorization when a token exists), and hands that to <see cref="ProxiedFileDownloader"/>
/// because it builds its own requests rather than using Velopack's <c>HttpClientFileDownloader</c> — the
/// latter adds <c>User-Agent: Velopack/x.y.z</c> itself. The GitHub REST API rejects a request with no
/// User-Agent with "403 Request forbidden by administrative rules", and the update feed read is exactly
/// such a request. Without the header the feed lookup can only ever succeed via the lua.tools API mirror,
/// so when that mirror is down or rate-limited the app never sees a release at all. That is precisely the
/// reported symptom (CI publishes releases, the client detects nothing), so it is pinned here.
/// </remarks>
public class ProxiedFileDownloaderTests
{
    /// <summary>
    /// Captures the headers the downloader actually puts on the wire, using a loopback listener so the
    /// assertion does not depend on GitHub, the mirrors, or which candidate wins the fallback race.
    /// </summary>
    private static async Task<(string? UserAgent, int ValueCount)> CaptureUserAgentAsync(
        IDictionary<string, string>? headers)
    {
        int port = FreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var serve = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            string? ua = ctx.Request.Headers["User-Agent"];
            var body = Encoding.UTF8.GetBytes("{\"Assets\":[]}");
            ctx.Response.ContentLength64 = body.Length;
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
            // HttpListener joins repeated header lines with ", ".
            return (ua, ua is null ? 0 : ua.Split(", ").Length);
        });

        try
        {
            await new ProxiedFileDownloader().DownloadString($"http://127.0.0.1:{port}/feed", headers);
        }
        finally
        {
            await serve;
            listener.Stop();
        }

        return await serve;
    }

    private static int FreeTcpPort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>
    /// The exact shape Velopack uses for the release-feed request: Accept only, no User-Agent.
    /// </summary>
    [Fact]
    public async Task DownloadString_AddsUserAgent_WhenVelopackSuppliesOnlyAccept()
    {
        var (ua, count) = await CaptureUserAgentAsync(
            new Dictionary<string, string> { ["Accept"] = "application/vnd.github.v3+json" });

        Assert.False(string.IsNullOrWhiteSpace(ua)); // absent -> api.github.com answers 403
        Assert.Equal(1, count);                       // two values are rejected just like none
    }

    /// <summary>Same requirement on the package-download path, which uses the same request builder.</summary>
    [Fact]
    public async Task DownloadString_AddsUserAgent_WhenNoHeadersAreSupplied()
    {
        var (ua, count) = await CaptureUserAgentAsync(headers: null);

        Assert.False(string.IsNullOrWhiteSpace(ua));
        Assert.Equal(1, count);
    }

    /// <summary>
    /// A caller that supplies its own User-Agent must keep it. The header is added with
    /// TryAddWithoutValidation, which APPENDS, so adding ours unconditionally would yield a two-value
    /// header — rejected by GitHub exactly like a missing one.
    /// </summary>
    [Fact]
    public async Task DownloadString_HonoursCallerSuppliedUserAgent_WithoutDuplicatingIt()
    {
        var (ua, count) = await CaptureUserAgentAsync(new Dictionary<string, string>
        {
            ["Accept"] = "application/octet-stream",
            ["User-Agent"] = "CallerSupplied/9.9",
        });

        Assert.Equal("CallerSupplied/9.9", ua);
        Assert.Equal(1, count);
    }
}
