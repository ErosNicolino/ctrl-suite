using System.Text.Json;
namespace GraficaCaixa.Wpf;
internal static class BusinessUi
{
    public static async Task<T> ExecuteAsync<T>(DataStore store,string action,Guid id,object? expected,object args,Func<T> local,CancellationToken cancellation=default)
    {
        if(!store.IsRemote)return local();
        var result=await store.RemoteBusinessAsync(action,id,expected==null?null:DataStore.DesktopRevision(expected),args,cancellation);
        return result.Value is {} value&&value.ValueKind!=JsonValueKind.Null?value.Deserialize<T>()!:default!;
    }
    // Chamada de rede síncrona feita pela tela (PIN, Usuários, Permissões, Dispositivos, Auditoria): em vez de congelar,
    // a janela continua desenhando e pode ser movida; a janela ativa fica sem aceitar cliques até a resposta (evita clique duplo).
    public static void Pump(Task pending)
    {
        var app=System.Windows.Application.Current;if(app==null||!app.Dispatcher.CheckAccess())return;
        var active=app.Windows.OfType<System.Windows.Window>().FirstOrDefault(x=>x.IsActive&&x.IsEnabled);var focus=System.Windows.Input.Keyboard.FocusedElement;
        var cursor=System.Windows.Input.Mouse.OverrideCursor;var frame=new System.Windows.Threading.DispatcherFrame();
        pending.ContinueWith(_=>frame.Continue=false,TaskScheduler.Default);
        try{if(active!=null)active.IsEnabled=false;System.Windows.Input.Mouse.OverrideCursor=System.Windows.Input.Cursors.Wait;System.Windows.Threading.Dispatcher.PushFrame(frame);}
        finally{System.Windows.Input.Mouse.OverrideCursor=cursor;if(active!=null){active.IsEnabled=true;focus?.Focus();}}
    }
    public static async Task<List<Guid>> SaveOrderAsync(DataStore store,ScheduledOrder order,Guid? correctedSale=null,decimal? correctedAmount=null,string reason="",CancellationToken cancellation=default)
    {
        if(!store.IsRemote)return await Task.Run(()=>store.Update(d=>OrderOperations.Save(d,order,App.CurrentUser,correctedSale,correctedAmount,reason)),cancellation);
        var result=await store.RemoteDesktopWriteAsync("desktop/orders/save",new DesktopOrderCommand(Guid.NewGuid(),order.Number>0?order.Revision:null,order,correctedSale,correctedAmount,reason),cancellation);
        return result.ChangedSessions.ToList();
    }
}
