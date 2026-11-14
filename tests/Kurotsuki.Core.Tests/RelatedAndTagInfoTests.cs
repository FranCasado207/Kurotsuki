using System.Net;
using Kurotsuki.Core.Booru;

namespace Kurotsuki.Core.Tests;

public class RelatedAndTagInfoTests
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
    public async Task ParentAndChildrenAreReadFromEveryEngineThatHasThem()
    {
        var danbooru = new DanbooruClient(new HttpClient(new Stub(_ => """[{"id":1,"parent_id":7,"has_children":false,"tag_string":"a"},{"id":2,"parent_id":null,"has_children":true,"tag_string":"a"},{"id":3,"tag_string":"a"}]""")), "https://d", null, null);
        var posts = await danbooru.SearchPostsAsync(null, 0, 10);
        Assert.Equal("parent:7", posts[0].RelatedQuery);
        Assert.Equal("parent:2", posts[1].RelatedQuery);
        Assert.Null(posts[2].RelatedQuery);

        var e621 = new E621Client(new HttpClient(new Stub(_ => """{"posts":[{"id":9,"relationships":{"parent_id":4,"has_children":false},"file":{"md5":"m","ext":"png"},"tags":{}}]}""")), "https://e", null, null);
        Assert.Equal("parent:4", (await e621.SearchPostsAsync(null, 0, 1))[0].RelatedQuery);

        var gel = new GelbooruFamilyClient(new HttpClient(new Stub(_ => """{"post":[{"id":5,"parent_id":0,"has_children":"true"}]}""")), "https://g", null, null);
        Assert.Equal("parent:5", (await gel.SearchPostsAsync(null, 0, 1))[0].RelatedQuery);

        // The parent link is live data only: it must not leak into the JSON the Java app also reads.
        var json = System.Text.Json.JsonSerializer.Serialize(posts[0], Kurotsuki.Core.Config.ConfigManager.Json);
        Assert.DoesNotContain("arentId", json);
        Assert.DoesNotContain("hasChildren", json);
    }

    [Fact]
    public async Task DanbooruTagInfoCollectsWikiAliasesAndImplications()
    {
        var stub = new Stub(r =>
        {
            var url = r.RequestUri!.AbsoluteUri;
            return url.Contains("wiki_pages") ? """[{"title":"cat_ears","body":"Ears of a [[cat]] on a [[character|person]].\r\n[b]Bold[/b]"}]"""
                : url.Contains("tag_aliases") && url.Contains("antecedent_name") ? """[{"antecedent_name":"cat_ears","consequent_name":"animal_ears"}]"""
                : url.Contains("tag_aliases") ? """[{"antecedent_name":"neko_mimi","consequent_name":"cat_ears"}]"""
                : url.Contains("tag_implications") && url.Contains("antecedent_name") ? """[{"antecedent_name":"cat_ears","consequent_name":"animal_ear_fluff"}]"""
                : """[{"antecedent_name":"nekomata","consequent_name":"cat_ears"}]""";
        });
        var client = new DanbooruClient(new HttpClient(stub), "https://danbooru.donmai.us", "me", "k");
        var info = await client.TagInfoAsync("cat_ears");

        Assert.NotNull(info);
        Assert.Equal(["animal_ears"], info.AliasOf);
        Assert.Equal(["neko_mimi"], info.Aliases);
        Assert.Equal(["animal_ear_fluff"], info.Implies);
        Assert.Equal(["nekomata"], info.ImpliedBy);
        Assert.Equal("Ears of a cat on a person.\nBold", info.PlainWiki);
        Assert.Equal(5, stub.Requests.Count);
        Assert.All(stub.Requests, r => Assert.Contains("login=me", r.RequestUri!.AbsoluteUri));
    }

    [Fact]
    public async Task TagInfoIsNullWhenTheBooruKnowsNothing()
    {
        var client = new DanbooruClient(new HttpClient(new Stub(_ => "[]")), "https://danbooru.donmai.us", null, null);
        Assert.Null(await client.TagInfoAsync("nothing"));
        Assert.Null(await ((IBooruClient)new MoebooruClient(new HttpClient(new Stub(_ => "[]")), "https://m")).TagInfoAsync("x"));
    }

    [Fact]
    public void ReverseSearchLinksCarryTheEncodedImageAddress()
    {
        var post = new Post { PreviewUrl = "https://p/1.jpg", FileUrl = "ftp://nope", SampleUrl = "" };
        Assert.Equal("https://p/1.jpg", ReverseImageSearch.ImageUrl(post));
        Assert.Null(ReverseImageSearch.ImageUrl(new Post { FileUrl = "javascript:alert(1)" }));
        var url = ReverseImageSearch.Engines[0].Url("https://a/b c.png?x=1&y=2");
        Assert.Equal("https://saucenao.com/search.php?url=https%3A%2F%2Fa%2Fb%20c.png%3Fx%3D1%26y%3D2", url);
        Assert.All(ReverseImageSearch.Engines, e => Assert.StartsWith("https://", e.Url("https://a/b")));
    }
}
