using System;
using Microsoft.Extensions.DependencyInjection;
using SharpHook;
using SyncClipboard.Core;
using SyncClipboard.Core.Clipboard;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Desktop.ClipboardAva;
using SyncClipboard.Desktop.ClipboardAva.ClipboardReader;
using SyncClipboard.Desktop.ClipboardAva.Fingerprint;
using SyncClipboard.Desktop.Utilities;
using SyncClipboard.Desktop.Utilities.CaretPositionProvider;
using SyncClipboard.Desktop.Utilities.ForegroundWindowInfoProvider;
using SyncClipboard.Desktop.Utilities.MousePositionProvider;
using SyncClipboard.Desktop.Views;
using SyncClipboard.Core.Utilities.Network;

namespace SyncClipboard.Desktop;

public class AppServices
{
    public static void ConfigDesktopCommonService(IServiceCollection services)
    {
        AppCore.ConfigCommonService(services);
        AppCore.ConfigurateViewModels(services);
        AppCore.ConfigurateUserService(services);

        services.AddTransient<IAppConfig, AppConfig>();

        services.AddSingleton<IMainWindowDialog, Services.AvaloniaDialog>();
        services.AddKeyedSingleton<IMainWindowDialog>("HistoryWindow", (sp, key) =>
        {
            var historyWindow = sp.GetRequiredKeyedService<IWindow>("HistoryWindow") as HistoryWindow;
            return new Services.AvaloniaDialog(historyWindow!);
        });
        services.AddSingleton<IContextMenu, TrayIconContextMenu>();
        services.AddSingleton<MultiSourceClipboardReader>();
        services.AddSingleton<IClipboardReader, AvaloniaClipboardReader>();

        // 注册剪贴板指纹提供者
        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IWifiNetworkInfoProvider, LinuxWifiNetworkInfoProvider>();
            services.AddSingleton<IClipboardFingerprintProvider, LinuxClipboardFingerprintProvider>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            // 原生 macOS 入口会在自己的 AppServices 中覆盖这些实现；通用入口打包时没有那层注册，
            // 因此先提供可运行的桌面默认实现，避免缺少窗口、热键和前台检测服务导致启动闪退。
            services.AddSingleton<IClipboardFingerprintProvider, MacOSClipboardFingerprintProvider>();
            services.AddSingleton<ICaretPositionProvider, FakeCaretPositionProvider>();
            services.AddSingleton<IForegroundWindowInfoProvider, FakeForegroundWindowInfoProvider>();
            services.AddSingleton<IMousePositionProvider, FakeMousePositionProvider>();
        }
        else if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IClipboardFingerprintProvider, WindowsClipboardFingerprintProvider>();
        }

        services.AddSingleton<IClipboardFactory, ClipboardFactory>();
        services.AddSingleton<ClipboardListener>();
        services.AddSingleton<IClipboardChangingListener>(sp => sp.GetRequiredService<ClipboardListener>());
        services.AddSingleton<IClipboardMoniter>(sp => sp.GetRequiredService<ClipboardListener>());
        services.AddTransient<IClipboardSetter<TextProfile>, TextClipboardSetter>();
        services.AddTransient<IClipboardSetter<FileProfile>, FileClipboardSetter>();
        services.AddTransient<IClipboardSetter<ImageProfile>, ImageClipboardSetter>();
        services.AddTransient<IClipboardSetter<GroupProfile>, FileClipboardSetter>();

        services.AddSingleton<IGlobalHook>((sp) => new SimpleGlobalHook(true));

        services.AddTransient<IFontManager, FontManager>();
        services.AddTransient<IThreadDispatcher, ThreadDispatcher>();

        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IClipboardReader, XClipReader>();
            services.AddSingleton<IClipboardReader, WlClipboardReader>();
            services.AddSingleton<ICaretPositionProvider, CaretPositionProvider>();
            services.AddSingleton<IForegroundWindowInfoProvider, LinuxForegroundWindowInfoProvider>();
            services.AddSingleton<IMousePositionProvider, MousePositionProvider>();
        }

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IWifiNetworkInfoProvider, WindowsWifiNetworkInfoProvider>();
            services.AddSingleton<ICaretPositionProvider, FakeCaretPositionProvider>();
            services.AddSingleton<IForegroundWindowInfoProvider, WindowsForegroundWindowInfoProvider>();
            services.AddSingleton<IMousePositionProvider, FakeMousePositionProvider>();
        }

        services.AddSingleton<IMainWindow, MainWindow>();
        services.AddSingleton<INativeHotkeyRegistry, SharpHookHotkeyRegistry>();
        services.AddSingleton<IForegroundWindowWatcher, PollingForegroundWindowWatcher>();
        services.AddSingleton<IClipboardOwnerProvider, ClipboardOwnerProvider>();
        services.AddKeyedSingleton<IWindow, HistoryWindow>("HistoryWindow");
    }

    public static ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();

        ConfigDesktopCommonService(services);

        return services;
    }
}
