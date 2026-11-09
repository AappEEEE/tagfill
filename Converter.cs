namespace TagFill;

public sealed record FillResult(bool Ok, string Message, string OutputPath = "",
    string Title = "", string Artist = "", string Album = "", string Date = "");

/// <summary>编排：读标签 → 智能文件名解析 → refuse 判断 → QQ 补齐 → 写标签（in-place）。</summary>
public static class Converter
{
    private static readonly HashSet<string> UnknownArtists =
        new(StringComparer.OrdinalIgnoreCase) { "未知歌手", "unknown", "unknown artist", "" };

    public static async Task<FillResult> Fill(string audioPath)
    {
        var ext = Path.GetExtension(audioPath).ToLowerInvariant();
        if (ext != ".flac" && ext != ".mp3")
            return new(false, $"不支持的文件格式 {ext}（只支持 FLAC / MP3）");

        var base_ = Path.GetFileNameWithoutExtension(audioPath) ?? "";
        var candidates = SmartFilename.Parse(base_);

        AudioTags tags;
        try { tags = TagReader.Read(audioPath); }
        catch (Exception e) { return new(false, "读取标签失败：" + e.Message); }

        // 身份：标签优先，文件名兜底
        var title = (string.IsNullOrWhiteSpace(tags.Title)
            ? (candidates.Count > 0 ? candidates[0].Title : "")
            : tags.Title.Trim()).Trim();
        var artist = (string.IsNullOrWhiteSpace(tags.Artist)
            ? (candidates.Count > 0 ? candidates[0].Artist : "")
            : tags.Artist.Trim()).Trim();

        var album = tags.Album.Trim();
        var albumArtist = tags.AlbumArtist.Trim();
        var date = tags.Date.Trim();
        var track = tags.Track.Trim();
        bool hasCover = tags.HasCover;

        if (string.IsNullOrWhiteSpace(title))
            return new(false, "判断不出歌名（文件名和标签里都没有歌名）");

        byte[]? cover = null;
        bool coverIsPng = false;

        bool needFill = string.IsNullOrEmpty(album) || string.IsNullOrEmpty(date) || !hasCover;

        if (needFill)
        {
            // 智能识别：标签 + 文件名候选逐个用 QQ 反查验证，命中即锁定歌手/歌名
            var id = await FindIdentity(tags.Artist, tags.Title, title, candidates);
            if (id == null)
                return new(false,
                    $"识别不出歌手，QQ 音乐也查不到（文件名建议包含「歌手 和 歌名」，如「歌手 - 歌名」）");

            artist = id.Value.Artist;
            title = id.Value.Title;
            var release = id.Value.Release;

            if (string.IsNullOrEmpty(album)) album = release.Album;
            if (string.IsNullOrEmpty(date)) date = release.Date;
            if (!hasCover)
            {
                (cover, coverIsPng) = await QqMusic.FetchCover(release.AlbumMid);
                if (cover == null)
                    return new(false, $"找到了专辑但拿不到封面：{artist} - {title}");
            }
        }

        // artist 已最终确定（QQ 消歧后），再补 albumArtist
        if (string.IsNullOrWhiteSpace(albumArtist))
            albumArtist = artist;

        // 写标签：临时文件 + 覆盖原文件
        try
        {
            var dir = Path.GetDirectoryName(audioPath) ?? "";
            var tmp = Path.Combine(dir, base_ + ".tagfill.tmp" + ext);
            await TagWriter.Write(audioPath, tmp, title, artist, album, albumArtist, date, track, cover, coverIsPng);
            File.Move(tmp, audioPath, overwrite: true);
        }
        catch (Exception e)
        {
            return new(false, "写入失败：" + e.Message);
        }

        return new(true, "已补齐", audioPath, title, artist, album, date);
    }

    /// <summary>返回第一个被 QQ 验证通过的 (歌手, 歌名, 专辑信息)；都失败返回 null。</summary>
    private static async Task<(string Artist, string Title, Release Release)?> FindIdentity(
        string tagArtist, string tagTitle, string titleFallback, List<(string A, string T)> candidates)
    {
        var tries = new List<(string, string)>();

        // 1. 标签里的歌手 + 歌名
        if (!string.IsNullOrWhiteSpace(tagArtist) && !UnknownArtists.Contains(tagArtist)
            && !string.IsNullOrWhiteSpace(tagTitle))
            tries.Add((tagArtist.Trim(), tagTitle.Trim()));

        // 2. 标签里的歌手 + 文件名里的歌名（标签歌名可能乱码/缺失）
        if (!string.IsNullOrWhiteSpace(tagArtist) && !UnknownArtists.Contains(tagArtist)
            && !string.IsNullOrWhiteSpace(titleFallback))
            tries.Add((tagArtist.Trim(), titleFallback.Trim()));

        // 3. 文件名候选（正向 + 反向）
        foreach (var (a, t) in candidates)
            if (!string.IsNullOrWhiteSpace(a) && !UnknownArtists.Contains(a) && !string.IsNullOrWhiteSpace(t))
                tries.Add((a, t));

        foreach (var (a, t) in tries)
        {
            var r = await QqMusic.FindRelease(a, t);
            if (r != null) return (a, t, r);
        }

        return null;
    }
}
