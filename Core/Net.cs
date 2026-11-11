using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace CoreLauncher.Core;

public static class Net
{
    public static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("CoreLauncher/0.2 (public-demo)");
        return c;
    }

    public static Task<string> GetStringAsync(string url) => Http.GetStringAsync(url);
    public static Task<byte[]> GetBytesAsync(string url) => Http.GetByteArrayAsync(url);
}
