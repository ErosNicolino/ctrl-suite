namespace GraficaCaixa;

// Only concurrent reads are shared. No completed response survives a session or permission change.
public sealed class RemoteQueryFlight
{
    sealed class Flight
    {
        public readonly CancellationTokenSource Cancellation=new();
        public Task<object> Task=null!;
        public int Consumers;
    }
    readonly object gate=new();
    readonly Dictionary<(Type,string),Flight> flights=[];
    public async Task<T> ReadAsync<T>(string path,Func<CancellationToken,Task<T>> read,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Flight flight;var key=(typeof(T),path);
        lock(gate)
        {
            if(!flights.TryGetValue(key,out flight!))
            {
                flight=new();flights.Add(key,flight);
                flight.Task=Task.Run(async()=> (object)(await read(flight.Cancellation.Token).ConfigureAwait(false))!);
            }
            flight.Consumers++;
        }
        try{return (T)await flight.Task.WaitAsync(cancellation).ConfigureAwait(false);}
        finally
        {
            lock(gate)
            {
                if(--flight.Consumers==0)
                {
                    if(flights.TryGetValue(key,out var current)&&ReferenceEquals(current,flight))flights.Remove(key);
                    if(!flight.Task.IsCompleted)flight.Cancellation.Cancel();
                    _=flight.Task.ContinueWith(t=>{_ = t.Exception;flight.Cancellation.Dispose();},TaskScheduler.Default);
                }
            }
        }
    }
    public void Invalidate()
    {
        lock(gate){foreach(var f in flights.Values)f.Cancellation.Cancel();flights.Clear();}
    }
}
