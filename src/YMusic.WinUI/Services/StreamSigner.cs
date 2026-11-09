#nullable enable

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace YMusic.WinUI.Services;

public static class StreamSigner
{
    private const string SecretSalt = "XDXFnebAaaWaytaSession";

    public sealed record StreamInfo(string Host, string Path, string Timestamp, string Sign);

    /// <summary>
    /// Parses the XML payload returned from Yandex Music downloadInfoUrl.
    /// Expected format:
    /// &lt;download-info&gt;
    ///   &lt;host&gt;...&lt;/host&gt;
    ///   &lt;path&gt;...&lt;/path&gt;
    ///   &lt;ts&gt;...&lt;/ts&gt;
    ///   &lt;s&gt;...&lt;/s&gt;
    /// &lt;/download-info&gt;
    /// </summary>
    public static StreamInfo ParseDownloadInfoXml(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            throw new ArgumentException("XML content cannot be null or empty.", nameof(xmlContent));
        }

        var doc = XDocument.Parse(xmlContent);
        var root = doc.Root;
        if (root == null)
        {
            throw new InvalidOperationException("Invalid XML document: missing root element.");
        }

        string host = root.Element("host")?.Value ?? string.Empty;
        string path = root.Element("path")?.Value ?? string.Empty;
        string ts = root.Element("ts")?.Value ?? string.Empty;
        string s = root.Element("s")?.Value ?? string.Empty;

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(s))
        {
            throw new InvalidOperationException("Failed to extract required streaming parameters from XML.");
        }

        return new StreamInfo(host, path, ts, s);
    }

    /// <summary>
    /// Computes the signed streaming MP3 URL according to Yandex Music specifications:
    /// Target = Salt + path.Substring(1) + s
    /// Hash = MD5(UTF8(Target)) in lowercase hex
    /// Direct URL = https://{host}/get-mp3/{hash}/{ts}{path}
    /// </summary>
    public static string BuildDirectUrl(string host, string path, string ts, string s)
    {
        string trimmedPath = path.StartsWith('/') ? path.Substring(1) : path;
        string targetString = SecretSalt + trimmedPath + s;

        byte[] inputBytes = Encoding.UTF8.GetBytes(targetString);
        byte[] hashBytes = MD5.HashData(inputBytes);

        var sb = new StringBuilder(hashBytes.Length * 2);
        foreach (byte b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }
        string hash = sb.ToString();

        string normalizedPath = path.StartsWith('/') ? path : "/" + path;
        return $"https://{host}/get-mp3/{hash}/{ts}{normalizedPath}";
    }

    /// <summary>
    /// Convenience method to parse XML and generate direct URL in one step.
    /// </summary>
    public static string SignFromXml(string xmlContent)
    {
        var info = ParseDownloadInfoXml(xmlContent);
        return BuildDirectUrl(info.Host, info.Path, info.Timestamp, info.Sign);
    }
}
