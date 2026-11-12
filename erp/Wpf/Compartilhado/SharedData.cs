using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace GraficaCaixa;

public sealed record ServerConnection(string Url, string Key, string CertificatePin)
{
    public Guid? DeviceId {get;init;}
    public string Purpose {get;init;}="device";
    public DateTimeOffset? PairingExpiresAt {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Urls {get;init;}
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SelectedUrl {get;init;}
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> Endpoints => NormalizeEndpoints(Url,Urls);
    [System.Text.Json.Serialization.JsonIgnore]
    public string PairingCode => Purpose=="pairing"&&PairingExpiresAt>DateTimeOffset.UtcNow?Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this))):throw new InvalidOperationException("Gere um novo código temporário de pareamento. Credenciais de dispositivo não são códigos de conexão.");
    public static ServerConnection Parse(string code)
    {
        try
        {
            var settings=JsonSerializer.Deserialize<ServerConnection>(Encoding.UTF8.GetString(Convert.FromBase64String(code.Trim())))!;
            if(!IsHttpsUrl(settings.Url)||settings.Key is not {Length:64}||settings.CertificatePin.Length!=64||settings.Endpoints.Any(x=>!IsHttpsUrl(x))||settings.Purpose!="pairing"||settings.PairingExpiresAt<=DateTimeOffset.UtcNow||settings.PairingExpiresAt==null)throw new FormatException();
            return settings;
        }
        catch { throw new InvalidOperationException("O código de conexão não é válido. Copie o código completo do computador principal."); }
    }
    public static IReadOnlyList<string> NormalizeEndpoints(string url,IEnumerable<string>? urls)
        => new[]{url}.Concat(urls??[]).Where(IsHttpsUrl).Select(x=>x.TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    static bool IsHttpsUrl(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttps;
}
public sealed record CommitRequest(Guid OperationId,long ExpectedVersion,AppData Data,int ClientOrderDetailsVersion=0);
public sealed record RestoreRequest(string Path);

public sealed record NetworkAdapterSnapshot(string Name,string Description,bool IsUp,IReadOnlyList<IPAddress> Addresses);

public static class NetworkEndpointDiscovery
{
    public static string GetLanAddress()
        => NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up && x.NetworkInterfaceType!=NetworkInterfaceType.Loopback).OrderByDescending(x=>x.GetIPProperties().GatewayAddresses.Any(g=>g.Address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork)).SelectMany(x=>x.GetIPProperties().UnicastAddresses).Select(x=>x.Address).FirstOrDefault(x=>x.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork && !x.ToString().StartsWith("169.254."))?.ToString()??Environment.MachineName;

    public static string? GetTailscaleAddress()
        => FindTailscaleAddress(NetworkInterface.GetAllNetworkInterfaces().Select(x=>new NetworkAdapterSnapshot(x.Name,x.Description,x.OperationalStatus==OperationalStatus.Up,x.GetIPProperties().UnicastAddresses.Select(a=>a.Address).ToArray())));

    public static string? FindTailscaleAddress(IEnumerable<NetworkAdapterSnapshot> adapters)
        => adapters.Where(x=>x.IsUp&&IsTailscaleAdapter(x.Name,x.Description)).SelectMany(x=>x.Addresses).FirstOrDefault(IsTailscaleIpv4)?.ToString();

    public static bool IsTailscaleAdapter(string? name,string? description)
        => (name?.Contains("tailscale",StringComparison.OrdinalIgnoreCase)??false)||(description?.Contains("tailscale",StringComparison.OrdinalIgnoreCase)??false);

    public static bool IsTailscaleIpv4(IPAddress address)
    {
        if(address.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)return false;
        var bytes=address.GetAddressBytes();return bytes[0]==100&&bytes[1]>=64&&bytes[1]<=127;
    }

    public static IReadOnlyList<string> BuildUrls(string lanAddress,string? tailscaleAddress,int port)
        => new[]{lanAddress,tailscaleAddress}.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>$"https://{x}:{port}").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

public enum EndpointProbeResult { Available,Unavailable,CertificateRejected,Unauthorized }

public sealed class RemoteDataClient
{
    ServerConnection settings;
    readonly HttpClient http;
    readonly SemaphoreSlim authGate=new(1,1);
    readonly RemoteQueryFlight queries=new();
    SessionTokens? session;
    long observedVersion=-1;
    public DateTime ObservedChangeUtc {get;private set;}
    public UserIdentity? User => session?.User;
    public RemoteDataClient(ServerConnection settings)
    {
        if(settings.DeviceId==null||settings.Purpose!="device")throw new InvalidOperationException("Pareie este dispositivo antes de entrar.");
        this.settings=settings;
        if(!Uri.TryCreate(settings.SelectedUrl??settings.Url,UriKind.Absolute,out var endpoint)||endpoint.Scheme!=Uri.UriSchemeHttps)throw new InvalidOperationException("A autenticação exige HTTPS.");
        var handler=CreatePinnedHandler(settings.CertificatePin);
        http=new HttpClient(handler){BaseAddress=new Uri((settings.SelectedUrl??settings.Url).TrimEnd('/')+"/"),Timeout=TimeSpan.FromSeconds(25)};
        http.DefaultRequestHeaders.Add("X-Device-Key",settings.Key);
    }
    public static async Task<ServerConnection> PairAsync(ServerConnection code,string name){
        if(code.Purpose!="pairing"||code.PairingExpiresAt<=DateTimeOffset.UtcNow)throw new InvalidOperationException("Código de pareamento expirado.");
        using var http=new HttpClient(CreatePinnedHandler(code.CertificatePin)){BaseAddress=new Uri((code.SelectedUrl??code.Url).TrimEnd('/')+"/"),Timeout=TimeSpan.FromSeconds(15)};
        using var response=await http.PostAsJsonAsync("devices/pair",new PairDeviceRequest(code.Key,name));Check(response);var d=(await response.Content.ReadFromJsonAsync<DeviceCredential>())!;
        return code with {Key=d.Secret,DeviceId=d.Id,Purpose="device",PairingExpiresAt=null};
    }
    public static bool CertificateMatchesPin(X509Certificate2? certificate,string expectedPin)
        => certificate!=null&&certificate.GetCertHashString(HashAlgorithmName.SHA256).Equals(expectedPin,StringComparison.OrdinalIgnoreCase);
    static HttpClientHandler CreatePinnedHandler(string pin)=>new(){ServerCertificateCustomValidationCallback=(_,certificate,_,_)=>CertificateMatchesPin(certificate,pin),AutomaticDecompression=DecompressionMethods.All};
    public static async Task<ServerConnection> SelectEndpointAsync(ServerConnection settings,TimeSpan? probeTimeout=null,Func<Uri,TimeSpan,Task<EndpointProbeResult>>? probe=null)
    {
        var endpoints=settings.Endpoints;var timeout=probeTimeout??TimeSpan.FromSeconds(3);var certificateRejected=false;
        probe??=((uri,limit)=>ProbeAsync(uri,settings.Key,settings.CertificatePin,limit));
        foreach(var endpoint in endpoints)
        {
            var result=await probe(new Uri(endpoint),timeout);
            if(result==EndpointProbeResult.Available)return settings with {SelectedUrl=endpoint};
            if(result==EndpointProbeResult.Unauthorized)throw new InvalidOperationException("O código de conexão foi recusado pelo computador principal. Gere um novo código no PC1.");
            certificateRejected|=result==EndpointProbeResult.CertificateRejected;
        }
        if(certificateRejected)throw new InvalidOperationException("A conexão foi recusada por segurança porque o certificado do computador principal não corresponde ao código de conexão.");
        throw new IOException(endpoints.Count>1?"Não foi possível acessar o computador principal pela rede local nem pelo Tailscale. Verifique se o PC1 está ligado, se o ERP principal está aberto e se o Tailscale está conectado.":"Não foi possível acessar o computador principal pela rede local. Verifique se o PC1 está ligado e se o ERP principal está aberto.");
    }
    static async Task<EndpointProbeResult> ProbeAsync(Uri endpoint,string key,string pin,TimeSpan timeout)
    {
        var pinRejected=0;
        var handler=new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,certificate,_,_)=>{var accepted=CertificateMatchesPin(certificate,pin);if(!accepted)Interlocked.Exchange(ref pinRejected,1);return accepted;}};
        using var client=new HttpClient(handler){BaseAddress=new Uri(endpoint.ToString().TrimEnd('/')+"/"),Timeout=Timeout.InfiniteTimeSpan};
        using var cancellation=new CancellationTokenSource(timeout);
        try
        {
            using var response=await client.GetAsync("ping",cancellation.Token);
            return response.StatusCode==HttpStatusCode.Unauthorized?EndpointProbeResult.Unauthorized:response.IsSuccessStatusCode?EndpointProbeResult.Available:EndpointProbeResult.Unavailable;
        }
        catch(HttpRequestException){return Volatile.Read(ref pinRejected)==1?EndpointProbeResult.CertificateRejected:EndpointProbeResult.Unavailable;}
        catch(OperationCanceledException){return EndpointProbeResult.Unavailable;}
    }
    void Observe(AppData data){if(data.DataVersion!=observedVersion){observedVersion=data.DataVersion;ObservedChangeUtc=DateTime.UtcNow;}PaymentPolicy.Configure(data.PaymentRates);}
    public async Task PingAsync(CancellationToken cancellation)
    {
        try{using var response=await http.GetAsync("ping",cancellation).ConfigureAwait(false);await CheckAsync(response,cancellation).ConfigureAwait(false);}
        catch(HttpRequestException ex){throw Offline(ex);}
        catch(OperationCanceledException ex)when(!cancellation.IsCancellationRequested){throw new TimeoutException("Tempo limite ao verificar a conexão.",ex);}
    }
    public AppData Load(){var data=Read<AppData>("data");Observe(data);return data;}
    public async Task<AppData> LoadAsync(CancellationToken cancellation){var data=await QueryAsync<AppData>("data",cancellation).ConfigureAwait(false);Observe(data);return data;}
    public Task<T> QueryAsync<T>(string path,CancellationToken cancellation=default)
        =>queries.ReadAsync(path,async token=>{
            try{using var response=await SendAsync(HttpMethod.Get,path,null,token).ConfigureAwait(false);await CheckAsync(response,token).ConfigureAwait(false);var result=(await response.Content.ReadFromJsonAsync<T>(cancellationToken:token).ConfigureAwait(false))!;
                if(result is ModulePage page&&page.Data.TryGetProperty("DataVersion",out var version)&&version.TryGetInt64(out var number)&&number!=observedVersion){observedVersion=number;ObservedChangeUtc=DateTime.UtcNow;}
                return result;}
            catch(OperationCanceledException ex)when(!token.IsCancellationRequested){throw new TimeoutException("Tempo limite ao carregar os dados. Tente novamente; a conexão geral não foi marcada como indisponível.",ex);}
            catch(HttpRequestException ex){throw Offline(ex);}
        },cancellation);
    public async Task<WebCommandResult> StageAsync(Guid id,WebStageCommand command,CancellationToken cancellation=default)
    {
        for(var attempt=0;attempt<2;attempt++)
        {
            try{using var response=await SendAsync(HttpMethod.Patch,$"api/v1/pedidos/{id}/etapa",command,cancellation).ConfigureAwait(false);await CheckAsync(response,cancellation).ConfigureAwait(false);return (await response.Content.ReadFromJsonAsync<WebCommandResult>(cancellationToken:cancellation).ConfigureAwait(false))!;}
            catch(HttpRequestException)when(attempt==0){}
        }
        throw new IOException("Confira o histórico antes de repetir a alteração.");
    }
    public DesktopWriteResult DesktopWrite(string path,object command)
    {
        for(var attempt=0;attempt<2;attempt++)try {
            using var response=Send(HttpMethod.Post,path,command);Check(response);
            var result=response.Content.ReadFromJsonAsync<DesktopWriteResult>().GetAwaiter().GetResult()!;queries.Invalidate();if(result.Revision!=observedVersion){observedVersion=result.Revision;ObservedChangeUtc=DateTime.UtcNow;}return result;
        }catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException){if(attempt==1)throw new IOException("A conexão caiu ao confirmar. Confira o histórico antes de repetir.",ex);}
        throw new InvalidOperationException();
    }
    public async Task<T> ReadPostAsync<T>(string path,object command,CancellationToken cancellation=default){if(JsonSerializer.SerializeToUtf8Bytes(command).Length>262144)throw new InvalidOperationException("Os dados desta consulta excedem 256 KiB. Reduza o arquivo de revisão; nada foi enviado.");using var response=await SendAsync(HttpMethod.Post,path,command,cancellation).ConfigureAwait(false);await CheckAsync(response,cancellation).ConfigureAwait(false);return (await response.Content.ReadFromJsonAsync<T>(cancellationToken:cancellation).ConfigureAwait(false))!;}
    // Foto do produto: o JSON leva a imagem já reduzida (até ~1 MB), acima do limite de 256 KiB dos comandos comuns.
    public async Task<T> UploadAsync<T>(string path,object command,CancellationToken cancellation=default){try{using var response=await SendAsync(HttpMethod.Post,path,command,cancellation).ConfigureAwait(false);await CheckAsync(response,cancellation).ConfigureAwait(false);return (await response.Content.ReadFromJsonAsync<T>(cancellationToken:cancellation).ConfigureAwait(false))!;}catch(HttpRequestException ex){throw Offline(ex);}}
    public async Task<byte[]?> BytesAsync(string path,CancellationToken cancellation=default){try{using var response=await SendAsync(HttpMethod.Get,path,null,cancellation).ConfigureAwait(false);if(response.StatusCode==System.Net.HttpStatusCode.NotFound)return null;await CheckAsync(response,cancellation).ConfigureAwait(false);return await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);}catch(HttpRequestException ex){throw Offline(ex);}}
    public async Task<DesktopWriteResult> DesktopWriteAsync(string path,object command,CancellationToken cancellation=default)
    {
        if(JsonSerializer.SerializeToUtf8Bytes(command).Length>262144)throw new InvalidOperationException("O comando excede 256 KiB. Reduza apenas os dados desta operação; nada foi enviado.");
        for(var attempt=0;attempt<2;attempt++)try {
            using var response=await SendAsync(HttpMethod.Post,path,command,cancellation).ConfigureAwait(false);
            await CheckAsync(response,cancellation).ConfigureAwait(false);
            var result=(await response.Content.ReadFromJsonAsync<DesktopWriteResult>(cancellationToken:cancellation).ConfigureAwait(false))!;
            queries.Invalidate();if(result.Revision!=observedVersion){observedVersion=result.Revision;ObservedChangeUtc=DateTime.UtcNow;}return result;
        }catch(Exception ex)when(ex is HttpRequestException || ex is OperationCanceledException && !cancellation.IsCancellationRequested){if(attempt==1)throw new IOException("A conexão caiu ao confirmar. Confira o histórico antes de repetir.",ex);}
        throw new InvalidOperationException();
    }
    public List<BackupInfo> ListBackups()=>Read<List<BackupInfo>>("backups");
    T Read<T>(string path)
    {
        try { using var response=Send(HttpMethod.Get,path);Check(response);return response.Content.ReadFromJsonAsync<T>().GetAwaiter().GetResult()!; }
        catch(HttpRequestException ex){throw Offline(ex);}catch(TaskCanceledException ex){throw new TimeoutException("Tempo limite ao carregar os dados. Tente novamente.",ex);}
    }
    static Exception Offline(Exception ex)=>new IOException("Não foi possível estabelecer a conexão com o computador principal. Confira o PC1 e a conexão de rede/Tailscale.",ex);
    static void Check(HttpResponseMessage response)=>CheckAsync(response).GetAwaiter().GetResult();
    static async Task CheckAsync(HttpResponseMessage response,CancellationToken cancellation=default)
    {
        if(response.IsSuccessStatusCode)return;
        if(response.StatusCode==HttpStatusCode.Unauthorized)throw new InvalidOperationException("Sessão expirada ou encerrada. Entre novamente com usuário e senha.");
        if(response.StatusCode==HttpStatusCode.Forbidden){var denied=FriendlyReason(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));var get=response.RequestMessage?.Method==HttpMethod.Get;throw new UnauthorizedAccessException(denied.Length>0&&denied!="Acesso recusado."?denied+(get?"":" Nada foi gravado."):get?"Sem permissão para carregar estes dados.":"Seu usuário não tem permissão para esta operação. Nada foi gravado.");}
        if(response.StatusCode==HttpStatusCode.RequestTimeout||response.StatusCode==HttpStatusCode.GatewayTimeout)throw new TimeoutException("Tempo limite ao carregar os dados. Tente novamente.");
        var message=await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        if((int)response.StatusCode==426)throw new InvalidOperationException("Atualize o ERP antes de salvar. Esta gravação não foi executada.");
        if(response.StatusCode==HttpStatusCode.Conflict)throw new InvalidOperationException(message.StartsWith("Login duplicado",StringComparison.Ordinal)?"Este login já está em uso. Escolha outro login individual.":"Os dados mudaram em outro computador. Atualize a tela e tente novamente; sua operação não foi gravada.");
        var reason=FriendlyReason(message);
        var read=response.RequestMessage?.Method==HttpMethod.Get;
        throw new InvalidOperationException((int)response.StatusCode is 400 or 422?(reason.Length>0?reason:read?"Não foi possível carregar estes dados.":"Confira os dados informados.")+(read?"":" Nada foi gravado."):"O computador principal não conseguiu concluir a operação"+(reason.Length>0?": "+reason:".")+" Tente novamente.");
    }
    // Resposta do servidor em texto ou ProblemDetails (JSON): usa "detail" ou "title"; nunca mostra JSON ao usuário.
    static string FriendlyReason(string body)
    {
        body=(body??"").Trim();if(body.Length==0)return "";
        if(body.StartsWith('{'))try{using var doc=System.Text.Json.JsonDocument.Parse(body);foreach(var key in new[]{"detail","title"})if(doc.RootElement.TryGetProperty(key,out var v)&&v.ValueKind==System.Text.Json.JsonValueKind.String&&!string.IsNullOrWhiteSpace(v.GetString()))return v.GetString()!.Trim();return "";}catch(System.Text.Json.JsonException){return "";}
        return body.Length>240?body[..240]+"…":body;
    }
    public static void EnsureCompatibleForWrite(AppData data)
    { if(data.OrderDetailsVersion<9)throw new InvalidOperationException("Atualize primeiro o ERP do computador principal para 2026.10.7.1 antes de salvar, para preservar leitura por usuário e composição financeira."); }
    public T Update<T>(Func<AppData,T> action)=>throw new InvalidOperationException("Transporte legado desativado. Atualize o ERP e use o comando específico; nada foi gravado.");
    public void RestoreBackup(string path)
    { using var response=Send(HttpMethod.Post,"restore",new RestoreRequest(path));Check(response); }
    // Confere a senha de acesso no computador principal.
    public LoginCheck VerifyLogin(string user,string password)=>VerifyLoginAsync(user,password).GetAwaiter().GetResult();
    public async Task<LoginCheck> VerifyLoginAsync(string user,string password,CancellationToken cancellation=default)
    {
        await authGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            using var response=await http.PostAsJsonAsync("auth/login",new LoginRequest(user,password),cancellation).ConfigureAwait(false);
            if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)return new(false,false,"Usuário ou senha inválidos.");
            await CheckAsync(response,cancellation).ConfigureAwait(false);queries.Invalidate();session=(await response.Content.ReadFromJsonAsync<SessionTokens>(cancellationToken:cancellation).ConfigureAwait(false))!;Persist();return new(true,session.MustChange);
        }
        catch(HttpRequestException ex){throw Offline(ex);}catch(OperationCanceledException ex)when(!cancellation.IsCancellationRequested){throw new TimeoutException("Tempo limite ao entrar. Tente novamente.",ex);}
        finally{authGate.Release();}
    }
    public void ChangeLoginPassword(string user,string current,string next)
    {
        try
        {
            using var response=Send(HttpMethod.Post,"auth/password",new PasswordChangeRequest(user,current,next));
            if(response.StatusCode==HttpStatusCode.NotFound)throw new InvalidOperationException("Atualize primeiro o ERP do computador principal para trocar a senha de acesso.");
            if(response.StatusCode==HttpStatusCode.Forbidden)throw new UnauthorizedAccessException(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Check(response);
        }
        catch(HttpRequestException ex){throw Offline(ex);}catch(TaskCanceledException ex){throw new TimeoutException("Tempo limite ao carregar os dados. Tente novamente.",ex);}
    }
    HttpResponseMessage Send(HttpMethod method,string path,object? body=null,CancellationToken cancellation=default)
        {var pending=SendAsync(method,path,body,cancellation);if(!pending.IsCompleted)UiWait?.Invoke(pending);return pending.GetAwaiter().GetResult();}
    // Preenchido pelo desktop: enquanto uma chamada síncrona espera o servidor, a janela continua desenhando em vez de congelar.
    public static Action<Task>? UiWait;
    async Task<HttpResponseMessage> SendAsync(HttpMethod method,string path,object? body=null,CancellationToken cancellation=default)
    {
        var current=await AccessAsync(null,cancellation).ConfigureAwait(false);
        async Task<HttpResponseMessage> Request(SessionTokens access)
        {
            using var request=new HttpRequestMessage(method,path);request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",access.AccessToken);
            if(body!=null)request.Content=JsonContent.Create(body,body.GetType());
            return await http.SendAsync(request,cancellation).ConfigureAwait(false);
        }
        var response=await Request(current).ConfigureAwait(false);
        if(response.StatusCode==HttpStatusCode.Unauthorized){response.Dispose();current=await AccessAsync(current.AccessToken,cancellation).ConfigureAwait(false);response=await Request(current).ConfigureAwait(false);}
        return response;
    }
    async Task<SessionTokens> AccessAsync(string? rejected,CancellationToken cancellation)
    {
        await authGate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if(session==null)throw new InvalidOperationException("Entre com usuário e senha.");
            if(session.ExpiresAt<=DateTimeOffset.UtcNow.AddSeconds(30)||rejected==session.AccessToken)await RefreshAsync(cancellation).ConfigureAwait(false);
            return session!;
        }
        finally{authGate.Release();}
    }
    async Task RefreshAsync(CancellationToken cancellation)
    {
        using var response=await http.PostAsJsonAsync("auth/refresh",new RefreshRequest(session!.RefreshToken),cancellation).ConfigureAwait(false);
        if(!response.IsSuccessStatusCode){session=null;queries.Invalidate();GraficaCaixa.Wpf.ClientCredentialStore.ClearSession(settings);throw new InvalidOperationException("Sessão expirada ou encerrada. Entre novamente com usuário e senha.");}
        session=(await response.Content.ReadFromJsonAsync<SessionTokens>(cancellationToken:cancellation).ConfigureAwait(false))!;Persist();
    }
    void Persist(){if(session!=null)GraficaCaixa.Wpf.ClientCredentialStore.SaveSession(settings,session);}
    public bool ResumeSession()=>ResumeSessionAsync().GetAwaiter().GetResult();
    public async Task<bool> ResumeSessionAsync(CancellationToken cancellation=default)
    {
        await authGate.WaitAsync(cancellation).ConfigureAwait(false);
        try{queries.Invalidate();session=GraficaCaixa.Wpf.ClientCredentialStore.LoadSession(settings);if(session==null)return false;await RefreshAsync(cancellation).ConfigureAwait(false);}
        catch(InvalidOperationException){session=null;GraficaCaixa.Wpf.ClientCredentialStore.ClearSession(settings);return false;}
        finally{authGate.Release();}
        var user=await QueryAsync<UserIdentity>("auth/me",cancellation).ConfigureAwait(false);
        await authGate.WaitAsync(cancellation).ConfigureAwait(false);try{session=session! with {User=user};Persist();}finally{authGate.Release();}return true;
    }
    public void Logout()=>LogoutAsync().GetAwaiter().GetResult();
    public async Task LogoutAsync()
    {
        try{if(session!=null){using var response=await SendAsync(HttpMethod.Post,"auth/logout").ConfigureAwait(false);await CheckAsync(response).ConfigureAwait(false);}}
        finally{await authGate.WaitAsync().ConfigureAwait(false);try{session=null;queries.Invalidate();GraficaCaixa.Wpf.ClientCredentialStore.ClearSession(settings);}finally{authGate.Release();}}
    }
    public IReadOnlyList<DeviceSummary> Devices()=>Read<List<DeviceSummary>>("devices");
    public IReadOnlyList<SessionSummary> Sessions()=>Read<List<SessionSummary>>("auth/sessions");
    public void EndSession(Guid id){using var response=Send(HttpMethod.Post,"auth/sessions/end",new SessionEnd(id));Check(response);}
    public void RevokeDevice(Guid id){using var response=Send(HttpMethod.Post,"devices/revoke",new DeviceRevoke(id));Check(response);}
    public void RevokePairings(){using var response=Send(HttpMethod.Post,"devices/pairings/revoke");Check(response);}
    public ServerConnection CreatePairing(){using var response=Send(HttpMethod.Post,"devices/pairings");Check(response);var ticket=response.Content.ReadFromJsonAsync<PairingTicket>().GetAwaiter().GetResult()!;var urls=settings.Endpoints.Where(u=>{var host=new Uri(u).Host;return !host.Equals("localhost",StringComparison.OrdinalIgnoreCase)&&!(IPAddress.TryParse(host,out var ip)&&IPAddress.IsLoopback(ip));}).ToArray();return settings with {Url=urls.FirstOrDefault()??settings.Url,Urls=urls,SelectedUrl=null,Key=ticket.Ticket,DeviceId=null,Purpose="pairing",PairingExpiresAt=ticket.ExpiresAt};}
    public void RotateDevice(){using var response=Send(HttpMethod.Post,"devices/rotate");Check(response);var d=response.Content.ReadFromJsonAsync<DeviceCredential>().GetAwaiter().GetResult()!;GraficaCaixa.Wpf.ClientCredentialStore.ClearSession(settings);session=null;settings=settings with {Key=d.Secret};http.DefaultRequestHeaders.Remove("X-Device-Key");http.DefaultRequestHeaders.Add("X-Device-Key",d.Secret);GraficaCaixa.Wpf.ClientCredentialStore.SaveConnection(settings);}
    public IReadOnlyList<UserSummary> ListUsers()=>Read<List<UserSummary>>("auth/users");
    public Dictionary<string,bool> EffectivePermissions()=>Read<Dictionary<string,bool>>("auth/permissions");
    public IReadOnlyList<PermissionState> PermissionPanel(Guid? id,string? role)=>Read<List<PermissionState>>((id is Guid user?$"auth/permission-panel?userId={user}&":"auth/permission-panel?")+$"role={Uri.EscapeDataString(role??"")}");
    public void SavePermissions(PermissionEdit edit){using var response=Send(HttpMethod.Post,"auth/permissions",edit);Check(response);}
    public IReadOnlyList<RoleSummary> ListRoles()=>Read<List<RoleSummary>>("auth/roles");
    public void CreateRole(RoleCreate edit){using var response=Send(HttpMethod.Post,"auth/roles",edit);Check(response);}
    public void DeleteRole(RoleDelete edit){using var response=Send(HttpMethod.Post,"auth/roles/delete",edit);Check(response);}
    public void RevokeSessions(Guid id){using var response=Send(HttpMethod.Post,"auth/revoke",new SessionRevoke(id));Check(response);}
    public AuditEntry? AuditEntryFull(Guid id)=>Read<AuditEntry>($"audit/entry?id={id}");
    public IReadOnlyList<AuditEntry> QueryAudit(AuditFilter? filter=null)
    {
        filter??=new();string Enc(string? value)=>Uri.EscapeDataString(value??"");
        return Read<List<AuditEntry>>($"audit?from={Enc(filter.From?.ToString("O"))}&to={Enc(filter.To?.ToString("O"))}&user={Enc(filter.User)}&module={Enc(filter.Module)}&action={Enc(filter.Action)}&entity={Enc(filter.Entity)}&id={Enc(filter.Id)}");
    }
    public void SaveUser(UserEdit edit){using var response=Send(HttpMethod.Post,"auth/users",edit);Check(response);}
    public void AuthorizeReceipt(ReceiptAuditRequest receipt){using var response=Send(HttpMethod.Post,"receipts/authorize",receipt);Check(response);}
    public bool OwnerConfigured()=>Read<bool>("auth/owner/configured");
    public void AuthenticateOwner(string pin,bool setup){using var response=Send(HttpMethod.Post,"auth/owner",new OwnerPinRequest(pin,setup));Check(response);}
    public void ResetOwnerPin(string password,string newPin){using var response=Send(HttpMethod.Post,"auth/owner/reset",new OwnerPinReset(password,newPin));Check(response);}
}

public sealed class SharedDataServer : IAsyncDisposable
{
    readonly WebApplication app;
    readonly ServerIdentity identity;
    readonly PersistentServerLog? log;
    BackupScheduler? backups;
    public ServerConnection Connection {get;}
    SharedDataServer(WebApplication app,ServerConnection connection,ServerIdentity identity,PersistentServerLog? log){this.app=app;Connection=connection;this.identity=identity;this.log=log;}
    public static bool IsAuthorized(string supplied,string key)
    {
        var expected="Bearer "+key;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes(expected));
    }
    public static async Task<SharedDataServer> StartAsync(DataStore store,string identityFolder,int port=5074,string? logsFolder=null,bool allowIdentityCreation=true,string? legacyIdentityFolder=null,WebApiOptions? webOptions=null)
    {
        store.EnsureInitialized();store.RecoverBusinessArtifacts();
        var authentication=new IdentitySecurity(store);authentication.Migrate();
        var identity=ServerIdentityStore.LoadOrCreate(identityFolder,allowIdentityCreation,legacyIdentityFolder);
        var certificate=identity.Certificate;
        PersistentServerLog? log=null;
        if(!string.IsNullOrWhiteSpace(logsFolder))log=new PersistentServerLog(logsFolder);
        var backups=new BackupScheduler(store,(kind,message)=>log?.Write(kind,message),BackupScheduler.FilesOf(webOptions?.PhotosRoot,webOptions?.DocumentsDirectory));
        var address=NetworkEndpointDiscovery.GetLanAddress();
        var urls=NetworkEndpointDiscovery.BuildUrls(address,NetworkEndpointDiscovery.GetTailscaleAddress(),port);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions {Args=[],ContentRootPath=identityFolder});
        webOptions ??= new WebApiOptions();
        WebApiV1.Configure(builder,webOptions);
        BrowserCertificateSource? browserCertificate=null;
        if(webOptions.Browser.Enabled)
        {
            if(webOptions.Browser.Port==port)throw new InvalidOperationException("Listener browser deve preservar a porta WPF.");
            try
            {
                browserCertificate=new BrowserCertificateSource(webOptions.Browser);
                BrowserGateway.Configure(builder,identityFolder);
            }
            catch(Exception ex) when(ex is CryptographicException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                // Fail closed for browsers, preserving the existing desktop listener during certificate incidents.
                log?.Write("browser-indisponivel","Certificado browser indisponível/recusado; listener WPF preservado.");
            }
        }
        ResponseCompressionSetup.Configure(builder.Services);
        builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(options=>
        {
            options.Limits.MaxRequestBodySize=32*1024*1024;
            options.ListenAnyIP(port,listen=>listen.UseHttps(certificate));
            if(browserCertificate!=null)options.Listen(IPAddress.Parse(webOptions.Browser.BindAddress),webOptions.Browser.Port,listen=>listen.UseHttps(tls=>tls.ServerCertificateSelector=(_,name)=>browserCertificate.Select(name)));
        });
        var app=builder.Build();BrowserGateway? trackingGateway=null;
        ResponseCompressionSetup.Use(app);
        if(browserCertificate!=null)
        {
            app.Lifetime.ApplicationStopped.Register(browserCertificate.Dispose);
            // Acompanhamento do pedido: a página pública lê só o pedido do link e os dados da gráfica.
            var companyJson=new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var browser=new BrowserGateway(app,webOptions.Browser,authentication,identityFolder,id=>store.ReadWork(db=>{var company=JsonSerializer.Deserialize<CompanyProfile>(WebQueries.Metadata(db,"company_profile_v1","{}"),companyJson)??new();ScheduledOrder? order=null;if(id is Guid orderId){try{order=MobileReceiptPdf.ReadSnapshot(db,orderId);}catch(WebRequestException){}}return(order,company);}),
                number=>store.ReadWork(db=>{var ids=new List<Guid>();using var cmd=WebQueries.Command(db,"SELECT id FROM scheduled_orders WHERE order_number=@n LIMIT 5",("@n",number));using var reader=cmd.ExecuteReader();while(reader.Read())ids.Add(Guid.Parse(reader.GetString(0)));return (IEnumerable<Guid>)ids;}));
            trackingGateway=browser;
            app.Use(browser.Invoke);
        }
        app.UseCors("web-v1");
        WebApiV1.Map(app,store,webOptions);
        app.Use(async(context,next)=>
        {
            var supplied=context.Request.Headers.Authorization.ToString();
            var path=context.Request.Path.Value??"";
            if((path.StartsWith("/auth/",StringComparison.Ordinal)||path.StartsWith("/devices/",StringComparison.Ordinal))&&context.Request.ContentLength>8192){context.Response.StatusCode=413;return;}
            if(path is "/ping" or "/devices/pair"){await next(context);return;}
            var technical=path is "/health" or "/auth/login" or "/auth/refresh";
            var device=context.Request.Headers["X-Device-Key"].ToString();
            var deviceId=authentication.AuthenticateDevice(device);
            if(deviceId==null){LogDenied(log,context);context.Response.StatusCode=401;return;}
            context.Items["device"]=deviceId.Value;
            if(!technical){var user=supplied.StartsWith("Bearer ",StringComparison.Ordinal)?authentication.Authenticate(supplied[7..],deviceId):null;if(user==null){context.Response.StatusCode=401;return;}context.Items["identity"]=user;}
            try{await next(context);}catch(WebRequestException){throw;}catch(BadHttpRequestException) when(path.StartsWith("/api/v1/",StringComparison.Ordinal)){throw;}catch(OperationCanceledException) when(context.RequestAborted.IsCancellationRequested){throw;}catch(SessionInvalidException){context.Response.StatusCode=401;}catch(Microsoft.Data.Sqlite.SqliteException ex) when(ex.SqliteErrorCode==19){context.Response.StatusCode=409;await context.Response.WriteAsync("Login duplicado ou registro em conflito.");}catch(UnauthorizedAccessException ex){context.Response.StatusCode=403;await context.Response.WriteAsync(string.IsNullOrWhiteSpace(ex.Message)||ex.Message==new UnauthorizedAccessException().Message?"Acesso recusado.":ex.Message);}catch(InvalidOperationException){log?.Write("requisicao-invalida",$"Operação recusada: {context.Request.Method} {path}");context.Response.StatusCode=400;await context.Response.WriteAsync("Não foi possível concluir a operação. Confira os dados informados.");}catch(Exception ex){var origin=new System.Diagnostics.StackTrace(ex).GetFrames().FirstOrDefault()?.GetMethod();log?.Write("erro-api",$"{ex.GetType().Name}: {context.Request.Method} {path} em {origin?.DeclaringType?.Name}.{origin?.Name}");context.Response.StatusCode=500;await context.Response.WriteAsync("Não foi possível concluir a operação no computador principal.");}
        });
        app.MapGet("/ping",()=>Results.Ok(new {ok=true}));
        app.MapGet("/health",()=>
        {
            try
            {
                var options=new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=store.DatabasePath,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false};
                using var connection=new Microsoft.Data.Sqlite.SqliteConnection(options.ToString());
                connection.Open();
                using var command=connection.CreateCommand();
                command.CommandText="SELECT COUNT(*) FROM metadata";
                command.ExecuteScalar();
                return Results.Ok(new {status="ok",database="available",version=typeof(SharedDataServer).Assembly.GetName().Version?.ToString(),authentication="individual-sessions",authorization="profile-user-exceptions",devices="individual-credentials"});
            }
            catch { return Results.Problem("Banco indisponível.",statusCode:503); }
        });
        app.MapGet("/data",(HttpContext context)=>store.AuthorizedView((UserIdentity)context.Items["identity"]!));
        app.MapGet("/backups",(HttpContext context)=>{authentication.Require((UserIdentity)context.Items["identity"]!,"system.backups");return store.ListBackups();});
        // Backup programado: situação (último/próximo) e "Fazer backup agora". Executado aqui, no computador principal.
        app.MapGet("/desktop/backup/status",(HttpContext context)=>{authentication.Require((UserIdentity)context.Items["identity"]!,"system.backups");return backups.Status();});
        app.MapPost("/desktop/backup/run",(HttpContext context)=>{var actor=(UserIdentity)context.Items["identity"]!;authentication.Require(actor,"system.backups");var schedule=backups.Schedule();if(string.IsNullOrWhiteSpace(schedule.Destination))throw new WebRequestException(422,"Configure e salve a pasta de destino do backup antes.");try{return backups.Run(schedule);}catch(InvalidOperationException ex){throw new WebRequestException(409,ex.Message);}});
        // Desktop commands have a finite, scoped limit; the existing 32 MiB global limit remains unchanged.
        app.Use(async(context,next)=>{
            if(!context.Request.Path.StartsWithSegments("/desktop")){await next(context);return;}
            // Envio de foto do produto leva o JPG (até 8 MB) em base64 no JSON: limite próprio só nesta rota.
            long limit=context.Request.Path.Equals("/desktop/products/photos",StringComparison.Ordinal)?12*1024*1024:256*1024;
            var feature=context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if(feature is {IsReadOnly:false})feature.MaxRequestBodySize=limit;
            if(context.Request.ContentLength>limit){context.Response.StatusCode=413;return;}
            try{await next(context);}catch(WebRequestException ex){await Results.Problem(statusCode:ex.Status,title:ex.Message).ExecuteAsync(context);}
            catch(BadHttpRequestException ex){context.Response.StatusCode=ex.StatusCode;}
        });
        UserIdentity DesktopActor(HttpContext c)=>(UserIdentity)c.Items["identity"]!;
        app.MapGet("/desktop/owner/indicators",(HttpContext c)=>store.BusinessOwnerIndicators(DesktopActor(c)));
        app.MapGet("/desktop/order-extras/{id:guid}",(HttpContext c,Guid id)=>store.BusinessOrderExtras(DesktopActor(c),id));
        app.MapGet("/desktop/reports/year-ago",(HttpContext c,DateTime from,DateTime to)=>store.BusinessReportYearAgo(DesktopActor(c),from,to));
        app.MapGet("/desktop/reports/day",(HttpContext c,DateTime date)=>store.BusinessReportDay(DesktopActor(c),date));
        app.MapGet("/desktop/today",(HttpContext c)=>store.BusinessToday(DesktopActor(c)));
        app.MapGet("/desktop/customer-last-order/{id:guid}",(HttpContext c,Guid id)=>store.BusinessLastOrder(DesktopActor(c),id));
        app.MapGet("/desktop/audit-summary",(HttpContext c,int offset)=>store.BusinessAuditBrief(DesktopActor(c),offset));
        app.MapGet("/desktop/lookup/products",(HttpContext c,string search)=>store.BusinessProductLookup(DesktopActor(c),search));
        app.MapGet("/desktop/lookup/customers",(HttpContext c,string search)=>store.BusinessCustomerLookup(DesktopActor(c),search));
        app.MapGet("/desktop/cancelled",(HttpContext c)=>store.BusinessCancelled(DesktopActor(c)));
        app.MapGet("/desktop/edit/{kind}/{id:guid}",(HttpContext c,string kind,Guid id)=>store.BusinessContext(DesktopActor(c),kind,id));
        app.MapGet("/desktop/owner/summary",(HttpContext c,DateTime from,DateTime to)=>store.BusinessOwnerSummary(DesktopActor(c),from,to));
        app.MapGet("/desktop/top-products",(HttpContext c)=>Results.Ok(store.BusinessTopProducts(DesktopActor(c))));
        // Link de acompanhamento do pedido (para o botão do desktop): só com o acompanhamento ligado e quem vê pedidos.
        app.MapGet("/desktop/acompanhamento",()=>new{ativo=trackingGateway?.TrackingEnabled==true});
        app.MapGet("/desktop/orders/{id:guid}/acompanhamento",(HttpContext c,Guid id)=>store.ReadWork(db=>{MobileAccess.Require(Permissions.Resolve(db,DesktopActor(c)),"view.orders");if(trackingGateway==null)throw new WebRequestException(404,"O link de acompanhamento está desligado.");var number=WebQueries.ReadOrders(db," WHERE id=@id",("@id",id)).FirstOrDefault()?.Number??throw new WebRequestException(404,"Pedido não encontrado.");var url=trackingGateway.TrackingUrl(id,number)??throw new WebRequestException(404,"O link de acompanhamento está desligado.");return new{url};}));
        // O mesmo link para o celular (botão "Link de acompanhamento" no pedido): devolve o link e a mensagem pronta do modelo.
        app.MapGet("/api/v1/pedidos/{id:guid}/acompanhamento",(HttpContext c,Guid id)=>store.WebReadAsync((UserIdentity)c.Items["identity"]!,"view.orders",(db,p)=>{
            if(trackingGateway?.TrackingEnabled!=true)throw new WebRequestException(404,"O link de acompanhamento está desligado.");
            var order=WebQueries.ReadOrders(db," WHERE id=@id",("@id",id)).FirstOrDefault()??throw new WebRequestException(404,"Pedido não encontrado.");
            var url=trackingGateway.TrackingUrl(id,order.Number)??throw new WebRequestException(404,"O link de acompanhamento está desligado.");
            var company=JsonSerializer.Deserialize<CompanyProfile>(WebQueries.Metadata(db,"company_profile_v1","{}"),new JsonSerializerOptions(JsonSerializerDefaults.Web))??new();
            var state=JsonSerializer.Deserialize<Prompt12State>(WebQueries.Metadata(db,"prompt12_v1","{}"),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
            return new{url,message=CommunicationOperations.TrackingMessage(state,company,order.CustomerName,order.Number,url),phone=order.Contact};},c.RequestAborted)).RequireCors("web-v1");
        app.MapGet("/desktop/owner/forecast",(HttpContext c)=>store.BusinessOwnerForecast(DesktopActor(c)));
        app.MapGet("/desktop/owner/expenses",(HttpContext c,int offset,string filter,string search)=>store.BusinessExpenses(DesktopActor(c),offset,filter,search));
        app.MapPost("/desktop/business",(HttpContext c,BusinessCommand x)=>store.BusinessExecute(DesktopActor(c),x));
        app.MapPost("/desktop/orders/save",(HttpContext c,DesktopOrderCommand x)=>store.DesktopSaveOrder(DesktopActor(c),x));
        app.MapPost("/desktop/orders/{id:guid}/payments",(HttpContext c,Guid id,DesktopPaymentCommand x)=>store.DesktopReceive(DesktopActor(c),id,x));
        app.MapPost("/desktop/customers/{id:guid}",(HttpContext c,Guid id,DesktopCustomerCommand x)=>store.DesktopSaveCustomer(DesktopActor(c),id,x));
        app.MapPost("/desktop/notes",(HttpContext c,DesktopNotesCommand x)=>store.DesktopSaveNotes(DesktopActor(c),x));
        app.MapPost("/desktop/products",(HttpContext c,DesktopProductCommand x)=>store.DesktopSaveProduct(DesktopActor(c),x,webOptions.PhotosRoot));
        // Fotos do produto pelo servidor (o notebook não alcança o compartilhamento): enviar, ler (0 = Principal) e descartar não usadas.
        app.MapPost("/desktop/products/photos",(HttpContext c,DesktopPhotoUpload x)=>store.DesktopUploadPhoto(DesktopActor(c),x,webOptions.PhotosRoot));
        app.MapPost("/desktop/products/photos/discard",(HttpContext c,DesktopPhotoDiscard x)=>store.DesktopDiscardPhotos(DesktopActor(c),x,webOptions.PhotosRoot));
        app.MapGet("/desktop/products/{id:guid}/photos/{index:int}",async (HttpContext c,Guid id,int index,int? w)=>await WebFiles.OpenAsync(store.DesktopPhotoFile(DesktopActor(c),id,index,webOptions),"image/jpeg","produto.jpg",w,c.RequestAborted));
        app.MapPost("/desktop/reminders",(HttpContext c,DesktopReminderCommand x)=>store.DesktopSaveReminder(DesktopActor(c),x));
        app.MapPost("/desktop/purchase-needs",(HttpContext c,DesktopNeedCommand x)=>store.DesktopSaveNeed(DesktopActor(c),x));
        app.MapGet("/desktop/context/{kind}/{id:guid}",(HttpContext c,string kind,Guid id)=>store.DesktopContext(DesktopActor(c),kind,id));
        app.MapGet("/desktop/reminders/{id:guid}",(HttpContext c,Guid id)=>store.DesktopReminder(DesktopActor(c),id));
        app.MapGet("/desktop/purchase-needs/{id:guid}",(HttpContext c,Guid id)=>store.DesktopNeed(DesktopActor(c),id));
        app.MapGet("/desktop/notes",(HttpContext c)=>store.DesktopNotes(DesktopActor(c)));
        app.MapPost("/commit",()=>Results.Json(new{message="Atualize o ERP para a versão de comandos específicos antes de salvar. Esta gravação não foi executada."},statusCode:426));
        // Restaurar backup substitui o banco oficial: só é aceito no próprio computador principal.
        app.MapPost("/restore",(HttpContext context,RestoreRequest request)=>
        {
            if(context.Connection.RemoteIpAddress is not IPAddress ip||!IPAddress.IsLoopback(ip)){log?.Write("restauracao-recusada",Origin(context));return Results.Text("Por segurança, a restauração de backup só pode ser feita no computador principal (PC1).",statusCode:403);}
            var actor=(UserIdentity)context.Items["identity"]!;if(actor.Role!="Administrador")throw new UnauthorizedAccessException();authentication.Require(actor,"system.backups");store.RestoreBackup(request.Path,actor);return Results.Ok();
        });
        // Senha de acesso conferida no servidor: os outros computadores não recebem nem guardam a senha.
        app.MapPost("/auth/login",(HttpContext context,LoginRequest request)=>
        {
            var device=(Guid)context.Items["device"]!;var session=authentication.Login(request.User,request.Password,Origin(context)+" / dispositivo="+device,device);
            return session==null?Results.Text("Usuário ou senha inválidos.",statusCode:401):Results.Ok(session);
        });
        app.MapPost("/auth/refresh",(HttpContext context,RefreshRequest request)=>{var session=authentication.Refresh(request.RefreshToken,Origin(context),(Guid)context.Items["device"]!);return session==null?Results.Unauthorized():Results.Ok(session);});
        app.MapPost("/devices/pair",(HttpContext c,PairDeviceRequest request)=>{if(string.IsNullOrWhiteSpace(request.Name)||request.Name.Length>80)return Results.BadRequest();var d=authentication.PairDevice(request,Origin(c));return d==null?Results.Unauthorized():Results.Ok(d);});
        app.MapPost("/devices/pairings",(HttpContext c)=>Results.Ok(authentication.CreatePairing((UserIdentity)c.Items["identity"]!,Origin(c))));
        app.MapPost("/devices/pairings/revoke",(HttpContext c)=>{authentication.RevokePairings((UserIdentity)c.Items["identity"]!,Origin(c));return Results.Ok();});
        app.MapGet("/devices",(HttpContext c)=>Results.Ok(authentication.Devices((UserIdentity)c.Items["identity"]!)));
        app.MapPost("/devices/revoke",(HttpContext c,DeviceRevoke request)=>{authentication.RevokeDevice((UserIdentity)c.Items["identity"]!,request.DeviceId,Origin(c));return Results.Ok();});
        app.MapPost("/devices/rotate",(HttpContext c)=>Results.Ok(authentication.RotateDevice((UserIdentity)c.Items["identity"]!,Origin(c))));
        app.MapGet("/auth/sessions",(HttpContext c)=>Results.Ok(authentication.Sessions((UserIdentity)c.Items["identity"]!)));
        app.MapPost("/auth/sessions/end",(HttpContext c,SessionEnd request)=>{authentication.EndSession((UserIdentity)c.Items["identity"]!,request.SessionId,Origin(c));return Results.Ok();});
        app.MapPost("/auth/logout",(HttpContext context)=>{authentication.Logout((UserIdentity)context.Items["identity"]!,Origin(context));return Results.Ok();});
        app.MapGet("/desktop/receivables",(HttpContext c,bool? losses,string? search,string? status,DateTime? from,DateTime? to,string? sort,string? filter,int? page)=>{if((search?.Length??0)>200||(page??1)<1||(page??1)>100000)throw new WebRequestException(400,"Confira a pesquisa e a página.");return Results.Ok(store.ReceivablesView((UserIdentity)c.Items["identity"]!,losses==true,search??"",status??"",from,to,sort??"old",filter??"pending",page??1));});
        app.MapGet("/auth/permissions",(HttpContext c)=>Results.Ok(authentication.Effective((UserIdentity)c.Items["identity"]!)));
        app.MapPost("/receipts/authorize",(HttpContext c,ReceiptAuditRequest receipt)=>{authentication.AuthorizeReceipt((UserIdentity)c.Items["identity"]!,receipt,Origin(c));return Results.Ok();});
        app.MapGet("/auth/permission-panel",(HttpContext c,Guid? userId,string? role)=>Results.Ok(authentication.PermissionPanel((UserIdentity)c.Items["identity"]!,userId,role)));
        app.MapPost("/auth/permissions",(HttpContext c,PermissionEdit edit)=>{authentication.SavePermissions((UserIdentity)c.Items["identity"]!,edit,Origin(c));return Results.Ok();});
        app.MapGet("/auth/roles",(HttpContext c)=>Results.Ok(authentication.RoleList((UserIdentity)c.Items["identity"]!)));
        app.MapPost("/auth/roles",(HttpContext c,RoleCreate edit)=>{authentication.CreateRole((UserIdentity)c.Items["identity"]!,edit,Origin(c));return Results.Ok();});
        app.MapPost("/auth/roles/delete",(HttpContext c,RoleDelete edit)=>{authentication.DeleteRole((UserIdentity)c.Items["identity"]!,edit.Name,Origin(c));return Results.Ok();});
        app.MapPost("/auth/revoke",(HttpContext c,SessionRevoke edit)=>{authentication.RevokeSessions((UserIdentity)c.Items["identity"]!,edit.UserId,Origin(c));return Results.Ok();});
        app.MapGet("/audit",(HttpContext c)=>
        {
            var user=(UserIdentity)c.Items["identity"]!;var q=c.Request.Query;string? Text(string key)=>string.IsNullOrWhiteSpace(q[key])?null:q[key].ToString();DateTime? Date(string key)=>DateTime.TryParse(Text(key),null,System.Globalization.DateTimeStyles.RoundtripKind,out var result)?result:null;
            var entity=Text("entity");var id=Text("id");var permissions=authentication.Effective(user);
            if(!permissions.GetValueOrDefault("system.audit"))
            {
                if(string.IsNullOrWhiteSpace(id)||!Guid.TryParse(id,out _))throw new UnauthorizedAccessException();
                var key=entity switch {"Pedido"=>"view.orders","Caixa"=>"view.closed","Lançamento"=>"view.cash",_=>"system.audit"};authentication.Require(user,key);
            }
            // Desempenho: pesquisa sobre a auditoria resumida (sem os textos antes/depois, que somam centenas de MB). Histórico de um registro
            // (id informado, poucos eventos) volta completo; na pesquisa geral o detalhe busca o evento completo em /audit/entry.
            var rows=AuditQuery.Run(store.LoadForAudit(),new(Date("from"),Date("to"),Text("user"),Text("module"),Text("action"),entity,id));
            if(!string.IsNullOrWhiteSpace(id)){var full=store.AuditEntriesFull(rows.Select(x=>x.Id)).ToDictionary(x=>x.Id);rows=rows.Select(x=>full.GetValueOrDefault(x.Id)??x).ToList();}
            return Results.Ok(rows);
        });
        app.MapGet("/audit/entry",(HttpContext c,Guid id)=>{authentication.Require((UserIdentity)c.Items["identity"]!,"system.audit");return store.AuditEntryFull(id) is {} entry?Results.Ok(entry):Results.NotFound();});
        app.MapGet("/auth/me",(HttpContext context)=>Results.Ok((UserIdentity)context.Items["identity"]!));
        app.MapGet("/auth/users",(HttpContext context)=>Results.Ok(authentication.Users((UserIdentity)context.Items["identity"]!)));
        app.MapPost("/auth/users",(HttpContext context,UserEdit request)=>{authentication.SaveUser((UserIdentity)context.Items["identity"]!,request,Origin(context));return Results.Ok();});
        app.MapGet("/auth/owner/configured",(HttpContext context)=>{authentication.Require((UserIdentity)context.Items["identity"]!,"system.owner");return Results.Ok(authentication.OwnerConfigured());});
        app.MapPost("/auth/owner",(HttpContext context,OwnerPinRequest request)=>{authentication.Require((UserIdentity)context.Items["identity"]!,"system.owner");authentication.OwnerAuthenticate((UserIdentity)context.Items["identity"]!,request,Origin(context));return Results.Ok();});
        app.MapPost("/auth/owner/reset",(HttpContext context,OwnerPinReset request)=>{authentication.Require((UserIdentity)context.Items["identity"]!,"system.owner");authentication.ResetOwnerPin((UserIdentity)context.Items["identity"]!,request,Origin(context));return Results.Ok();});
        app.MapPost("/auth/password",(HttpContext context,PasswordChangeRequest request)=>
        {
            try{authentication.ChangePassword((UserIdentity)context.Items["identity"]!,request.CurrentPassword,request.NewPassword,Origin(context));}
            catch(UnauthorizedAccessException ex){log?.Write("troca-senha-recusada",Origin(context));return Results.Text(ex.Message,statusCode:403);}
            log?.Write("senha-alterada",Origin(context));return Results.Ok();
        });
        try{await app.StartAsync();log?.Write("servidor-iniciado",$"Porta {port}; banco configurado disponível.");}
        catch{browserCertificate?.Dispose();backups.Dispose();await app.DisposeAsync();identity.Dispose();throw;}
        var local=allowIdentityCreation?authentication.BootstrapDevice(Environment.MachineName,"Inicialização de instalação individual/teste"):null;
        return new SharedDataServer(app,new ServerConnection(urls[0],local?.Secret??"",identity.CertificatePin){Urls=urls,DeviceId=local?.Id},identity,log){backups=backups};
    }
    // Confiabilidade (pacote 11): o endereço do celular (ex.: IP do Tailscale) só existe depois que a rede sobe.
    public static bool BrowserAddressReady(WebApiOptions options)
    {
        if(!options.Browser.Enabled||!IPAddress.TryParse(options.Browser.BindAddress,out var ip)||IPAddress.IsLoopback(ip)||ip.Equals(IPAddress.Any)||ip.Equals(IPAddress.IPv6Any))return true;
        return NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Any(a=>a.Address.Equals(ip));
    }
    static string Origin(HttpContext context)=>context.Connection.RemoteIpAddress is IPAddress ip?(ip.IsIPv4MappedToIPv6?ip.MapToIPv4():ip).ToString():"desconhecido";
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,DateTime> deniedLogged=new();
    // Registra acessos sem o código de conexão correto (no máximo uma linha por endereço a cada 10 minutos).
    static void LogDenied(PersistentServerLog? log,HttpContext context)
    {
        var origin=Origin(context);var now=DateTime.UtcNow;
        if(deniedLogged.TryGetValue(origin,out var last)&&now-last<TimeSpan.FromMinutes(10))return;
        deniedLogged[origin]=now;log?.Write("acesso-negado",$"{origin} {context.Request.Path}");
    }
    public async ValueTask DisposeAsync(){backups?.Dispose();await app.StopAsync();await app.DisposeAsync();log?.Write("servidor-encerrado","Encerramento concluído.");identity.Dispose();}
}

/// <summary>Compressão das respostas (JSON e arquivos do celular): 5 a 10 vezes menos bytes na rede e no Tailscale.
/// Rotas que carregam tokens ou CSRF (/auth, /devices, /browser, autorização de recibo) ficam sem compressão (BREACH).</summary>
public static class ResponseCompressionSetup
{
    public static void Configure(IServiceCollection services)
    {
        services.AddResponseCompression(options=>
        {
            options.EnableForHttps=true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes=ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json","application/manifest+json","image/svg+xml"]);
        });
    }
    public static bool Compressible(PathString path)
        => !(path.StartsWithSegments("/auth")||path.StartsWithSegments("/devices")||path.StartsWithSegments("/browser")||path.StartsWithSegments("/receipts/authorize"));
    public static void Use(WebApplication app)=>app.UseWhen(c=>Compressible(c.Request.Path),branch=>branch.UseResponseCompression());
}




