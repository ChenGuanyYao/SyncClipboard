using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SyncClipboard.Core.Commons;
using SyncClipboard.Core.Interfaces;
using SyncClipboard.Core.Models.UserConfigs;

namespace SyncClipboard.Core.UserServices.ServerService;

/// <summary>
/// 局域网自动发现响应器：复用 SyncClipboard 服务器端口监听 UDP，避免移动端扫描大量 IP/端口。
/// 这里只暴露服务地址和认证需求，不返回账号、密码等敏感配置。
/// </summary>
internal sealed class ServerDiscoveryResponder
{
    public const string ProbeMessage = "YUNJIAN_DISCOVER_V1";
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.255.76.67");
    private const string DeviceIdFileName = "YunJianDiscoveryDeviceId.txt";

    private readonly ServerConfig _serverConfig;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cancellationTokenSource;
    private UdpClient? _udpClient;

    public ServerDiscoveryResponder(ServerConfig serverConfig, ILogger logger)
    {
        _serverConfig = serverConfig;
        _logger = logger;
    }

    public void Start()
    {
        Stop();
        if (!_serverConfig.EnableLocalDiscovery || _serverConfig.EnableCustomConfigurationFile)
        {
            // 自定义 Kestrel 配置可能使用任意监听地址和协议，不能对外宣告固定发现端点。
            return;
        }

        var port = _serverConfig.EffectivePort;

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _udpClient = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true,
            };
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            var joinedAddresses = JoinMulticastGroups(_udpClient);

            _ = Task.Run(() => ReceiveLoopAsync(_cancellationTokenSource.Token));
            _ = _logger.WriteAsync(
                ServerService.LOG_TAG,
                $"Local discovery responder started on UDP port {port}, multicast {MulticastAddress}, interfaces: {string.Join(", ", joinedAddresses)}.");
        }
        catch (Exception ex)
        {
            // 发现服务失败不应影响原有 HTTP 同步服务，最多让手机端回退到手动填写地址。
            _ = _logger.WriteAsync(ServerService.LOG_TAG, $"Failed to start local discovery responder: {ex.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
        }
        catch
        {
            // ignore
        }
        _udpClient?.Dispose();
        _udpClient = null;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var udpClient = _udpClient;
        if (udpClient == null)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync(cancellationToken);
                var message = Encoding.UTF8.GetString(result.Buffer).Trim();
                if (!IsProbeMessage(message))
                {
                    continue;
                }

                var response = BuildResponse();
                var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response));
                await udpClient.SendAsync(payload, payload.Length, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                await _logger.WriteAsync(ServerService.LOG_TAG, $"Local discovery responder error: {ex.Message}");
            }
        }
    }

    private string[] JoinMulticastGroups(UdpClient udpClient)
    {
        var joinedAddresses = new List<string>();
        foreach (var address in GetActiveIPv4AddressValues())
        {
            try
            {
                // macOS 在存在 VPN/多网卡时，默认组播接口可能不是 Wi-Fi。
                // 逐个网卡 join 可以保证手机从当前局域网发出的发现包能被桌面端收到。
                udpClient.JoinMulticastGroup(MulticastAddress, address);
                joinedAddresses.Add(address.ToString());
            }
            catch (Exception ex)
            {
                _ = _logger.WriteAsync(ServerService.LOG_TAG, $"Join local discovery multicast group skipped on {address}: {ex.Message}");
            }
        }

        if (joinedAddresses.Count > 0)
        {
            return joinedAddresses.ToArray();
        }

        // 没拿到可用网卡时保留系统默认行为，避免发现服务因为枚举异常完全不可用。
        udpClient.JoinMulticastGroup(MulticastAddress);
        return ["default"];
    }

    private static bool IsProbeMessage(string message)
    {
        return string.Equals(message, ProbeMessage, StringComparison.Ordinal)
            || string.Equals(message, "YUNJIAN_DISCOVER", StringComparison.Ordinal);
    }

    private object BuildResponse()
    {
        var scheme = _serverConfig.EnableHttps ? "https" : "http";
        var addresses = GetActiveIPv4Addresses();
        var deviceId = GetOrCreateDeviceId();
        var port = _serverConfig.EffectivePort;
        return new
        {
            service = "SyncClipboard",
            protocol = "syncclipboard-local",
            version = 1,
            deviceId,
            deviceName = Environment.MachineName,
            name = Environment.MachineName,
            scheme,
            port,
            path = "/SyncClipboard.json",
            authRequired = !string.IsNullOrEmpty(_serverConfig.UserName) || !string.IsNullOrEmpty(_serverConfig.Password),
            usernameHint = string.IsNullOrWhiteSpace(_serverConfig.UserName) ? null : _serverConfig.UserName,
            addresses,
            endpoints = addresses.Select(address => $"{scheme}://{address}:{port}").ToArray(),
        };
    }

    private static string GetOrCreateDeviceId()
    {
        var path = Env.FullPath(DeviceIdFileName);
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(existing))
                {
                    return existing;
                }
            }

            // deviceId 只用于局域网内稳定识别桌面端，不能每次启动变化，否则手机端无法自动回连上次选择的设备。
            var deviceId = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, deviceId);
            return deviceId;
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    private static string[] GetActiveIPv4Addresses()
    {
        return GetActiveIPv4AddressValues()
            .Select(address => address.ToString())
            .Distinct()
            .ToArray();
    }

    private static IPAddress[] GetActiveIPv4AddressValues()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
            .Where(networkInterface => networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address)
            .Where(address => !IPAddress.IsLoopback(address))
            .Distinct()
            .ToArray();
    }
}
