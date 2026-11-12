using GraficaCaixa;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;

// Pacote 11 (confiabilidade): depois de queda de luz o serviço precisa subir mesmo sem a rede do celular pronta.
public static class BootTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static WebApiOptions Mobile(string address)=>new(){Browser=new BrowserOptions{Enabled=true,BindAddress=address}};
    public static async Task Run()
    {
        Check(SharedDataServer.BrowserAddressReady(new WebApiOptions()),"celular desligado: nada a esperar");
        Check(SharedDataServer.BrowserAddressReady(Mobile("127.0.0.1")),"endereço local de teste sempre existe");
        Check(!SharedDataServer.BrowserAddressReady(Mobile("203.0.113.77")),"endereço que ainda não existe (rede/Tailscale fora) é detectado");
        var local=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a));
        if(local!=null)Check(SharedDataServer.BrowserAddressReady(Mobile(local.ToString())),"endereço da placa de rede ligada é reconhecido");
        // Partida que falha não deixa porta, identidade ou certificado presos: a segunda tentativa sobe na mesma porta.
        var folder=Path.Combine(Path.GetTempPath(),"erp-boot-"+Guid.NewGuid().ToString("N"));var store=new DataStore(Path.Combine(folder,"Dados"));store.EnsureInitialized();
        store.Update(d=>{d.Prompt12.LoginPassword=LoginSecurity.Create(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18)));return 0;});
        var probe=TcpListener.Create(0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        var blocker=new TcpListener(IPAddress.IPv6Any,port){ExclusiveAddressUse=true};blocker.Server.DualMode=true;blocker.Start();
        try{await using var failed=await SharedDataServer.StartAsync(store,Path.Combine(folder,"Identidade"),port);Check(false,"porta ocupada deveria falhar");}
        catch(IOException){Check(true,"porta ocupada: a partida falha com erro de rede (o serviço tenta de novo)");}
        finally{blocker.Stop();}
        await using(var server=await SharedDataServer.StartAsync(store,Path.Combine(folder,"Identidade"),port))
        {
            using var http=new HttpClient(new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,cert,_,_)=>RemoteDataClient.CertificateMatchesPin(cert,server.Connection.CertificatePin)}){BaseAddress=new Uri($"https://127.0.0.1:{port}/")};
            using var ping=await http.GetAsync("ping");Check(ping.IsSuccessStatusCode,"segunda tentativa sobe na mesma porta e responde");
        }
        Console.WriteLine($"BootTests: {passed} aprovados");
    }
}
