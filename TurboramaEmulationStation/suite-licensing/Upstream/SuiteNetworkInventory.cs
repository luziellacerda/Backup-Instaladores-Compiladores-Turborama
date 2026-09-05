using System.Security;
using System.Security.Cryptography;
using System.Text;
using TurboRamaSuite.Network;

namespace TurboBoxManager.Licensing;

internal interface ISuiteNetworkIdentity : ISuiteMachineIdentity
{
    string SignNetwork(NetworkMachineProof proof);
}

internal sealed partial class SuiteCngMachineIdentity : ISuiteNetworkIdentity
{
    public string SignNetwork(NetworkMachineProof proof)
    {
        NetworkInventoryContract.Validate(new(proof.SchemaVersion,proof.ProductId,proof.LicenseId,
            proof.DeviceId,proof.SessionId,proof.AppScope,proof.Action,proof.ContextHash));
        if(!NetworkInventoryContract.Hex(proof.ChallengeId))throw new SecurityException("Desafio de rede inválido.");
        lock(_gate)
        {
            using var selected=OpenExistingSelectedKey();
            ValidateKey(selected.Key,selected.Profile,selected.Provider);
            using var rsa=new RSACng(selected.Key);
            var device=DescribeWithOpenKey(rsa,selected.Profile);
            if(device.DeviceId!=proof.DeviceId)throw new SecurityException("Identidade de rede divergente.");
            var message=NetworkInventoryContract.SigningMessage(proof);
            byte[] signature=[];
            try
            {
                signature=rsa.SignData(message,HashAlgorithmName.SHA256,RSASignaturePadding.Pss);
                return Convert.ToBase64String(signature);
            }
            finally{CryptographicOperations.ZeroMemory(message);CryptographicOperations.ZeroMemory(signature);}
        }
    }
}

public static partial class SuiteOnlineLicenseProtocol
{
    internal static NetworkAssertion ParseNetworkAssertion(ReadOnlySpan<byte> bytes,ReadOnlySpan<byte> spki,
        string keyId,NetworkChallengeRequest expected,bool result,string? challengeId,long now)
    {
        var kind=result?NetworkInventoryContract.ResultKind:NetworkInventoryContract.ChallengeKind;
        var assertion=ParseSignedAssertion<NetworkAssertion>(bytes,spki,keyId,kind,
            Encoding.ASCII.GetBytes(result?NetworkInventoryContract.ResultDomain:NetworkInventoryContract.ChallengeDomain),
            CanonicalNetworkAssertion);
        if(assertion.Kind!=kind||assertion.SchemaVersion!=1||assertion.ProductId!=expected.ProductId||
            assertion.LicenseId!=expected.LicenseId||assertion.DeviceId!=expected.DeviceId||
            assertion.SessionId!=expected.SessionId||assertion.AppScope!=expected.AppScope||
            assertion.Action!=expected.Action||assertion.ContextHash!=expected.ContextHash||
            result&&assertion.ChallengeId!=challengeId)
            throw new SecurityException("Resposta de rede fora do contexto.");
        ValidateFreshServerTime(assertion.ServerTimeUnixSeconds,now);
        if(!result)ValidateFreshChallenge(assertion.ServerTimeUnixSeconds,assertion.ExpiresAtUnixSeconds,now);
        return assertion;
    }

    private static byte[] CanonicalNetworkAssertion(NetworkAssertion a)
    {
        NetworkInventoryContract.Validate(new(a.SchemaVersion,a.ProductId,a.LicenseId,a.DeviceId,a.SessionId,a.AppScope,a.Action,a.ContextHash));
        if(!NetworkInventoryContract.Hex(a.ChallengeId))throw new SecurityException("Desafio inválido.");
        if(a.Kind==NetworkInventoryContract.ChallengeKind)
        {
            if(a.Status!="ISSUED")throw new SecurityException("Estado do desafio inválido.");
            try{var nonce=Convert.FromBase64String(a.Nonce);if(nonce.Length!=32||Convert.ToBase64String(nonce)!=a.Nonce)throw new SecurityException("Nonce inválido.");}
            catch(FormatException){throw new SecurityException("Nonce inválido.");}
            ValidateChallengeWindow(a.ServerTimeUnixSeconds,a.ExpiresAtUnixSeconds);
        }
        else if(a.Kind!=NetworkInventoryContract.ResultKind||a.Status!="ACCEPTED"||a.Nonce!=""||a.ExpiresAtUnixSeconds!=a.ServerTimeUnixSeconds)
            throw new SecurityException("Resultado complementar inválido.");
        return NetworkInventoryContract.Canonical(a);
    }
}

internal sealed partial class SuiteLicenseClient
{
    internal async Task ReportNetworkAsync(AuthorizedStoreContext current,NetworkInterfaceSignal[] interfaces,CancellationToken ct)
    {
        // Tests and old identity adapters can omit the optional network capability.
        if(_identity is not ISuiteNetworkIdentity network)return;
        current.ThrowIfUnauthorized();
        var device=UseMachineIdentity(_identity.Describe);
        if(device.DeviceId!=current.DeviceId)throw new SecurityException("Identidade de rede divergente.");
        var context=new NetworkInventoryContext(1,NetworkInventoryContract.Product,current.LicenseId,
            device.DeviceId,current.SessionId,"EMULATIONSTATION",NetworkInventoryContract.Action,
            device.HardwareFingerprint,device.AgentVersion,NowUnixSeconds(),interfaces);
        NetworkInventoryContract.Validate(context,NowUnixSeconds());
        var hash=NetworkInventoryContract.Hash(context);
        var request=new NetworkChallengeRequest(1,context.ProductId,context.LicenseId,context.DeviceId,
            context.SessionId,context.AppScope,context.Action,hash);
        var challenge=await PostAsync(NetworkInventoryContract.ChallengeRoute,request,
            bytes=>SuiteOnlineLicenseProtocol.ParseNetworkAssertion(bytes,_onlineAssertionSpki,
                _onlineAssertionKeyId,request,false,null,NowUnixSeconds()),ct).ConfigureAwait(false);
        RegisterChallenge(new(1,challenge.ChallengeId,challenge.Nonce,challenge.ExpiresAtUnixSeconds));
        current.ThrowIfUnauthorized();
        var signature=UseMachineIdentity(()=>network.SignNetwork(new(1,context.ProductId,context.LicenseId,
            context.DeviceId,context.SessionId,context.AppScope,context.Action,hash,challenge.ChallengeId,
            challenge.Nonce,challenge.ExpiresAtUnixSeconds)));
        _=await PostAsync(NetworkInventoryContract.InventoryRoute,new NetworkInventoryProof(context,challenge.ChallengeId,signature),
            bytes=>SuiteOnlineLicenseProtocol.ParseNetworkAssertion(bytes,_onlineAssertionSpki,
                _onlineAssertionKeyId,request,true,challenge.ChallengeId,NowUnixSeconds()),ct).ConfigureAwait(false);
    }
}
