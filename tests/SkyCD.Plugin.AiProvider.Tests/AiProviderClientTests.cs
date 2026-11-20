using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AiProvider.Tests;

public sealed class AiProviderClientTests
{
    private static AiProviderSettings Settings() => new()
    {
        Enabled = true,
        ApiKey = "secret",
        Model = "gpt-4o-mini"
    };

    private static DescriptionContext Context(bool withImage = false)
    {
        var parts = new List<DescriptionContextPart>
        {
            new("filename", "text/plain", "holiday.jpg")
        };
        if (withImage)
        {
            parts.Add(new DescriptionContextPart("image", "image/png", Data: TestImages.CreatePng(800, 600)));
        }

        return new DescriptionContext("entry-1", "source-1", "context-1", new DescriptionContextOptions(), parts);
    }

    [Fact]
    public async Task GenerateAsync_SendsAuthorizedChatRequestAndReturnsDescription()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"choices":[{"message":{"content":"  A blue sunset over the sea.  "}}]}
            """));
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.GenerateAsync(Settings(), Context(), CancellationToken.None);

        Assert.Equal("A blue sunset over the sea.", result.Text);
        Assert.Equal("OpenAI", result.Agent);
        Assert.Equal("gpt-4o-mini", result.Model);
        Assert.Equal("cloud", result.ProcessingDestination);
        Assert.Equal("English", result.Language);
        Assert.False(string.IsNullOrWhiteSpace(result.PromptVersion));
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer secret", handler.LastRequest.Headers.Authorization!.ToString());
        Assert.Contains("\"model\":\"gpt-4o-mini\"", handler.LastBody);
        Assert.Contains("holiday.jpg", handler.LastBody);
        Assert.Contains("UNTRUSTED FILE DATA", handler.LastBody);
    }

    [Fact]
    public async Task GenerateAsync_WhenResizeEnabled_SendsDownscaledPngImage()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"choices":[{"message":{"content":"ok"}}]}
            """));
        var client = new AiProviderClient(new HttpClient(handler));

        await client.GenerateAsync(Settings(), Context(withImage: true), CancellationToken.None);

        Assert.Contains("data:image/png;base64,", handler.LastBody);
    }

    [Fact]
    public async Task GenerateAsync_WhenResizeDisabled_KeepsOriginalContentType()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"choices":[{"message":{"content":"ok"}}]}
            """));
        var client = new AiProviderClient(new HttpClient(handler));
        var settings = Settings() with { ResizeLargeImages = false };

        await client.GenerateAsync(settings, Context(withImage: true), CancellationToken.None);

        Assert.Contains("data:image/png;base64,", handler.LastBody);
    }

    [Fact]
    public async Task GenerateAsync_WhenProviderFails_MapsErrorAndMessage()
    {
        var handler = new RecordingHandler(_ => Json("""{"error":{"message":"bad key"}}""",
            HttpStatusCode.Unauthorized));
        var client = new AiProviderClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<AiProviderException>(() =>
            client.GenerateAsync(Settings(), Context(), CancellationToken.None));

        Assert.Contains("401", exception.Message);
        Assert.Contains("bad key", exception.Message);
    }

    [Fact]
    public async Task GenerateAsync_WhenContextHasNoPermittedParts_Throws()
    {
        var handler = new RecordingHandler(_ => Json("""{"choices":[]}"""));
        var client = new AiProviderClient(new HttpClient(handler));
        var context = new DescriptionContext("entry-1", "source-1", "context-1",
            new DescriptionContextOptions(), Array.Empty<DescriptionContextPart>());

        await Assert.ThrowsAsync<AiProviderException>(() =>
            client.GenerateAsync(Settings(), context, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task GenerateAsync_WhenDescriptionEmpty_Throws()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"choices":[{"message":{"content":"   "}}]}
            """));
        var client = new AiProviderClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<AiProviderException>(() =>
            client.GenerateAsync(Settings(), Context(), CancellationToken.None));

        Assert.Contains("empty description", exception.Message);
    }

    [Fact]
    public async Task ListModelsAsync_ParsesDistinctSortedIds()
    {
        var handler = new RecordingHandler(_ => Json("""
            {"data":[{"id":"b"},{"id":"a"},{"id":"b"},{"object":"model"},{"id":42}]}
            """));
        var client = new AiProviderClient(new HttpClient(handler));

        var models = await client.ListModelsAsync(Settings(), CancellationToken.None);

        Assert.Equal(new[] { "a", "b" }, models);
        Assert.Equal("https://api.openai.com/v1/models", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task CheckConnectionAsync_WhenReachable_ReportsModels()
    {
        var handler = new RecordingHandler(_ => Json("""{"data":[{"id":"m1"}]}"""));
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.CheckConnectionAsync(Settings(), CancellationToken.None);

        Assert.True(result.IsConnected);
        Assert.Equal(new[] { "m1" }, result.Models);
        Assert.Contains("OpenAI", result.Message);
    }

    [Fact]
    public async Task CheckConnectionAsync_WhenUnreachable_ReportsFailure()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("boom")
        });
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.CheckConnectionAsync(Settings(), CancellationToken.None);

        Assert.False(result.IsConnected);
        Assert.Contains("500", result.Message);
        Assert.Empty(result.Models);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => this.respond = respond;

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return respond(request);
        }
    }
}
