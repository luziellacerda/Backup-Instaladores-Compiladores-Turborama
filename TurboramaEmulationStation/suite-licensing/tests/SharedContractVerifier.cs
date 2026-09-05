using System.Security;
using TurboBoxManager.Licensing;
using TurboRamaSuite.Network;

namespace TurboBoxManager.CatalogVerifier;

public static partial class SuiteProtocolVerifier
{
    private static async Task VerifySharedContractAsync()
    {
        using var signer=new TestOnlineAssertionSigner();
        var time=new ManualTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
        var authority=TestAuthority(time,TimeSpan.FromHours(1),signer);
        foreach(var fault in new[]{"legacy-challenge","legacy-result","conflict"})
        {
            var identity=new TestMachineIdentity();
            using var client=SuiteLicenseClient.CreateForVerifier(authority,identity,
                new SessionAuthorityHandler(time,signer,120,false,contractFault:fault),time);
            var error=await ThrowsAsync<SuiteApiException>(()=>client.OpenSessionAsync(
                LicenseId,SessionId,false,CancellationToken.None),"Unsafe shared result must not authorize.");
            Equal(fault=="conflict"?"ES_SESSION_CONFLICT":"INVALID_RESPONSE",error.Code,
                "Shared result must distinguish a verified conflict from incompatible authority.");
            Equal(fault=="legacy-challenge"?0:1,identity.SignCalls,
                "The new signed challenge kind must be verified before the machine signs.");
        }
        var request=new NetworkChallengeRequest(1,NetworkInventoryContract.Product,LicenseId,DeviceId,
            SessionId,"EMULATIONSTATION",NetworkInventoryContract.Action,new string('a',64));
        var challenge=new NetworkAssertion(1,NetworkInventoryContract.ChallengeKind,request.ProductId,
            request.LicenseId,request.DeviceId,request.SessionId,request.AppScope,request.Action,
            request.ContextHash,ChallengeId,"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=","ISSUED",1_800_000_000,1_800_000_060);
        var spki=signer.ExportSubjectPublicKeyInfo();
        var envelope=signer.SignRaw(challenge,challenge.Kind,NetworkInventoryContract.ChallengeDomain);
        var parsed=SuiteOnlineLicenseProtocol.ParseNetworkAssertion(envelope,spki,signer.KeyId,
            request,false,null,1_800_000_000);
        Equal(challenge,parsed,"The client must verify the canonical server network assertion.");
        ExpectSecurity(()=>SuiteOnlineLicenseProtocol.ParseNetworkAssertion(envelope,spki,signer.KeyId,
            request with {AppScope="SUITE"},false,null,1_800_000_000),"Network assertions must bind the app.");
        var result=challenge with {Kind=NetworkInventoryContract.ResultKind,Status="ACCEPTED",Nonce="",ExpiresAtUnixSeconds=challenge.ServerTimeUnixSeconds};
        var signedResult=signer.SignRaw(result,result.Kind,NetworkInventoryContract.ResultDomain);
        _=SuiteOnlineLicenseProtocol.ParseNetworkAssertion(signedResult,spki,signer.KeyId,
            request,true,ChallengeId,1_800_000_000);
        ExpectSecurity(()=>SuiteOnlineLicenseProtocol.ParseNetworkAssertion(signedResult,spki,signer.KeyId,
            request,true,new string('9',64),1_800_000_000),"Network results must bind the consumed challenge.");
        Console.WriteLine("SHARED_CLIENT_CONTRACT=OK (header, old authority before signing, stripped result, signed conflict, network assertion scope)");
    }
}
