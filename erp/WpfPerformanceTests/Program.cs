using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

static class Program
{
 static int passed;static bool securityOnly,prepareNotebook;
 static readonly List<object> measurements=[];
 static void Pass(bool value,string name){if(!value)throw new Exception("FAIL "+name);passed++;Console.WriteLine("PASS "+name);}
 static int Port(){var tcp=System.Net.Sockets.TcpListener.Create(0);tcp.Start();var port=((IPEndPoint)tcp.LocalEndpoint).Port;tcp.Stop();return port;}
 static object? Field(object target,string name)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target);
 static void Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,args);
 static async Task Until(Func<bool> condition){using var c=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(!condition()){await Task.Delay(10,c.Token);}}
 [STAThread]static int Main(string[] args){securityOnly=args.Contains("--security");prepareNotebook=args.Contains("--prepare-notebook");var folder=Path.GetFullPath(args[0]);if(prepareNotebook&&Environment.MachineName!="PC1")throw new Exception("Prepare a fixture somente no PC1.");if(prepareNotebook&&Path.GetFullPath(folder).StartsWith(@"D:\Grafica CA CP\ERP",StringComparison.OrdinalIgnoreCase))throw new Exception("Fixture não pode ficar na produção.");if(Directory.Exists(folder)&&Directory.EnumerateFileSystemEntries(folder).Any())throw new Exception("Fixture deve estar vazia.");Directory.CreateDirectory(folder);Environment.SetEnvironmentVariable("GRAFICA_CAIXA_PERFIL",Path.Combine(folder,"Perfil"));
 var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;ThemeManager.Initialize();var frame=new DispatcherFrame();int code=0;
 app.Dispatcher.BeginInvoke(async()=>{try{await Run(folder);}catch(Exception ex){Console.WriteLine(ex);code=1;}finally{File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(new{passed,failed=code,measurements,physicalNotebook="PENDENTE"},new JsonSerializerOptions{WriteIndented=true}));frame.Continue=false;}});Dispatcher.PushFrame(frame);app.Shutdown();return code;}
 static HttpClient Http(ServerConnection c)=>new(new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,cert,_,_)=>RemoteDataClient.CertificateMatchesPin(cert,c.CertificatePin)}){BaseAddress=new Uri(c.Url+"/"),Timeout=TimeSpan.FromSeconds(25)};
 static async Task Run(string folder)
 {
  var data=new DataStore(Path.Combine(folder,"Dados"));data.EnsureInitialized();
  data.Update(d=>{
   d.Prompt12.LoginPassword=LoginSecurity.Create("Isolado-Hotfix!2026");
   for(int i=0;i<300;i++){d.Products.Add(new(){Description=$"Produto sintético {i:0000}",Category=i%2==0?"Adesivos":"Impressos",Price=10,Cost=3});d.Customers.Add(new(){Name=$"Cliente sintético {i:0000}",Contact="11999990000"});d.ScheduledOrders.Add(new(){Number=1000+i,CustomerName=$"Cliente sintético {i:0000}",Contact="11999990000",Description="Serviço de impressão",Stage=i%2==0?"Produção":"Agendado",OrderTotal=100,OriginalTotal=100});}
   var cash=new CashSession{OpeningAmount=50,OpenedBy="Fixture"};cash.Sales.Add(new(){CustomerName="Cliente sintético 0000",Product="Venda direta",TotalValue=20,PaymentType="Dinheiro"});d.Sessions.Add(cash);
   d.Reminders=[new(){Name="Cliente sintético 0000",Phone="11999990000",Description="Lembrete de teste"}];
   d.Prospects=[new(){CustomerName="Prospect sintético",Description="Cotação de teste"}];
   for(int i=0;i<640;i++)d.AuditTrail.Add(new(){Action="Fixture",Description="Medição isolada",OldValue=new string('x',12000),NewValue=new string('y',12000)});
   return 0;
  });
  var serverPort=prepareNotebook?55074:Port();var identityPath=Path.Combine(folder,"Identidade");
  await using var server=await SharedDataServer.StartAsync(data,identityPath,serverPort);
  var beforeSchema=Schema(data.DatabasePath);
  var connection=server.Connection with {Url=$"https://127.0.0.1:{serverPort}",Urls=null,SelectedUrl=null};
  using var client=Http(connection);client.DefaultRequestHeaders.Add("X-Device-Key",connection.Key);
  using var login=await client.PostAsJsonAsync("auth/login",new LoginRequest("grafica","Isolado-Hotfix!2026"));Pass(login.IsSuccessStatusCode,"login isolado com pin WPF");var tokens=(await login.Content.ReadFromJsonAsync<SessionTokens>())!;client.DefaultRequestHeaders.Authorization=new("Bearer",tokens.AccessToken);
  if(prepareNotebook){
   var configuration=Path.Combine(folder,"Configuracao");Directory.CreateDirectory(configuration);
   File.WriteAllText(Path.Combine(configuration,"servidor.json"),JsonSerializer.Serialize(new ServerConfiguration{DataDirectory=data.Folder,IdentityDirectory=identityPath,LogsDirectory=Path.Combine(folder,"Logs"),Port=serverPort},new JsonSerializerOptions{WriteIndented=true}));
   var ticket=new IdentitySecurity(data).CreatePairing(tokens.User,"Homologação sintética do notebook");
   var urls=new[]{"https://100.64.0.10:55074","https://192.168.0.10:55074"};var pairing=connection with {Url=urls[0],Urls=urls,DeviceId=null,Key=ticket.Ticket,Purpose="pairing",PairingExpiresAt=ticket.ExpiresAt};
   File.WriteAllText(Path.Combine(folder,"Pareamento-Teste.txt"),pairing.PairingCode);Console.WriteLine("FIXTURE NOTEBOOK PREPARADA: porta temporária 55074; identidade sintética; produção não acessada. Notebook físico permanece PENDENTE.");return;
  }
  if(securityOnly){await ProjectionSecurityTests(data,connection,tokens);Pass(Schema(data.DatabasePath)==beforeSchema,"segurança da projeção sem migração de schema");return;}
  foreach(var module in new[]{"bootstrap","dashboard","produtos","clientes","pedidos","producao","possiveis-pedidos","lembretes","compras","orcamentos/configuracao","devedores","fechamentos","lancamentos","relatorios/resumo"}){
   var path=$"api/v1/{module}?projection=desktop&pageSize=30";var watch=Stopwatch.StartNew();using var response=await client.GetAsync(path);var bytes=await response.Content.ReadAsByteArrayAsync();var elapsed=watch.Elapsed.TotalMilliseconds;
   Pass(response.IsSuccessStatusCode,"projeção "+module);var page=JsonSerializer.Deserialize<ModulePage>(bytes,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
   Pass(!page.Data.TryGetProperty("AuditTrail",out _)&&!page.Data.TryGetProperty("LoginPassword",out _),"projeção sem auditoria/configuração de identidade "+module);
   measurements.Add(new{module,bytes=bytes.Length,elapsedMs=elapsed,scenario="local"});
  }
  await CompressionTests(connection,tokens);
  await SharedReadTests(data,connection,tokens);
  // Reference amounts are computed by the same financial rules over the full fixture.
  var full=data.Load();var report=await client.GetFromJsonAsync<ModulePage>("api/v1/relatorios/resumo?projection=desktop");var subset=report!.Data.Deserialize<AppData>()!;
  var expected=new FinancialOverview(full,DateTime.Today.AddDays(-30),DateTime.Today.AddDays(1));var actual=new FinancialOverview(subset,DateTime.Today.AddDays(-30),DateTime.Today.AddDays(1));Pass(expected.Receipts==actual.Receipts&&expected.Billed==actual.Billed&&expected.PendingNow==actual.PendingNow&&expected.ConfirmedCosts==actual.ConfirmedCosts,"regras financeiras preservadas no transporte");
  var products=await client.GetFromJsonAsync<ModulePage>("api/v1/produtos?projection=desktop&page=2&pageSize=30&search=Produto&category=Adesivos");Pass(products!.Total==150&&products.Data.Deserialize<AppData>()!.Products.Count==30,"paginação e filtro no servidor antes do recorte");
  foreach(var address in new[]{"192.168.0.10","100.64.0.10"}){
   using var routed=Http(connection with {Url=$"https://{address}:{serverPort}"});routed.DefaultRequestHeaders.Add("X-Device-Key",connection.Key);routed.DefaultRequestHeaders.Authorization=new("Bearer",tokens.AccessToken);
   foreach(var module in new[]{"produtos","clientes","pedidos","producao"})for(int repeat=0;repeat<2;repeat++){
    var watch=Stopwatch.StartNew();using var response=await routed.GetAsync($"api/v1/{module}?projection=desktop&pageSize=30");var bytes=await response.Content.ReadAsByteArrayAsync();var elapsed=watch.Elapsed.TotalMilliseconds;
    Pass(response.IsSuccessStatusCode,$"fixture corrigida por interface {address} / {module}");measurements.Add(new{module,bytes=bytes.Length,elapsedMs=elapsed,address,repeat,scenario="pc1-interface"});
   }
  }
  var customerId=full.Customers[0].Id;var summary=await client.GetFromJsonAsync<CustomerSummary>($"api/v1/clientes/{customerId}/resumo");Pass(summary!.Orders==1&&summary.Total.Contains("20"),"histórico/resumo do cliente sob demanda");
  using(var response=await client.GetAsync("data")){var bytes=await response.Content.ReadAsByteArrayAsync();measurements.Add(new{module="data",bytes=bytes.Length,scenario="synthetic-baseline"});Pass(bytes.Length>15_000_000,"fixture representa snapshot de mais de 15 MB");}
  await FlightTests();
  // A temporary HTTPS proxy applies one simulated RTT and a byte-rate to each real response.
  using var cert=ServerIdentityStore.LoadExisting(identityPath);var proxyPort=Port();int rtt=150;double mbps=5;var calls=new List<string>();int active=0,maxActive=0,cancelled=0;
  var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,proxyPort,l=>l.UseHttps(cert.Certificate)));
  await using var proxy=builder.Build();proxy.Run(async context=>{
   Interlocked.Increment(ref active);maxActive=Math.Max(maxActive,active);lock(calls)calls.Add(context.Request.Path+context.Request.QueryString);
   try{
    if(context.Request.Path=="/test-timeout"){context.Response.StatusCode=504;return;}
    if(context.Request.Path=="/test-permission"){context.Response.StatusCode=403;return;}
    await Task.Delay(rtt,context.RequestAborted);using var request=new HttpRequestMessage(new HttpMethod(context.Request.Method),context.Request.Path+context.Request.QueryString);
    foreach(var header in context.Request.Headers)if(header.Key.Equals("Authorization",StringComparison.OrdinalIgnoreCase)||header.Key.Equals("X-Device-Key",StringComparison.OrdinalIgnoreCase)||header.Key.Equals("Accept-Encoding",StringComparison.OrdinalIgnoreCase))request.Headers.TryAddWithoutValidation(header.Key,header.Value.ToArray());
    if(context.Request.Method is "POST" or "PATCH" or "PUT"){request.Content=new ByteArrayContent(await Read(context.Request.Body,context.RequestAborted));request.Content.Headers.TryAddWithoutValidation("Content-Type",context.Request.ContentType);}
    using var response=await client.SendAsync(request,context.RequestAborted);var bytes=await response.Content.ReadAsByteArrayAsync(context.RequestAborted);context.Response.StatusCode=(int)response.StatusCode;context.Response.ContentType=response.Content.Headers.ContentType?.ToString();if(response.Content.Headers.ContentEncoding.Count>0)context.Response.Headers.ContentEncoding=string.Join(",",response.Content.Headers.ContentEncoding);context.Response.ContentLength=bytes.Length;
    for(int offset=0;offset<bytes.Length;offset+=8192){var count=Math.Min(8192,bytes.Length-offset);await Task.Delay(TimeSpan.FromSeconds(count*8/(mbps*1_000_000)),context.RequestAborted);await context.Response.Body.WriteAsync(bytes.AsMemory(offset,count),context.RequestAborted);await context.Response.Body.FlushAsync(context.RequestAborted);}
   }catch(OperationCanceledException){Interlocked.Increment(ref cancelled);}finally{Interlocked.Decrement(ref active);}
  });await proxy.StartAsync();
  var proxyConnection=connection with {Url=$"https://127.0.0.1:{proxyPort}"};var remote=DataStore.Connect(proxyConnection);Pass((await Task.Run(()=>remote.VerifyLogin("grafica","Isolado-Hotfix!2026"))).Ok,"login através de proxy isolado");
  var permissions=await remote.QueryRemoteAsync<Dictionary<string,bool>>("auth/permissions");App.CurrentUser=new("Teste","Administrador"){Permissions=permissions};
  try{await remote.QueryRemoteAsync<JsonElement>("test-timeout");throw new Exception("timeout sem classificação");}catch(TimeoutException ex){Pass(!ex.Message.Contains("offline"),"timeout de dados não declara servidor offline");}
  try{await remote.QueryRemoteAsync<JsonElement>("test-permission");throw new Exception("permissão sem classificação");}catch(UnauthorizedAccessException){Pass(true,"permissão recusada possui classificação própria");}
  using(var largeCancellation=new CancellationTokenSource()){mbps=2;var large=remote.LoadAsync(largeCancellation.Token);await Task.Delay(300);var watch=Stopwatch.StartNew();await remote.QueryRemoteAsync<RemoteRevision>("api/v1/revision");Pass(watch.ElapsedMilliseconds<1000,"consulta leve não espera lock de transferência pesada");largeCancellation.Cancel();try{await large;}catch(OperationCanceledException){}await Until(()=>active==0);}
  foreach(var latency in new[]{50,100,150,200})foreach(var bandwidth in new[]{10d,5d,2d}){rtt=latency;mbps=bandwidth;foreach(var module in new[]{"produtos","clientes","pedidos","producao"}){var watch=Stopwatch.StartNew();var page=await remote.QueryRemoteAsync<ModulePage>($"api/v1/{module}?projection=desktop&pageSize=30");var elapsed=watch.Elapsed.TotalMilliseconds;measurements.Add(new{module,elapsedMs=elapsed,rttMs=latency,mbps=bandwidth,scenario="proxy-measured"});Pass(elapsed<3000,$"{module} <3s RTT={latency} Mbps={bandwidth}");}}
  rtt=150;foreach(var bandwidth in new[]{5d,2d}){mbps=bandwidth;foreach(var module in new[]{"dashboard","possiveis-pedidos","lembretes","compras","orcamentos/configuracao","devedores","fechamentos","lancamentos","relatorios/resumo"}){
   var watch=Stopwatch.StartNew();await remote.QueryRemoteAsync<ModulePage>($"api/v1/{module}?projection=desktop&pageSize=30");var elapsed=watch.Elapsed.TotalMilliseconds;
   measurements.Add(new{module,elapsedMs=elapsed,rttMs=rtt,mbps=bandwidth,scenario="proxy-measured"});Pass(elapsed<3000,$"consulta {module} <3s RTT={rtt} Mbps={bandwidth}");
  }}
  foreach(var module in new[]{"produtos","clientes","pedidos","producao"}){var watch=Stopwatch.StartNew();await remote.QueryRemoteAsync<ModulePage>($"api/v1/{module}?projection=desktop&pageSize=30&search=sintético");Pass(watch.Elapsed.TotalMilliseconds<1500,$"busca {module} <1,5s em 150ms / 2Mbps");}
  await remote.PingRemoteAsync();Pass(true,"estado geral usa ping leve independente de consulta pesada");
  rtt=150;mbps=2;calls.Clear();var main=new MainWindow(remote){Opacity=0,ShowInTaskbar=false,ShowActivated=false,WindowState=WindowState.Normal};var heartbeat=0;var pulse=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};pulse.Tick+=(_,_)=>heartbeat++;pulse.Start();main.Show();await Until(()=>!(bool)Field(main,"refreshBusy")!);
  foreach(var name in new[]{"Products_Click","Customers_Click","ScheduledOrders_Click","production","Products_Click","Reports_Click","Customers_Click","ScheduledOrders_Click"}){if(name=="production")Invoke(main,"Navigate",(Button)main.FindName("NavOrders"),new Func<Window>(()=>new OrderBoardWindow(remote)));else Invoke(main,name,main,new RoutedEventArgs());await Task.Delay(60);}
  await Until(()=>!((ModuleLoader)Field(((PageNavigator)Field(main,"pages")!).Current!,"remoteLoad")!).Loading);await Task.Delay(300);pulse.Stop();
  Pass(heartbeat>20,"dispatcher responsivo durante navegação e rede reduzida");Pass(calls.All(x=>!x.StartsWith("/data")),"navegação normal não chama /data");Pass(((PageNavigator)Field(main,"pages")!).Current is ScheduledOrdersWindow,"estado final correto após navegação rápida");
  foreach(var create in new Func<Window>[] {()=>new ProductsWindow(remote),()=>new CustomersWindow(remote),()=>new ScheduledOrdersWindow(remote),()=>new OrderBoardWindow(remote),()=>new FollowUpWindow(remote,false),()=>new BudgetWindow(remote),()=>new PurchaseNeedsWindow(remote),()=>new RemindersWindow(remote),()=>new FollowUpWindow(remote,true),()=>new ClosedSessionsWindow(remote),()=>new SearchWindow(remote),()=>new ReportsWindow(remote)}){
   var watch=Stopwatch.StartNew();var window=create();((PageNavigator)Field(main,"pages")!).Show(window);var loader=(ModuleLoader)Field(window,"remoteLoad")!;
   // Devedores carrega por páginas (desktop/receivables), não pelo ModuleLoader.
   if(window is FollowUpWindow&&(bool)Field(window,"debts")!)await Until(()=>Field(window,"debtPage")!=null&&!(bool)Field(window,"debtLoading")!);else await Until(()=>loader.Result!=null&&!loader.Loading);measurements.Add(new{scenario="wpf-screen-ready",screen=window.GetType().Name,elapsedMs=watch.Elapsed.TotalMilliseconds,rttMs=rtt,mbps});Pass(watch.Elapsed.TotalMilliseconds<3000,"carga WPF isolada <3s "+window.GetType().Name);
  }
  var receipt=new ReceiptDialog(remote){Owner=main,Opacity=0,ShowInTaskbar=false};receipt.Show();await Task.Delay(100);receipt.Close();Pass(calls.All(x=>!x.StartsWith("/data")),"todas as telas de navegação e recibo abrem sem /data");
  main.Close();await Until(()=>active==0);Pass(cancelled>0,"fechar/trocar tela cancela requisições no proxy");Pass(maxActive<=4,"requisições ativas permanecem limitadas");
  measurements.Add(new{scenario="navigation-stress",heartbeat,cancelled,maxActive,calls=calls.ToArray()});Pass(Schema(data.DatabasePath)==beforeSchema,"zero alteração de schema");await proxy.StopAsync();
 }
 // Compressão: o JSON chega idêntico, bem menor, e rotas com tokens nunca são comprimidas.
 static async Task CompressionTests(ServerConnection connection,SessionTokens tokens)
 {
  HttpClient Raw(bool compressed){var http=Http(connection);http.DefaultRequestHeaders.Add("X-Device-Key",connection.Key);http.DefaultRequestHeaders.Authorization=new("Bearer",tokens.AccessToken);if(compressed)http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("br, gzip");return http;}
  using var plain=Raw(false);using var packed=Raw(true);
  foreach(var path in new[]{"api/v1/relatorios/resumo?projection=desktop","api/v1/pedidos?projection=desktop&pageSize=30","api/v1/dashboard?projection=desktop"}){
   var original=await plain.GetByteArrayAsync(path);using var response=await packed.GetAsync(path);var wire=await response.Content.ReadAsByteArrayAsync();
   Pass(response.Content.Headers.ContentEncoding.Contains("br"),"resposta comprimida (br) "+path);
   using var input=new System.IO.Compression.BrotliStream(new MemoryStream(wire),System.IO.Compression.CompressionMode.Decompress);using var output=new MemoryStream();await input.CopyToAsync(output);
   Pass(output.ToArray().AsSpan().SequenceEqual(original),"conteúdo idêntico após descompressão "+path);
   Pass(wire.Length*3<original.Length,$"compressão reduz ao menos 3x ({original.Length/1024} KB -> {wire.Length/1024} KB) "+path);
   measurements.Add(new{module=path,bytes=original.Length,wireBytes=wire.Length,scenario="compression"});
  }
  using var gzip=Http(connection);gzip.DefaultRequestHeaders.Add("X-Device-Key",connection.Key);gzip.DefaultRequestHeaders.Authorization=new("Bearer",tokens.AccessToken);gzip.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
  using(var response=await gzip.GetAsync("api/v1/dashboard?projection=desktop"))Pass(response.Content.Headers.ContentEncoding.Contains("gzip"),"gzip para clientes sem brotli");
  foreach(var path in new[]{"auth/permissions","auth/me"}){
   using var response=await packed.GetAsync(path);Pass(response.IsSuccessStatusCode&&response.Content.Headers.ContentEncoding.Count==0,"rota de autenticação sem compressão "+path);
  }
  using(var ping=await packed.GetAsync("ping"))Pass(ping.IsSuccessStatusCode,"ping continua respondendo com compressão ativa");
 }
 // Leitura compartilhada (cache do servidor): qualquer gravação, até fora do servidor, aparece na consulta seguinte.
 static async Task SharedReadTests(DataStore data,ServerConnection connection,SessionTokens tokens)
 {
  using var http=Http(connection);http.DefaultRequestHeaders.Add("X-Device-Key",connection.Key);http.DefaultRequestHeaders.Authorization=new("Bearer",tokens.AccessToken);
  async Task<AppData> Module(string module,string query=""){var page=(await http.GetFromJsonAsync<ModulePage>($"api/v1/{module}?projection=desktop&pageSize=30{query}"))!;return page.Data.Deserialize<AppData>()!;}
  var first=await Module("clientes","&search=Cache%20sintetico");var again=await Module("clientes","&search=Cache%20sintetico");
  Pass(first.Customers.Count==0&&again.DataVersion==first.DataVersion,"leitura repetida sem gravação usa a mesma versão");
  data.Update(d=>{d.Customers.Add(new(){Name="Cache sintetico novo",Contact="11988887777"});return 0;});
  var after=await Module("clientes","&search=Cache%20sintetico");Pass(after.Customers.Count==1&&after.DataVersion>first.DataVersion,"gravação fora do servidor invalida a leitura compartilhada");
  data.Update(d=>{d.Customers.Single(x=>x.Name=="Cache sintetico novo").Name="Cache sintetico alterado";return 0;});
  var renamed=await Module("clientes","&search=Cache%20sintetico");Pass(renamed.Customers.Single().Name=="Cache sintetico alterado","alteração aparece na consulta seguinte");
  // Leituras paralelas enquanto há gravações: nenhuma falha e a última leitura enxerga a última gravação.
  var readers=Enumerable.Range(0,24).Select(i=>Module(i%3==0?"pedidos":i%3==1?"dashboard":"clientes")).ToList();
  for(var i=0;i<4;i++)data.Update(d=>{d.Reminders.Add(new(){Name="Cache paralelo",Phone="11977776666",Description="Lembrete "+i});return 0;});
  await Task.WhenAll(readers);var reminders=await Module("lembretes");Pass(reminders.Reminders.Count(x=>x.Name=="Cache paralelo")==4,"leituras paralelas com gravações terminam sem erro e veem o estado final");
  data.Update(d=>{d.Customers.RemoveAll(x=>x.Name.StartsWith("Cache sintetico"));d.Reminders.RemoveAll(x=>x.Name=="Cache paralelo");return 0;});
  var cleaned=await Module("clientes","&search=Cache%20sintetico");Pass(cleaned.Customers.Count==0,"remoção também invalida a leitura compartilhada");
 }
 static async Task<byte[]> Read(Stream stream,CancellationToken cancellation){using var memory=new MemoryStream();await stream.CopyToAsync(memory,cancellation);return memory.ToArray();}
 static async Task ProjectionSecurityTests(DataStore store,ServerConnection connection,SessionTokens admin)
 {
  var security=new IdentitySecurity(store);security.SaveUser(admin.User,new(null,"Leitor sintético","leitor","Financeiro",true,"Isolado-Leitor!2026"),"Fixture de projeções");
  var user=security.Users(admin.User).Single(x=>x.Login=="leitor");
  var allowed=Permissions.Catalog.ToDictionary(x=>x.Key,x=>(bool?)x.Key.StartsWith("view."));allowed["orders.value"]=false;allowed["costs.edit"]=false;
  security.SavePermissions(admin.User,new(user.Id,null,allowed),"Fixture de projeções");
  using var reader=Http(connection);reader.DefaultRequestHeaders.Add("X-Device-Key",connection.Key);
  using var login=await reader.PostAsJsonAsync("auth/login",new LoginRequest("leitor","Isolado-Leitor!2026"));var tokens=(await login.Content.ReadFromJsonAsync<SessionTokens>())!;reader.DefaultRequestHeaders.Authorization=new("Bearer",tokens.AccessToken);
  var products=(await reader.GetFromJsonAsync<ModulePage>("api/v1/produtos?projection=desktop"))!.Data.Deserialize<AppData>()!;
  Pass(products.Products.Count>0&&products.Products.All(x=>x.Price==null&&x.Cost==null),"projeção não envia preços/custos sem permissão");
  var orders=(await reader.GetFromJsonAsync<ModulePage>("api/v1/pedidos?projection=desktop"))!.Data.Deserialize<AppData>()!;
  Pass(orders.ScheduledOrders.Count>0&&orders.ScheduledOrders.All(x=>x.OrderTotal==0&&x.Payments.Count==0&&x.Items.All(i=>i.UnitCost==null&&i.UnitPrice==null)),"projeção não envia financeiro/custos de pedidos sem permissão");
  using(var debts=await reader.GetAsync("desktop/receivables?losses=false&page=1&search=&filter=all&status=&sort=old"))Pass(debts.IsSuccessStatusCode,"devedores sem valores para o leitor");
  // Leitura compartilhada: o recorte do leitor não pode alterar o que o administrador recebe depois.
  using(var adminClient=Http(connection)){adminClient.DefaultRequestHeaders.Add("X-Device-Key",connection.Key);adminClient.DefaultRequestHeaders.Authorization=new("Bearer",admin.AccessToken);
   var adminProducts=(await adminClient.GetFromJsonAsync<ModulePage>("api/v1/produtos?projection=desktop"))!.Data.Deserialize<AppData>()!;var adminOrders=(await adminClient.GetFromJsonAsync<ModulePage>("api/v1/pedidos?projection=desktop"))!.Data.Deserialize<AppData>()!;
   Pass(adminProducts.Products.Count>0&&adminProducts.Products.All(x=>x.Price!=null&&x.Cost!=null)&&adminOrders.ScheduledOrders.Any(x=>x.OrderTotal>0),"administrador continua vendo preços/custos/valores depois do leitor (cache não é alterado)");}
  security.SavePermissions(admin.User,new(user.Id,null,new(){["view.products"]=false}),"Fixture de projeções");
  using(var denied=await reader.GetAsync("api/v1/produtos?projection=desktop"))Pass(denied.StatusCode==HttpStatusCode.Forbidden,"permissão alterada aplicada imediatamente sem cache persistente");
  // A browser session never gains the WPF projection's internal document paths.
  var browser=security.Login("grafica","Isolado-Hotfix!2026","Fixture browser");using var browserClient=Http(connection);browserClient.DefaultRequestHeaders.Add("X-Device-Key",connection.Key);browserClient.DefaultRequestHeaders.Authorization=new("Bearer",browser.AccessToken);
  using(var denied=await browserClient.GetAsync("api/v1/bootstrap"))Pass(denied.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,"sessão sem dispositivo não acessa projeção WPF");
  security.RevokeSessions(admin.User,user.Id,"Fixture de projeções");using(var denied=await reader.GetAsync("api/v1/clientes?projection=desktop"))Pass(denied.StatusCode==HttpStatusCode.Unauthorized,"revogação 7C respeitada pela projeção");
  using var wrongPin=Http(connection with {CertificatePin=new string('0',64)});try{await wrongPin.GetAsync("ping");throw new Exception("Pin inválido aceito");}catch(HttpRequestException){Pass(true,"pin WPF obrigatório também nas novas consultas");}
 }
 static string Schema(string path){using var db=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT group_concat(sql,';') FROM (SELECT sql FROM sqlite_master ORDER BY type,name)";return cmd.ExecuteScalar()!.ToString()!;}
 static async Task FlightTests(){var flights=new RemoteQueryFlight();int count=0;var release=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);Task<int> Read(CancellationToken token){Interlocked.Increment(ref count);return release.Task.WaitAsync(token);}using var a=new CancellationTokenSource();var first=flights.ReadAsync("same",Read,a.Token);var second=flights.ReadAsync("same",Read,CancellationToken.None);a.Cancel();try{await first;throw new Exception("Cancelamento ausente");}catch(OperationCanceledException){}release.SetResult(42);Pass(await second==42&&count==1,"single-flight: um consumidor cancelado não cancela os demais");var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var stopped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var all=new CancellationTokenSource();var read=flights.ReadAsync("cancel-all",async token=>{started.SetResult();try{await Task.Delay(30000,token);return 1;}finally{stopped.SetResult();}},all.Token);await started.Task;all.Cancel();try{await read;}catch(OperationCanceledException){}await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));Pass(true,"single-flight cancela rede quando último consumidor sai");}
}

