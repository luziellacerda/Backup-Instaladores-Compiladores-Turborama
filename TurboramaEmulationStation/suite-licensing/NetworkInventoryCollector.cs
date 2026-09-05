using System.Net.NetworkInformation;
using System.Security.Cryptography;
using TurboBoxManager.Licensing;
using TurboRamaSuite.Network;

namespace TurboRama.EmulationStation.Access;

// Optional diagnostics run outside authorization/heartbeat. A collector failure
// never revokes a valid session, extends its lifetime, or delays game launch.
internal sealed class NetworkInventoryCollector : IDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _worker;
    private long _networkChanges;

    internal NetworkInventoryCollector(SuiteLicenseClient client,SuiteLicensingRuntime runtime,CancellationToken lifetime)
    {
        _stop=CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        NetworkChange.NetworkAddressChanged+=OnChanged;
        _worker=RunAsync(client,runtime,_stop.Token);
    }
    internal static NetworkInventoryCollector? TryStart(SuiteLicenseClient client,SuiteLicensingRuntime runtime,CancellationToken lifetime)
    {
        try{return new(client,runtime,lifetime);}catch{return null;}
    }
    private void OnChanged(object? sender,EventArgs args)=>Interlocked.Increment(ref _networkChanges);

    private async Task RunAsync(SuiteLicenseClient client,SuiteLicensingRuntime runtime,CancellationToken ct)
    {
        string? lastReport=null;long lastEpoch=-1;DateTimeOffset lastAttempt=DateTimeOffset.MinValue;
        try
        {
            while(!ct.IsCancellationRequested)
            {
                var current=runtime.CurrentContext;
                if(current?.IsAuthorized==true&&DateTimeOffset.UtcNow-lastAttempt>=TimeSpan.FromMinutes(1))
                {
                    lastAttempt=DateTimeOffset.UtcNow;
                    try
                    {
                        var signals=Collect();
                        var digest=Convert.ToHexString(SHA256.HashData(NetworkInventoryContract.Canonical(signals)));
                        var identity=current.SessionId+":"+digest;
                        var epoch=Volatile.Read(ref _networkChanges);
                        if(identity!=lastReport||epoch!=lastEpoch)
                        {
                            lastAttempt=DateTimeOffset.UtcNow;
                            using var report=CancellationTokenSource.CreateLinkedTokenSource(ct,current.AuthorizationCancellationToken);
                            report.CancelAfter(TimeSpan.FromSeconds(10));
                            await client.ReportNetworkAsync(current,signals,report.Token).ConfigureAwait(false);
                            lastReport=identity;lastEpoch=epoch;
                        }
                    }
                    catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
                    catch{lastAttempt=DateTimeOffset.UtcNow; /* No identifiers, raw addresses or exception details are logged. */}
                }
                await Task.Delay(TimeSpan.FromSeconds(2),ct).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){}
    }

    internal static NetworkInterfaceSignal[] Collect()=>NetworkInterface.GetAllNetworkInterfaces()
        .Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        .Where(n=>!new[] {"virtual","hyper-v","vpn","loopback","vmware","tap-","wsl"}.Any(v=>n.Description.Contains(v,StringComparison.OrdinalIgnoreCase)))
        .Select(n=>(Bytes:n.GetPhysicalAddress().GetAddressBytes(),Type:n.NetworkInterfaceType))
        .Where(n=>n.Bytes.Length==6&&(n.Bytes[0]&1)==0&&n.Bytes.Any(b=>b!=0))
        .Select(n=>new NetworkInterfaceSignal(string.Join(":",n.Bytes.Select(b=>b.ToString("X2"))),
            n.Type==NetworkInterfaceType.Wireless80211?"WIRELESS":"ETHERNET",(n.Bytes[0]&2)!=0,false))
        .DistinctBy(n=>n.Mac).OrderBy(n=>n.Mac,StringComparer.Ordinal).Take(8).ToArray();

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged-=OnChanged;
        _stop.Cancel();
        // Optional diagnostics cannot hold the helper open after licensing has
        // stopped. The native parent retains its independent hard exit bound.
        try{_worker.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();}
        catch(OperationCanceledException){}
        catch(TimeoutException){}
        if(_worker.IsCompleted)_stop.Dispose();
    }
}
