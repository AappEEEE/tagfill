using System.Diagnostics;
using System.Text.Json.Nodes;

namespace TagFill;

public sealed record AudioTags(string Title, string Artist, string Album, string AlbumArtist,
    string Date, string Track, bool HasCover);

/// <summary>用 ffprobe 读标签，统一 FLAC/MP3（避免手写 VORBIS_COMMENT / ID3 解析器）。</summary>
public static class TagReader
{
    public static string? LocateFfprobe()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffprobe.exe");
        if (File.Exists(local)) return local;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var p = Path.Combine(dir.Trim(), "ffprobe.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static AudioTags Read(string path)
    {
        var ffprobe = LocateFfprobe()
            ?? throw new Exception("找不到 ffprobe.exe（请与程序放在同一目录，或加入 PATH）");

        var psi = new ProcessStartInfo(ffprobe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[]
        {
            "-v", "error", "-show_entries", "format_tags", "-show_streams", "-of", "json", path,
        })
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi) ?? throw new Exception("无法启动 ffprobe");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0)
            throw new Exception("ffprobe 读取失败：" + (stderr.Length > 200 ? stderr[..200] : stderr));

        return ParseJson(stdout);
    }

    private static AudioTags ParseJson(string json)
    {
        var root = JsonNode.Parse(json);

        // ffprobe 的 format.tags 键大小写不一致：FLAC(VORBIS_COMMENT) 常是 TITLE/ARTIST/ALBUM/DATE，
        // MP3(ID3) 是小写 title/artist…。用大小写不敏感字典统一读取。
        var tagsObj = root?["format"]?["tags"]?.AsObject();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tagsObj != null)
            foreach (var kv in tagsObj)
                map[kv.Key] = kv.Value?.ToString().Trim() ?? "";

        string G(params string[] keys)
        {
            foreach (var k in keys)
                if (map.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                    return v;
            return "";
        }

        var title = G("title");
        var artist = G("artist");
        var album = G("album");
        var albumArtist = G("album_artist", "albumartist");
        var date = G("date", "year");
        var track = G("track", "tracknumber");

        // 封面：attached_pic 标记的流，或 video 流 codec_name ∈ {mjpeg, png}
        bool hasCover = false;
        var streams = root?["streams"]?.AsArray();
        if (streams != null)
        {
            foreach (var s in streams)
            {
                var disp = s?["disposition"]?["attached_pic"];
                if (disp != null && disp.GetValue<int>() == 1) { hasCover = true; break; }
                var cn = (string?)s?["codec_name"];
                var ct = (string?)s?["codec_type"];
                if (ct == "video" && (cn == "mjpeg" || cn == "png")) { hasCover = true; break; }
            }
        }

        return new AudioTags(title, artist, album, albumArtist, date, track, hasCover);
    }
}
