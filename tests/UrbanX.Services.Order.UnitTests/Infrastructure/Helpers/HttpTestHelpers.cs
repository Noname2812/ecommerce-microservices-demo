namespace UrbanX.Services.Order.UnitTests.Infrastructure.Helpers;

/// <summary>HTTP test doubles shared by infrastructure client unit tests.</summary>
public sealed class TimeoutFaultingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(
            new TaskCanceledException(
                "The request was canceled due to the configured HttpClient.Timeout of 5 seconds elapsing."));
}

public sealed class CallbackHttpMessageHandler(
    Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await callback(request);
        response.RequestMessage = request;
        return response;
    }
}
