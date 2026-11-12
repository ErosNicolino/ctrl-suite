using System.Diagnostics;
using System.Reflection;
using GraficaCaixa;
using Microsoft.Data.Sqlite;

// Medição (não é teste): tempo de cada etapa de uma gravação do servidor, numa CÓPIA do banco informado.
// Uso: dotnet run --project IntegrityTests -- --write-perf "<caminho do caixa_dados.db>"
public static class WritePerfProbe
{
    public static void Run(string source)
    {
        var dir=Path.Combine(Path.GetTempPath(),"erpp"+Guid.NewGuid().ToString("N")[..6]);Directory.CreateDirectory(dir);
        try
        {
            using(var src=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=source,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            using(var dst=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(dir,"caixa_dados.db"),Pooling=false}.ToString())){src.Open();dst.Open();src.BackupDatabase(dst);}
            var store=new DataStore(dir);const BindingFlags f=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
            var read=typeof(DataStore).GetMethod("ReadForWrite",f)!;var normalize=typeof(DataStore).GetMethod("Normalize",f)!;
            var save=typeof(DataStore).GetMethods(f).Single(m=>m.Name=="SaveUnsafe");
            var who=new UserIdentity(Guid.NewGuid(),"Medição","medicao","Administrador",Guid.NewGuid());
            for(var round=0;round<4;round++)
            {
                var sw=Stopwatch.StartNew();long last=0;var line=$"rodada {round}:";
                void Lap(string name){var now=sw.ElapsedMilliseconds;line+=$" {name} {now-last}";last=now;}
                var current=(AppData)read.Invoke(store,null)!;Lap("ler");
                var next=ServerAuthorization.CloneForWrite(current);Lap("copiar");
                ServerAuthorization.ValidateAndAudit(current,next,who,new Dictionary<string,bool>());Lap("validar");
                next=(AppData)normalize.Invoke(null,[next])!;Lap("normalizar");
                FinancialIntegrity.ValidateTransition(current,next);Lap("integridade");
                save.Invoke(store,[next,null,"","",null]);Lap("gravar");
                Console.WriteLine(line+$" | total {sw.ElapsedMilliseconds} ms");
            }
        }
        finally{SqliteConnection.ClearAllPools();try{Directory.Delete(dir,true);}catch(IOException){}}
    }
}
