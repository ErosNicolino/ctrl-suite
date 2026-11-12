using GraficaCaixa;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if(args.Length==2&&args[0]=="--security7c-preflight")
{
    using var c=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=args[1],Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();
    using(var cmd=c.CreateCommand()){cmd.CommandText="PRAGMA quick_check";if(cmd.ExecuteScalar()?.ToString()!="ok")throw new InvalidDataException("Banco não íntegro.");}
    foreach(var table in new[]{"auth_users","auth_sessions","auth_role_permissions","auth_user_permissions","auth_used_refresh","audit_trail","remote_operations"}){using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@t";cmd.Parameters.AddWithValue("@t",table);if(Convert.ToInt32(cmd.ExecuteScalar())!=1)throw new InvalidOperationException("Base 7B necessária.");}
    foreach(var (table,required) in new[]{("audit_trail",new[]{"user_id","user_login","session_id"}),("remote_operations",new[]{"actor_id","payload_hash"})}){using var cmd=c.CreateCommand();cmd.CommandText=$"PRAGMA table_info({table})";using var reader=cmd.ExecuteReader();var cols=new HashSet<string>();while(reader.Read())cols.Add(reader.GetString(1));if(required.Any(x=>!cols.Contains(x)))throw new InvalidOperationException("Schema 7B incompleto.");}
    Console.WriteLine("PREFLIGHT 7C OK: base 7B íntegra; nenhuma escrita.");return;
}

if(args.Length==3&&args[0]=="--security7c-bootstrap")
{
    var principal=new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
    if(!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))throw new UnauthorizedAccessException("Administração local do PC1 obrigatória.");
    var paths=DataDirectoryResolver.ResolveForServer(args[1]);var store=new DataStore(paths.DataDirectory,true);var security=new IdentitySecurity(store);
    using var identity=ServerIdentityStore.LoadExisting(paths.IdentityDirectory);
    var credential=security.BootstrapDevice(Environment.MachineName,"Administração local Windows: "+principal.Identity?.Name);
    var endpoints=NetworkEndpointDiscovery.BuildUrls(NetworkEndpointDiscovery.GetLanAddress(),NetworkEndpointDiscovery.GetTailscaleAddress(),paths.Port);
    var connection=new ServerConnection($"https://127.0.0.1:{paths.Port}",credential.Secret,identity.CertificatePin){Urls=endpoints,DeviceId=credential.Id};
    GraficaCaixa.Wpf.ClientCredentialStore.SaveConnection(connection,Path.GetFullPath(args[2]));
    var legacy=Path.Combine(Path.GetFullPath(args[2]),"conexao.txt");if(File.Exists(legacy))File.Delete(legacy);
    Console.WriteLine("BOOTSTRAP LOCAL OK: credencial individual cifrada com DPAPI; nenhum segredo exibido.");return;
}

if(args.Length==2&&args[0]=="--phase7b-state")
{
    using var c=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=args[1],Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();
    using var hash=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
    foreach(var (table,sql) in new[]{("auth_users","SELECT id,name,login,role,active,hex(password_hash),hex(salt),iterations FROM auth_users ORDER BY id"),("remote_operations","SELECT id FROM remote_operations ORDER BY id"),("auth_role_permissions","SELECT role,permission,allowed FROM auth_role_permissions ORDER BY role,permission"),("auth_user_permissions","SELECT user_id,permission,allowed FROM auth_user_permissions ORDER BY user_id,permission"),("audit_trail","SELECT id FROM audit_trail WHERE entity_type<>'Segurança' OR action LIKE 'Usuário%' OR action IN ('Permissões alteradas','Senha redefinida') ORDER BY id")})
    {
        using var exists=c.CreateCommand();exists.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@table";exists.Parameters.AddWithValue("@table",table);if(Convert.ToInt32(exists.ExecuteScalar())==0)continue;
        using var read=c.CreateCommand();read.CommandText=sql;using var r=read.ExecuteReader();while(r.Read()){var row=Enumerable.Range(0,r.FieldCount).Select(i=>r.IsDBNull(i)?null:r.GetValue(i).ToString()).ToArray();hash.AppendData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(row)));}
    }
    Console.WriteLine(Convert.ToHexString(hash.GetHashAndReset()));return;
}
if(args.Length==3&&args[0]=="--auth-backup")
{
    using var source=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=args[1],Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
    if(File.Exists(args[2]))throw new InvalidOperationException("O destino do backup já existe.");
    using var target=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=args[2],Pooling=false}.ToString());source.Open();target.Open();source.BackupDatabase(target);Console.WriteLine("BACKUP OK");return;
}
if(args.Length==2&&args[0]=="--auth-migrate")
{
    var database=Path.GetFullPath(args[1]);if(Path.GetFileName(database)!="caixa_dados.db")throw new InvalidOperationException("Banco central esperado: caixa_dados.db.");
    new IdentitySecurity(new DataStore(Path.GetDirectoryName(database)!,true)).Migrate();Console.WriteLine("MIGRAÇÃO 7A + 7B + 7C OK — ADITIVA / IDEMPOTENTE");return;
}
if(args.Length==2&&args[0]=="--auth-preflight")
{
    using var c=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=args[1],Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();
    using(var check=c.CreateCommand()){check.CommandText="PRAGMA quick_check";if(check.ExecuteScalar()?.ToString()!="ok")throw new InvalidOperationException("Banco não íntegro.");}
    using(var table=c.CreateCommand()){table.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='auth_users'";if(Convert.ToInt64(table.ExecuteScalar())==1){using var users=c.CreateCommand();users.CommandText="SELECT COUNT(*) FROM auth_users WHERE active=1 AND role='Administrador' AND length(password_hash)=32 AND length(salt)>=16 AND iterations BETWEEN 100000 AND 2000000";if(Convert.ToInt64(users.ExecuteScalar())<1)throw new InvalidOperationException("Administrador ativo ausente.");Console.WriteLine("PREFLIGHT AUTH OK");return;}}
    using(var prompt=c.CreateCommand()){prompt.CommandText="SELECT value FROM metadata WHERE key='prompt12_v1'";var credential=System.Text.Json.JsonSerializer.Deserialize<Prompt12State>(prompt.ExecuteScalar()?.ToString()??"{}")?.LoginPassword;if(credential==null||credential.Hash.Length!=32||credential.Salt.Length<16||credential.Iterations is <100000 or >2000000)throw new InvalidOperationException("Configure a senha de acesso no ERP atual antes da migração.");}
    Console.WriteLine("PREFLIGHT AUTH OK");return;
}

var configurationPath = ArgumentValue(args, "--configuration") ?? DataDirectoryResolver.DefaultConfigurationPath;
if (args.Contains("--validate", StringComparer.OrdinalIgnoreCase))
{
    var validated = ServerRecoveryValidator.Validate(configurationPath);
    Console.WriteLine($"VALIDO: configuração={validated.ConfigurationPath}; banco={validated.DataDirectory}; identidade=presente; porta={validated.Port}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "GraficaCaixaServer");
builder.Services.AddSingleton(new ServerLaunchOptions(configurationPath));
builder.Services.AddHostedService<ServerWorker>();
await builder.Build().RunAsync();

static string? ArgumentValue(string[] args, string name)
{
    var index = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

sealed record ServerLaunchOptions(string ConfigurationPath);

sealed class ServerWorker(ServerLaunchOptions options) : BackgroundService
{
    SharedDataServer? server;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var paths = ServerRecoveryValidator.Validate(options.ConfigurationPath);
        var log = new PersistentServerLog(paths.LogsDirectory);
        try
        {
            var store = new DataStore(paths.DataDirectory, requireExistingDatabase: true);
            var configuration=System.Text.Json.JsonSerializer.Deserialize<ServerConfiguration>(await File.ReadAllTextAsync(options.ConfigurationPath,stoppingToken))!;
            DataStore.LockFreeReads=configuration.LeiturasSemTrava;if(!configuration.LeiturasSemTrava)log.Write("leituras-com-trava","LeiturasSemTrava=false: leituras esperam as gravações (comportamento anterior).");
            var webOptions=configuration.WebApi with {DocumentsDirectory=string.IsNullOrWhiteSpace(configuration.WebApi.DocumentsDirectory)?paths.ErpRoot:configuration.WebApi.DocumentsDirectory,PhotosDirectory=string.IsNullOrWhiteSpace(configuration.WebApi.PhotosDirectory)?paths.ErpRoot:configuration.WebApi.PhotosDirectory};
            // Fotos de produto: ficam em ERP\Produtos\Imagens. As gravadas na pasta de documentos do celular (até 2026.10.28.3) vêm para cá.
            try{var (moved,duplicates)=WebFiles.MigrateProductPhotos(webOptions.DocumentsDirectory,webOptions.PhotosRoot);if(moved+duplicates>0)log.Write("fotos-migradas",$"{moved} foto(s) movida(s) para {Path.Combine(webOptions.PhotosRoot,"Produtos","Imagens")}; {duplicates} nome(s) repetido(s) ficaram com um arquivo só.");}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){log.Write("fotos-migracao-adiada",ex.Message);}
            if(webOptions.Browser.Enabled)certificateRenewer=new BrowserCertificateRenewer(webOptions.Browser,status=>store.WriteMetadataValue(BrowserCertificateRenewer.StatusKey,System.Text.Json.JsonSerializer.Serialize(status)),log.Write);
            // Confiabilidade (pacote 11): depois de queda de luz o IP do Tailscale/rede pode ainda não existir.
            // Os PCs não ficam esperando: o servidor sobe sem o acesso do celular e o liga sozinho quando o endereço aparece.
            var mobileOn=false;var failures=0;var mobileFailures=0;var skipMobileUntil=DateTime.MinValue;var waitingLogged=false;
            while(!stoppingToken.IsCancellationRequested)
            {
                var ready=SharedDataServer.BrowserAddressReady(webOptions);
                var wantMobile=webOptions.Browser.Enabled&&ready&&DateTime.UtcNow>=skipMobileUntil&&mobileFailures<5;
                if(server is null||wantMobile&&!mobileOn)
                {
                    var current=wantMobile?webOptions:webOptions with {Browser=webOptions.Browser with {Enabled=false}};
                    if(server is not null){await server.DisposeAsync();server=null;}
                    try
                    {
                        server = await SharedDataServer.StartAsync(store, paths.IdentityDirectory, paths.Port, paths.LogsDirectory, allowIdentityCreation: false,webOptions:current);
                        mobileOn=current.Browser.Enabled;failures=0;
                        if(mobileOn){if(waitingLogged||mobileFailures>0)log.Write("celular-ligado","Acesso do celular disponível.");waitingLogged=false;}
                        else if(webOptions.Browser.Enabled&&!ready&&!waitingLogged){log.Write("celular-aguardando-rede",$"Endereço {webOptions.Browser.BindAddress} ainda não existe; PCs atendidos, o celular liga sozinho quando a rede subir.");waitingLogged=true;}
                    }
                    catch(Exception ex) when(ex is System.Net.Sockets.SocketException or IOException && !stoppingToken.IsCancellationRequested)
                    {
                        server=null;failures++;
                        if(current.Browser.Enabled){mobileFailures++;skipMobileUntil=DateTime.UtcNow.AddSeconds(30);log.Write("celular-falhou",$"Tentativa {mobileFailures}/5: {ex.GetType().Name}: {ex.Message}. PCs sobem sem o celular agora.");if(mobileFailures==5)log.Write("celular-desligado","Acesso do celular não pôde ser ligado; reinicie o serviço depois de conferir a rede.");continue;}
                        if(failures>60)throw;
                        log.Write("aguardando-rede",$"Tentativa {failures}: {ex.GetType().Name}: {ex.Message}. Nova tentativa em 10 s.");
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(server is null?10:30), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log.Write("falha-inicializacao", $"{ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    BrowserCertificateRenewer? certificateRenewer;
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        certificateRenewer?.Dispose();
        if (server is not null) { await server.DisposeAsync(); server = null; }
        await base.StopAsync(cancellationToken);
    }
}

static class ServerRecoveryValidator
{
    public static ServerRuntimePaths Validate(string configurationPath)
    {
        var paths = DataDirectoryResolver.ResolveForServer(configurationPath);
        if (!Directory.Exists(paths.ErpRoot)) throw new InvalidOperationException($"Estrutura ERP não encontrada em {paths.ErpRoot}.");
        if (!File.Exists(Path.Combine(paths.DataDirectory, "caixa_dados.db")))
            throw new InvalidOperationException("caixa_dados.db não foi encontrado. O serviço não criará uma base vazia.");
        using var identity = ServerIdentityStore.LoadOrCreate(paths.IdentityDirectory, allowCreate: false);
        Directory.CreateDirectory(paths.LogsDirectory);
        return paths;
    }
}
