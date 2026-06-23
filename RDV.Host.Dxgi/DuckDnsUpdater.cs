namespace RDV.Host.Dxgi;

public sealed class DuckDnsUpdater : IDisposable
{
    private readonly string _token;
    private readonly string _subdomain;
    private readonly HttpClient _http = new();
    private System.Threading.Timer? _timer;

    public DuckDnsUpdater(string token, string subdomain)
    {
        _token = token;
        _subdomain = subdomain;
    }

    public void Start()
    {
        _ = UpdateAsync();
        _timer = new System.Threading.Timer(_ => _ = UpdateAsync(), null,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    private async Task UpdateAsync()
    {
        try
        {
            var url = $"https://www.duckdns.org/update?domains={_subdomain}&token={_token}&ip=";
            await _http.GetStringAsync(url);
        }
        catch { }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _http.Dispose();
    }
}
