using System.Net;
using System.Net.Http;
using AwesomeAssertions;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

/// <summary>Manifest/package requests follow redirects by hand and check every hop.</summary>
public class ColituUpdateRedirectTests
{
    private static readonly string[] Mirrors = ["https://mirror.example.test"];

    /// <summary>Answers each request from a script: a Location for a redirect, or 200 with a body.</summary>
    private sealed class ScriptedHandler(params (HttpStatusCode Status, string? Location)[] steps) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var (status, location) = steps[Math.Min(Requests.Count - 1, steps.Length - 1)];
            var response = new HttpResponseMessage(status) { RequestMessage = request };
            if (location != null)
            {
                response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            }
            else
            {
                response.Content = new StringContent("ok");
            }
            return Task.FromResult(response);
        }
    }

    private static Task<HttpResponseMessage> Send(ScriptedHandler handler, string start = "https://colitu.com/downloads/windows/latest.json") =>
        ColituUpdateService.SendCheckedAsync(new HttpClient(handler), new Uri(start), null,
            HttpCompletionOption.ResponseContentRead, CancellationToken.None, Mirrors);

    [Fact]
    public async Task AllowedRedirects_AreFollowed()
    {
        var handler = new ScriptedHandler(
            (HttpStatusCode.Found, "https://dl.colitu.com/a.exe"),
            (HttpStatusCode.MovedPermanently, "https://mirror.example.test/b.exe"),
            (HttpStatusCode.TemporaryRedirect, "/c.exe"),
            (HttpStatusCode.OK, null));

        using var response = await Send(handler);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Requests.Select(uri => uri.AbsoluteUri).Should().Equal(
            "https://colitu.com/downloads/windows/latest.json",
            "https://dl.colitu.com/a.exe",
            "https://mirror.example.test/b.exe",
            "https://mirror.example.test/c.exe");
    }

    [Fact]
    public async Task NoRedirect_ReturnsTheResponse()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, null));
        using var response = await Send(handler);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("https://evil.example.test/x.exe")]          // host not on the list
    [InlineData("https://colitu.com.example.test/x.exe")]    // look-alike suffix
    [InlineData("http://colitu.com/x.exe")]                  // plain http
    [InlineData("https://user:pass@colitu.com/x.exe")]       // user info
    [InlineData("https://colitu.com:8443/x.exe")]            // port other than 443
    [InlineData("https://mirror.example.test:8443/x.exe")]   // mirror on another port
    [InlineData("ftp://colitu.com/x.exe")]
    public async Task UntrustedRedirect_IsRefused(string location)
    {
        var handler = new ScriptedHandler((HttpStatusCode.Found, location), (HttpStatusCode.OK, null));

        var send = () => Send(handler);

        await send.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().HaveCount(1, "the untrusted target must never be requested");
    }

    [Fact]
    public async Task FourthRedirect_IsRefused()
    {
        var handler = new ScriptedHandler((HttpStatusCode.Found, "https://colitu.com/next"));

        var send = () => Send(handler);

        await send.Should().ThrowAsync<InvalidOperationException>().WithMessage("*too many*");
        handler.Requests.Should().HaveCount(1 + ColituUpdateService.MaxRedirects);
    }

    [Fact]
    public async Task RedirectWithoutLocation_IsRefused()
    {
        var handler = new ScriptedHandler((HttpStatusCode.Found, null));
        // A 302 with content but no Location header.
        var send = () => Send(handler);
        await send.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Handler_DoesNotFollowRedirects_AndPinsEveryConnection()
    {
        using var handler = ColituUpdateService.CreateHandler();
        handler.AllowAutoRedirect.Should().BeFalse();
        handler.UseProxy.Should().BeFalse();
        handler.SslOptions.RemoteCertificateValidationCallback.Should().NotBeNull();
        handler.ConnectCallback.Should().NotBeNull();
    }

    [Fact]
    public void MaskForLog_HidesTheHost()
    {
        var masked = ColituUpdateService.MaskForLog(new Uri("https://user:pw@node7.secret-mirror.example.test:8443/x"));
        masked.Should().Be("https://***@***.test:8443");
        masked.Should().NotContain("secret").And.NotContain("node7").And.NotContain("pw");
        ColituUpdateService.MaskForLog(new Uri("http://203.0.113.9/x")).Should().Be("http://***:80");
    }
}
