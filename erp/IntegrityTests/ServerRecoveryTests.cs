using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using GraficaCaixa;

static class ServerRecoveryTests
{
    static int passed;
    static void Check(bool condition,string name){if(!condition)throw new Exception("FALHOU: "+name);Console.WriteLine("OK  "+name);passed++;}

    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"erp-recovery-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var erp=Path.Combine(root,"Grafica CA CP","ERP");
            var data=Path.Combine(erp,"Dados");var config=Path.Combine(erp,"Configuracao","servidor.json");
            var identity=Path.Combine(erp,"Servidor","Identidade");var logs=Path.Combine(erp,"Servidor","Logs");
            Directory.CreateDirectory(Path.Combine(erp,"Programa"));Directory.CreateDirectory(data);
            var store=new DataStore(data);store.EnsureInitialized();store.Update(d=>{d.ImprovementNotes="BANCO ARTIFICIAL FASE 6A";d.Prompt12.LoginPassword=LoginSecurity.Create(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18)));return 0;});
            var port=FreePort();
            DataDirectoryResolver.Save(config,new ServerConfiguration{DataDirectory=data,IdentityDirectory=identity,LogsDirectory=logs,Port=port});
            using(var created=ServerIdentityStore.LoadOrCreate(identity,true)){}
            var keyHash=Hash(Path.Combine(identity,ServerIdentityStore.KeyFileName));
            var certificateHash=Hash(Path.Combine(identity,ServerIdentityStore.CertificateFileName));
            using var before=ServerIdentityStore.LoadExisting(identity);var keyA=before.Key;var pinA=before.CertificatePin;

            var publishedServer=Environment.GetEnvironmentVariable("ERP_FASE6A_SERVER_EXE");
            if(!string.IsNullOrWhiteSpace(publishedServer))
            {
                var start=new System.Diagnostics.ProcessStartInfo(publishedServer){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
                start.ArgumentList.Add("--validate");start.ArgumentList.Add("--configuration");start.ArgumentList.Add(config);
                using var process=System.Diagnostics.Process.Start(start)??throw new Exception("Não foi possível iniciar o host publicado.");
                var output=process.StandardOutput.ReadToEnd();var error=process.StandardError.ReadToEnd();process.WaitForExit(30000);
                Check(process.ExitCode==0&&output.Contains("VALIDO",StringComparison.OrdinalIgnoreCase),"executável self-contained valida a restauração simulada: "+error);
            }

            var paths=DataDirectoryResolver.ResolveForServer(config);
            Check(paths.DataDirectory==data&&paths.IdentityDirectory==identity&&paths.LogsDirectory==logs,"estrutura persistente é resolvida por servidor.json");
            using(var after=ServerIdentityStore.LoadOrCreate(identity,false))
                Check(after.Key==keyA&&after.CertificatePin==pinA,"Key A, Certificado A e Pin A permanecem após restauração simulada");

            RunHost(store,identity,logs,port,keyA,pinA).GetAwaiter().GetResult();
            Check(Hash(Path.Combine(identity,ServerIdentityStore.KeyFileName))==keyHash&&Hash(Path.Combine(identity,ServerIdentityStore.CertificateFileName))==certificateHash,"host de teste não altera a identidade");
            Check(Directory.GetFiles(erp,"caixa_dados.db",SearchOption.AllDirectories).Length==1,"não surge base paralela");

            var legacy=Path.Combine(root,"identidade-legada");var migrated=Path.Combine(root,"identidade-migrada");
            using var legacyIdentity=ServerIdentityStore.LoadOrCreate(legacy,true);var legacyKey=legacyIdentity.Key;var legacyPin=legacyIdentity.CertificatePin;
            using var migratedIdentity=ServerIdentityStore.LoadOrCreate(migrated,true,legacy);
            Check(migratedIdentity.Key==legacyKey&&migratedIdentity.CertificatePin==legacyPin,"identidade legada válida é copiada sem regeneração");

            var missingRoot=Path.Combine(root,"estrutura-sem-banco");var missingData=Path.Combine(missingRoot,"Dados");Directory.CreateDirectory(missingData);
            var missingConfig=Path.Combine(missingRoot,"Configuracao","servidor.json");
            DataDirectoryResolver.Save(missingConfig,new ServerConfiguration{DataDirectory=missingData});
            try{DataDirectoryResolver.ResolveForServer(missingConfig);throw new Exception("FALHOU: ausência de banco deveria bloquear");}
            catch(InvalidOperationException ex){Check(ex.Message.Contains("não criará",StringComparison.OrdinalIgnoreCase),"banco ausente interrompe sem criar base vazia");}
            Check(!File.Exists(Path.Combine(missingData,"caixa_dados.db")),"validação não cria banco ausente");

            var partial=Path.Combine(root,"identidade-incompleta");Directory.CreateDirectory(partial);File.WriteAllText(Path.Combine(partial,ServerIdentityStore.KeyFileName),Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            try{using var _=ServerIdentityStore.LoadOrCreate(partial,true);throw new Exception("FALHOU: identidade parcial deveria bloquear");}
            catch(InvalidOperationException){Check(!File.Exists(Path.Combine(partial,ServerIdentityStore.CertificateFileName)),"identidade parcial não é sobrescrita nem regenerada");}

            var rotating=new PersistentServerLog(Path.Combine(root,"logs-rotacao"),120,3);
            for(var i=0;i<30;i++)rotating.Write("teste","mensagem controlada "+i+new string('x',30));
            Check(Directory.GetFiles(Path.GetDirectoryName(rotating.Path)!,"servidor.log*").Length<=4,"logs possuem rotação e retenção limitada");
            Console.WriteLine($"TOTAL: {passed} testes de recuperação aprovados");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try{Directory.Delete(root,true);}catch(IOException){}
        }
    }

    static async Task RunHost(DataStore store,string identity,string logs,int port,string key,string pin)
    {
        await using var server=await SharedDataServer.StartAsync(store,identity,port,logs,false);
        var connection=new ServerConnection($"https://127.0.0.1:{port}",key,pin);
        var selected=await RemoteDataClient.SelectEndpointAsync(connection,TimeSpan.FromSeconds(5));
        Check(selected.CertificatePin==pin,"PC conecta no host restaurado com o mesmo pin");var loaded=store.Load();
        Check(loaded.ImprovementNotes=="BANCO ARTIFICIAL FASE 6A","host restaurado usa somente o banco artificial configurado");
        Check(server.Connection.CertificatePin==pin,"host restaurado expõe o mesmo CertificatePin");
    }

    static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
