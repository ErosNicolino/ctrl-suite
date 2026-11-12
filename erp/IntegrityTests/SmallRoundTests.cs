using GraficaCaixa;
using GraficaCaixa.Wpf;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public static class SmallRoundTests
{
    static int passed;
    static readonly OperationContext User=OperationContext.Administrator("Operador de teste");
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static T Clone<T>(T data)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(data))!;
    static void Reject(Action action,string name){try{action();}catch(InvalidOperationException){Check(true,name);return;}throw new Exception("FALHOU: "+name);}
    public static void Run()
    {
        Exception? error=null;var thread=new Thread(()=>{try{Execute();}catch(Exception ex){error=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)throw error;
    }
    static void Execute()
    {
        CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("pt-BR");var app=new App();app.InitializeComponent();
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../work/fotos-custos-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")));Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"Impressões"));
        var store=new DataStore(Path.Combine(root,"db"));store.EnsureInitialized();store.Update(d=>{d.DocumentsRoot=root;return 0;});
        var product=new Product{Description="Caneca",Cost=14,Price=32};ProductPhotos.SaveAsync(store,product,null,null,User).GetAwaiter().GetResult();Check(store.Load().Products.Single().PhotoPath=="","produto sem foto funciona");
        var input=Path.Combine(root,"foto-grande.png");var pixels=new byte[2400*1600*4];for(var i=0;i<pixels.Length;i+=4){pixels[i]=90;pixels[i+1]=160;pixels[i+2]=220;pixels[i+3]=255;}
        var source=BitmapSource.Create(2400,1600,96,96,PixelFormats.Bgra32,null,pixels,2400*4);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(source));using(var file=File.Create(input))png.Save(file);
        var bytes=ProductPhotos.Optimize(input);var preview=ProductPhotos.Preview(bytes);Check(preview.PixelWidth==1200&&preview.PixelHeight==800&&bytes.Length<500_000,"PNG grande otimizado a 1200x800, sem distorção");
        var jpg=Path.Combine(root,"foto.jpg");File.WriteAllBytes(jpg,bytes);Check(ProductPhotos.Optimize(jpg).Length>0,"JPG suportado");var invalid=Path.Combine(root,"invalido.jpg");File.WriteAllText(invalid,"isto não é imagem");Reject(()=>ProductPhotos.Optimize(invalid),"arquivo falso recusado com mensagem amigável");Reject(()=>ProductPhotos.Resolve(root,"../../outro.jpg"),"caminho fora da pasta de fotos bloqueado");
        var original=store.Load().Products.Single();var edited=Clone(original);ProductPhotos.SaveAsync(store,edited,original,[new PhotoSlot{Bytes=bytes}],User).GetAwaiter().GetResult();var photo=store.Load().Products.Single();var first=ProductPhotos.Resolve(root,photo.PhotoPath);Check(File.Exists(first)&&!Path.IsPathRooted(photo.PhotoPath),"foto em arquivo, referência relativa persistida");
        var persisted=new DataStore(store.Folder).Load().Products.Single();Check(persisted.PhotoPath==photo.PhotoPath,"associação preservada ao reabrir banco");
        using(var c=new SqliteConnection("Data Source="+store.DatabasePath)){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM metadata WHERE key='products_v1'";var json=(string)cmd.ExecuteScalar()!;Check(json.Length<2000&&json.Contains(photo.PhotoPath)&&!json.Contains("base64"),"banco guarda somente referência da imagem");}
        edited=Clone(photo);edited.Description="Caneca renomeada";ProductPhotos.SaveAsync(store,edited,photo,null,User).GetAwaiter().GetResult();Check(File.Exists(first)&&store.Load().Products.Single().PhotoPath==photo.PhotoPath,"renomear produto mantém foto");
        // Troca por outra foto (a mesma foto de novo seria reaproveitada, sem arquivo novo).
        for(var i=0;i<pixels.Length;i+=4)pixels[i]=30;var other=ProductPhotos.Optimize(BitmapSource.Create(2400,1600,96,96,PixelFormats.Bgra32,null,pixels,2400*4));
        original=store.Load().Products.Single();edited=Clone(original);ProductPhotos.SaveAsync(store,edited,original,[new PhotoSlot{Bytes=other}],User).GetAwaiter().GetResult();photo=store.Load().Products.Single();Check(!File.Exists(first)&&File.Exists(ProductPhotos.Resolve(root,photo.PhotoPath)),"troca elimina foto anterior sem órfão");
        var stale=Clone(photo);store.Update(d=>{d.Products[0].Description="Editado em outro PC";return 0;});Reject(()=>ProductPhotos.SaveAsync(store,Clone(stale),stale,[new PhotoSlot{Bytes=bytes}],User).GetAwaiter().GetResult(),"foto respeita concorrência entre PCs");Check(Directory.GetFiles(Path.Combine(root,"Produtos","Imagens"),"*.jpg").Length==1,"falha de gravação não deixa foto órfã");
        original=store.Load().Products.Single();var shared=new Product{Description="Referência compartilhada",PhotoPath=original.PhotoPath};store.Update(d=>{d.Products.Add(shared);return 0;});edited=Clone(original);edited.PhotoPath="";ProductPhotos.SaveAsync(store,edited,original,null,User).GetAwaiter().GetResult();Check(File.Exists(ProductPhotos.Resolve(root,shared.PhotoPath)),"remover associação não apaga foto usada por outro produto");ProductPhotos.DeleteAsync(store,shared,User).GetAwaiter().GetResult();Check(Directory.GetFiles(Path.Combine(root,"Produtos","Imagens"),"*.jpg").Length==0,"última referência removida apaga imagem");Check(store.Load().Products.Count==1,"remoção da foto preserva produto");
        var data=new AppData{Company=new CompanyProfile{Name="Gráfica Cidade Dutra Ctrl A Ctrl P"},ManualDebts=[new ManualDebt{CustomerName="João",Amount=80,Description="Impressão antiga",OriginDate=new DateTime(2020,1,1)}]};var debt=data.ManualDebts.Single();var message=CollectionControl.Message(data,debt.Id,false);Check(message.Contains("João")&&message.Contains("80,00")&&message.Contains("Impressão antiga")&&message.Contains("Gráfica Cidade Dutra Ctrl A Ctrl P")&&!message.Contains("dias"),"cobrança antiga cordial com dados e assinatura");
        debt.CustomerName="null";debt.Description="undefined";message=CollectionControl.Message(data,debt.Id,false);Check(message.StartsWith("Olá, tudo bem?")&&!message.Contains("null")&&!message.Contains("undefined")&&!message.Contains("referente"),"cobrança sem campos vazios ou marcadores");
        var order=new ScheduledOrder{CustomerName="Ana",Number=7,OrderTotal=100};data.ScheduledOrders.Add(order);Check(CollectionControl.Message(data,order.Id,true).Contains("pedido #7"),"cobrança identifica número do pedido");
        try{var oldClipboard=Clipboard.GetDataObject();try{Clipboard.SetText(message);Check(Clipboard.GetText()==message,"texto copiado integralmente para o clipboard, sem envio");}finally{RestoreClipboard(oldClipboard);}}catch(System.Runtime.InteropServices.ExternalException){Console.WriteLine("PULADO área de transferência indisponível nesta sessão");}
        var cash=new CashSession{OpenedAt=DateTime.Now.AddHours(-1),OpeningAmount=1000};var money=new AppData{Sessions=[cash]};var before=new FinancialOverview(money,DateTime.Today,DateTime.Today.AddDays(1));var withdrawal=new CashWithdrawal{Amount=200,Reason="Retirada de teste",CreatedAt=DateTime.Now};CashOperations.Withdraw(money,cash.Id,withdrawal,User);Check(cash.ExpectedCash==800&&cash.WithdrawalsTotal==200,"sangria reduz dinheiro esperado de 1000 para 800");Check(withdrawal.UserName==User.UserName&&money.AuditTrail.Single().EntityId==withdrawal.Id.ToString(),"sangria registra usuário, motivo, data e auditoria");
        var after=new FinancialOverview(money,DateTime.Today,DateTime.Today.AddDays(1));Check(after.Withdrawals==200&&after.GrossProfit==before.GrossProfit&&after.ConfirmedCosts==before.ConfirmedCosts,"sangria não reduz lucro nem duplica custos");CashOperations.Withdraw(money,cash.Id,withdrawal,User);Check(cash.Withdrawals.Count==1,"repetição da sangria não duplica movimento");store.Update(d=>{d.Sessions.Add(cash);return 0;});Check(store.Load().Sessions.Single().Withdrawals.Single().UserName==User.UserName,"histórico de sangria persiste com usuário");Check(new CashWithdrawal().UserName=="","sangrias antigas não recebem usuário inventado");
        FinancialOperations.CloseCash(money,cash.Id,Clone(cash),800,User);Check(money.CashClosureVersions.Single().ExpectedCash==800&&cash.DeclaredClosingAmount==800,"fechamento usa valor após sangria sem diferença falsa");
        ReportWriter.OutputFolder=root;var report=ReportWriter.Write(cash);var pdfText=System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(report));Check(pdfText.Contains("SANGRIAS DO CAIXA")&&pdfText.Contains("Retirada de teste"),"PDF de fechamento inclui histórico e motivo da sangria");
        if(!File.Exists(Path.Combine(AppContext.BaseDirectory,"Fixtures","banco-ativo-antes.db"))){Console.WriteLine("PULADO migração: cópia privada do banco (Fixtures) não está nesta máquina");return;}
        var copy=Path.Combine(root,"migracao");Directory.CreateDirectory(copy);File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","banco-ativo-antes.db"),Path.Combine(copy,"caixa_dados.db"));
        JsonObject beforeDetails;Dictionary<string,long> counts=new();
        using(var c=new SqliteConnection("Data Source="+Path.Combine(copy,"caixa_dados.db"))){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM metadata WHERE key='order_details_v1'";beforeDetails=JsonNode.Parse((string)cmd.ExecuteScalar()!)!.AsObject();foreach(var table in new[]{"sales","cash_sessions","scheduled_orders","order_items","order_payments","customers"}){cmd.CommandText="SELECT COUNT(*) FROM "+table;counts[table]=Convert.ToInt64(cmd.ExecuteScalar());}}
        var migration=new DataStore(copy);migration.EnsureInitialized();var migrated=migration.Load();
        using(var c=new SqliteConnection("Data Source="+migration.DatabasePath)){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM metadata WHERE key='order_details_v1'";var raw=(string)cmd.ExecuteScalar()!;foreach(var value in beforeDetails.Select(x=>x.Value).OfType<JsonObject>())value.Remove("MaterialCost");Check(JsonNode.DeepEquals(beforeDetails,JsonNode.Parse(raw)),"migração remove apenas custo global, preservando todos os outros detalhes");foreach(var table in counts.Keys){cmd.CommandText="SELECT COUNT(*) FROM "+table;Check(Convert.ToInt64(cmd.ExecuteScalar())==counts[table],"migração preserva "+table);}cmd.CommandText="PRAGMA integrity_check";Check((string)cmd.ExecuteScalar()! =="ok","banco migrado permanece íntegro");}
        var archives=Directory.GetFiles(Path.Combine(copy,"Backups"),"custos-globais-antigos-*.json");Check(archives.Length==1&&JsonNode.Parse(File.ReadAllText(archives[0]))!.AsArray().Count==2,"dois custos globais exportados antes da exclusão");Check(Directory.GetFiles(Path.Combine(copy,"Backups"),"antes-remover-custo-global-*.db").Length==1,"backup SQLite antes da migração");var auditCount=migrated.AuditTrail.Count;migration.EnsureInitialized();Check(migration.Load().AuditTrail.Count==auditCount,"migração idempotente");
        var costOrder=new ScheduledOrder{ItemCostsEnabled=true,OrderTotal=500,Items=[new OrderItem{Product="Caneca",Quantity=10,UnitCost=14,CostReviewed=true},new OrderItem{Product="Camiseta",Quantity=5,UnitCost=18,CostReviewed=true},new OrderItem{Product="Banner",Quantity=1,UnitCost=25,CostReviewed=true}],FreightCost=20,AdditionalCost=10};Check(costOrder.TotalCost==285&&costOrder.Profit==215,"custos por item, frete, adicionais e lucro preservados");costOrder.Items[0].CostReviewed=false;Check(costOrder.CostStatus=="Pendente"&&costOrder.Profit==null,"pendente preservado");costOrder.IgnoreCosts=true;Check(costOrder.CostStatus=="Ignorado"&&costOrder.Profit==null,"ignorado preservado");costOrder.IgnoreCosts=false;costOrder.Items[0].CostReviewed=true;costOrder.Items[0].UnitCost=0;Check(costOrder.CostStatus=="Conferido","custo zero conferido preservado");
        var editor=new ProductEditor(product,root);Render(editor,Path.Combine(root,"produto.png"));var costEditor=new ScheduledOrderDialog(costOrder,store);costEditor.OpenCostTab();Check(costEditor.FindName("MaterialCostBox")==null,"campo antigo removido da interface");Render(costEditor,Path.Combine(root,"custos.png"));Console.WriteLine($"TOTAL: {passed} verificações. Pasta: {root}");
    }
    static void RestoreClipboard(IDataObject? value)
    {
        for(var attempt=0;attempt<10;attempt++){try{if(value!=null)Clipboard.SetDataObject(value,true);else Clipboard.Clear();return;}catch(System.Runtime.InteropServices.COMException) when(attempt<9){Thread.Sleep(50);}}
    }
    static void Render(Window window,string path)
    {
        window.ApplyTemplate();var visual=(FrameworkElement)window.Content;visual.Measure(new Size(window.Width,window.Height));visual.Arrange(new Rect(0,0,window.Width,window.Height));visual.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);var backdrop=new DrawingVisual();using(var dc=backdrop.RenderOpen())dc.DrawRectangle(window.Background??Brushes.White,null,new Rect(0,0,window.Width,window.Height));bitmap.Render(backdrop);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
}
