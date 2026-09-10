using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Models.UserConfigs;
using SyncClipboard.Core.Utilities;

namespace SyncClipboard.Core.ViewModels;

public partial class ServerConfigViewModel : ObservableObject
{
    #region server properties
    [ObservableProperty]
    private bool serverEnable;
    partial void OnServerEnableChanged(bool value) => ServerConfig = ServerConfig with { SwitchOn = value };

    [ObservableProperty]
    private bool enableHttps;
    partial void OnEnableHttpsChanged(bool value) => ServerConfig = ServerConfig with { EnableHttps = value };

    public static readonly IEnumerable<string> CertificatePemFileTypes = [".pem"];
    [ObservableProperty]
    private string certificatePemPath = string.Empty;
    partial void OnCertificatePemPathChanged(string value) => ServerConfig = ServerConfig with { CertificatePemPath = value };

    public static readonly IEnumerable<string> CertificatePemKeyFileTypes = [".pem"];
    [ObservableProperty]
    private string certificatePemKeyPath = string.Empty;
    partial void OnCertificatePemKeyPathChanged(string value) => ServerConfig = ServerConfig with { CertificatePemKeyPath = value };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalDiscoveryEnabled))]
    private bool enableCustomConfigurationFile;
    partial void OnEnableCustomConfigurationFileChanged(bool value) => ServerConfig = ServerConfig with
    {
        EnableCustomConfigurationFile = value,
        // 自定义 Kestrel 配置无法可靠推导监听端点，不能与固定端口的自动发现同时启用。
        EnableLocalDiscovery = value ? false : ServerConfig.EnableLocalDiscovery
    };

    public static readonly IEnumerable<string> CustomConfigurationFileTypes = [".json"];
    [ObservableProperty]
    private string customConfigurationFilePath = string.Empty;
    partial void OnCustomConfigurationFilePathChanged(string value) => ServerConfig = ServerConfig with { CustomConfigurationFilePath = value };

    [ObservableProperty]
    private uint maxHistoryCount;
    partial void OnMaxHistoryCountChanged(uint value) => ServerConfig = ServerConfig with { MaxHistoryCount = value };

    [ObservableProperty]
    private uint historyRetentionMinutes;
    partial void OnHistoryRetentionMinutesChanged(uint value) => ServerConfig = ServerConfig with { HistoryRetentionMinutes = value };

    [RelayCommand]
    private static void OpenCustomConfigDescLink()
    {
        Sys.OpenWithDefaultApp(I18n.Strings.CustomConfigFileLink);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerConfigDescription))]
    private ServerConfig serverConfig = new();
    partial void OnServerConfigChanged(ServerConfig value)
    {
        if (value.EnableLocalDiscovery && value.EnableCustomConfigurationFile)
        {
            // 兼容历史配置：两项同时开启时优先保留自定义服务器配置，关闭发现避免返回错误地址。
            ServerConfig = value with { EnableLocalDiscovery = false };
            return;
        }

        if (value.EnableLocalDiscovery && value.Port != ServerConfig.DefaultPort)
        {
            // 开启局域网自动发现时必须固定端口，避免手机端发现到的地址和实际监听端口不一致。
            ServerConfig = value with { Port = ServerConfig.DefaultPort };
            return;
        }

        ServerEnable = value.SwitchOn;
        EnableLocalDiscovery = value.EnableLocalDiscovery;
        EnableHttps = value.EnableHttps;
        CertificatePemPath = value.CertificatePemPath;
        CertificatePemKeyPath = value.CertificatePemKeyPath;
        EnableCustomConfigurationFile = value.EnableCustomConfigurationFile;
        CustomConfigurationFilePath = value.CustomConfigurationFilePath;
        MaxHistoryCount = value.MaxHistoryCount;
        HistoryRetentionMinutes = value.HistoryRetentionMinutes;
        _configManager.SetConfig(value);

        OnPropertyChanged(nameof(ShowHttpsConfig));
        OnPropertyChanged(nameof(ShowHttpsCertConfig));
        OnPropertyChanged(nameof(IsLocalDiscoveryEnabled));
        OnPropertyChanged(nameof(IsCustomConfigurationFileEnabled));
    }

    #endregion

    #region view properties
    public bool ShowHttpsConfig => !EnableCustomConfigurationFile;
    public bool ShowHttpsCertConfig => EnableHttps && !EnableCustomConfigurationFile;
    public bool IsServerPortEditable => !EnableLocalDiscovery;
    public bool IsLocalDiscoveryEnabled => !EnableCustomConfigurationFile;
    public bool IsCustomConfigurationFileEnabled => !EnableLocalDiscovery;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerConfigDescription))]
    [NotifyPropertyChangedFor(nameof(IsServerPortEditable))]
    [NotifyPropertyChangedFor(nameof(IsCustomConfigurationFileEnabled))]
    private bool enableLocalDiscovery;
    partial void OnEnableLocalDiscoveryChanged(bool value)
    {
        var next = ServerConfig with
        {
            EnableLocalDiscovery = value,
            EnableCustomConfigurationFile = value ? false : ServerConfig.EnableCustomConfigurationFile
        };
        if (value)
        {
            next = next with { Port = ServerConfig.DefaultPort };
        }

        ServerConfig = next;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerConfigDescription))]
    public bool showServerPassword = false;

    public string ServerConfigDescription =>
@$"{I18n.Strings.Port}{new string('\t', int.Parse(I18n.Strings.PortTabRepeat))}: {ServerConfig.EffectivePort}
{I18n.Strings.UserName}{new string('\t', int.Parse(I18n.Strings.UserNameTabRepeat))}: {ServerConfig.UserName}
{I18n.Strings.Password}{new string('\t', int.Parse(I18n.Strings.PasswordTabRepeat))}: {GetPasswordString(ServerConfig.Password, ShowServerPassword)}
{I18n.Strings.LocalNetworkDiscovery}: {(ServerConfig.EnableLocalDiscovery ? I18n.Strings.On : I18n.Strings.Off)}";

    private static string GetPasswordString(string origin, bool? show)
    {
        return show ?? false ? origin : "*********";
    }

    #endregion

    private readonly ConfigManager _configManager;

    public ServerConfigViewModel(ConfigManager configManager)
    {
        _configManager = configManager;
        _configManager.ListenConfig<ServerConfig>(config => ServerConfig = config);
        serverConfig = _configManager.GetConfig<ServerConfig>();
        serverEnable = serverConfig.SwitchOn;
        enableLocalDiscovery = serverConfig.EnableLocalDiscovery;
        enableHttps = serverConfig.EnableHttps;
        certificatePemPath = serverConfig.CertificatePemPath;
        certificatePemKeyPath = serverConfig.CertificatePemKeyPath;
        enableCustomConfigurationFile = serverConfig.EnableCustomConfigurationFile;
        customConfigurationFilePath = serverConfig.CustomConfigurationFilePath;
        maxHistoryCount = serverConfig.MaxHistoryCount;
        historyRetentionMinutes = serverConfig.HistoryRetentionMinutes;
    }

    public string? SetServerConfig(string portString, string username, string password)
    {
        var port = ServerConfig.DefaultPort;
        if (!ServerConfig.EnableLocalDiscovery && !ushort.TryParse(portString, out port))
        {
            return I18n.Strings.PortRangeIs;
        }
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return I18n.Strings.UsernameOrPasswordBlank;
        }

        ServerConfig = ServerConfig with { Password = password, Port = port, UserName = username };

        return null;
    }
}
