using GraficaCaixa;
using System.Diagnostics;

// Fila da trava de dados (2026.10.21.2): operações simultâneas no mesmo processo continuam uma de cada vez,
// todas terminam e quem espera é chamado logo que a anterior acaba (antes cada espera dormia 100 ms).
public static class LockQueueTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"cacp-lock-queue-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new DataStore(folder);store.EnsureInitialized();store.Load();
            var inside=0;var overlap=false;var done=0;
            void Work(){store.Update(d=>{if(Interlocked.Increment(ref inside)>1)overlap=true;Thread.Sleep(15);Interlocked.Decrement(ref inside);return 0;});Interlocked.Increment(ref done);}
            var serial=Stopwatch.StartNew();for(var i=0;i<4;i++)Work();serial.Stop();
            var parallel=Stopwatch.StartNew();Task.WaitAll(Enumerable.Range(0,8).Select(_=>Task.Run(Work)).ToArray());parallel.Stop();
            Check(done==12,"todas as operações terminaram");
            Check(!overlap,"nunca duas operações ao mesmo tempo");
            var perOperation=serial.Elapsed.TotalMilliseconds/4;
            Console.WriteLine($"   série {perOperation:0} ms por operação; 8 simultâneas {parallel.ElapsedMilliseconds} ms");
            Check(parallel.Elapsed.TotalMilliseconds<8*perOperation+400,"simultâneas sem espera extra de 100 ms por operação");
            var other=new DataStore(folder);Check(other.Load()!=null,"segunda instância no mesmo banco usa a mesma fila");
            Console.WriteLine($"LockQueueTests: {passed} verificações OK");
        }
        finally{try{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}catch{}}
    }
}
