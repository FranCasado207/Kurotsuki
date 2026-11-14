using System.Net;
using Kurotsuki.Core.Booru;

namespace Kurotsuki.Core.Tests;

public class ClientTests
{
    private sealed class Stub(Func<HttpRequestMessage, string> reply) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(request)) });
        }
    }

    [Fact]
    public async Task GelbooruReadsTheWrappedPostList()
    {
        var stub = new Stub(_ => """{"@attributes":{"count":1},"post":[{"id":5,"score":2,"width":3,"height":4,"image":"a.png","rating":"general","tags":"x y","preview_url":"https://p/1","file_url":"https://f/1","sample_url":"","source":""}]}""");
        var client = new GelbooruFamilyClient(new HttpClient(stub), "https://gelbooru.com", "key", "7");
        var posts = await client.SearchPostsAsync("cat dog", 2, 100);

        var post = Assert.Single(posts);
        Assert.Equal(5, post.Id);
        Assert.Equal("https://f/1", post.FileUrl);
        var url = stub.Requests[0].RequestUri!.AbsoluteUri;
        Assert.Contains("pid=2", url);
        Assert.Contains("limit=100", url);
        Assert.Contains("tags=cat%20dog", url);
        Assert.Contains("api_key=key&user_id=7", url);
    }

    [Fact]
    public async Task DanbooruPagesStartAtOneAndMapRatings()
    {
        var stub = new Stub(_ => """[{"id":1,"score":9,"md5":"m","file_ext":"jpg","file_url":"https://f","large_file_url":"https://l","preview_file_url":"https://p","image_width":1,"image_height":2,"rating":"g","source":"s","tag_string":"a b","tag_string_artist":"a","tag_string_general":"b"}]""");
        var client = new DanbooruClient(new HttpClient(stub), "https://danbooru.donmai.us", "me", "k");
        var post = Assert.Single(await client.SearchPostsAsync("a", 0, 100));

        Assert.Contains("page=1", stub.Requests[0].RequestUri!.Query);
        Assert.Equal("general", post.Rating);
        Assert.Equal("m.jpg", post.Image);
        Assert.Equal(TagCategory.Artist, post.CategoryOf("a"));
    }

    [Fact]
    public async Task E621SendsBasicAuthAndReadsTagCategories()
    {
        var stub = new Stub(_ => """{"posts":[{"id":3,"file":{"width":1,"height":2,"md5":"m","ext":"png","url":"https://f"},"preview":{"url":null},"sample":{"url":"https://s"},"score":{"total":4},"rating":"e","sources":["https://src"],"tags":{"artist":["art"],"general":["g1","g2"]},"pools":[8]}]}""");
        var client = new E621Client(new HttpClient(stub), "https://e621.net", "me", "key");
        var post = Assert.Single(await client.SearchPostsAsync(null, 0, 10));

        Assert.StartsWith("Basic ", stub.Requests[0].Headers.GetValues("Authorization").Single());
        Assert.Equal("explicit", post.Rating);
        Assert.Equal("art g1 g2", post.Tags);
        Assert.Equal(TagCategory.Artist, post.CategoryOf("art"));
        Assert.Equal([8L], post.PoolIds);
        Assert.Null(post.PreviewUrl);
        Assert.DoesNotContain("tags=", stub.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task HydrusSendsKeyAsHeaderNotInUrls()
    {
        var stub = new Stub(r => r.RequestUri!.AbsolutePath.EndsWith("search_files")
            ? """{"file_ids":[10,11]}"""
            : """{"services":{"svc":{"type":5}},"metadata":[{"file_id":10,"hash":"h","ext":".png","width":1,"height":2,"tags":{"svc":{"0":{"display_tags":["creator:bob","plain"]}}}}]}""");
        var client = new HydrusClient(new HttpClient(stub), "http://127.0.0.1:45869", "SECRET");
        var post = Assert.Single(await client.SearchPostsAsync("plain", 0, 1));

        Assert.All(stub.Requests, r => Assert.Equal("SECRET", r.Headers.GetValues("Hydrus-Client-API-Access-Key").Single()));
        Assert.DoesNotContain("SECRET", post.FileUrl);
        Assert.Equal("h.png", post.Image);
        Assert.Equal(TagCategory.Artist, post.CategoryOf("creator:bob"));
    }

    [Fact]
    public async Task HttpErrorsSurfaceTheStatus()
    {
        var handler = new Stub(_ => "");
        var failing = new HttpClient(new FailingHandler());
        var client = new MoebooruClient(failing, "https://yande.re");
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchPostsAsync("x", 0, 10));
        Assert.Contains("429", ex.Message);
        _ = handler;
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode)429));
    }

    [Theory]
    [InlineData(BooruEngine.DanbooruSoftware, null, "https://d.example/", "https://d.example/posts/4")]
    [InlineData(BooruEngine.GelbooruFamily, null, "https://g.example", "https://g.example/index.php?page=post&s=view&id=4")]
    [InlineData(BooruEngine.MoebooruShimmie, BooruFlavor.Moebooru, "https://m.example", "https://m.example/post/show/4")]
    [InlineData(BooruEngine.MoebooruShimmie, BooruFlavor.Shimmie, "https://s.example", "https://s.example/post/view/4")]
    public void PostPageUrls(BooruEngine engine, BooruFlavor? flavor, string baseUrl, string expected) =>
        Assert.Equal(expected, BooruHttp.PageUrl(engine, flavor, baseUrl, 4));
}

public class ErrorHintTests
{
    private sealed class Status(System.Net.HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code));
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.Unauthorized, "401", true)]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable, "503", true)]
    [InlineData(System.Net.HttpStatusCode.NotFound, "404", false)]
    public async Task AccessProblemsSuggestAddingCredentials(System.Net.HttpStatusCode code, string number, bool hint)
    {
        var client = new GelbooruFamilyClient(new HttpClient(new Status(code)), "https://gelbooru.com", null, null);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchPostsAsync("x", 0, 1));
        Assert.Contains(number, ex.Message);
        Assert.Equal(hint, ex.Message.Contains("API key"));
    }
}
