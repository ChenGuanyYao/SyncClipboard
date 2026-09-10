using Microsoft.Extensions.DependencyInjection;
using NativeNotification.Interface;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;
using SyncClipboard.Server.Core;

namespace SyncClipboard.Core.UserServices.ServerService;

public class ServerService : Service
{
    private readonly SemaphoreSlim _lifecycleSemaphore = new(1, 1);
    private Microsoft.AspNetCore.Builder.WebApplication? _app;
    private ServerDiscoveryResponder? discoveryResponder;
    public readonly static string SERVICE_NAME = I18n.Strings.Server;
    public const string LOG_TAG = "INNERSERVER";

    private readonly ConfigManager _configManager;
    private ServerConfig _serverConfig = new();
    private ProgramConfig _programConfig = new();
    private readonly ToggleMenuItem _toggleMenuItem;

    private readonly IServiceProvider _serviceProvider;
    private readonly IContextMenu _contextMenu;
    private readonly ILogger _logger;
    private readonly ITrayIcon _trayIcon;

    private INotificationManager NotificationManager => _serviceProvider.GetRequiredService<INotificationManager>();

    public ServerService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _trayIcon = serviceProvider.GetRequiredService<ITrayIcon>();
        _logger = serviceProvider.GetRequiredService<ILogger>();
        _configManager = serviceProvider.GetRequiredService<ConfigManager>();
        _contextMenu = serviceProvider.GetRequiredService<IContextMenu>();
        _toggleMenuItem = new ToggleMenuItem(
            SERVICE_NAME,
            _serverConfig.SwitchOn,
            (status) =>
            {
                _configManager.SetConfig(_serverConfig with { SwitchOn = status });
            }
        );
    }

    protected override void StartService()
    {
        _configManager.ListenConfig<ServerConfig>(ConfigChanged);
        _configManager.ListenConfig<ProgramConfig>(DiagnoseModeChanged);
        _serverConfig = _configManager.GetConfig<ServerConfig>();
        _programConfig = _configManager.GetConfig<ProgramConfig>();
        _contextMenu.AddMenuItem(_toggleMenuItem, SyncService.ContextMenuGroupName);
        RequestServerRestart();
    }

    private void ConfigChanged(ServerConfig config)
    {
        if (config != _serverConfig)
        {
            _serverConfig = config;
            RequestServerRestart();
        }
    }

    private void DiagnoseModeChanged(ProgramConfig config)
    {
        if (config.DiagnoseMode != _programConfig.DiagnoseMode)
        {
            _programConfig = config;
            RequestServerRestart();
        }
    }

    private void RequestServerRestart()
    {
        // 配置更新可能连续发生；由同一把锁串行化启停，避免旧实例尚未释放端口时启动新实例。
        _ = RestartServerAsync();
    }

    private async Task RestartServerAsync()
    {
        await _lifecycleSemaphore.WaitAsync();
        try
        {
            var serverConfig = _serverConfig;
            var programConfig = _programConfig;
            _toggleMenuItem.Checked = serverConfig.SwitchOn;
            await StopServerCoreAsync();
            if (!serverConfig.SwitchOn)
            {
                return;
            }

            try
            {
                var app = await Web.StartAsync(
                    new ServerPara(
                        serverConfig.EffectivePort,
                        Env.AppDataDirectory,
                        serverConfig.UserName,
                        serverConfig.Password,
                        serverConfig.EnableHttps,
                        serverConfig.CertificatePemPath,
                        serverConfig.CertificatePemKeyPath,
                        serverConfig.EnableCustomConfigurationFile,
                        serverConfig.CustomConfigurationFilePath,
                        programConfig.DiagnoseMode,
                        serverConfig.MaxHistoryCount,
                        serverConfig.HistoryRetentionMinutes,
                        _serviceProvider
                    )
                );

                ServerDiscoveryResponder? responder = null;
                if (serverConfig.EnableLocalDiscovery)
                {
                    responder = new ServerDiscoveryResponder(serverConfig, _logger);
                    responder.Start();
                }

                _app = app;
                discoveryResponder = responder;
                _trayIcon.SetStatusString(SERVICE_NAME, "Running.", false);
            }
            catch (Exception ex)
            {
                await _logger.WriteAsync(LOG_TAG, ex.ToString());
                _trayIcon.SetStatusString(SERVICE_NAME, ex.Message, true);
                NotificationManager.ShowText(I18n.Strings.FailedToStartServer, ex.Message);
            }
        }
        finally
        {
            _lifecycleSemaphore.Release();
        }
    }

    protected override void StopSerivce()
    {
        _ = StopServerAsync();
    }

    private async Task StopServerAsync()
    {
        await _lifecycleSemaphore.WaitAsync();
        try
        {
            await StopServerCoreAsync();
        }
        finally
        {
            _lifecycleSemaphore.Release();
        }
    }

    private async Task StopServerCoreAsync()
    {
        _trayIcon.SetStatusString(SERVICE_NAME, "Stopped.");
        var oldApp = _app;
        var oldDiscoveryResponder = discoveryResponder;
        _app = null;
        discoveryResponder = null;
        oldDiscoveryResponder?.Stop();
        if (oldApp is not null)
        {
            try
            {
                await oldApp.StopAsync();
                await oldApp.DisposeAsync();
            }
            catch (Exception ex)
            {
                // 停止失败也必须释放生命周期锁，让后续配置变更能够重新尝试启动并显示真实错误。
                await _logger.WriteAsync(LOG_TAG, $"Failed to stop embedded server: {ex}");
            }
        }
    }
}
