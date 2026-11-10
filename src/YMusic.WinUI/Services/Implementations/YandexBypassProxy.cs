#nullable enable

using System.Net;
using System.Net.Http;

namespace YMusic.WinUI.Services.Implementations;

/// <summary>
/// Custom IWebProxy that automatically bypasses system proxy/VPN
/// for Yandex API and CDN storage domains while preserving system proxy for
/// external services (such as lyrics APIs, GitHub updates, Discord, etc.).
/// Includes smart automatic fallback if direct connection is blocked by local ISP.
/// </summary>
public sealed class YandexBypassProxy : IWebProxy
{
    private static readonly string[] YandexDomainSuffixes =
    {
        ".yandex.ru",
        ".yandex.net",
        ".yandex.com",
        ".yandex.by",
        ".yandex.kz",
        ".yastatic.net",
        ".yandexcloud.net"
    };

    private static readonly string[] YandexExactDomains =
    {
        "yandex.ru",
        "yandex.net",
        "yandex.com",
        "yandex.by",
        "yandex.kz",
        "yastatic.net",
        "yandexcloud.net"
    };

    public static YandexBypassProxy Instance { get; } = new();

    private volatile bool _isEnabled = true;
    private volatile bool _fallbackToProxy = false;
    private int _consecutiveDirectFailures = 0;

    /// <summary>
    /// Gets or sets whether proxy bypass for Yandex domains is enabled.
    /// If false, standard system proxy settings are used for all hosts.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    /// <summary>
    /// When true, direct bypass is temporarily suspended and system proxy is used
    /// (e.g. if the user is in a region where the local ISP blocks direct Yandex connections).
    /// </summary>
    public bool FallbackToProxy
    {
        get => _fallbackToProxy;
        set => _fallbackToProxy = value;
    }

    public ICredentials? Credentials
    {
        get => HttpClient.DefaultProxy.Credentials;
        set => HttpClient.DefaultProxy.Credentials = value;
    }

    public static bool IsYandexHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        foreach (var exact in YandexExactDomains)
        {
            if (string.Equals(host, exact, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var suffix in YandexDomainSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public bool IsBypassed(Uri host)
    {
        if (host == null) return false;

        if (_isEnabled && IsYandexHost(host.Host))
        {
            // If direct connection failed repeatedly, fallback to system proxy
            if (_fallbackToProxy)
            {
                return HttpClient.DefaultProxy.IsBypassed(host);
            }

            // Otherwise, bypass proxy and connect directly via native interface
            return true;
        }

        // For non-Yandex hosts (or if disabled), defer to system proxy
        return HttpClient.DefaultProxy.IsBypassed(host);
    }

    public Uri? GetProxy(Uri destination)
    {
        if (destination == null) return null;

        if (IsBypassed(destination))
        {
            return null;
        }

        return HttpClient.DefaultProxy.GetProxy(destination);
    }

    public void ReportDirectSuccess()
    {
        Interlocked.Exchange(ref _consecutiveDirectFailures, 0);
    }

    public void ReportDirectFailure()
    {
        if (!_isEnabled) return;

        if (Interlocked.Increment(ref _consecutiveDirectFailures) >= 2)
        {
            if (!_fallbackToProxy)
            {
                System.Diagnostics.Debug.WriteLine("[YandexBypassProxy] Direct connection to Yandex failed; auto-falling back to system proxy.");
                _fallbackToProxy = true;
            }
        }
    }

    public void ReportProxyFailure()
    {
        if (!_isEnabled) return;

        if (_fallbackToProxy)
        {
            System.Diagnostics.Debug.WriteLine("[YandexBypassProxy] Proxy failed with region restriction; switching back to direct bypass.");
            _fallbackToProxy = false;
            Interlocked.Exchange(ref _consecutiveDirectFailures, 0);
        }
    }

    /// <summary>
    /// Creates a SocketsHttpHandler configured to use YandexBypassProxy.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(TimeSpan? connectTimeout = null, TimeSpan? pooledLifetime = null)
    {
        return new SocketsHttpHandler
        {
            Proxy = Instance,
            UseProxy = true,
            ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = pooledLifetime ?? TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 20,
            EnableMultipleHttp2Connections = true
        };
    }

    /// <summary>
    /// Creates an HttpClient configured with YandexBypassProxy.
    /// </summary>
    public static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient(CreateHandler())
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30)
        };
        return client;
    }
}

