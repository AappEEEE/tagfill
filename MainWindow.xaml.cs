using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;

namespace TagFill;

public partial class MainWindow : Window
{
    public sealed class FileRow
    {
        public string File { get; set; } = "";
        public string Status { get; set; } = "等待";
        public string Info { get; set; } = "";
    }

    private readonly ObservableCollection<FileRow> _rows = new();

    public MainWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _rows;
    }

    private void PickButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择音频文件",
            Filter = "音频文件 (*.flac;*.mp3)|*.flac;*.mp3|FLAC (*.flac)|*.flac|MP3 (*.mp3)|*.mp3|所有文件 (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FileNames)
        {
            if (!_rows.Any(r => r.File == f))
                _rows.Add(new FileRow { File = f, Status = "等待", Info = "" });
        }
        StartButton.IsEnabled = _rows.Count > 0;
        StatusText.Text = $"已选 {_rows.Count} 个文件";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        StartButton.IsEnabled = false;
        StatusText.Text = "就绪（原文件将被原地补齐，请先备份）";
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0) return;
        PickButton.IsEnabled = ClearButton.IsEnabled = StartButton.IsEnabled = false;
        try
        {
            foreach (var row in _rows)
            {
                if (row.Status == "已补齐") continue;
                row.Status = "处理中…";
                row.Info = "";
                StatusText.Text = "处理：" + Path.GetFileName(row.File);

                var r = await Converter.Fill(row.File);
                row.Status = r.Ok ? "已补齐" : "拒绝/失败";
                row.Info = r.Ok ? $"{r.Artist} · {r.Album} · {r.Date}" : r.Message;
            }
            StatusText.Text = "全部处理完成";
        }
        catch (Exception ex)
        {
            StatusText.Text = "出错：" + ex.Message;
        }
        finally
        {
            PickButton.IsEnabled = ClearButton.IsEnabled = true;
            StartButton.IsEnabled = _rows.Any(r => r.Status != "已补齐");
        }
    }
}
