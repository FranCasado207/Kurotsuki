using System.Net;
using System.Text;

namespace Kurotsuki.App.Tests;

/// <summary>A booru in memory: a Danbooru-style API with posts and one pool, plus tiny PNGs for every media URL.</summary>
public sealed class StubBooru : HttpMessageHandler
{
    public const string Host = "https://stub.example";
    public const int PoolPages = 4;
    public static readonly byte[] Png = Convert.FromBase64String("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");

    public List<string> Requests { get; } = [];

    /// <summary>Extra media by path (e.g. "/media/clip.mp4"); anything else under /media/ is the tiny PNG.</summary>
    public static System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Files { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        lock (Requests)
        {
            Requests.Add(uri.PathAndQuery);
        }
        return Task.FromResult(Handle(uri));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Handle(Uri uri)
    {
        var path = uri.AbsolutePath;
        if (path.StartsWith("/media/"))
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Files.TryGetValue(path, out var file) ? file : Png) };
        }
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (path == "/posts.json")
        {
            var tags = query["tags"] ?? "";
            if (tags.StartsWith("id:"))
            {
                return Json("[" + string.Join(",", tags[3..].Split(',').Select(id => Post(int.Parse(id), "cover"))) + "]");
            }
            if (tags.StartsWith("pool:"))
            {
                return Json("[" + string.Join(",", Enumerable.Range(1, PoolPages).Select(i => Post(100 + i, "page_" + i))) + "]");
            }
            if (int.Parse(query["page"] ?? "1") > 1)
            {
                return Json("[]");
            }
            return Json("[" + string.Join(",", Enumerable.Range(1, 7).Select(i => Post(i, i % 2 == 0 ? "cat_ears solo" : "dog"))) + "]");
        }
        if (path == "/tags.json")
        {
            return Json("""[{"name":"cat_ears"},{"name":"cat_girl"}]""");
        }
        if (path == "/pools.json")
        {
            return Json(Pool());
            }
        if (path == "/pools/1.json")
        {
            return Json(Pool().Trim('[', ']'));
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string Pool() =>
        "[{\"id\":1,\"name\":\"Stub_comic\",\"description\":\"A test pool\",\"category\":\"series\",\"post_count\":" + PoolPages
        + ",\"post_ids\":[" + string.Join(",", Enumerable.Range(1, PoolPages).Select(i => 100 + i))
        + "],\"created_at\":\"2026-01-02T03:04:05.000+00:00\",\"updated_at\":\"2026-02-03T00:00:00.000+00:00\"}]";

    private static string Post(int id, string tags) =>
        $"{{\"id\":{id},\"score\":{id},\"md5\":\"{id:x32}\",\"file_ext\":\"png\",\"file_url\":\"{Host}/media/file{id}.png\","
        + $"\"large_file_url\":\"{Host}/media/file{id}.png\",\"preview_file_url\":\"{Host}/media/thumb{id}.png\","
        + $"\"image_width\":48,\"image_height\":64,\"rating\":\"g\",\"source\":\"https://src.example/{id}\","
        + $"\"tag_string\":\"{tags} artist_x\",\"tag_string_artist\":\"artist_x\",\"tag_string_general\":\"{tags}\"}}";
}
