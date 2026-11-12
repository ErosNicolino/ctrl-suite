using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace GraficaCaixa;

public sealed record BrowserOptions
{
    public bool Enabled { get; init; }
    public int Port { get; init; } = 443;
    public string BindAddress { get; init; } = "127.0.0.1";
    public string HostName { get; init; } = "";
    public string CertificatePem { get; init; } = "";
    public string PrivateKeyPem { get; init; } = "";
    // Link "acompanhe seu pedido" (OrderTracking). Desligado por padrão.
    public TrackingOptions Acompanhamento { get; init; } = new();
    public void Validate()
    {
        if (!Enabled) return;
        if (Port is < 1 or > 65535 || !IPAddress.TryParse(BindAddress, out var ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) ||
            Uri.CheckHostName(HostName) != UriHostNameType.Dns || HostName.Contains(':') ||
            !Path.IsPathFullyQualified(CertificatePem) || !Path.IsPathFullyQualified(PrivateKeyPem))
            throw new InvalidOperationException("Browser exige endereço de escuta restrito, hostname e certificado explícitos.");
    }
    public X509Certificate2 LoadCertificate()=>LoadCertificate(CertificatePem,PrivateKeyPem);
    public X509Certificate2 LoadCertificate(string certificatePem,string privateKeyPem)
    {
        var pem = X509Certificate2.CreateFromPemFile(certificatePem, privateKeyPem);
        // Reimport for the Windows TLS provider. Never replace the desktop identity/pin.
        using (pem)
        {
            var certificate = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
            using var chain = new X509Chain();
            chain.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
            if (!certificate.HasPrivateKey || !certificate.MatchesHostname(HostName) || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow.AddDays(1) || !chain.Build(certificate))
            { certificate.Dispose(); throw new InvalidOperationException("Certificado browser sem confiança, nome, validade ou chave adequados."); }
            return certificate;
        }
    }
}

// Certificado do acesso pelo celular e do link de acompanhamento (o Tailscale emite por 90 dias). Nos últimos 30 dias o serviço pede
// um novo; só troca os arquivos depois de conferir nome, confiança, chave e validade maior. BrowserCertificateSource carrega em até 5 min.
// Falhou: o certificado atual fica, nova tentativa em 12 h, e o Início avisa (a chave StatusKey alimenta o aviso).
public sealed record BrowserCertificateStatus(DateTime? ExpiresAt,DateTime CheckedAt,DateTime? RenewedAt,string LastError);
public sealed class BrowserCertificateRenewer : IDisposable
{
    public const string StatusKey="browser_certificate_v1";
    public const int RenewDays=30;
    readonly BrowserOptions options;
    readonly Action<BrowserCertificateStatus>? publish;
    readonly Action<string,string>? log;
    readonly Action<string,string,string> issue;
    readonly Func<string,string,X509Certificate2> validate;
    readonly Timer? timer;
    DateTime nextAttempt=DateTime.MinValue;
    DateTime? renewedAt;
    string lastError="";
    public BrowserCertificateRenewer(BrowserOptions options,Action<BrowserCertificateStatus>? publish=null,Action<string,string>? log=null,Action<string,string,string>? issue=null,Func<string,string,X509Certificate2>? validate=null,bool start=true)
    {
        this.options=options;this.publish=publish;this.log=log;this.issue=issue??Tailscale;this.validate=validate??options.LoadCertificate;
        if(start)timer=new Timer(_=>{try{Check(DateTime.UtcNow);}catch(Exception ex){log?.Invoke("certificado-erro",ex.Message);}},null,TimeSpan.FromMinutes(2),TimeSpan.FromHours(1));
    }
    public BrowserCertificateStatus Check(DateTime utcNow)
    {
        DateTime? expires=null;
        try{using var current=X509CertificateLoader.LoadCertificateFromFile(options.CertificatePem);expires=current.NotAfter.ToUniversalTime();}
        catch(Exception ex) when(ex is CryptographicException or IOException or UnauthorizedAccessException){lastError="Não foi possível ler o certificado atual.";}
        if(expires!=null&&expires.Value-utcNow>TimeSpan.FromDays(RenewDays))lastError="";
        else if(utcNow>=nextAttempt)
        {
            nextAttempt=utcNow.AddHours(12);
            try{expires=Renew(expires);renewedAt=utcNow;lastError="";log?.Invoke("certificado-renovado",$"Válido até {expires:yyyy-MM-dd}.");}
            catch(Exception ex) when(ex is InvalidOperationException or CryptographicException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){lastError=ex.Message;log?.Invoke("certificado-falhou",ex.Message);}
        }
        var status=new BrowserCertificateStatus(expires,utcNow,renewedAt,lastError);publish?.Invoke(status);return status;
    }
    DateTime Renew(DateTime? expires)
    {
        var (certificate,key)=(options.CertificatePem,options.PrivateKeyPem);var (newCertificate,newKey)=(certificate+".novo",key+".novo");
        try
        {
            issue(options.HostName,newCertificate,newKey);
            DateTime until;using(var fresh=validate(newCertificate,newKey))until=fresh.NotAfter.ToUniversalTime();
            if(expires!=null&&until<=expires)throw new InvalidOperationException($"O certificado emitido não é mais novo (vale até {until:yyyy-MM-dd}).");
            // Chave e certificado precisam combinar: se a segunda troca falhar, a primeira volta.
            var oldKey=File.Exists(key)?File.ReadAllBytes(key):null;
            File.Move(newKey,key,true);
            try{File.Move(newCertificate,certificate,true);}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){if(oldKey!=null)File.WriteAllBytes(key,oldKey);throw;}
            return until;
        }
        finally{foreach(var left in new[]{newCertificate,newKey})try{if(File.Exists(left))File.Delete(left);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}}
    }
    static void Tailscale(string host,string certificate,string key)
    {
        var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Tailscale","tailscale.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        foreach(var arg in new[]{"cert","--min-validity",$"{RenewDays*24}h","--cert-file",certificate,"--key-file",key,host})start.ArgumentList.Add(arg);
        using var process=System.Diagnostics.Process.Start(start)??throw new InvalidOperationException("Não foi possível iniciar o Tailscale.");
        var error=process.StandardError.ReadToEndAsync();_=process.StandardOutput.ReadToEndAsync();
        if(!process.WaitForExit(120000)){try{process.Kill();}catch(InvalidOperationException){}throw new InvalidOperationException("O Tailscale não respondeu em 2 minutos.");}
        if(process.ExitCode!=0){var text=error.GetAwaiter().GetResult().Trim();throw new InvalidOperationException("Tailscale recusou: "+(text.Length>200?text[..200]:text));}
    }
    public void Dispose()=>timer?.Dispose();
}

public sealed class BrowserCertificateSource : IDisposable
{
    readonly BrowserOptions options;
    readonly object gate=new();
    readonly List<X509Certificate2> certificates=[];
    X509Certificate2 current;
    DateTime checkedAt=DateTime.UtcNow;
    public BrowserCertificateSource(BrowserOptions options) {this.options=options;current=options.LoadCertificate();certificates.Add(current);}
    public X509Certificate2 Select(string? name)
    {
        lock(gate)
        {
            if(!string.Equals(name,options.HostName,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("SNI browser inválido.");
            if(DateTime.UtcNow-checkedAt>TimeSpan.FromMinutes(5))
            {
                checkedAt=DateTime.UtcNow;
                try {var candidate=options.LoadCertificate();if(candidate.Thumbprint!=current.Thumbprint){certificates.Add(candidate);current=candidate;}else candidate.Dispose();}
                catch(Exception) {if(current.NotAfter.ToUniversalTime()<=DateTime.UtcNow)throw;}
            }
            if(current.NotAfter.ToUniversalTime()<=DateTime.UtcNow)throw new InvalidOperationException("Certificado browser expirado.");
            return current;
        }
    }
    public void Dispose(){lock(gate)foreach(var cert in certificates)cert.Dispose();}
}

// Transport adapter for the SAME device and session model from 7C.
// Cookie stores a protected device credential and an opaque 7C session ID.
// Rotating access/refresh tokens stay in a protected server-side envelope.
public sealed class BrowserGateway
{
    // Comandos POST liberados além dos de pedido: repasse da taxa e Novo lançamento. Origem, CSRF e sessão são exigidos do mesmo jeito.
    public static bool IsSafeCommand(string method,string path)=>method=="POST"&&path is "/api/v1/operacao-pedidos/repasse" or "/api/v1/lancamentos";
    public const string Version = "2026.11.11.1";
    const string DeviceCookie = "__Host-cacp-device", SessionCookie = "__Host-cacp-session";
    readonly BrowserOptions options;
    readonly IdentitySecurity security;
    readonly IDataProtector protector;
    readonly IAntiforgery antiforgery;
    readonly string vault;
    readonly SemaphoreSlim credentialGate = new(1, 1);
    readonly Dictionary<string, (byte[] Data, string Mime)> assets = new(StringComparer.Ordinal);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    // Acompanhamento do pedido: chave do servidor (null = desligado), leitura do pedido e limite de acessos (120 por minuto, no total).
    readonly byte[]? trackingKey;
    readonly Func<Guid?, (ScheduledOrder? Order, CompanyProfile Company)>? trackingLookup; readonly Func<long, IEnumerable<Guid>> trackingIdsByNumber = _ => [];
    static readonly string TrackingStyleHash = "'sha256-" + Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(OrderTracking.Style))) + "'";
    readonly object trackingGate = new(); DateTime trackingWindow = DateTime.UtcNow; int trackingCount;
    public bool TrackingEnabled => trackingKey != null && trackingLookup != null;
    public string? TrackingUrl(Guid orderId, long orderNumber)
    {
        if (trackingKey == null) return null;
        var configured = options.Acompanhamento.Endereco.Trim().TrimEnd('/');
        var root = configured.Length > 0 ? configured : $"https://{options.HostName}{(options.Port == 443 ? "" : ":" + options.Port)}";
        return root + "/p/" + OrderTracking.ShortToken(trackingKey, orderNumber, orderId);
    }
    async Task ServeTracking(HttpContext c, string token, bool onBrowserPort)
    {
        if (!TrackingEnabled || !onBrowserPort || !c.Request.IsHttps || c.Request.Method != "GET" && c.Request.Method != "HEAD") { c.Response.StatusCode = 404; return; }
        var publicHost = Uri.TryCreate(options.Acompanhamento.Endereco, UriKind.Absolute, out var configured) ? configured.Host : "";
        var host = c.Request.Host.Host;
        if (!string.Equals(host, options.HostName, StringComparison.OrdinalIgnoreCase) && !(publicHost.Length > 0 && string.Equals(host, publicHost, StringComparison.OrdinalIgnoreCase))) { c.Response.StatusCode = 421; return; }
        lock (trackingGate) { if (DateTime.UtcNow - trackingWindow > TimeSpan.FromMinutes(1)) { trackingWindow = DateTime.UtcNow; trackingCount = 0; } if (++trackingCount > 120) { c.Response.StatusCode = 429; return; } }
        // Logotipo da página (o mesmo do celular): só a imagem, sem nada do pedido; pode ficar em cache.
        if (token is OrderTracking.LogoFile or OrderTracking.PreviewImageFile)
        {
            var (bytes, mime) = assets[token == OrderTracking.LogoFile ? "/app/logotipo_2026.svg" : "/app/brand.png"];
            c.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'"; c.Response.Headers.CacheControl = "public, max-age=86400";
            c.Response.ContentType = mime; c.Response.ContentLength = bytes.Length;
            if (c.Request.Method == "GET") await c.Response.Body.WriteAsync(bytes, c.RequestAborted);
            return;
        }
        c.Response.Headers["Content-Security-Policy"] = $"default-src 'none'; style-src {TrackingStyleHash}; img-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
        c.Response.Headers.CacheControl = "no-store"; c.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        var (order, company) = trackingLookup!(OrderTracking.Read(trackingKey!, token, trackingIdsByNumber));
        var visible = order != null && OrderTracking.Visible(order, DateTime.Now, options.Acompanhamento.DiasDepoisDeEntregue);
        c.Response.StatusCode = visible ? 200 : 404; c.Response.ContentType = "text/html; charset=utf-8";
        if (c.Request.Method == "GET") await c.Response.WriteAsync(visible ? OrderTracking.Page(order!, company, options.Acompanhamento.MostrarSaldo, "https://" + c.Request.Host.Value) : OrderTracking.NotFoundPage(company), c.RequestAborted);
    }
    static readonly Dictionary<string, string> AssetTypes = new()
    {
        ["index.html"]="text/html; charset=utf-8",["app.css"]="text/css; charset=utf-8",["app.js"]="text/javascript; charset=utf-8",
        ["order-editor.js"]="text/javascript; charset=utf-8",["order-finance.js"]="text/javascript; charset=utf-8", ["sale-editor.js"]="text/javascript; charset=utf-8",
        ["operational.js"]="text/javascript; charset=utf-8",["actions.js"]="text/javascript; charset=utf-8",["tools.js"]="text/javascript; charset=utf-8",
        ["sw.js"]="text/javascript; charset=utf-8",["manifest.webmanifest"]="application/manifest+json",["brand.png"]="image/png",["logotipo_2026.svg"]="image/svg+xml",
        ["icon-192.png"]="image/png",["icon-512.png"]="image/png",["icon-maskable-512.png"]="image/png",["apple-touch-icon.png"]="image/png"
    };
    public static void Configure(WebApplicationBuilder builder, string identityFolder)
    {
        builder.Services.AddDataProtection().SetApplicationName("GraficaCACP.Browser7C")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(identityFolder,"BrowserKeys"))).ProtectKeysWithDpapi();
        builder.Services.AddAntiforgery(o => { o.HeaderName="X-CACP-CSRF"; o.Cookie.Name="__Host-cacp-csrf"; o.Cookie.SecurePolicy=CookieSecurePolicy.Always; o.Cookie.SameSite=SameSiteMode.Strict; o.Cookie.HttpOnly=true; o.Cookie.Path="/"; });
    }
    public BrowserGateway(WebApplication app, BrowserOptions options, IdentitySecurity security, string identityFolder, Func<Guid?, (ScheduledOrder? Order, CompanyProfile Company)>? trackingLookup = null, Func<long, IEnumerable<Guid>>? trackingIdsByNumber = null)
    {
        this.options=options; this.security=security;
        if(options.Acompanhamento.Ativo&&trackingLookup!=null){trackingKey=OrderTracking.LoadOrCreateKey(identityFolder);this.trackingLookup=trackingLookup;if(trackingIdsByNumber!=null)this.trackingIdsByNumber=trackingIdsByNumber;}
        // QR da ficha do pedido aponta para o pedido no celular (mesmo endereço e porta deste acesso).
        ReportWriter.OrderLinkBase=$"https://{options.HostName}{(options.Port==443?"":":"+options.Port)}/app/#pedidos/";
        protector=app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("browser-transport-v1");
        antiforgery=app.Services.GetRequiredService<IAntiforgery>();
        vault=Path.Combine(identityFolder,"BrowserCredentials"); Directory.CreateDirectory(vault);
        foreach (var (name,mime) in AssetTypes)
        {
            using var stream=typeof(BrowserGateway).Assembly.GetManifestResourceStream("CacpPwa."+name) ?? throw new InvalidOperationException("PWA asset ausente: "+name);
            using var memory=new MemoryStream(); stream.CopyTo(memory); assets.Add("/app/"+name,(memory.ToArray(),mime));
        }
        // Housekeeping only removes expired transport envelopes; authorization always comes from 7C.
        foreach(var file in Directory.EnumerateFiles(vault,"*.protected"))
            if (File.GetLastWriteTimeUtc(file)<DateTime.UtcNow.AddDays(-31)) File.Delete(file);
    }
    string? Unprotect(string? value)
    { try { return value is {Length:>0 and <4096}?protector.Unprotect(value):null; } catch(CryptographicException) { return null; } }
    static CookieOptions Cookie(DateTimeOffset expiry) => new() { Secure=true,HttpOnly=true,SameSite=SameSiteMode.Strict,Path="/",Expires=expiry,IsEssential=true };
    void ClearSession(HttpContext c) => c.Response.Cookies.Delete(SessionCookie,Cookie(DateTimeOffset.UnixEpoch));
    void SetSession(HttpContext c,SessionTokens tokens) => c.Response.Cookies.Append(SessionCookie,protector.Protect(tokens.User.SessionId.ToString()),Cookie(tokens.AbsoluteExpiresAt));
    string Envelope(Guid id)=>Path.Combine(vault,id.ToString("N")+".protected");
    void Save(SessionTokens tokens)
    {
        var path=Envelope(tokens.User.SessionId); var temp=path+".tmp";
        File.WriteAllText(temp,protector.Protect(JsonSerializer.Serialize(tokens,Json)));File.Move(temp,path,true);
    }
    SessionTokens? Load(Guid id)
    {
        var path=Envelope(id); if(!File.Exists(path))return null;
        try { return JsonSerializer.Deserialize<SessionTokens>(protector.Unprotect(File.ReadAllText(path)),Json); }
        catch(CryptographicException) { return null; } catch(JsonException) { return null; }
    }
    async Task<SessionTokens?> Resolve(HttpContext c,Guid device)
    {
        if(!Guid.TryParse(Unprotect(c.Request.Cookies[SessionCookie]),out var id))return null;
        await credentialGate.WaitAsync(c.RequestAborted);
        try
        {
            return await Task.Run(() =>
            {
                var tokens=Load(id); if(tokens==null||tokens.User.DeviceId!=device)return null;
                if(tokens.ExpiresAt>DateTimeOffset.UtcNow.AddSeconds(30) && security.Authenticate(tokens.AccessToken,device)!=null)
                    return tokens;
                var renewed=security.Refresh(tokens.RefreshToken,Origin(c),device);
                if(renewed!=null)Save(renewed); else File.Delete(Envelope(id));
                return renewed;
            },c.RequestAborted);
        }
        finally { credentialGate.Release(); }
    }
    static string Origin(HttpContext c)=>c.Connection.RemoteIpAddress?.ToString()??"browser";
    static void Headers(HttpContext c)
    {
        c.Response.Headers.CacheControl="no-store";
        c.Response.Headers["X-Content-Type-Options"]="nosniff";
        c.Response.Headers["X-Frame-Options"]="DENY";
        c.Response.Headers["Referrer-Policy"]="no-referrer";
        c.Response.Headers["Permissions-Policy"]="camera=(), microphone=(), geolocation=(), payment=()";
        c.Response.Headers["Content-Security-Policy"]="default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; font-src 'self'; manifest-src 'self'; worker-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'; object-src 'none'";
        c.Response.Headers["Strict-Transport-Security"]="max-age=31536000";
    }
    public async Task Invoke(HttpContext c,RequestDelegate next)
    {
        var path=c.Request.Path.Value??"";
        var onBrowserPort=c.Connection.LocalPort==options.Port;
        var browserRoute=path.StartsWith("/browser/",StringComparison.Ordinal)||path.StartsWith("/app",StringComparison.Ordinal);
        var cookieRequest=c.Request.Cookies.ContainsKey(DeviceCookie)||c.Request.Cookies.ContainsKey(SessionCookie);
        if(!onBrowserPort&&!browserRoute&&!cookieRequest){await next(c);return;}
        Headers(c);
        // Página pública do acompanhamento (só existe quando ligada; ver ServeTracking). Antes da conferência do endereço do celular,
        // porque ela também aceita o endereço público configurado.
        if(path.StartsWith("/p/",StringComparison.Ordinal)){await ServeTracking(c,path[3..],onBrowserPort);return;}
        if(!onBrowserPort||!c.Request.IsHttps||!string.Equals(c.Request.Host.Host,options.HostName,StringComparison.OrdinalIgnoreCase)||c.Request.Host.Port!=options.Port&&!(options.Port==443&&c.Request.Host.Port==null))
        { c.Response.StatusCode=421;return; }
        try
        {
            if(path.StartsWith("/app",StringComparison.Ordinal))
            {
                if(c.Request.Method!="GET"&&c.Request.Method!="HEAD"){c.Response.StatusCode=405;return;}
                if(path=="/app"){c.Response.Redirect("/app/");return;}
                var key=path=="/app/"?"/app/index.html":path;
                if(!assets.TryGetValue(key,out var asset)){c.Response.StatusCode=404;return;}
                c.Response.ContentType=asset.Mime;
                // Explicit public shell only, conditional revalidation for version changes.
                c.Response.Headers.CacheControl="no-cache";
                if(key=="/app/sw.js")c.Response.Headers["Service-Worker-Allowed"]="/app/";
                if(c.Request.Method=="GET")await c.Response.Body.WriteAsync(asset.Data,c.RequestAborted);
                return;
            }
            if(path=="/browser/bootstrap"&&c.Request.Method=="GET")
            {
                var secret=Unprotect(c.Request.Cookies[DeviceCookie]);var device=security.AuthenticateDevice(secret);
                var tokens=device.HasValue?await Resolve(c,device.Value):null;
                if(tokens==null)ClearSession(c);
                await Results.Json(new {version=Version,paired=device.HasValue,tracking=TrackingEnabled,user=tokens?.User,permissions=tokens==null?null:security.Effective(tokens.User),csrf=antiforgery.GetAndStoreTokens(c).RequestToken},Json).ExecuteAsync(c);return;
            }
            if(path.StartsWith("/browser/",StringComparison.Ordinal))
            {
                if(c.Request.Method!="POST"){c.Response.StatusCode=404;return;}
                var origin=$"https://{c.Request.Host}";
                if(c.Request.Headers.Origin.ToString()!=origin||c.Request.Headers["Sec-Fetch-Site"].ToString() is "cross-site" or "same-site")
                {c.Response.StatusCode=403;return;}
                if(!c.Request.HasJsonContentType()){c.Response.StatusCode=415;return;}
                var limit=c.Features.Get<IHttpMaxRequestBodySizeFeature>();if(limit is {IsReadOnly:false})limit.MaxRequestBodySize=8192;
                if(c.Request.ContentLength>8192){c.Response.StatusCode=413;return;}
                await antiforgery.ValidateRequestAsync(c);
                await credentialGate.WaitAsync(c.RequestAborted);
                try { await AuthPost(c,path); } finally { credentialGate.Release(); }
                return;
            }
            // Explicit 8C/8D command allowlist. Every write requires origin, CSRF, session and domain authorization.
            var safePatch=c.Request.Method=="PATCH"&&System.Text.RegularExpressions.Regex.IsMatch(path,@"^/api/v1/(pedidos/[a-fA-F0-9-]{36}/(observacao|etapa)|clientes/[a-fA-F0-9-]{36}/basico)$");
            var safeOrderWrite=(c.Request.Method=="POST"&&System.Text.RegularExpressions.Regex.IsMatch(path,@"^/api/v1/pedidos(/previa|/[a-fA-F0-9-]{36}/(previa|recebimentos(/previa)?|entrega))?$")) || (c.Request.Method=="PATCH"&&System.Text.RegularExpressions.Regex.IsMatch(path,@"^/api/v1/pedidos/[a-fA-F0-9-]{36}$"));
            var safePaymentSuggestion=IsSafeCommand(c.Request.Method,path);
            if(!path.StartsWith("/api/v1/",StringComparison.Ordinal)||(c.Request.Method!="GET"&&!safePatch&&!safeOrderWrite&&!safePaymentSuggestion))
            {c.Response.StatusCode=403;return;}
            if(safePatch||safeOrderWrite||safePaymentSuggestion)
            {
                if(c.Request.Headers.Origin.ToString()!=$"https://{c.Request.Host}"||c.Request.Headers["Sec-Fetch-Site"].ToString() is "cross-site" or "same-site"){c.Response.StatusCode=403;return;}
                if(!c.Request.HasJsonContentType()){c.Response.StatusCode=415;return;}
                await antiforgery.ValidateRequestAsync(c);
            }
            var deviceSecret=Unprotect(c.Request.Cookies[DeviceCookie]);var deviceId=security.AuthenticateDevice(deviceSecret);
            var session=deviceId.HasValue?await Resolve(c,deviceId.Value):null;
            if(session==null){ClearSession(c);c.Response.StatusCode=401;return;}
            c.Request.Headers["X-Device-Key"]=deviceSecret;c.Request.Headers.Authorization="Bearer "+session.AccessToken;
            if(path.StartsWith("/api/v1/documentos/",StringComparison.Ordinal))
                c.Response.OnStarting(()=>{if(c.Response.StatusCode==200)c.Response.Headers.ContentDisposition="inline; filename=documento.pdf";return Task.CompletedTask;});
            await next(c);
        }
        catch(AntiforgeryValidationException){c.Response.StatusCode=403;}
        catch(BadHttpRequestException ex){c.Response.StatusCode=ex.StatusCode;}
        catch(JsonException){c.Response.StatusCode=400;}
        catch(SessionInvalidException){ClearSession(c);c.Response.StatusCode=401;}
        catch(UnauthorizedAccessException){c.Response.StatusCode=403;}
        catch(OperationCanceledException) when(c.RequestAborted.IsCancellationRequested){}
        catch(Exception){if(!c.Response.HasStarted){c.Response.StatusCode=500;await c.Response.WriteAsync("Não foi possível concluir a consulta.");}}
    }
    async Task AuthPost(HttpContext c,string path)
    {
        var secret=Unprotect(c.Request.Cookies[DeviceCookie]);var device=security.AuthenticateDevice(secret);
        if(path=="/browser/pair")
        {
            if(device!=null){c.Response.StatusCode=409;return;}
            var input=await c.Request.ReadFromJsonAsync<BrowserPair>(Json,c.RequestAborted);
            if(input==null||input.Name is not {Length:>0 and <=80}||input.Code is not {Length:>0 and <=4096}){c.Response.StatusCode=400;return;}
            string ticket=input.Code.Trim();
            if(ticket.Length!=64)
            { try { var legacy=ServerConnection.Parse(ticket);ticket=legacy.Key; } catch(InvalidOperationException){c.Response.StatusCode=400;return;} }
            var paired=await Task.Run(()=>security.PairDevice(new(ticket,input.Name.Trim()),Origin(c)),c.RequestAborted);
            if(paired==null){c.Response.StatusCode=401;return;}
            c.Response.Cookies.Append(DeviceCookie,protector.Protect(paired.Secret),Cookie(DateTimeOffset.UtcNow.AddYears(1)));
            ClearSession(c);c.Response.StatusCode=204;return;
        }
        if(device==null){c.Response.StatusCode=401;return;}
        if(path=="/browser/login")
        {
            var input=await c.Request.ReadFromJsonAsync<LoginRequest>(Json,c.RequestAborted);
            if(input==null){c.Response.StatusCode=400;return;}
            // Repeated login revokes this browser's previous session; never leaves an orphan login.
            var tokens=await Task.Run(()=>security.Login(input.User,input.Password,Origin(c)+" / browser="+device,device),c.RequestAborted);
            if(tokens==null){c.Response.StatusCode=401;return;}
            if(Guid.TryParse(Unprotect(c.Request.Cookies[SessionCookie]),out var previous))
            {var old=Load(previous);if(old?.User.DeviceId==device){security.Logout(old.User,Origin(c));File.Delete(Envelope(previous));}}
            Save(tokens);SetSession(c,tokens);c.Response.StatusCode=204;return;
        }
        if(path=="/browser/logout")
        {
            if(Guid.TryParse(Unprotect(c.Request.Cookies[SessionCookie]),out var id))
            {
                var tokens=Load(id);
                if(tokens?.User.DeviceId==device)
                {
                    var actor=security.Authenticate(tokens.AccessToken,device)??security.Refresh(tokens.RefreshToken,Origin(c),device)?.User;
                    if(actor!=null)security.Logout(actor,Origin(c));
                    File.Delete(Envelope(id));
                }
            }
            ClearSession(c);c.Response.StatusCode=204;return;
        }
        c.Response.StatusCode=404;
    }
    public sealed record BrowserPair(string Code,string Name);
}
