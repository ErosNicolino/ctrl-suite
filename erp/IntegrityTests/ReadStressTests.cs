using GraficaCaixa;
using Microsoft.Data.Sqlite;

// Leituras sem trava (2026.10.21.3): gravações e leituras ao mesmo tempo. Cada leitura tem de ser uma foto coerente do banco
// (cada gravação cria 1 pedido e soma 1 na versão), a leitura não pode gravar, as taxas finais são as da última gravação
// e "LeiturasSemTrava": false volta ao caminho antigo.
public static class ReadStressTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"cacp-read-stress-"+Guid.NewGuid().ToString("N"));
        var before=DataStore.LockFreeReads;
        try
        {
            DataStore.LockFreeReads=true;
            var store=new DataStore(folder);store.EnsureInitialized();store.Update(d=>{d.ScheduledOrders.Add(new ScheduledOrder{CustomerName="Base",Description="Item",Stage="Produção",DueDate=DateTime.Today.AddDays(3),OrderTotal=10,OriginalTotal=10});return 0;});
            (long Version,long Orders) Snapshot()=>store.ReadWork(c=>{long S(string sql){using var cmd=c.CreateCommand();cmd.CommandText=sql;return Convert.ToInt64(cmd.ExecuteScalar());}var v=S("SELECT value FROM metadata WHERE key='data_version'");Thread.Sleep(2);return (v,S("SELECT COUNT(*) FROM scheduled_orders"));});
            var first=Snapshot();var offset=first.Orders-first.Version;
            const int writes=25;var writing=true;var reads=0;var full=0;var incoherent=0;var errors=new List<string>();decimal lastRate=0;
            var readers=Enumerable.Range(0,4).Select(n=>Task.Run(()=>{
                while(Volatile.Read(ref writing))
                {
                    try
                    {
                        var s=Snapshot();if(s.Orders-s.Version!=offset)Interlocked.Increment(ref incoherent);Interlocked.Increment(ref reads);
                        if(n==0){var d=store.LoadForAudit();if(d.ScheduledOrders.Count-d.DataVersion!=offset)Interlocked.Increment(ref incoherent);Interlocked.Increment(ref full);}
                    }
                    catch(Exception ex){lock(errors)errors.Add(ex.GetType().Name+": "+ex.Message);}
                }
            })).ToArray();
            for(var i=0;i<writes;i++){var rate=i%2==0?1.25m:0.75m;store.Update(d=>{d.ScheduledOrders.Add(new ScheduledOrder{CustomerName="Cliente "+i,Description="Item",Stage="Produção",DueDate=DateTime.Today.AddDays(3),OrderTotal=10,OriginalTotal=10});d.PaymentRates["PIX"]=rate;return 0;});lastRate=rate;}
            Volatile.Write(ref writing,false);Task.WaitAll(readers);
            Console.WriteLine($"   {writes} gravações, {reads} leituras simples e {full} leituras completas ao mesmo tempo");
            Check(errors.Count==0,"nenhuma leitura falhou durante as gravações"+(errors.Count>0?": "+errors[0]:""));
            Check(reads>writes&&full>0,"leituras rodaram enquanto as gravações aconteciam");
            Check(incoherent==0,"toda leitura é uma foto coerente (pedidos e versão do mesmo instante)");
            var end=Snapshot();Check(end.Orders-end.Version==offset&&end.Version==first.Version+writes,"estado final com todas as gravações");
            store.LoadForAudit();Check(PaymentPolicy.Rate("PIX")==lastRate,"taxas finais são as da última gravação");
            var blocked=false;try{store.ReadWork(c=>{using var cmd=c.CreateCommand();cmd.CommandText="UPDATE metadata SET value='x' WHERE key='improvement_notes'";return cmd.ExecuteNonQuery();});}catch(SqliteException){blocked=true;}
            Check(blocked,"leitura sem trava não consegue gravar");
            DataStore.LockFreeReads=false;var old=Snapshot();Check(old.Orders-old.Version==offset,"chave desligada volta ao caminho com trava");
            Console.WriteLine($"ReadStressTests: {passed} verificações OK");
        }
        finally{DataStore.LockFreeReads=before;try{SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}catch{}}
    }
}
