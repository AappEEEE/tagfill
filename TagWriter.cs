using System.Diagnostics;

namespace TagFill;

/// <summary>用 ffmpeg `-c copy` 写标签（不重编码，位级无损），封面缺失时附加、已有时保留。</summary>
public static class TagWriter
{
    public static string? LocateFfmpeg()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';'))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var p = Path.Combine(dir.Trim(), "ffmpeg.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static async Task Write(string inputPath, string outputPath,
        string title, string artist, string album, string albumArtist, string date, string track,
        byte[]? cover, bool coverIsPng)
    {
        var ffmpeg = LocateFfmpeg()
            ?? throw new Exception("找不到 ffmpeg.exe（请与程序放在同一目录，或加入 PATH）");

        var isMp3 = Path.GetExtension(inputPath).Equals(".mp3", StringComparison.OrdinalIgnoreCase);

        string? coverFile = null;
        if (cover != null && cover.Length > 0)
        {
            // 封面临时文件写到音频所在目录（不写 %TEMP%，避免受限环境写不了），用完即删。
            var dir = Path.GetDirectoryName(inputPath) ?? "";
            coverFile = Path.Combine(dir, "." + Guid.NewGuid().ToString("N") + (coverIsPng ? ".png" : ".jpg"));
            File.WriteAllBytes(coverFile, cover);
        }

        try
        {
            var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error", "-i", inputPath };
            if (coverFile != null) { args.Add("-i"); args.Add(coverFile); }

            if (coverFile != null)
            {
                // 源文件本无封面：音频 + 新封面
                args.Add("-map"); args.Add("0:a");
                args.Add("-map"); args.Add("1");
                args.Add("-disposition:v"); args.Add("attached_pic");
            }
            else
            {
                // 源文件已有封面：映射全部流（保留既有 attached_pic）
                args.Add("-map"); args.Add("0");
            }

            args.Add("-c"); args.Add("copy");
            if (isMp3) { args.Add("-id3v2_version"); args.Add("3"); }

            AddMeta(args, "title", title);
            AddMeta(args, "artist", artist);
            AddMeta(args, "album_artist", albumArtist);
            AddMeta(args, "album", album);
            AddMeta(args, "date", date);
            AddMeta(args, "track", track);

            args.Add(outputPath);

            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi) ?? throw new Exception("无法启动 ffmpeg");
            var errTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            var err = await errTask;

            if (p.ExitCode != 0 || !File.Exists(outputPath))
                throw new Exception("写标签失败：" + (err.Length > 200 ? err[..200] : err));
        }
        finally
        {
            if (coverFile != null)
            {
                try { File.Delete(coverFile); } catch { /* 忽略 */ }
            }
        }
    }

    private static void AddMeta(List<string> args, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        args.Add("-metadata");
        args.Add($"{key}={value}");
    }
}
