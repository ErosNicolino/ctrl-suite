using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
public static class ProductCostTests
{
    static int count;
    static readonly OperationContext User=OperationContext.Administrator("Teste de custos");
    static void Check(bool ok,string message){if(!ok)throw new Exception("FALHOU: "+message);Console.WriteLine("OK "+message);count++;}
    static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    static void Reject(Action a,string name){try{a();}catch(Exception ex) when(ex is InvalidOperationException or SessionInvalidException){Check(true,name);return;}throw new Exception("Não bloqueou: "+name);}
    public static void Run()
    {
        CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("pt-BR");
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../work/products-costs-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")));Directory.CreateDirectory(root);
        var data=new AppData();var product=new Product{Description="Caneca de Porcelána Branca",Category="Canecas",Unit="un",Cost=14,Price=32};
        ProductOperations.Save(data,product,null,User);Check(data.Products.Count==1&&product.Profit==18&&product.Margin==56.25m,"produto incluído; lucro e margem");
        var search=new ProductSearch(data.Products);Check(search.Find("CANECA").Count==1&&search.Find("porcelana").Count==1&&search.Find("branca caneca").Count==1,"busca por qualquer palavra, acentos e caixa");
        Check(new Product{Price=0,Cost=1}.Margin==null,"preço zero sem divisão por zero");
        var order=new ScheduledOrder{CustomerName="Teste",Number=1,Stage="Entregue",CreatedAt=new DateTime(2026,8,1),OrderTotal=500,OriginalTotal=500,ItemCostsEnabled=true,PayAfterDelivery=true,Items=[new OrderItem{ProductId=product.Id,Product=product.Description,UnitPrice=32,Quantity=10,UnitCost=14,CostReviewed=true},new OrderItem{Product="Camiseta",Quantity=5,UnitCost=18,CostReviewed=true},new OrderItem{Product="Banner",Quantity=1,UnitCost=25,CostReviewed=true}],FreightCost=20,AdditionalCost=10};
        data.ScheduledOrders.Add(order);Check(order.ItemsCost==255&&order.TotalCost==285&&order.Profit==215,"três itens, frete, adicionais e lucro independente do pagamento");
        var changed=Clone(data.Products.Single());changed.Cost=17;changed.Price=35;ProductOperations.Save(data,changed,data.Products.Single(),User);Check(order.Items[0].UnitPrice==32&&order.Items[0].UnitCost==14&&order.Profit==215,"alteração cadastral preserva preço, custo e lucro históricos");
        Reject(()=>ProductOperations.Delete(data,data.Products.Single(),User),"exclusão de produto vinculado bloqueada");
        var inactive=Clone(data.Products.Single());inactive.Active=false;ProductOperations.Save(data,inactive,data.Products.Single(),User);Check(new ProductSearch(data.Products).Find("caneca").Count==0,"inativo fora do autocomplete");
        var disposable=new Product{Description="Sem vínculo"};ProductOperations.Save(data,disposable,null,User);ProductOperations.Delete(data,data.Products.Single(x=>x.Id==disposable.Id),User);Check(data.Products.Count==1,"exclusão de produto sem vínculo");
        order.Items[1].CostReviewed=false;Check(order.CostStatus=="Pendente"&&order.Profit==null,"um item pendente impede lucro");order.IgnoreCosts=true;Check(order.CostStatus=="Ignorado"&&order.Profit==null,"ignorado não é custo zero");order.IgnoreCosts=false;order.Items[1].CostReviewed=true;
        var negotiated=new ScheduledOrder{CustomerName="Negociação",UseItemPrices=true,DiscountPercent=10,ItemCostsEnabled=true,Items=[new OrderItem{ProductId=product.Id,Product=product.Description,Quantity=2,UnitPrice=30,UnitCost=14,CostReviewed=true}]};
        var negotiationData=new AppData{Products=[Clone(product)]};OrderOperations.Save(negotiationData,negotiated,User);var negotiatedSaved=negotiationData.ScheduledOrders.Single();Check(negotiatedSaved.OrderTotal==54&&negotiatedSaved.Profit==26&&negotiationData.Products.Single().Price==32,"preço negociado e desconto final sem alterar cadastro");
        var zero=new ScheduledOrder{ItemCostsEnabled=true,OrderTotal=100,Items=[new OrderItem{Product="Serviço",UnitCost=0,CostReviewed=true}]};Check(zero.CostStatus=="Conferido"&&zero.Profit==100,"custo zero revisado é conferido");
        var legacy=new ScheduledOrder{FreightCost=10,OrderTotal=100,Items=[new OrderItem{Product="Antigo"}]};Check(legacy.CostStatus=="Pendente"&&legacy.Profit==null,"pedido antigo permanece pendente sem confirmação automática");
        var payment=new OrderPayment{Amount=50,SaleId=Guid.NewGuid()};order.Payments.Add(payment);var cash=new CashSession{Sales=[new Sale{Id=payment.SaleId.Value,Product="Parcela do pedido #1",TotalValue=50,Units=1}]};data.Sessions.Add(cash);
        var money=JsonSerializer.Serialize(data.Sessions);var payments=JsonSerializer.Serialize(order.Payments);var draft=Clone(order);draft.Items[0].UnitCost=15;draft.FreightCost=30;
        // Custo é controle interno: corrigir custo de pedido concluído não exige mais justificativa (pedido do usuário, 19/10/2026); o motivo, se informado, vai para a auditoria.
        draft.CostChangeReason="Frete e compra conferidos";OrderOperations.SaveCosts(data,draft,User);
        Check(order.TotalCost==305&&order.Profit==195&&order.OrderTotal==500&&order.Items[1].UnitCost==18&&order.Items[2].UnitCost==25,"correção individual recalcula lucro e preserva demais itens");
        Check(money==JsonSerializer.Serialize(data.Sessions)&&payments==JsonSerializer.Serialize(order.Payments),"correção de custo não muda caixa nem pagamentos");
        Check(data.AuditTrail.Last().Reason==draft.CostChangeReason&&data.AuditTrail.Last().OldValue!=data.AuditTrail.Last().NewValue&&data.AuditTrail.Last().UserName==User.UserName,"auditoria de custo com usuário, valores e justificativa");
        Reject(()=>OrderOperations.SaveCosts(data,draft,User),"conflito de revisão bloqueia custo antigo");
        var pending=Clone(order);pending.Id=Guid.NewGuid();pending.Number=2;pending.Payments=[];pending.Items=Clone(order.Items);foreach(var i in pending.Items)i.Id=Guid.NewGuid();pending.Items[0].CostReviewed=false;data.ScheduledOrders.Add(pending);
        var ignored=Clone(pending);ignored.Id=Guid.NewGuid();ignored.Number=3;foreach(var i in ignored.Items)i.Id=Guid.NewGuid();ignored.IgnoreCosts=true;data.ScheduledOrders.Add(ignored);
        var overview=new FinancialOverview(data,new DateTime(2026,8,1),new DateTime(2026,9,1));Check(overview.CompletedCount==3&&overview.ConfirmedCount==1&&overview.PendingCount==1&&overview.IgnoredCount==1&&overview.CostsIncomplete,"cobertura identifica conferido, pendente e ignorado");
        Check(overview.ConfirmedCosts==305&&overview.CostSeries.Sum(x=>x.Value)==305&&overview.GrossProfit==overview.Billed-305,"card, série e lucro bruto usam a mesma fonte");
        var manual=new ScheduledOrder{CustomerName="Manual",OrderTotal=35,Items=[new OrderItem{Product="Impressão especial cliente X"}]};OrderOperations.Save(data,manual,User);Check(data.Products.Count==1&&data.ScheduledOrders.Last().CostStatus=="Pendente","produto manual sem cadastro automático");
        var oldDebt=new ManualDebt{CustomerName="João",Amount=100,OriginDate=new DateTime(2026,8,1)};var otherDebt=new ManualDebt{CustomerName="João",Amount=80,OriginDate=new DateTime(2026,6,1)};data.ManualDebts.AddRange([oldDebt,otherDebt]);Check((new DateTime(2026,9,22)-oldDebt.DebtDate).Days==52&&oldDebt.DaysOpen==(DateTime.Today-oldDebt.DebtDate).Days,"idade dinâmica e exemplo 52 dias");Check(data.ManualDebts.Sum(x=>x.Remaining(data))==180&&oldDebt.DebtDate!=otherDebt.DebtDate,"duas dívidas independentes e total consolidado");CollectionControl.Register(data,new CollectionContact{DebtId=oldDebt.Id,Status="Negociação",Notes="Teste"});Check(CollectionControl.Status(data,oldDebt.Id,false)=="Negociação","dívida antiga usa cobrança existente");
        var store=new DataStore(Path.Combine(root,"db"));store.EnsureInitialized();store.Update(d=>{d.Products=Clone(data.Products);d.ScheduledOrders=Clone(data.ScheduledOrders);d.Sessions=Clone(data.Sessions);d.ManualDebts=Clone(data.ManualDebts);d.CollectionContacts=Clone(data.CollectionContacts);return 0;});
        var versionBefore=store.Load().DataVersion;Reject(()=>store.Update<int>(d=>{d.Products.Clear();throw new InvalidOperationException("Falha simulada");}),"falha parcial de cadastro não persiste");Check(store.Load().DataVersion==versionBefore&&store.Load().Products.Count==1,"rollback preserva cadastro e versão");
        var loaded=store.Load();var saved=loaded.ScheduledOrders.First(x=>x.Id==order.Id);Check(saved.TotalCost==305&&saved.Profit==195&&saved.Items[0].Id==order.Items[0].Id&&saved.Items[0].ProductId==product.Id,"SQLite preserva IDs, vínculos, custo e lucro por item");Check(loaded.ScheduledOrders.Single(x=>x.Id==ignored.Id).CostStatus=="Ignorado"&&loaded.ScheduledOrders.Single(x=>x.Id==pending.Id).Profit==null,"SQLite preserva Ignorado e Pendente");Check(loaded.ManualDebts.First().DebtDate==oldDebt.DebtDate&&loaded.Products.Single().Cost==15,"SQLite preserva data da dívida e custo conferido sincronizado");
        var sale=new Sale{ProductId=product.Id,SuggestedUnitCost=17,ProductUnit="un",Product="Venda direta",Units=1,UnitValue=35};store.Update(d=>{d.Sessions[0].Sales.Add(sale);return 0;});Check(store.Load().Sessions[0].Sales.Last().SuggestedUnitCost==17,"novo lançamento preserva custo sugerido");
        Reject(()=>store.CommitRemote(new CommitRequest(Guid.NewGuid(),store.Load().DataVersion,store.Load(),4)),"cliente antigo bloqueado para preservar novos dados");
        var text=BudgetSummary.Format(product.Description,32);Check(text.Contains(product.Description)&&text.Contains("32,00")&&!text.Contains("14,00")&&!text.Contains("Custo"),"resumo WhatsApp não expõe custo");
        Check(ProductCatalog.FromProducts(data.Products).Count==0,"catálogo usa cadastro e respeita inativos");
        Exception? uiError=null;var thread=new Thread(()=>{try{Ui(store,root);}catch(Exception ex){uiError=ex;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(uiError!=null)throw uiError;
        Console.WriteLine($"TOTAL: {count} verificações. Artefatos: {root}");
    }
    static void Ui(DataStore store,string root)
    {
        var app=new App();app.InitializeComponent();
        var order=store.Load().ScheduledOrders.First();
        var dialog=new ScheduledOrderDialog(order,store);dialog.OpenCostTab();Render(dialog,Path.Combine(root,"custos.png"));Check(((TextBlock)dialog.FindName("ProfitText")).Text.Contains("195"),"aba Custo exibe lucro correto ao reabrir");
        var products=new ProductsWindow(store);Render(products,Path.Combine(root,"produtos.png"));
        var debts=new FollowUpEditor(true,new ManualDebt{OriginDate=new DateTime(2026,8,1),Amount=100},null);Render(debts,Path.Combine(root,"divida.png"));
        var report=new ReportsWindow(store);Render(report,Path.Combine(root,"relatorios.png"));
        var budget=new BudgetWindow(store);Render(budget,Path.Combine(root,"orcamento.png"));
        var fresh=new ScheduledOrderDialog(store);Render(fresh,Path.Combine(root,"pedido.png"));
        var sale=new SaleWindow(store:store);Render(sale,Path.Combine(root,"lancamento.png"));
        Check(true,"telas alteradas instanciam e renderizam sem erro WPF");
    }
    static void Render(Window window,string path)
    {
        window.ApplyTemplate();var visual=(FrameworkElement)window.Content;visual.Measure(new Size(window.Width,window.Height));visual.Arrange(new Rect(0,0,window.Width,window.Height));visual.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);var backdrop=new DrawingVisual();using(var dc=backdrop.RenderOpen())dc.DrawRectangle(window.Background??Brushes.White,null,new Rect(0,0,window.Width,window.Height));bitmap.Render(backdrop);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
}
