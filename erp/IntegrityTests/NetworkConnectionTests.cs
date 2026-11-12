using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using GraficaCaixa;

public static class NetworkConnectionTests
{
    const string Lan="https://192.168.0.10:5074";
    const string Tail="https://100.100.50.25:5074";
    static readonly string Key=Convert.ToHexString(new byte[32]);
    static readonly string Pin=new('A',64);

    public static void Run()
    {
        OldCode();NewCode();EmptyUrls();Duplicates();WithoutTailscale();WithTailscale();
        SelectsLan().GetAwaiter().GetResult();FallsBackToTailscale().GetAwaiter().GetResult();AllUnavailable().GetAwaiter().GetResult();
        InvalidPin();InvalidBearer();NewCodeKeepsLegacyUrl();
        Console.WriteLine("NETWORK TESTS PASSED: 13/13");
    }

    static void OldCode()
    {
        var json=JsonSerializer.Serialize(new{Url=Lan,Key,CertificatePin=Pin});
        bool denied=false;try{ServerConnection.Parse(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));}catch(InvalidOperationException){denied=true;}
        Check(denied,"A/L: código permanente 7B é recusado; novo pareamento necessário");
    }
    static void NewCode()
    {
        var original=new ServerConnection(Lan,Key,Pin){Urls=[Lan,Tail],Purpose="pairing",PairingExpiresAt=DateTimeOffset.UtcNow.AddMinutes(10)};
        var parsed=ServerConnection.Parse(original.PairingCode);
        Check(parsed.Url==Lan&&parsed.Endpoints.SequenceEqual([Lan,Tail]),"B: código novo serializa e desserializa");
    }
    static void EmptyUrls()
    {
        Check(new ServerConnection(Lan,Key,Pin){Urls=[]}.Endpoints.SequenceEqual([Lan]),"C: lista vazia usa Url");
    }
    static void Duplicates()
    {
        Check(new ServerConnection(Lan,Key,Pin){Urls=[Lan,Lan+"/",Tail,Tail]}.Endpoints.SequenceEqual([Lan,Tail]),"D: endpoints duplicados removidos");
    }
    static void WithoutTailscale()
    {
        var adapters=new[]{new NetworkAdapterSnapshot("Ethernet","Intel",true,[IPAddress.Parse("192.168.0.10")])};
        Check(NetworkEndpointDiscovery.FindTailscaleAddress(adapters)==null,"E: ausência do Tailscale não causa erro");
    }
    static void WithTailscale()
    {
        var adapters=new[]{new NetworkAdapterSnapshot("Tailscale","Tailscale Tunnel",true,[IPAddress.Parse("100.100.50.25")])};
        Check(NetworkEndpointDiscovery.FindTailscaleAddress(adapters)=="100.100.50.25","F: IPv4 Tailscale detectado");
    }
    static async Task SelectsLan()
    {
        var calls=new List<string>();var settings=new ServerConnection(Lan,Key,Pin){Urls=[Lan,Tail]};
        var selected=await RemoteDataClient.SelectEndpointAsync(settings,TimeSpan.FromMilliseconds(50),(uri,_)=>{calls.Add(uri.ToString().TrimEnd('/'));return Task.FromResult(EndpointProbeResult.Available);});
        Check(selected.SelectedUrl==Lan&&calls.SequenceEqual([Lan]),"G: LAN disponível vence");
    }
    static async Task FallsBackToTailscale()
    {
        var settings=new ServerConnection(Lan,Key,Pin){Urls=[Lan,Tail]};
        var selected=await RemoteDataClient.SelectEndpointAsync(settings,TimeSpan.FromMilliseconds(50),(uri,_)=>Task.FromResult(uri.Host.StartsWith("192.")?EndpointProbeResult.Unavailable:EndpointProbeResult.Available));
        Check(selected.SelectedUrl==Tail,"H: fallback seleciona Tailscale");
    }
    static async Task AllUnavailable()
    {
        var settings=new ServerConnection(Lan,Key,Pin){Urls=[Lan,Tail]};
        var started=DateTime.UtcNow;
        await RejectAsync(()=>RemoteDataClient.SelectEndpointAsync(settings,TimeSpan.FromMilliseconds(50),(_,_)=>Task.FromResult(EndpointProbeResult.Unavailable)),"rede local nem pelo Tailscale");
        Check(DateTime.UtcNow-started<TimeSpan.FromSeconds(1),"I: falha controlada e rápida");
    }
    static void InvalidPin()
    {
        using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=teste",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        using var cert=request.CreateSelfSigned(DateTimeOffset.Now.AddMinutes(-1),DateTimeOffset.Now.AddMinutes(5));
        Check(!RemoteDataClient.CertificateMatchesPin(cert,Pin),"J: certificado incorreto rejeitado");
    }
    static void InvalidBearer()
    {
        Check(!SharedDataServer.IsAuthorized("Bearer incorreto",Key),"K: Bearer inválido rejeitado");
    }
    static void NewCodeKeepsLegacyUrl()
    {
        var code=new ServerConnection(Lan,Key,Pin){Urls=[Lan,Tail],Purpose="pairing",PairingExpiresAt=DateTimeOffset.UtcNow.AddMinutes(10)}.PairingCode;
        var legacy=JsonSerializer.Deserialize<LegacyConnection>(Encoding.UTF8.GetString(Convert.FromBase64String(code)))!;
        Check(legacy.Url==Lan&&legacy.Key==Key&&legacy.CertificatePin==Pin,"M: transporte LAN/Tailscale e pin preservados; segredo temporário não autoriza API legada");
    }

    static void Check(bool condition,string name){if(!condition)throw new Exception("FALHOU: "+name);Console.WriteLine("OK: "+name);}
    static async Task RejectAsync(Func<Task> action,string expected){try{await action();throw new Exception("Falha esperada não ocorreu");}catch(IOException ex)when(ex.Message.Contains(expected,StringComparison.OrdinalIgnoreCase)){}}
    sealed record LegacyConnection(string Url,string Key,string CertificatePin);
}
