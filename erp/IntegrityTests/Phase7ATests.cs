using GraficaCaixa;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class Phase7ATests
{
    static int passed;
    static void Pass(bool condition,string message){if(!condition)throw new Exception("FAIL "+message);passed++;Console.WriteLine("PASS "+message);}
    static void Sql(string database,string sql){using var c=new SqliteConnection($"Data Source={database};Pooling=False");c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.ExecuteNonQuery();}
    static string Value(string database,string sql){using var c=new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;return cmd.ExecuteScalar()?.ToString()??"";}
    public static void Run(string work,string snapshot)
    {RunAsync(work,snapshot).GetAwaiter().GetResult();}
    static async Task RunAsync(string work,string snapshot)
    {
        work=Path.GetFullPath(work);snapshot=Path.GetFullPath(snapshot);
        Directory.CreateDirectory(work);
        var copyFolder=Path.Combine(work,"snapshot");Directory.CreateDirectory(copyFolder);var copy=Path.Combine(copyFolder,"caixa_dados.db");File.Copy(snapshot,copy,false);
        var originalPrompt=Value(copy,"SELECT value FROM metadata WHERE key='prompt12_v1'");
        var counts=Value(copy,"SELECT (SELECT COUNT(*) FROM sales)||':'||(SELECT COUNT(*) FROM scheduled_orders)||':'||(SELECT COUNT(*) FROM cash_sessions)");
        var snapshotStore=new DataStore(copyFolder,true);var migrator=new IdentitySecurity(snapshotStore);migrator.Migrate();migrator.Migrate();
        Pass(Value(copy,"SELECT COUNT(*) FROM auth_users")=="1","migração idempotente no snapshot real; um usuário legado");
        Pass(Value(copy,"SELECT value FROM metadata WHERE key='prompt12_v1'")==originalPrompt,"metadados, PIN e senha originais preservados byte a byte");
        Pass(Value(copy,"SELECT (SELECT COUNT(*) FROM sales)||':'||(SELECT COUNT(*) FROM scheduled_orders)||':'||(SELECT COUNT(*) FROM cash_sessions)")==counts,"vendas, pedidos e caixas preservados na migração");
        var credential=JsonSerializer.Deserialize<Prompt12State>(originalPrompt)!.LoginPassword!;
        Pass(Value(copy,"SELECT hex(password_hash) FROM auth_users")==Convert.ToHexString(credential.Hash)&&Value(copy,"SELECT hex(salt) FROM auth_users")==Convert.ToHexString(credential.Salt),"hash, salt e parâmetros existentes preservados");
        var folder=Path.Combine(work,"functional");var store=new DataStore(folder);store.EnsureInitialized();const string password="Teste7A-Senha!123";
        var loginCredential=LoginSecurity.Create(password);var ownerCredential=OwnerSecurity.Create("928374");store.Update(d=>{d.Prompt12.LoginPassword=loginCredential;d.Prompt12.OwnerPin=ownerCredential;return 0;});
        var now=DateTimeOffset.UtcNow;var security=new IdentitySecurity(store,()=>now);security.Migrate();
        var session=security.Login("grafica",password,"correct")??throw new Exception("Login esperado.");Pass(true,"login correto, alias existente sem acento");
        Pass(security.Login("grafica","errada","wrong")==null&&security.Login("inexistente",password,"unknown")==null,"senha errada e usuário inexistente recusados");
        Pass(security.Authenticate(session.AccessToken)?.UserId==session.User.UserId,"identidade obtida da sessão");
        Pass(security.Authenticate(new string('0',64))==null&&security.Authenticate(session.AccessToken[..63]+(session.AccessToken[63]=='A'?'B':'A'))==null,"token inválido e adulterado recusados");
        var forged=Convert.ToHexString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(session.User with {UserId=Guid.NewGuid(),Role="Financeiro"})));Pass(security.Authenticate(forged)==null,"alteração manual de userId e role não produz token válido");
        Pass(!Value(store.DatabasePath,"SELECT access_hash||refresh_hash FROM auth_sessions").Contains(session.AccessToken)&&!Value(store.DatabasePath,"SELECT access_hash||refresh_hash FROM auth_sessions").Contains(session.RefreshToken),"somente hashes dos tokens no banco");
        now=now.AddMinutes(16);Pass(security.Authenticate(session.AccessToken)==null,"access token expirado recusado");
        var renewed=security.Refresh(session.RefreshToken,"refresh")??throw new Exception("Refresh esperado.");Pass(renewed.AccessToken!=session.AccessToken&&renewed.RefreshToken!=session.RefreshToken,"refresh válido com rotação dos dois tokens");
        Pass(security.Refresh(session.RefreshToken,"replay")==null&&security.Authenticate(renewed.AccessToken)==null,"reutilização de refresh revoga a sessão");
        session=security.Login("grafica",password,"logout")!;security.Logout(session.User,"logout");Pass(security.Authenticate(session.AccessToken)==null&&security.Refresh(session.RefreshToken,"logout")==null,"logout invalida access e refresh");
        for(var i=0;i<5;i++)security.Login("grafica","erro","bruteforce");Pass(security.Login("grafica",password,"bruteforce")==null,"cinco falhas ativam proteção existente");LoginThrottle.Reset("account:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("GRAFICA"))));
        var admin=security.Login("grafica",password,"admin")!;security.SaveUser(admin.User,new(null,"Maria","maria","Financeiro",true,"Maria-Senha!123"),"admin");
        var maria=security.Login("maria","Maria-Senha!123","maria")!;Pass(maria.User.Role=="Financeiro"&&maria.User.UserId!=admin.User.UserId,"usuários individuais e perfis existentes");
        var denied=false;try{security.SaveUser(maria.User,new(null,"Ataque","attack","Administrador",true,password),"maria");}catch(UnauthorizedAccessException){denied=true;}Pass(denied,"Financeiro não pode administrar usuários ou perfis");
        security.SaveUser(admin.User,new(maria.User.UserId,"Maria","maria","Financeiro",false,null),"admin");Pass(security.Authenticate(maria.AccessToken)==null&&security.Login("maria","Maria-Senha!123","inactive")==null,"usuário inativo e sessões anteriores recusados");
        LoginThrottle.Reset("account:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("GRAFICA"))));
        var identityFolder=Path.Combine(work,"identity");var logs=Path.Combine(work,"logs");const int port=15074;
        var server=await SharedDataServer.StartAsync(store,identityFolder,port,logs);var connection=server.Connection with {Url=$"https://127.0.0.1:{port}",Urls=null,SelectedUrl=null};
        using var handler=new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,cert,_,_)=>RemoteDataClient.CertificateMatchesPin(cert,connection.CertificatePin)};
        using var http=new HttpClient(handler){BaseAddress=new Uri(connection.Url+"/")};http.DefaultRequestHeaders.Add("X-Device-Key",connection.Key);
        async Task<SessionTokens> LoginHttp(object body){using var response=await http.PostAsJsonAsync("auth/login",body);Pass(response.IsSuccessStatusCode,"HTTPS login na API");return (await response.Content.ReadFromJsonAsync<SessionTokens>())!;}
        var httpSession=await LoginHttp(new{User="Gráfica",Password=password,Role="Financeiro",UserId=Guid.NewGuid()});Pass(httpSession.User.Role=="Administrador"&&httpSession.User.UserId==admin.User.UserId,"role e userId fornecidos no login não alteram identidade");
        http.DefaultRequestHeaders.Authorization=new("Bearer",httpSession.AccessToken);
        using(var me=await http.GetAsync("auth/me"))Pass(me.IsSuccessStatusCode&&(await me.Content.ReadFromJsonAsync<UserIdentity>())!.SessionId==httpSession.User.SessionId,"/auth/me com sessão válida");
        using(var data=await http.GetAsync("data")){var text=await data.Content.ReadAsStringAsync();Pass(data.IsSuccessStatusCode&&!text.Contains(Convert.ToBase64String(loginCredential.Hash))&&!text.Contains(Convert.ToBase64String(loginCredential.Salt))&&!text.Contains(Convert.ToBase64String(ownerCredential.Hash)),"hashes e salts nunca chegam ao cliente, incluindo /data");}
        http.DefaultRequestHeaders.Authorization=new("Bearer",connection.Key);using(var response=await http.GetAsync("auth/me"))Pass(response.StatusCode==HttpStatusCode.Unauthorized,"pairing sozinho não autentica usuário");using(var response=await http.GetAsync("data"))Pass(response.StatusCode==HttpStatusCode.Unauthorized,"pairing sozinho não acessa dados");
        http.DefaultRequestHeaders.Authorization=new("Bearer",new string('F',64));using(var response=await http.GetAsync("auth/me"))Pass(response.StatusCode==HttpStatusCode.Unauthorized,"API recusa token adulterado");
        using(var wrong=await http.PostAsJsonAsync("auth/login",new LoginRequest("grafica","wrong")))using(var unknown=await http.PostAsJsonAsync("auth/login",new LoginRequest("does-not-exist","wrong")))Pass(wrong.StatusCode==unknown.StatusCode&&await wrong.Content.ReadAsStringAsync()==await unknown.Content.ReadAsStringAsync(),"mesma mensagem externa para credenciais inválidas");
        var client=new RemoteDataClient(connection);Pass(client.VerifyLogin("grafica",password).Ok&&client.Load()!=null,"cliente centralizado PC1 e transporte PC2 compatível");
        var clientSession=client.User!;Sql(store.DatabasePath,$"UPDATE auth_sessions SET access_expires=0 WHERE id='{clientSession.SessionId}'");Pass(client.Load()!=null,"cliente renova sessão após 401 por access expirado");
        client.AuthenticateOwner("928374",false);Pass(client.OwnerConfigured(),"PIN validado no servidor sem exposição de hash");
        client.Update(d=>{d.Prompt12.Expenses.Add(new Expense{Description="Regressão proprietário"});return 0;});Pass(store.Load().Prompt12.Expenses.Any(e=>e.Description=="Regressão proprietário"),"PC1 preserva gravações da Área do Proprietário com PIN validado");
        var remoteData=client.Load();remoteData.AuditTrail.Add(new AuditEntry{Action="Teste identidade",UserName="Falsificado"});remoteData.Prompt12.LoginPassword=LoginSecurity.Create("HashAdulterado123");Pass(store.CommitRemote(new(Guid.NewGuid(),remoteData.DataVersion,remoteData,8),client.User!),"commit autenticado preserva credenciais");
        Pass(store.Load().AuditTrail.Last(x=>x.Action=="Operação remota validada").UserName==client.User!.Name && store.Load().AuditTrail.All(x=>x.Action!="Teste identidade")&&Convert.ToBase64String(store.Load().Prompt12.LoginPassword!.Hash)==Convert.ToBase64String(loginCredential.Hash),"auditoria recebe identidade confiável e ignora hash enviado pelo cliente");
        var second=new RemoteDataClient(connection);Pass(second.VerifyLogin("grafica",password).Ok&&second.User!.SessionId!=client.User!.SessionId,"dois clientes mantêm sessões independentes");
        var certPin=connection.CertificatePin;await server.DisposeAsync();server=await SharedDataServer.StartAsync(store,identityFolder,port,logs,false);
        try
        {
            http.DefaultRequestHeaders.Authorization=new("Bearer",httpSession.AccessToken);using(var response=await http.GetAsync("auth/me"))Pass(response.IsSuccessStatusCode&&server.Connection.CertificatePin==certPin,"reiniciar servidor preserva sessões e identidade HTTPS");
            var refreshed=await http.PostAsJsonAsync("auth/refresh",new RefreshRequest(httpSession.RefreshToken));Pass(refreshed.IsSuccessStatusCode,"refresh continua funcional após reinício");var refreshedTokens=(await refreshed.Content.ReadFromJsonAsync<SessionTokens>())!;refreshed.Dispose();http.DefaultRequestHeaders.Authorization=new("Bearer",refreshedTokens.AccessToken);
            using(var logout=await http.PostAsync("auth/logout",null))Pass(logout.IsSuccessStatusCode,"/auth/logout funciona");using(var reuse=await http.GetAsync("auth/me"))Pass(reuse.StatusCode==HttpStatusCode.Unauthorized,"API não reutiliza sessão após logout");using(var revoked=await http.PostAsJsonAsync("auth/refresh",new RefreshRequest(refreshedTokens.RefreshToken)))Pass(revoked.StatusCode==HttpStatusCode.Unauthorized,"refresh revogado recusado pela API");
            var httpDenied=false;try{new RemoteDataClient(connection with {Url=$"http://127.0.0.1:{port}"});}catch(InvalidOperationException){httpDenied=true;}Pass(httpDenied,"cliente recusa endpoint HTTP antes de transmitir senha");
            Pass(NetworkEndpointDiscovery.BuildUrls("192.168.0.5","100.64.10.5",5074).SequenceEqual(new[]{"https://192.168.0.5:5074","https://100.64.10.5:5074"}),"LAN e Tailscale preservam HTTPS e porta");
            client.Logout();second.Logout();
            var logText=string.Join("\n",Directory.GetFiles(logs).Select(File.ReadAllText));Pass(!logText.Contains(password)&&!logText.Contains(httpSession.AccessToken)&&!logText.Contains(httpSession.RefreshToken)&&!logText.Contains(connection.Key),"logs não contêm senhas, tokens ou pairing");
            var auditText=JsonSerializer.Serialize(store.Load().AuditTrail);Pass(!auditText.Contains(password)&&!auditText.Contains(httpSession.AccessToken)&&!auditText.Contains(httpSession.RefreshToken)&&!auditText.Contains(connection.Key),"audit trail sem segredos");
        }
        finally{await server.DisposeAsync();}
        Console.WriteLine($"FASE7A: {passed} PASS; 0 FAIL");
    }
}

