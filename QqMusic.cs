using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace TagFill;

public sealed record SearchItem(string SongName, List<string> Singers, string AlbumMid, string AlbumName);
public sealed record Release(string AlbumMid, string Album, string Date);

/// <summary>QQ 音乐查库：搜索 → 专辑详情 → 选发行最早的原版专辑 → 封面。复刻 flac2am.py 规则。</summary>
public static class QqMusic
{
    private const string Ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36";
    private const string Referer = "https://y.qq.com";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static void AddHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation("User-Agent", Ua);
        req.Headers.TryAddWithoutValidation("Referer", Referer);
    }

    public static async Task<List<SearchItem>> Search(string keyword)
    {
        var url = "https://c.y.qq.com/soso/fcgi-bin/client_search_cp?w="
                  + Uri.EscapeDataString(keyword) + "&p=1&n=20&format=json&cr=1";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeaders(req);
        using var resp = await Http.SendAsync(req);
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
        var list = json?["data"]?["song"]?["list"]?.AsArray();
        var result = new List<SearchItem>();
        if (list == null) return result;
        foreach (var it in list)
        {
            var singers = new List<string>();
            var arr = it?["singer"]?.AsArray();
            if (arr != null)
                foreach (var s in arr)
                    singers.Add((string?)s?["name"] ?? "");
            result.Add(new SearchItem(
                (string?)it?["songname"] ?? "",
                singers,
                (string?)it?["albummid"] ?? "",
                (string?)it?["albumname"] ?? ""));
        }
        return result;
    }

    /// <summary>歌名精确 + 歌手匹配，取发行日期最早；无结果返回 null。</summary>
    public static async Task<Release?> FindRelease(string artist, string title)
    {
        var cands = new List<(string mid, string album, string date)>();
        foreach (var s in await Search($"{artist} {title}"))
        {
            if ((s.SongName ?? "").Trim() != title.Trim()) continue;
            if (!s.Singers.Any(x => x == artist)) continue;
            if (string.IsNullOrWhiteSpace(s.AlbumMid)) continue;
            var (albumName, publishDate) = await AlbumDetail(s.AlbumMid);
            var album = string.IsNullOrWhiteSpace(albumName) ? s.AlbumName : albumName.Trim();
            cands.Add((s.AlbumMid, album, (publishDate ?? "").Trim()));
            await Task.Delay(250);
        }
        if (cands.Count == 0) return null;
        var best = cands.OrderBy(c => string.IsNullOrEmpty(c.date) ? "9999" : c.date).First();
        return new Release(best.mid, best.album, best.date);
    }

    private static async Task<(string Album, string Date)> AlbumDetail(string albumMid)
    {
        var body = new JsonObject
        {
            ["comm"] = new JsonObject { ["ct"] = 24, ["cv"] = 0 },
            ["req"] = new JsonObject
            {
                ["module"] = "music.musichallAlbum.AlbumInfoServer",
                ["method"] = "GetAlbumDetail",
                ["param"] = new JsonObject { ["albumMid"] = albumMid },
            },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://u.y.qq.com/cgi-bin/musicu.fcg")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        AddHeaders(req);
        using var resp = await Http.SendAsync(req);
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
        var basic = json?["req"]?["data"]?["basicInfo"];
        return ((string?)basic?["albumName"] ?? "", (string?)basic?["publishDate"] ?? "");
    }

    /// <summary>优先 1000x1000，退 500x500；返回原始字节（QQ 封面本就是 JPEG）。</summary>
    public static async Task<(byte[]? Data, bool IsPng)> FetchCover(string albumMid)
    {
        foreach (var url in new[]
        {
            $"https://y.qq.com/music/photo_new/T002R1000x1000M000{albumMid}.jpg",
            $"https://y.qq.com/music/photo_new/T002R500x500M000{albumMid}.jpg",
        })
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                AddHeaders(req);
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) continue;
                var data = await resp.Content.ReadAsByteArrayAsync();
                if (data.Length == 0) continue;
                bool isPng = data.Length > 8
                             && data[0] == 0x89 && data[1] == 0x50
                             && data[2] == 0x4E && data[3] == 0x47;
                return (data, isPng);
            }
            catch { /* 尝试下一档 */ }
        }
        return (null, false);
    }
}
