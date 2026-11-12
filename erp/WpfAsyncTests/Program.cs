using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Text.Json;
using System.IO;

static class Program
{
    static int passed;
    static void Pass(bool value,string message){if(!value)throw new Exception("FAIL "+message);passed++;Console.WriteLine("PASS "+message);}
    static object? Field(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target);
    static void Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);
    static async Task Until(Func<bool> ready){using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));while(!ready()){timeout.Token.ThrowIfCancellationRequested();await Task.Delay(10,timeout.Token);}}
    [STAThread] static int Main(string[] args)
    {
        var folder=Path.GetFullPath(args.Length>0?args[0]:Path.Combine(Path.GetTempPath(),"erp-wpf-async-"+Guid.NewGuid().ToString("N")));if(Directory.Exists(folder)&&Directory.EnumerateFileSystemEntries(folder).Any())throw new InvalidOperationException("Use uma pasta nova e vazia de teste WPF.");Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("GRAFICA_CAIXA_PERFIL",Path.Combine(folder,"Perfil"));
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;ThemeManager.Initialize();
        var frame=new DispatcherFrame();int code=0;
        app.Dispatcher.BeginInvoke(async()=>{try{await Run(folder);}catch(Exception ex){Console.WriteLine(ex);code=1;}finally{frame.Continue=false;}});
        Dispatcher.PushFrame(frame);app.Shutdown();return code;
    }
    static async Task Run(string folder)
    {
        var data=new DataStore(Path.Combine(folder,"Dados"));data.EnsureInitialized();
        data.Update(d=>{d.Prompt12.LoginPassword=LoginSecurity.Create("Wpf8C-Sintetico!2026");d.Customers=[new(){Name="Cliente sintético WPF"}];d.ScheduledOrders=[new(){Number=1042,CustomerName="Cliente sintético WPF",Description="Pedido sintético",Stage="Produção",OrderTotal=100,OriginalTotal=100}];return 0;});
        var port=System.Net.Sockets.TcpListener.Create(0);port.Start();var number=((System.Net.IPEndPoint)port.LocalEndpoint).Port;port.Stop();
        await using var server=await SharedDataServer.StartAsync(data,Path.Combine(folder,"Identidade"),number);
        var remote=DataStore.Connect(server.Connection);
        var login=await Task.Run(()=>remote.VerifyLogin("grafica","Wpf8C-Sintetico!2026"));Pass(login.Ok,"login WPF pin HTTPS em perfil isolado");
        var effective=await remote.QueryRemoteAsync<Dictionary<string,bool>>("auth/permissions");App.CurrentUser=new("Teste WPF","Administrador"){Permissions=effective};
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{await remote.LoadAsync(cancelled.Token);throw new Exception("cancelamento ausente");}catch(OperationCanceledException){Pass(true,"LoadAsync respeita cancelamento");}}
        var snapshot=await remote.LoadAsync();var change=remote.LastChangeUtc;await Task.Delay(30);await remote.LoadAsync();Pass(remote.LastChangeUtc==change,"LastChangeUtc não muda sem alteração no servidor");
        var main=new MainWindow(remote){ShowInTaskbar=false,ShowActivated=false};
        // No native window is shown. Execute the real refresh against HTTPS while pumping its STA dispatcher.
        var heartbeat=0;var pulse=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1)};pulse.Tick+=(_,_)=>heartbeat++;pulse.Start();
        var before=DateTime.UtcNow;Invoke(main,"RefreshData",false,false);Pass((bool)Field(main,"refreshBusy")!,"refresh WPF inicia sem bloquear retorno ao dispatcher");
        await Until(() => !(bool)Field(main,"refreshBusy")!);pulse.Stop();Pass(heartbeat>0,"dispatcher atende eventos durante refresh HTTPS");
        Pass(Field(main,"dashboardSnapshot") is AppData loaded&&loaded.ScheduledOrders.Count==1,"dashboard carrega snapshot correto");
        var update=((TextBlock)main.FindName("LastUpdateText")).Text;var observed=Field(main,"dashboardSnapshot");Invoke(main,"RefreshData",false,false);await Until(()=>!(bool)Field(main,"refreshBusy")!);Pass(ReferenceEquals(observed,Field(main,"dashboardSnapshot")),"revisão igual reutiliza snapshot sem /data");
        ((TextBox)main.FindName("OrdersSearchBox")).Text="sintético";Invoke(main,"RefreshData",false,true);await Until(()=>!(bool)Field(main,"refreshBusy")!);Pass(((TextBlock)main.FindName("LastUpdateText")).Text.StartsWith("Atualizado"),"busca remota atualiza horário real da consulta paginada");
        var orders=new ScheduledOrdersWindow(remote);await Until(()=>!(bool)Field(orders,"loading")!);Invoke(orders,"RefreshData",false);await Until(()=>!(bool)Field(orders,"loading")!);Pass(Field(orders,"loadedData") is AppData orderData&&orderData.ScheduledOrders.Count==1,"Pedidos WPF carrega dados por fluxo assíncrono");
        var id=snapshot.ScheduledOrders.Single().Id;var targetStage=snapshot.ScheduledOrders.Single().Stage==OrderStages.Production?OrderStages.Approval:OrderStages.Production;var command=new WebStageCommand(Guid.NewGuid(),snapshot.ScheduledOrders.Single().Revision,targetStage);var result=await remote.RemoteStageAsync(id,command);var replay=await remote.RemoteStageAsync(id,command);Pass(result.Revision==replay.Revision&&data.Load().ScheduledOrders.Single().Stage==targetStage,"comando WPF de etapa específico e idempotente");
        await remote.LoadAsync();Pass(remote.LastChangeUtc>change,"mudança real atualiza LastChangeUtc");
        orders.Close();main.Close();Pass(((CancellationTokenSource)Field(main,"dashboardCancellation")!).IsCancellationRequested,"fechamento cancela carga do dashboard");
        File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(new{passed,failed=0,dispatcherHeartbeat=heartbeat,smokeTotalElapsedMs=(DateTime.UtcNow-before).TotalMilliseconds,physicalPc1=false,physicalPc2=false,physicalNotebook=false,note="Smoke funcional STA/HTTPS; não mede notebook ou atraso Tailscale."}));Console.WriteLine("WPF ASYNC PASSED: "+passed);
    }
}
