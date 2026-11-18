using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Track.Core.Pricing;

namespace Track.Core.Tests.Pricing;

public class PriceLoaderTests
{
    private const string CachePath = @"C:\appdata\Track\prices.json";
    private const string Good = """{"gpt-x":{"input_cost_per_token":1,"output_cost_per_token":2}}""";
    private const string Older = """{"gpt-old":{"input_cost_per_token":1,"output_cost_per_token":2}}""";
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly MockFileSystem _fs = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly IPriceDownloader _downloader = Substitute.For<IPriceDownloader>();

    private PriceLoader Loader(IFileSystem? fs = null) => new(fs ?? _fs, CachePath, _downloader, _time);

    private void Cache(string json, TimeSpan age) =>
        _fs.AddFile(CachePath, new MockFileData(json) { LastWriteTime = (Now - age).UtcDateTime });

    [Fact]
    public async Task A_fresh_cache_is_used_without_downloading()
    {
        Cache(Good, TimeSpan.FromHours(23));

        var prices = (await Loader().LoadAsync());

        Assert.Equal((PriceSource.Cache, Now.AddHours(-23)), (prices.Source, prices.AsOfUtc));
        Assert.NotNull(prices.Table.Find("gpt-x"));
        _ = _downloader.DidNotReceiveWithAnyArgs().GetAsync(default);
    }

    [Fact]
    public async Task A_stale_cache_is_replaced_by_a_new_download()
    {
        Cache(Older, TimeSpan.FromHours(25));
        _downloader.GetAsync(Arg.Any<CancellationToken>()).Returns(Good);

        var prices = (await Loader().LoadAsync());

        Assert.Equal((PriceSource.Download, Now), (prices.Source, prices.AsOfUtc));
        Assert.NotNull(prices.Table.Find("gpt-x"));
        Assert.Equal(Good, _fs.File.ReadAllText(CachePath));
        Assert.False(_fs.File.Exists(CachePath + ".tmp"));
    }

    [Fact]
    public async Task Without_a_cache_the_download_is_used_and_saved()
    {
        _downloader.GetAsync(Arg.Any<CancellationToken>()).Returns(Good);

        Assert.Equal(PriceSource.Download, (await Loader().LoadAsync()).Source);
        Assert.Equal(Good, _fs.File.ReadAllText(CachePath));
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task When_offline_a_stale_cache_is_still_used(Type exception)
    {
        Cache(Older, TimeSpan.FromDays(10));
        _downloader.GetAsync(Arg.Any<CancellationToken>()).ThrowsAsync((Exception)Activator.CreateInstance(exception)!);

        var prices = (await Loader().LoadAsync());

        Assert.Equal(PriceSource.Cache, prices.Source);
        Assert.NotNull(prices.Table.Find("gpt-old"));
    }

    [Fact]
    public async Task When_offline_without_a_cache_the_bundled_prices_are_used()
    {
        _downloader.GetAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException());

        var prices = (await Loader().LoadAsync());

        Assert.Equal((PriceSource.Bundled, (DateTimeOffset?)null), (prices.Source, prices.AsOfUtc));
        Assert.Same(PriceTable.Bundled, prices.Table);
    }

    [Fact]
    public async Task A_broken_download_is_ignored_and_not_saved()
    {
        _downloader.GetAsync(Arg.Any<CancellationToken>()).Returns("<html>rate limited</html>");

        Assert.Equal(PriceSource.Bundled, (await Loader().LoadAsync()).Source);
        Assert.False(_fs.File.Exists(CachePath));
    }

    [Fact]
    public async Task A_corrupt_cache_counts_as_no_cache()
    {
        Cache("{broken", TimeSpan.FromHours(1));
        _downloader.GetAsync(Arg.Any<CancellationToken>()).Returns(Good);

        Assert.Equal(PriceSource.Download, (await Loader().LoadAsync()).Source);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task An_unreadable_cache_counts_as_no_cache_and_a_failed_save_is_harmless(Type exception)
    {
        var fs = Substitute.For<IFileSystem>();
        fs.File.Exists(CachePath).Returns(true);
        fs.File.ReadAllText(CachePath).Throws((Exception)Activator.CreateInstance(exception)!);
        fs.Path.GetDirectoryName(CachePath).Returns(@"C:\appdata\Track");
        fs.File.When(f => f.WriteAllText(Arg.Any<string>(), Arg.Any<string>())).Throw((Exception)Activator.CreateInstance(exception)!);
        _downloader.GetAsync(Arg.Any<CancellationToken>()).Returns(Good);

        var prices = (await Loader(fs).LoadAsync());

        Assert.Equal(PriceSource.Download, prices.Source);
    }

    [Fact]
    public async Task Unexpected_errors_are_not_hidden()
    {
        _downloader.GetAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Loader().LoadAsync());

        var fs = Substitute.For<IFileSystem>();
        fs.File.Exists(CachePath).Returns(true);
        fs.File.ReadAllText(CachePath).Throws(new InvalidOperationException());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Loader(fs).LoadAsync());

        _downloader.GetAsync(Arg.Any<CancellationToken>()).Returns(Good);
        var failingSave = Substitute.For<IFileSystem>();
        failingSave.Path.GetDirectoryName(CachePath).Returns(@"C:\appdata\Track");
        failingSave.File.When(f => f.WriteAllText(Arg.Any<string>(), Arg.Any<string>())).Throw(new InvalidOperationException());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Loader(failingSave).LoadAsync());
    }

    [Fact]
    public async Task The_downloader_fetches_the_litellm_price_list()
    {
        var handler = new StubHandler(Good);
        var downloader = new LiteLlmPriceDownloader(new HttpClient(handler));

        Assert.Equal(Good, await downloader.GetAsync(CancellationToken.None));
        Assert.Equal(LiteLlmPriceDownloader.Url, handler.RequestedUrl);
    }

    [Fact]
    public void Default_cache_path_is_in_the_track_folder_of_local_app_data()
    {
        Assert.Equal(@"C:\Users\me\AppData\Local\Track\prices.json", PriceLoader.DefaultCachePath(@"C:\Users\me\AppData\Local"));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public string? RequestedUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
