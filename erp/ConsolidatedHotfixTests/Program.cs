using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

static class Program
{
 static string folder=""; static int passed;static readonly ConcurrentQueue<string> calls=new();static int cancelled,active,maxActive,delay=30;
 static T Clone<T>(T x)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(x))!;
 static object Field(object x,string name)=>x.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(x)!;
 static void Invoke(object x,string name,params object?[] args)=>x.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(x,args);
 static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL "+label);passed++;Console.WriteLine("PASS "+label);}
 [STAThread] static int Main(string[] args){folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);Environment.SetEnvironmentVariable("GRAFICA_CAIXA_PERFIL",Path.Combine(folder,"Perfil"));var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;ThemeManager.Initialize();var frame=new DispatcherFrame();int code=0;app.Dispatcher.BeginInvoke(async()=>{try{await Run();}catch(Exception ex){Console.WriteLine(ex);code=1;}finally{File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(new{passed,failed=code,cancelled,maxActive,calls=calls.ToArray()},new JsonSerializerOptions{WriteIndented=true}));frame.Continue=false;}});Dispatcher.PushFrame(frame);app.Shutdown();return code;}
 static async Task Run()
 {
  Directory.CreateDirectory(Path.Combine(folder,"Documentos","Impressões"));var local=new DataStore(Path.Combine(folder,"Dados"));local.EnsureInitialized();
  var product=new Product{Description="Produto legado unitário",Unit="un",Cost=10,Price=20,ExactQuantityPrices=[new(){Quantity=1,TotalPrice=20}]};
  local.Update(d=>{d.Products=[product];d.DocumentsRoot=Path.Combine(folder,"Documentos");d.Prompt12.LoginPassword=LoginSecurity.Create("Hotfix-Fixture!2026");return 0;});
  var socket=System.Net.Sockets.TcpListener.Create(0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();
  await using var server=await SharedDataServer.StartAsync(local,Path.Combine(folder,"Identidade"),port);
  var conn=server.Connection with{Url=$"https://127.0.0.1:{port}",Urls=null,SelectedUrl=null};
  using var upstream=new HttpClient(new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,cert,_,_)=>RemoteDataClient.CertificateMatchesPin(cert,conn.CertificatePin)}){BaseAddress=new Uri(conn.Url+"/")};
  using var identity=ServerIdentityStore.LoadExisting(Path.Combine(folder,"Identidade"));var proxySocket=System.Net.Sockets.TcpListener.Create(0);proxySocket.Start();var proxyPort=((IPEndPoint)proxySocket.LocalEndpoint).Port;proxySocket.Stop();
  var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,proxyPort,l=>l.UseHttps(identity.Certificate)));
  await using var proxy=builder.Build();proxy.Run(async context=>{calls.Enqueue(context.Request.Method+" "+context.Request.Path+context.Request.QueryString);var count=Interlocked.Increment(ref active);maxActive=Math.Max(maxActive,count);try{await Task.Delay(delay,context.RequestAborted);using var req=new HttpRequestMessage(new HttpMethod(context.Request.Method),context.Request.Path+context.Request.QueryString);foreach(var h in context.Request.Headers)if(h.Key.Equals("Authorization",StringComparison.OrdinalIgnoreCase)||h.Key.Equals("X-Device-Key",StringComparison.OrdinalIgnoreCase))req.Headers.TryAddWithoutValidation(h.Key,h.Value.ToArray());if(context.Request.Method!="GET"){using var ms=new MemoryStream();await context.Request.Body.CopyToAsync(ms,context.RequestAborted);req.Content=new ByteArrayContent(ms.ToArray());req.Content.Headers.TryAddWithoutValidation("Content-Type",context.Request.ContentType);}using var response=await upstream.SendAsync(req,context.RequestAborted);context.Response.StatusCode=(int)response.StatusCode;context.Response.ContentType=response.Content.Headers.ContentType?.ToString();await response.Content.CopyToAsync(context.Response.Body,context.RequestAborted);}catch(OperationCanceledException){Interlocked.Increment(ref cancelled);}finally{Interlocked.Decrement(ref active);}});await proxy.StartAsync();
  conn=conn with{Url=$"https://127.0.0.1:{proxyPort}"};var remote=DataStore.Connect(conn);
  Check((await Task.Run(()=>remote.VerifyLogin("grafica","Hotfix-Fixture!2026"))).Ok,"login staging versão base");
  App.CurrentUser=OperationContext.Administrator("Fixture") with{Permissions=await remote.QueryRemoteAsync<Dictionary<string,bool>>("auth/permissions")};
  var original=local.Load().Products.Single();var editor=new ProductEditor(original){Opacity=0,ShowInTaskbar=false,ShowActivated=false};
  editor.Dispatcher.BeginInvoke(()=>{((TextBox)Field(editor,"cost")).Text="12,00";Invoke(editor,"Save");});
  Check(editor.ShowDialog()==true,"editor real altera custo e confirma");
  string? error=null;try{await Task.Run(()=>ProductPhotos.SaveAsync(remote,editor.Result!,original,null,App.CurrentUser));}catch(InvalidOperationException ex){error=ex.Message;}
    Check(error==null&&local.Load().Products.Single().Cost==12,"A custo direto persiste ao reabrir");
  {var old=local.Load().Products.Single();var copy=Clone(old);copy.Cost=10;await Task.Run(()=>ProductPhotos.SaveAsync(remote,copy,old,null,App.CurrentUser));}
  var draft=new ScheduledOrder{CustomerName="Fixture",OrderTotal=40,OriginalTotal=40,ItemCostsEnabled=true,Items=[new(){ProductId=product.Id,Product=product.Description,Quantity=2,UnitPrice=20,UnitCost=12,CostReviewed=true}],FreightCost=99,AdditionalCost=77};
  await SaveOrder(remote,draft);
  var saved=local.Load();var cost=saved.Products.Single().Cost;
    {Check(cost==12,"B pedido Conferido atualiza custo simples sem frete/adicionais");await Costs(local,remote,saved.ScheduledOrders.Single());await Ui(local,remote);}
  Check(true,"servidor isolado encerrado ao terminar fixture");
 }
 static async Task Until(Func<bool> check,string label="aguardar"){using var ct=new CancellationTokenSource(15000);try{while(!check())await Task.Delay(10,ct.Token);}catch(OperationCanceledException){throw new Exception("Timeout "+label);}}
 static IEnumerable<DependencyObject> Logical(DependencyObject root){yield return root;foreach(var x in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var y in Logical(x))yield return y;}
 static IEnumerable<DependencyObject> Visual(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var x in Visual(VisualTreeHelper.GetChild(root,i)))yield return x;}
 static ModuleLoader Loader(Window w)=>(ModuleLoader)Field(w,"remoteLoad");
 static void Capture(FrameworkElement root,string name){root.UpdateLayout();var bitmap=new RenderTargetBitmap(Math.Max(1,(int)root.ActualWidth),Math.Max(1,(int)root.ActualHeight),96,96,PixelFormats.Pbgra32);var backdrop=new DrawingVisual();using(var dc=backdrop.RenderOpen())dc.DrawRectangle(ThemeManager.Get("Paper"),null,new Rect(0,0,root.ActualWidth,root.ActualHeight));bitmap.Render(backdrop);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);}
 static async Task Reject(Func<Task> action,string label){bool rejected=false;try{await action();}catch(Exception e)when(e is InvalidOperationException or UnauthorizedAccessException){rejected=true;}Check(rejected,label);}
 // Gravações pelos comandos atuais do servidor (o transporte legado remote.Update foi desativado).
 static Task SaveOrder(DataStore remote,ScheduledOrder o)=>Task.Run(()=>remote.RemoteDesktopWriteAsync("desktop/orders/save",new DesktopOrderCommand(Guid.NewGuid(),o.Number>0?o.Revision:null,o)));
 static async Task Costs(DataStore local,DataStore remote,ScheduledOrder first)
 {
  async Task ChangeProduct(Guid id,Action<Product> change){var old=local.Load().Products.Single(x=>x.Id==id);var next=Clone(old);change(next);await Task.Run(()=>ProductPhotos.SaveAsync(remote,next,old,null,App.CurrentUser));}
  await ChangeProduct(first.Items[0].ProductId!.Value,p=>p.Cost=15);
  Check(local.Load().ScheduledOrders.Single(x=>x.Id==first.Id).Items[0].UnitCost==12,"C snapshot histórico conserva 12 depois do Produto 15");
  var audit=local.Load().AuditTrail.Single(x=>x.EntityType=="Produto"&&x.Action=="Custo confirmado pelo pedido");
  Check(audit.OldValue.Contains("10")&&audit.NewValue.Contains("12")&&audit.Description.Contains("Pedido #")&&audit.UserId==remote.AuthenticatedUser!.UserId.ToString()&&audit.Timestamp!=default,"auditoria servidor custo anterior/novo modalidade pedido usuário data");
  ScheduledOrder Order(Product p,int q=1,decimal? amount=17,bool reviewed=true,bool ignored=false)=>new(){CustomerName="Fixture",OrderTotal=100,OriginalTotal=100,ItemCostsEnabled=true,Items=[new(){ProductId=p.Id,Product=p.Description,Quantity=q,UnitPrice=20,UnitCost=amount,CostReviewed=reviewed,CostIgnored=ignored}]};
  var simple=local.Load().Products.Single();var fallback=Clone(simple);fallback.Cost=null;fallback.ExactQuantityPrices[0].TotalCost=10;Check(ProductCosting.Resolve(fallback,2).TotalCost==20,"custo unitário legado preservado quando padrão está vazio");
  foreach(var kind in new[]{"Pendente","Ignorado","Avulso","Vazio"}){var o=Order(simple);if(kind=="Pendente")o.Items[0].CostReviewed=false;if(kind=="Ignorado")o.Items[0].CostIgnored=true;if(kind=="Avulso")o.Items[0].ProductId=null;if(kind=="Vazio"){o.Items[0].UnitCost=null;o.Items[0].CostReviewed=false;}await SaveOrder(remote,o);Check(local.Load().Products.Single().Cost==15,kind+" não atualiza Produto");}
  var canceled=Order(simple);Check(local.Load().ScheduledOrders.All(x=>x.Id!=canceled.Id)&&local.Load().Products.Single().Cost==15,"cancelado não persiste pedido nem Produto");
  var tier=new Product{Description="Produto faixa",Cost=7,Price=20,UseQuantityPricing=true,QuantityPriceTiers=[new(){From=1,Through=9,UnitPrice=20,UnitCost=10},new(){From=10,UnitPrice=18,UnitCost=9}]};
  var exact=new Product{Description="Produto exato",Cost=7,Price=20,ExactQuantityPrices=[new(){Quantity=5,TotalPrice=90,TotalCost=50},new(){Quantity=10,TotalPrice=160,TotalCost=80}]};
  await Task.Run(()=>ProductPhotos.SaveAsync(remote,tier,null,null,App.CurrentUser));await Task.Run(()=>ProductPhotos.SaveAsync(remote,exact,null,null,App.CurrentUser));
  foreach(var p in new[]{tier,exact}){
   var old=local.Load().Products.Single(x=>x.Id==p.Id);var editor=new ProductEditor(old){Opacity=0,ShowInTaskbar=false,ShowActivated=false};editor.Dispatcher.BeginInvoke(()=>{Logical((DependencyObject)editor.Content).OfType<TextBox>().First(x=>x.ToolTip is string t&&t.StartsWith(p==tier?"Custo unitário desta faixa":"Custo total desta quantidade")).Text=p==tier?"11,00":"55,00";editor.UpdateLayout();Capture((FrameworkElement)editor.Content,p==tier?"editor-faixa":"editor-exato");Invoke(editor,"Save");});Check(editor.ShowDialog()==true,"editor real custo "+p.Description);await Task.Run(()=>ProductPhotos.SaveAsync(remote,editor.Result!,old,null,App.CurrentUser));var reopened=local.Load().Products.Single(x=>x.Id==p.Id);Check(p==tier?reopened.QuantityPriceTiers[0].UnitCost==11&&reopened.QuantityPriceTiers[1].UnitCost==9:reopened.ExactQuantityPrices[0].TotalCost==55&&reopened.ExactQuantityPrices[1].TotalCost==80,"custo direto por referência persiste "+p.Description);
  }
  var tierOrder=Order(tier,2,12);await SaveOrder(remote,tierOrder);var t=local.Load().Products.Single(x=>x.Id==tier.Id);Check(t.UseQuantityPricing&&t.Cost==7&&t.QuantityPriceTiers[0].UnitCost==12&&t.QuantityPriceTiers[1].UnitCost==9,"G Conferido atualiza somente faixa usada");
  var exactOrder=Order(exact,5,12);exactOrder.Items[0].EnteredTotalCost=63;await SaveOrder(remote,exactOrder);var e=local.Load().Products.Single(x=>x.Id==exact.Id);Check(e.Cost==7&&e.ExactQuantityPrices[0].TotalCost==63&&e.ExactQuantityPrices[1].TotalCost==80,"H Conferido atualiza somente exata com total preciso");
  var mismatch=Order(exact,3,17);await SaveOrder(remote,mismatch);Check(local.Load().Products.Single(x=>x.Id==exact.Id).Cost==7,"quantidade sem referência não converte exata em simples");
  var q1=Order(simple,1,16);await SaveOrder(remote,q1);Check(local.Load().Products.Single(x=>x.Id==simple.Id).Cost==16&&ProductCosting.Resolve(local.Load().Products.Single(x=>x.Id==simple.Id),1).Kind==ProductCostKind.Standard,"legado quantidade 1 resolve e atualiza custo padrão");
  var before=local.Load();var stale=Clone(before.Products.Single(x=>x.Id==simple.Id));await ChangeProduct(simple.Id,p=>p.Cost=18);await Reject(()=>{var changed=Clone(stale);changed.Cost=19;return Task.Run(()=>ProductPhotos.SaveAsync(remote,changed,stale,null,App.CurrentUser));},"I snapshot Produto obsoleto bloqueado");
  var staleOrder=Clone(first);staleOrder.Items[0].UnitCost=13;await Task.Run(()=>remote.RemoteBusinessAsync("order.cost",staleOrder.Id,null,OrderCostArgs.FromOrder(staleOrder)));await Reject(()=>Task.Run(()=>remote.RemoteBusinessAsync("order.cost",staleOrder.Id,null,OrderCostArgs.FromOrder(staleOrder))),"I revision Pedido obsoleta bloqueada");
  var state=local.Load();await Reject(()=>SaveOrder(remote,staleOrder),"pedido com revisão antiga recusado");Check(JsonSerializer.Serialize(state)==JsonSerializer.Serialize(local.Load()),"transação sem gravação parcial ou auditoria parcial");
  var security=new IdentitySecurity(local);var actor=remote.AuthenticatedUser!;security.SavePermissions(actor,new(actor.UserId,null,new(){{"costs.edit",false}}),"Fixture");await Reject(()=>ChangeProduct(simple.Id,p=>p.Cost=77),"costs.edit recusa gravação direta no servidor");await Reject(()=>SaveOrder(remote,Order(simple,1,77)),"costs.edit recusa Conferido no servidor");security.SavePermissions(actor,new(actor.UserId,null,new(){{"costs.edit",true}}),"Fixture");
  var replay=local.Load();replay.Products.Single(x=>x.Id==simple.Id).Cost=21;var request=new CommitRequest(Guid.NewGuid(),replay.DataVersion,replay,9);Check(local.CommitRemote(Clone(request),actor)&&local.CommitRemote(Clone(request),actor),"idempotência replay da mesma operação");var count=local.Load().AuditTrail.Count;Check(!local.CommitRemote(new(Guid.NewGuid(),replay.DataVersion,replay,9),actor)&&local.Load().AuditTrail.Count==count,"expectedRevision global obsoleta rejeitada sem auditoria adicional");
 }
 static async Task Ui(DataStore local,DataStore remote)
 {
  local.Update(d=>{for(int i=0;i<412;i++)d.Products.Add(new(){Description=$"Catálogo {i:000}",Category=i%2==0?"Categoria A":"Categoria B",Cost=10,Price=20,Active=i%11!=0});return 0;});
  var main=new MainWindow(remote){Width=1300,Height=850,WindowState=WindowState.Normal,Opacity=0,ShowInTaskbar=false,ShowActivated=false};main.Show();await Until(()=>Loader(main).Result!=null&&!Loader(main).Loading&&!(bool)Field(main,"refreshBusy"));var navigator=(PageNavigator)Field(main,"pages");
  async Task Open(string method){Invoke(main,method,main,new RoutedEventArgs());await Until(()=>navigator.Current!=null);// Devedores carrega pelo painel próprio (sem ModuleLoader.Result): basta ter os dados na tela.
   await Until(()=>navigator.Current is FollowUpWindow f?f.GetType().GetField("debtPage",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f)!=null&&!(bool)Field(f,"debtLoading"):Loader(navigator.Current!).Result!=null&&!Loader(navigator.Current!).Loading);await Task.Delay(80);}
  KeyEventArgs Escape(UIElement target,RoutedEvent? route=null){var e=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(main),Environment.TickCount,Key.Escape){RoutedEvent=route??Keyboard.KeyDownEvent};target.RaiseEvent(e);return e;}
  await Until(()=>active==0);calls.Clear();int pulses=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};timer.Tick+=(_,_)=>pulses++;timer.Start();
  await Open("Products_Click");var products=(ProductsWindow)navigator.Current!;var list=(ListBox)products.FindName("ProductsGrid");var loader=Loader(products);var content=(FrameworkElement)((ContentControl)main.FindName("PageHost")).Content;
  Guid[] Ids()=>list.Items.OfType<Product>().Select(x=>x.Id).ToArray();
  Check(Ids().Length==30&&loader.Page==1,"Produtos abertura somente 30 registros");await Task.Delay(150);Check(loader.Page==1,"abertura não antecipa os 412 produtos");
  Check(!Logical(content).OfType<Button>().Any(x=>x.Content is string s&&(s=="Anterior"||s=="Próxima"))&&!Logical(content).OfType<TextBlock>().Any(x=>x.Text.StartsWith("Página ")||x.Text.Contains("nesta página")),"paginação visível Produtos removida");
  async Task Scroll(){int next=loader.Page+1;content.UpdateLayout();list.UpdateLayout();var viewer=Visual(list).OfType<ScrollViewer>().First();viewer.ScrollToEnd();await Task.Delay(80);content.UpdateLayout();Console.WriteLine($"SCROLL page={loader.Page} viewport={viewer.ViewportHeight} extent={viewer.ExtentHeight} offset={viewer.VerticalOffset} loading={loader.Loading}");if(loader.Page<next&&!loader.Loading)viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.PreviewMouseWheelEvent});await Until(()=>loader.Page>=next&&!loader.Loading,"lote "+next);await Task.Delay(50);}
  var selected=list.Items[2];list.SelectedItem=selected;await Scroll();Check(list.SelectedItem==selected,"scroll Cards preserva seleção");await Scroll();Check(Ids().Length==90&&Ids().Distinct().Count()==90,"Cards três lotes sem duplicados");Capture(content,"produtos-cards");
  while(loader.HasMore)await Scroll();var expected=local.Load().Products.Where(x=>x.Active).Select(x=>x.Id).ToHashSet();Check(Ids().ToHashSet().SetEquals(expected)&&Ids().Length==expected.Count,"Cards até o final sem perdas nem duplicidades");
  ((ViewToggle)products.FindName("ProductsView")).Mode="Lista";Check(Ids().Length==expected.Count,"Cards para Lista sem duplicação");
  ((TextBox)products.FindName("SearchBox")).Text="Catálogo 411";await Until(()=>loader.Result!=null&&!loader.Loading&&Ids().Length==1);Check(list.Items.OfType<Product>().Single().Description=="Catálogo 411","busca global encontra Produto fora do primeiro lote");
  ((TextBox)products.FindName("SearchBox")).Text="";await Until(()=>loader.Result!=null&&!loader.Loading&&Ids().Length==30);await Scroll();await Scroll();Check(Ids().Length==90,"Lista carrega três lotes por scroll");Capture(content,"produtos-lista");while(loader.HasMore)await Scroll();Check(Ids().ToHashSet().SetEquals(expected)&&Ids().Length==expected.Count,"Lista até fim sem perdas nem duplicados");
  var box=(ComboBox)products.FindName("CategoryBox");box.SelectedItem="Categoria A";await Until(()=>loader.Result!=null&&!loader.Loading&&loader.Page==1&&list.Items.OfType<Product>().All(x=>x.Category=="Categoria A")&&Ids().Length==30);while(loader.HasMore)await Scroll();Check(Ids().ToHashSet().SetEquals(local.Load().Products.Where(x=>x.Active&&x.Category=="Categoria A").Select(x=>x.Id)),"categoria global no servidor");
  var inactive=(CheckBox)products.FindName("InactiveBox");inactive.IsChecked=true;Invoke(products,"Search_Changed",inactive,new RoutedEventArgs());await Until(()=>loader.Result!=null&&!loader.Loading&&loader.Page==1);while(loader.HasMore)await Scroll();Check(Ids().ToHashSet().SetEquals(local.Load().Products.Where(x=>x.Category=="Categoria A").Select(x=>x.Id)),"incluir inativos filtra todo catálogo");
  box.SelectedIndex=0;await Until(()=>loader.Result!=null&&!loader.Loading&&loader.Page==1);Check(Ids().Length==30,"limpar filtros reinicia lote");
  int page=loader.Page;for(int i=0;i<10;i++)loader.LoadMore();await Until(()=>loader.Page==page+1&&!loader.Loading);Check(loader.Page==page+1,"single-flight incremental repetido carrega somente um lote");
  delay=350;loader.LoadMore();((TextBox)products.FindName("SearchBox")).Text="Catálogo 409";await Until(()=>loader.Result!=null&&!loader.Loading&&Ids().Length==1);Check(list.Items.OfType<Product>().Single().Description=="Catálogo 409"&&loader.Page==1,"filtro cancela lote e rejeita resposta antiga");delay=30;
  box.IsDropDownOpen=true;Escape(box);Check(navigator.Current==products&&!box.IsDropDownOpen,"ESC dropdown fecha primeiro e preserva módulo");box.IsDropDownOpen=false;
  var context=new ContextMenu();context.Items.Add(new MenuItem{Header="Teste"});list.ContextMenu=context;context.IsOpen=true;Escape(list);Check(navigator.Current==products,"ESC menu de contexto preserva módulo");context.IsOpen=false;
  var modal=new Window{Owner=products,Opacity=0,ShowInTaskbar=false,Content=new TextBox(),Width=200,Height=100};modal.Show();Escape(main);Check(navigator.Current==products&&modal.IsVisible,"modal tem precedência sobre ESC de navegação");Escape((UIElement)modal.Content);Check(!modal.IsVisible&&navigator.Current==products,"ESC fecha janela filha primeiro");
  var popupText=new TextBox();var popupModal=new Window{Owner=products,Opacity=0,ShowInTaskbar=false,Content=popupText,Width=300,Height=150};SuggestionPopup.Attach(popupText,"Fixture",query=>new List<SuggestionOption>{new("Produto","Fixture",new Product())},_=>{});popupModal.Show();popupModal.Activate();popupText.Focus();popupText.Text="Produto";await Task.Delay(600);var popupEscape=Escape(popupText,Keyboard.PreviewKeyDownEvent);Check(popupEscape.Handled&&popupModal.IsVisible&&navigator.Current==products,"ESC autocomplete em modal fecha sugestões primeiro");Escape(popupText);Check(!popupModal.IsVisible&&navigator.Current==products,"ESC seguinte fecha modal com sugestões já fechadas");
  Escape(list);Check(navigator.Current==null&&((FrameworkElement)main.FindName("DashboardView")).Visibility==Visibility.Visible,"Produtos ESC Início");
  foreach(var method in new[]{"Customers_Click","Debtors_Click","ClosedSessions_Click","Search_Click"}){await Open(method);Escape((UIElement)((ContentControl)main.FindName("PageHost")).Content);Check(navigator.Current==null,"ESC Início "+method);await Until(()=>!Loader(main).Loading&&!(bool)Field(main,"refreshBusy"));}
  Invoke(main,"OpenOrders",(object?)null,(object?)null);await Until(()=>navigator.Current!=null&&Loader(navigator.Current).Result!=null&&!Loader(navigator.Current).Loading);Escape((UIElement)((ContentControl)main.FindName("PageHost")).Content);Check(navigator.Current==null,"Pedidos ESC Início");
  Escape(main);Check(main.IsVisible&&navigator.Current==null,"Início ESC continua aberto e sem ação");
  await Open("Customers_Click");var customerSearch=(TextBox)navigator.Current!.FindName("SearchBox");var preview=Escape(customerSearch,Keyboard.PreviewKeyDownEvent);Check(!preview.Handled,"autocomplete fechado deixa ESC chegar à navegação");Escape(customerSearch);Check(navigator.Current==null,"ESC com pesquisa focada retorna ao Início");
  var dirty=new Window{Content=new TextBox(),Opacity=0,ShowInTaskbar=false,ShowActivated=false};string value="old";UnsavedChanges.Attach(dirty,()=>{},()=>value);navigator.Show(dirty);await Task.Delay(50);value="new";dirty.Dispatcher.BeginInvoke(()=>{var guard=Application.Current.Windows.OfType<UnsavedChangesWindow>().Single();Logical((DependencyObject)guard.Content).OfType<Button>().Single(x=>Equals(x.Content,"Continuar editando")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));});Escape(main);Check(navigator.Current==dirty&&UnsavedChanges.IsDirty(dirty),"proteção alteração não salva permite continuar");value="old";Escape(main);Check(navigator.Current==null,"ESC navega depois de resolver alteração");
  await Open("Products_Click");products=(ProductsWindow)navigator.Current!;loader=Loader(products);list=(ListBox)products.FindName("ProductsGrid");content=(FrameworkElement)((ContentControl)main.FindName("PageHost")).Content;
  local.Update(d=>{d.Products.Add(new(){Description="Catálogo novo revisão",Category="Categoria A",Price=20});return 0;});loader.LoadMore();await Until(()=>loader.Page==1&&!loader.Loading&&loader.Result!=null&&loader.Result.Data.GetProperty("DataVersion").GetInt64()==local.Load().DataVersion,"revision reset");Check(list.Items.Count==30,"RemoteRevision alterada reinicia sem misturar páginas");
  delay=400;loader.LoadMore();Escape(list);await Task.Delay(550);Check(navigator.Current==null&&!loader.Loading,"sair de Produtos cancela carga pendente");delay=30;
  await Open("Products_Click");Check(Loader(navigator.Current!).Page==1,"retornar Produtos abre uma única primeira carga");Invoke(main,"Home_Click",main,new RoutedEventArgs());await Until(()=>active==0&&!Loader(main).Loading&&!(bool)Field(main,"refreshBusy"));timer.Stop();Check(pulses>50,"Dispatcher responsivo durante consultas e scroll");Check(calls.All(x=>!x.StartsWith("GET /data")),"/data navegação normal ZERO");File.WriteAllLines(Path.Combine(folder,"navigation-calls.log"),calls);
  await Open("Products_Click");products=(ProductsWindow)navigator.Current!;loader=Loader(products);list=(ListBox)products.FindName("ProductsGrid");var edited=(Product)list.Items[0];var watchdog=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};watchdog.Tick+=(_,_)=>{foreach(var w in Application.Current.Windows.OfType<Window>().Where(w=>w.IsVisible&&w!=main&&w!=products).ToArray()){Console.WriteLine("EDIT WINDOW "+w.GetType().Name+" "+w.Title+" "+string.Join(" / ",Logical(w).OfType<TextBlock>().Select(x=>x.Text)));if(w is AlertWindow)w.Close();}};watchdog.Start();products.Dispatcher.BeginInvoke(()=>{Console.WriteLine("EDIT CALLBACK");var editor=Application.Current.Windows.OfType<ProductEditor>().Single();((TextBox)Field(editor,"cost")).Text="23,00";Invoke(editor,"Save");});Invoke(products,"Edit",edited);watchdog.Stop();Console.WriteLine("EDIT RETURNED");await Until(()=>loader.Result!=null&&!loader.Loading&&list.Items.OfType<Product>().Any(x=>x.Id==edited.Id&&x.Cost==23));Check(local.Load().Products.Single(x=>x.Id==edited.Id).Cost==23&&navigator.Current==products,"Editar Produto e retornar à lista com custo persistido");main.Close();
 }
}



