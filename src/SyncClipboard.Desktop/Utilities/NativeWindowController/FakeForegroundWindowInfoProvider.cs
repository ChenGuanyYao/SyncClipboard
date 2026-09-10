using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models;

namespace SyncClipboard.Desktop.Utilities.NativeWindowController;

/// <summary>
/// 平台缺少原生前台窗口 API 时的降级实现。
/// 用于保证通用桌面包可启动；对应的前台应用过滤能力会自然失效。
/// </summary>
internal sealed class FakeForegroundWindowInfoProvider : INativeWindowController
{
    public WindowDetail? GetForegroundWindowDetail() => null;

    public WindowDetail? GetWindowDetail(NativeWindowInfo window) => null;

    public WindowInfo? GetForegroundWindowInfo() => null;

    public bool TryActivateWindow(NativeWindowInfo window) => false;
}
