using Avalonia;
using Avalonia.Media;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Desktop.Default;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (StartUpHelper.TryUpdateWindowsStartupTaskFromArguments(args, out var returnCode))
        {
            return returnCode;
        }

        if (AppInstance.EnsureSingleInstance(args) is false)
        {
            return (int)ReturnCode.Success;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            SafeWriteUnhandledExceptionLog(e);
            return (int)ReturnCode.UnhandledException;
        }

        return (int)ReturnCode.Success;
    }

    private static string Font(string name)
    {
        return $"avares://SyncClipboard.Desktop.Default/Assets/Fonts#{name}";
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure(() => new App(AppServices.ConfigureServices()))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = $"{Font("MiSans")}",
            });

    private static void SafeWriteUnhandledExceptionLog(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Env.LogFolder);
            var path = Path.Combine(Env.LogFolder, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss}.dmp");
            File.WriteAllText(path + ".txt", $"UnhandledException {exception.GetType()} {exception.Message} \n{exception.StackTrace}");
        }
        catch
        {
            // 崩溃日志只是辅助信息，写日志失败时不能掩盖真正的启动异常。
        }

        try
        {
            App.Current?.Logger?.Write($"UnhandledException {exception.GetType()} {exception.Message} \n {exception.StackTrace}");
            App.Current?.AppCore?.Stop();
        }
        catch
        {
            // 应用已处于异常退出路径，避免清理阶段继续抛错。
        }
    }
}
