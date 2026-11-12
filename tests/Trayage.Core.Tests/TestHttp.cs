using System.Net;
using System.Text;
using NSubstitute;

namespace Trayage.Core.Tests;

/// <summary>Fake HTTP plumbing for provider tests: a routed handler behind an <see cref="IHttpClientFactory"/>.</summary>
internal static class TestHttp
{
    public static IHttpClientFactory Factory(Func<HttpRequestMessage, HttpResponseMessage> route) =>
        Factory(req => Task.FromResult(route(req)));

    public static IHttpClientFactory Factory(Func<HttpRequestMessage, Task<HttpResponseMessage>> route)
    {
        var client = new HttpClient(new RoutingHandler(route));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        return factory;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class RoutingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => route(request);
    }
}
