using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

static class Program
{
 static int passed;static string folder="";static readonly ConcurrentQueue<string> calls=new();static int cancelled,active,maxActive,delay=80;
 static void Pass(bool value,string name){if(!value)throw new Exception("FAIL "+name);passed++;Console.WriteLine("PASS "+name);}
 static object? Field(object obj,string name)=>obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(obj);
 static void Invoke(object obj,string name,params object?[] args)=>obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(obj,args);
 static async Task Until(Func<bool> condition,string label="wait"){using var ct=new CancellationTokenSource(20000);try{while(!condition())await Task.Delay(10,ct.Token);}catch(OperationCanceledException){throw new Exception("Timeout "+label);}}
 static int Port(){var socket=System.Net.Sockets.TcpListener.Create(0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();return port;}
 static HttpClient Http(ServerConnection c)=>new(new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,cert,_,_)=>RemoteDataClient.CertificateMatchesPin(cert,c.CertificatePin)}){BaseAddress=new Uri(c.Url+"/"),Timeout=TimeSpan.FromSeconds(20)};
 static IEnumerable<DependencyObject> Visual(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var x in Visual(VisualTreeHelper.GetChild(root,i)))yield return x;}
 static IEnumerable<DependencyObject> Logical(DependencyObject root){yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var x in Logical(child))yield return x;}
 static ModuleLoader Loader(Window w)=>(ModuleLoader)Field(w,"remoteLoad")!;
 static Guid[] Ids(Window w)=>w switch {
  CustomersWindow=>((DataGrid)w.FindName("Grid")).Items.OfType<CustomerRow>().Select(x=>x.Id).ToArray(),
  ScheduledOrdersWindow=>((DataGrid)w.FindName("Grid")).Items.OfType<ScheduledOrderRow>().Select(x=>x.Id).ToArray(),
  SearchWindow=>((DataGrid)w.FindName("Grid")).Items.OfType<SearchSaleRow>().Select(x=>x.Sale.Id).ToArray(),
  ClosedSessionsWindow=>((ListBox)w.FindName("SessionList")).Items.OfType<SessionChoice>().Select(x=>x.Id).ToArray(),
  OrderBoardWindow=>new[]{"EntryList","ApprovalList","ProductionList","FinishList"}.SelectMany(n=>((ListBox)w.FindName(n)).Items.OfType<BoardCard>()).Select(x=>x.Id).ToArray(),_=>[]};
 static void Capture(FrameworkElement root,string name){root.UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)root.ActualWidth),Math.Max(1,(int)root.ActualHeight),96,96,PixelFormats.Pbgra32);var background=new DrawingVisual();using(var dc=background.RenderOpen())dc.DrawRectangle(ThemeManager.Get("Paper"),null,new Rect(0,0,root.ActualWidth,root.ActualHeight));bitmap.Render(background);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);}
 [STAThread]static int Main(string[] args){folder=Path.GetFullPath(args[0]);if(Directory.Exists(folder)&&Directory.EnumerateFileSystemEntries(folder).Any())throw new Exception("Fixture must be empty");Directory.CreateDirectory(folder);Environment.SetEnvironmentVariable("GRAFICA_CAIXA_PERFIL",Path.Combine(folder,"Perfil"));var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;ThemeManager.Initialize();var frame=new DispatcherFrame();int code=0;app.Dispatcher.BeginInvoke(async()=>{try{await Run();}catch(Exception ex){Console.WriteLine(ex);code=1;}finally{File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(new{passed,failed=code,cancelled,maxActive,calls=calls.ToArray()},new JsonSerializerOptions{WriteIndented=true}));frame.Continue=false;}});Dispatcher.PushFrame(frame);app.Shutdown();return code;}
 static async Task Run()
 {
  var local=new DataStore(Path.Combine(folder,"Dados"));local.EnsureInitialized();
  local.Update(d=>{d.Prompt12.LoginPassword=LoginSecurity.Create("Hotfix-Fixture!2026");d.DocumentsRoot=Path.Combine(folder,"Documentos");d.LogoPath="";d.Customers=[];d.ScheduledOrders=[];d.Sessions=[];d.Reminders=[];
   for(int i=0;i<95;i++){
    d.Customers.Add(new(){Name=$"Cliente {i:000}",Contact="11999990000"});
    d.ScheduledOrders.Add(new(){Number=1000+i,CustomerName=$"Cliente {i:000}",Contact="11999990000",Description=$"Impressão {i:000}",Stage=i%2==0?"Produção":"Agendado",ProductionBusinessDays=i==0?0:5,ArtworkApprovedAt=i%3==0?DateTime.Today.AddDays(-8):null,DueDate=i%4==0?DateTime.Today:i%4==2?DateTime.Today.AddDays(-2):DateTime.Today.AddDays(5),CreatedAt=DateTime.Today.AddMinutes(i),OrderTotal=100,OriginalTotal=100});
    var session=new CashSession{OpenedAt=DateTime.Today.AddDays(-i-1),ClosedAt=DateTime.Today.AddDays(-i-1).AddHours(18),OpenedBy="Fixture",ClosedBy="Fixture",OpeningAmount=50,DeclaredClosingAmount=50};
    session.Sales.Add(new(){CustomerName=$"Cliente {i:000}",Product=$"Serviço {i:000}",PaymentType=i%2==0?"Pix":"Dinheiro",TotalValue=20,CreatedAt=DateTime.Today.AddMinutes(-i)});d.Sessions.Add(session);
   }
   // Equal timestamps/name collisions and multiple pages within one session exercise stable boundaries and merge.
   for(int i=0;i<8;i++)d.Sessions[0].Sales.Add(new(){CustomerName="Cliente repetido",Product=$"Grupo {i}",TotalValue=15,PaymentType="Pix",CreatedAt=DateTime.Today.AddMinutes(-30)});
   d.Reminders.Add(new(){Phone="11999990000",Description="Lembrete inicial"});return 0;});
  Directory.CreateDirectory(Path.Combine(folder,"Documentos","Impressões"));
  var identityPath=Path.Combine(folder,"Identidade");var serverPort=Port();await using var server=await SharedDataServer.StartAsync(local,identityPath,serverPort);
  var conn=server.Connection with{Url=$"https://127.0.0.1:{serverPort}",Urls=null,SelectedUrl=null};
  using var upstream=Http(conn);using var cert=ServerIdentityStore.LoadExisting(identityPath);var proxyPort=Port();
  var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,proxyPort,l=>l.UseHttps(cert.Certificate)));
  await using var proxy=builder.Build();proxy.Run(async context=>{calls.Enqueue(context.Request.Path+context.Request.QueryString);var count=Interlocked.Increment(ref active);maxActive=Math.Max(maxActive,count);try{await Task.Delay(delay,context.RequestAborted);using var req=new HttpRequestMessage(new HttpMethod(context.Request.Method),context.Request.Path+context.Request.QueryString);foreach(var h in context.Request.Headers)if(h.Key.Equals("Authorization",StringComparison.OrdinalIgnoreCase)||h.Key.Equals("X-Device-Key",StringComparison.OrdinalIgnoreCase))req.Headers.TryAddWithoutValidation(h.Key,h.Value.ToArray());if(context.Request.Method!="GET"){using var ms=new MemoryStream();await context.Request.Body.CopyToAsync(ms,context.RequestAborted);req.Content=new ByteArrayContent(ms.ToArray());req.Content.Headers.TryAddWithoutValidation("Content-Type",context.Request.ContentType);}using var response=await upstream.SendAsync(req,context.RequestAborted);context.Response.StatusCode=(int)response.StatusCode;context.Response.ContentType=response.Content.Headers.ContentType?.ToString();await response.Content.CopyToAsync(context.Response.Body,context.RequestAborted);}catch(OperationCanceledException){Interlocked.Increment(ref cancelled);}finally{Interlocked.Decrement(ref active);}});await proxy.StartAsync();
  conn=conn with{Url=$"https://127.0.0.1:{proxyPort}"};var remote=DataStore.Connect(conn);Pass((await Task.Run(()=>remote.VerifyLogin("grafica","Hotfix-Fixture!2026"))).Ok,"login fixture");var permissions=await remote.QueryRemoteAsync<Dictionary<string,bool>>("auth/permissions");App.CurrentUser=new("Fixture","Administrador"){Permissions=permissions};
  var admin=remote.AuthenticatedUser!;var security=new IdentitySecurity(local);security.SaveUser(admin,new(null,"Usuário B","usuario_b","Administrador",true,"Hotfix-B!2026"),"Fixture");
  var other=DataStore.Connect(conn);Pass((await Task.Run(()=>other.VerifyLogin("usuario_b","Hotfix-B!2026"))).Ok,"troca usuário B");
  var main=new MainWindow(remote){Width=1300,Height=850,WindowState=WindowState.Normal,Opacity=0,ShowInTaskbar=false,ShowActivated=false};main.Show();await Until(()=>Loader(main).Result!=null&&!Loader(main).Loading&&!(bool)Field(main,"refreshBusy")!);
  var navigator=(PageNavigator)Field(main,"pages")!;
  async Task SetPage(Window w){navigator.Show(w);await Until(()=>Loader(w).Result!=null&&!Loader(w).Loading,"first "+w.GetType().Name);await Task.Delay(100);}
  async Task Badge(int expected){await Until(()=>((TextBlock)main.FindName("RemindersCount")).Text==expected.ToString(),"badge "+expected);Pass(true,"badge imediato "+expected);}
  Invoke(main,"Reminders_Click",main,new RoutedEventArgs());var reminders=(RemindersWindow)navigator.Current!;await Until(()=>Loader(reminders).Result!=null&&!Loader(reminders).Loading);await Badge(1);
  var id=local.Load().Reminders![0].Id;
  void Change(Action<Reminder>? action)=>Invoke(reminders,"Change",id,"Fixture leitura",action);
  Change(r=>r.ReadByUsers[admin.UserId]=DateTime.UtcNow);await Badge(0);
  var bpage=await other.QueryRemoteAsync<ModulePage>("api/v1/dashboard?projection=desktop");Pass(bpage.Counts["reminders"]==1,"leitura A não afeta B");
  Change(r=>r.ReadByUsers.Remove(admin.UserId));await Badge(1);
  Change(r=>r.DoneAt=DateTime.Now);await Badge(0);
  Change(r=>r.DoneAt=null);await Badge(1);
  Change(null);await Badge(0);
  ((TextBox)Field(reminders,"phone")!).Text="11999994447";((TextBox)Field(reminders,"description")!).Text="Novo não lido";Invoke(reminders,"Add");await Badge(1);
  var second=local.Load().Reminders!.Single().Id;Invoke(reminders,"Change",second,"Fixture B",new Action<Reminder>(r=>r.ReadByUsers[admin.UserId]=DateTime.UtcNow));await Badge(0);
  var reader=new RemindersWindow(other);await SetPage(reader);Pass(Loader(reader).Result!.Counts["reminders"]==1,"troca usuário conserva não lidos");
  var deadlines=new ProductionDeadlinesView{Store=remote};var dw=new Window{Content=deadlines,Width=600,Height=640,Opacity=0,ShowInTaskbar=false,Owner=main};dw.Show();await Until(()=>Field(deadlines,"data")!=null);
  async Task EditDeadline(ProductionDeadline? original,string description){bool checkedDialog=false;Application.Current.Dispatcher.BeginInvoke(()=>{var dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Title==(original==null?"Adicionar prazo":"Editar prazo"));Pass(!Logical((DependencyObject)dialog.Content).OfType<TextBlock>().Any(x=>x.Text=="Ordem de exibição"),"ordem ausente no diálogo");var boxes=Logical((DependencyObject)dialog.Content).OfType<TextBox>().ToArray();Pass(boxes.Length==3,"somente descrição prazo observação");boxes[0].Text=description;boxes[1].Text="5 dias";Capture((FrameworkElement)dialog.Content,original==null?"novo-prazo":"editar-prazo");checkedDialog=true;Logical((DependencyObject)dialog.Content).OfType<Button>().Single(b=>Equals(b.Content,"Salvar prazo")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));});Invoke(deadlines,"Edit",original);Pass(checkedDialog,"diálogo exercitado");await Until(()=>deadlines.IsEnabled&&local.Load().ProductionDeadlines.Any(x=>x.Description==description),"deadline saved");await Task.Delay(300);}
  var before=local.Load().ProductionDeadlines.ToDictionary(x=>x.Id,x=>x.Order);int max=before.Values.DefaultIfEmpty(0).Max();await EditDeadline(null,"Prazo novo hotfix");var created=local.Load().ProductionDeadlines.Single(x=>x.Description=="Prazo novo hotfix");Pass(created.Order==max+10,"novo prazo max + 10");await EditDeadline(created,"Prazo editado hotfix");var reopened=local.Load().ProductionDeadlines.Single(x=>x.Id==created.Id);Pass(reopened.Order==created.Order&&before.All(x=>local.Load().ProductionDeadlines.Single(r=>r.Id==x.Key).Order==x.Value),"editar/reabrir preserva todas as ordens");dw.Close();
  await Until(()=>active==0);calls.Clear();
  var pulseCount=0;var pulse=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};pulse.Tick+=(_,_)=>pulseCount++;pulse.Start();
  foreach(var (module,create) in new (string,Func<Window>)[]{("pedidos",()=>new ScheduledOrdersWindow(remote)),("clientes",()=>new CustomersWindow(remote)),("fechamentos",()=>new ClosedSessionsWindow(remote)),("lancamentos",()=>new SearchWindow(remote)),("producao",()=>new OrderBoardWindow(remote))}){
   var w=create();await SetPage(w);var loader=Loader(w);var content=(FrameworkElement)((ContentControl)main.FindName("PageHost")).Content;
   Pass(Ids(w).Length==30&&loader.Page==1,"primeiro lote limitado "+module);await Task.Delay(220);Pass(loader.Page==1,"abertura não carrega tudo "+module);
   Pass(!Logical(content).OfType<Button>().Any(b=>b.Content is string s&&(s=="Anterior"||s=="Próxima"))&&!Logical(content).OfType<TextBlock>().Any(x=>x.Text.StartsWith("Página ")),"paginação visual removida "+module);
   Capture(content,module+"-primeiro-lote");var expected=new List<Guid>();for(int page=1;;page++){var result=await remote.QueryRemoteAsync<ModulePage>($"api/v1/{module}?projection=desktop&pageSize=30&page={page}"+(module=="pedidos"?"&scope=active":""));var d=result.Data.Deserialize<AppData>()!;expected.AddRange(module=="clientes"?d.Customers.Select(x=>x.Id):module=="fechamentos"?d.Sessions.Select(x=>x.Id):module=="lancamentos"?d.Sessions.SelectMany(x=>x.Sales).Select(x=>x.Id):d.ScheduledOrders.Select(x=>x.Id));if((long)page*30>=result.Total)break;}
   int previous=30;while(loader.HasMore){int next=loader.Page+1;var viewer=Visual(content).OfType<ScrollViewer>().Where(s=>s.ScrollableHeight>0).OrderByDescending(s=>s.ScrollableHeight).First();viewer.ScrollToEnd();viewer.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.PreviewMouseWheelEvent});await Until(()=>loader.Page>=next&&!loader.Loading,"scroll "+module);await Task.Delay(80);var ids=Ids(w);Pass(ids.Length>previous&&ids.Distinct().Count()==ids.Length,"lote "+next+" anexa sem duplicados "+module);previous=ids.Length;}
   Pass(Ids(w).ToHashSet().SetEquals(expected)&&Ids(w).Length==expected.Count,"final sem perdas "+module);if(module!="producao"&&module!="lancamentos")Pass(Ids(w).SequenceEqual(expected),"ordenação de negócio "+module);if(module=="pedidos"){var orders=local.Load().ScheduledOrders.ToDictionary(x=>x.Id);var priorities=Ids(w).Select(id=>OrderOperations.OperationalPriority(orders[id])).ToArray();Pass(priorities.SequenceEqual(priorities.Order())&&new[]{0,1,2,3}.All(x=>priorities.Contains(x)),"IMEDIATO ATRASADOS HOJE DEMAIS preservados");}
   if(module=="fechamentos")Pass(((ListBox)w.FindName("SessionList")).Items.OfType<SessionChoice>().Count(x=>x.CanEdit)==5,"somente cinco fechamentos retificáveis");int atEnd=loader.Page;loader.LoadMore();await Task.Delay(200);Pass(loader.Page==atEnd&&!loader.Loading,"fim da lista "+module);
   // Refresh starts at page one and never keeps a stale continuation.
   if(module=="pedidos")Invoke(w,"RefreshData",false);else if(module=="fechamentos")Invoke(w,"LoadSessions",(Guid?)null);else Invoke(w,"LoadData");await Until(()=>loader.Page==1&&!loader.Loading&&Ids(w).Length==30,"refresh "+module);Pass(true,"refresh reinicia "+module);
   if(module is "clientes" or "pedidos" or "lancamentos"){
    var search=(TextBox)w.FindName("SearchBox");search.Text=module=="lancamentos"?"Serviço 094":"Cliente 094";Pass(Ids(w).Length==0,"busca limpa imediatamente "+module);await Until(()=>!loader.Loading&&Ids(w).Length==1,"full search "+module);Pass(true,"busca encontra último registro no servidor "+module);
    search.Text="ausente";await Until(()=>!loader.Loading&&loader.Result?.Total==0,"empty search");Pass(Ids(w).Length==0&&!loader.HasMore,"busca vazia "+module);
    search.Text="";await Until(()=>!loader.Loading&&Ids(w).Length==30);
    // Rapid changes cancel obsolete work and preserve debounce.
    delay=400;search.Text="Cliente 01";await Task.Delay(370);search.Text="Cliente 094";await Until(()=>!loader.Loading&&Ids(w).Length==1,"cancel replace");Pass(true,"busca anterior cancelada "+module);delay=80;
    search.Text="";await Until(()=>!loader.Loading&&Ids(w).Length==30);
    var grid=(DataGrid)w.FindName("Grid");var column=grid.Columns[0];var args=new DataGridSortingEventArgs(column);if(module=="pedidos")Invoke(w,"Grid_Sorting",grid,args);else typeof(DataGrid).GetMethod("OnSorting",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(grid,[args]);await Until(()=>!loader.Loading&&Ids(w).Length==30);Pass(loader.Page==1&&calls.Any(x=>x.Contains("&sort=")),"ordenação consulta servidor "+module);
    if(module=="lancamentos"){var sorted=grid.Items.OfType<SearchSaleRow>().Select(x=>x.Sale.CreatedAt).ToArray();Pass(sorted.SequenceEqual(sorted.Order()),"pesquisa ordena data no conjunto completo");}else if(module=="clientes")Pass(Ids(w)[0]==local.Load().Customers.OrderBy(x=>x.Name).First().Id,"clientes ordenação global");
    loader.LoadMore();await Until(()=>loader.Page==2&&!loader.Loading);var sortedIds=Ids(w);Pass(sortedIds.Distinct().Count()==60,"ordenação incremental sem duplicados "+module);if(module!="pedidos"){Invoke(w,"LoadData");await Until(()=>loader.Page==1&&!loader.Loading&&Ids(w).Length==30);} 
   }
   if(module=="pedidos"){
    var filter=(ComboBox)w.FindName("FilterBox");filter.SelectedItem=filter.Items.Cast<string>().Single(x=>x=="Produção");await Until(()=>!loader.Loading&&loader.Result?.Total==48,"filter full");Pass(Ids(w).Length==30,"filtro em todo conjunto");loader.LoadMore();await Until(()=>loader.Page==2&&!loader.Loading);Pass(Ids(w).Length==48,"filtro inclui segundo lote");filter.SelectedIndex=0;await Until(()=>!loader.Loading&&Ids(w).Length==30);
   }
   // Two scroll triggers share one in-flight continuation.
   int beforeCalls=calls.Count(x=>x.Contains($"/api/v1/{module}?")&&x.Contains("page=2&"));loader.LoadMore();loader.LoadMore();loader.LoadMore();await Until(()=>loader.Page==2&&!loader.Loading);Pass(calls.Count(x=>x.Contains($"/api/v1/{module}?")&&x.Contains("page=2&"))==beforeCalls+1,"carga incremental single-flight "+module);
   // Revision changes never join an offset from a different dataset.
   local.Update(d=>{d.Reminders!.Add(new(){Description="Revisão sintética",Phone="11999994441"});return 0;});loader.LoadMore();await Until(()=>loader.Page==1&&!loader.Loading&&Ids(w).Length==30,"revision restart");Pass(Ids(w).Distinct().Count()==30,"RemoteRevision reinicia sem perdas "+module);
   delay=500;loader.LoadMore();await Task.Delay(80);var oldCount=Ids(w).Length;await SetPage(new CustomersWindow(remote));await Task.Delay(600);Pass(Ids(w).Length==oldCount,"troca tela rejeita resposta tardia "+module);delay=80;
  }
  pulse.Stop();Pass(pulseCount>100,"UI responsiva durante scroll e consultas");Pass(cancelled>0,"cancelamento chega ao servidor");Pass(calls.All(x=>!x.StartsWith("/data")),"/data zero na navegação do hotfix");main.Close();await proxy.StopAsync();Console.WriteLine($"HOTFIX PASS {passed} checks");
 }
}
