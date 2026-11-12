namespace GraficaCaixa;

static class DataDirectoryResolverTests
{
    static int passed;
    static void Check(bool condition,string name){if(!condition)throw new Exception("FALHOU: "+name);Console.WriteLine("OK  "+name);passed++;}
    static void Fails(Action action,string name,string text)
    {
        try{action();throw new Exception("FALHOU (não lançou): "+name);}
        catch(InvalidOperationException ex){Check(ex.Message.Contains(text,StringComparison.OrdinalIgnoreCase),name);}
    }

    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"erp-data-path-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var legacy=Path.Combine(root,"Legado");
            var missingConfig=Path.Combine(root,"sem-config","servidor.json");
            var a=DataDirectoryResolver.ResolveForPrincipal(legacy,missingConfig);
            Check(!a.IsExplicit&&a.DataDirectory==Path.GetFullPath(legacy),"A: sem configuração usa caminho legado");

            var configured=Path.Combine(root,"Pasta com espaços","Dados");Directory.CreateDirectory(configured);File.WriteAllText(Path.Combine(configured,"caixa_dados.db"),"artificial");
            var config=Path.Combine(root,"Configuracao","servidor.json");DataDirectoryResolver.Save(config,new ServerConfiguration{DataDirectory=configured});
            var b=DataDirectoryResolver.ResolveForPrincipal(legacy,config);
            Check(b.IsExplicit&&b.DataDirectory==Path.GetFullPath(configured),"B/K: configuração válida suporta caminho com espaços");

            File.WriteAllText(config,"{ inválido");
            Check(DataDirectoryResolver.ResolveForMode(2,"",legacy,config)==null,"C: cliente remoto não acessa DataDirectory");

            DataDirectoryResolver.Save(config,new ServerConfiguration{DataDirectory=Path.Combine(root,"inexistente")});
            Fails(()=>DataDirectoryResolver.ResolveForPrincipal(legacy,config),"D: caminho inexistente falha controlada","não criará uma nova base");

            var withoutDatabase=Path.Combine(root,"SemBanco");Directory.CreateDirectory(withoutDatabase);
            Fails(()=>new DataStore(withoutDatabase,true),"E: banco esperado ausente não é criado","não criará uma nova base");
            Check(!File.Exists(Path.Combine(withoutDatabase,"caixa_dados.db")),"E: nenhuma base vazia criada");

            Fails(()=>DataDirectoryResolver.ValidateExplicitDirectory(@"\\servidor\pasta\Dados"),"F: caminho UNC rejeitado","UNC");
            Check(DataDirectoryResolver.ValidateExplicitDirectory(configured)==Path.GetFullPath(configured),"G: caminho local válido aceito");

            File.WriteAllText(config,"{ inválido");
            Fails(()=>DataDirectoryResolver.ResolveForPrincipal(legacy,config),"H: configuração inválida não faz fallback inseguro","não usará o caminho legado");

            var individual=Path.Combine(root,"Individual");
            var i=DataDirectoryResolver.ResolveForMode(0,individual,legacy,config)!;
            var individualStore=new DataStore(i.DataDirectory);individualStore.EnsureInitialized();
            Check(File.Exists(individualStore.DatabasePath),"I: uso individual preserva criação autorizada");

            DataDirectoryResolver.Save(config,new ServerConfiguration{DataDirectory=configured});
            var j=DataDirectoryResolver.ResolveForPrincipal(legacy,config);
            Check(j.ConfigurationPath==Path.GetFullPath(config)&&j.DataDirectory==Path.GetFullPath(configured),"J: configuração serializa e carrega corretamente");
            Check(j.DataDirectory==Path.GetFullPath(configured)&&j.DataDirectory!=Path.GetFullPath(legacy),"L: caminho explícito independe do fallback em LOCALAPPDATA");
            Console.WriteLine($"TOTAL FASE 5A: {passed} testes aprovados");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root,true);
        }
    }
}
