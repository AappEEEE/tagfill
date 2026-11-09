using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TagFill;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain", e.ExceptionObject as Exception);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 某些 WPF 内部实现会把临时文件写到 %TEMP%\WPF；在受限环境（沙箱/企业策略/
        // 重定向 TEMP）里该目录可能不存在或不可写。提前尽力创建，失败不影响启动。
        try
        {
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "WPF"));
        }
        catch
        {
            // 忽略：真正的失败兜底由下面的全局异常处理负责
        }

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // 记录并以「继续运行」处理，避免某个 UI 资源类异常让程序启动即闪退。
        LogCrash("Dispatcher", e.Exception);
        e.Handled = true;
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var log = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.AppendAllText(log,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source}){Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 写日志失败也无所谓
        }
    }
}
