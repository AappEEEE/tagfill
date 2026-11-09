using System.Text.RegularExpressions;

namespace TagFill;

/// <summary>
/// 智能解析文件名：不强求「歌手 - 歌名」，只要文件名里包含这两者即可。
/// 用多种分隔符拆成候选 (artist,title) 对（正向 + 反向各一份），再由 QQ 反查消歧。
/// </summary>
public static class SmartFilename
{
    private static readonly string[] Separators =
    {
        " - ", " – ", " — ", "－", "-", "–", "—", "_", "·", "・", "～", "~", "〜", "／", "/", "\\",
    };

    /// <summary>去掉轨道号前缀和已知格式/音质后缀噪音，返回干净基名（不含扩展名）。</summary>
    public static string Clean(string fileNameWithoutExt)
    {
        var s = (fileNameWithoutExt ?? "").Trim();

        // 去开头轨道号：[01] (01) 01. 01- 01_ 01) 01] "01 - " "01 "（后接非数字）
        s = Regex.Replace(s,
            @"^\s*(?:[\[\(]\s*\d{1,3}\s*[\]\)]|\d{1,3}\s*[\.\-_\)\]]|\d{1,3}\s+-\s*|\d{1,3}\s+(?=\D))",
            "");

        // 去结尾的格式/音质噪音： [FLAC] (mp3) 【无损】 (320k) 等
        s = Regex.Replace(s,
            @"\s*[\[\(（【]\s*(?:flac|mp3|ape|wav|alac|aac|ogg|wma|dsd|m4a|320k|128k|192k|320|hq|hd|hi-?res|lossless|无损|高音质)\s*[\]\)）】]\s*$",
            "", RegexOptions.IgnoreCase);

        return s.Trim();
    }

    /// <summary>
    /// 生成候选 (artist, title) 对：每种分隔符拆一次，正向 + 反向都进列表。
    /// 完全无分隔符时返回单条 ("", 整个基名)（当作歌名、歌手未知）。
    /// </summary>
    public static List<(string Artist, string Title)> Parse(string fileNameWithoutExt)
    {
        var base_ = Clean(fileNameWithoutExt);
        var result = new List<(string, string)>();

        foreach (var sep in Separators)
        {
            var idx = base_.IndexOf(sep, StringComparison.Ordinal);
            if (idx <= 0) continue;
            var left = base_[..idx].Trim();
            var right = base_[(idx + sep.Length)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;
            Add(result, (left, right));   // 歌手 - 歌名
            Add(result, (right, left));   // 歌名 - 歌手（反向）
        }

        if (result.Count == 0)
            Add(result, ("", base_));

        return result;
    }

    private static void Add(List<(string, string)> list, (string, string) item)
    {
        if (list.Any(x => x.Item1 == item.Item1 && x.Item2 == item.Item2)) return;
        list.Add(item);
    }
}
