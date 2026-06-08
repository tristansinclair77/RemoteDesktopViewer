using Mono.Nat;

namespace RDV.Host;

public static class UPnPHelper
{
    public static async Task<bool> TryMapPortAsync(int port, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<bool>();

        void OnDeviceFound(object? sender, DeviceEventArgs e)
        {
            _ = MapAsync(e.Device, port, tcs);
        }

        NatUtility.DeviceFound += OnDeviceFound;
        NatUtility.StartDiscovery();

        // Give routers up to 12 seconds to respond
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try { await Task.Delay(-1, linked.Token); }
        catch (OperationCanceledException) { }

        NatUtility.StopDiscovery();
        NatUtility.DeviceFound -= OnDeviceFound;

        return tcs.Task.IsCompletedSuccessfully && tcs.Task.Result;
    }

    private static async Task MapAsync(INatDevice device, int port, TaskCompletionSource<bool> tcs)
    {
        try
        {
            await device.CreatePortMapAsync(new Mapping(Protocol.Tcp, port, port, 0, "RDV Host"));
            tcs.TrySetResult(true);
        }
        catch
        {
            tcs.TrySetResult(false);
        }
    }
}
