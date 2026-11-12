using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
static class Prompt6Tests
{
    static readonly OperationContext Admin=OperationContext.Administrator("Teste Prompt 6");static int count;
    static T Copy<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    static void Check(bool yes,string text){if(!yes)throw new Exception("FALHOU: "+text);count++;Console.WriteLine("OK "+text);}
    static void Reject(Action action,string text){try{action();}catch(InvalidOperationException){Check(true,text);return;}catch(UnauthorizedAccessException){Check(true,text);return;}catch(SessionInvalidException){Check(true,text);return;}throw new Exception("FALHOU: "+text);}
    static PaymentPart Part(string type,decimal amount)=>new(){PaymentType=type,Amount=amount};
    static (AppData,CashSession,ScheduledOrder) Scenario(decimal total=1000){var cash=new CashSession{OpeningAmount=100};var order=new ScheduledOrder{CustomerName="Cliente Teste",Contact="11998367552",Description="Pedido",OrderTotal=total,OriginalTotal=total,Number=123};return(new AppData{Sessions=[cash],ScheduledOrders=[order]},cash,order);}
    public static void Run()
    {
        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var (data,cash,order)=Scenario();var parts=new[]{Part("PIX",400),Part("Cartão de débito",200),Part("Cartão de crédito 3x",400)};
        OrderOperations.AddPayments(data,order,parts,DateTime.Now,Admin);
        Check(order.PaidAmount==1000&&order.RemainingAmount==0&&order.FullyPaid,"pedido misto quitado 400 + 200 + 400");
        Check(order.Payments.Select(p=>p.EstimatedFee).SequenceEqual(new[]{0m,3.18m,27.16m})&&order.EstimatedFees==30.34m&&order.EstimatedNet==969.66m,"taxa por modalidade e líquido estimado");
        Check(cash.SalesTotal==1000&&cash.ExpectedCash==100,"taxas não geram despesa ou desconto no caixa");
        OrderOperations.AddPayments(data,order,parts,DateTime.Now,Admin);Check(cash.Sales.Count==3,"reenvio das linhas não duplica pagamento");
        var partial=Scenario();OrderOperations.AddPayments(partial.Item1,partial.Item3,[Part("PIX",250),Part("Dinheiro",150)],DateTime.Now,Admin);Check(partial.Item3.RemainingAmount==600&&!partial.Item3.FullyPaid&&partial.Item2.ExpectedCash==250,"pagamento parcial mantém saldo e dinheiro esperado");
        Reject(()=>OrderOperations.AddPayments(partial.Item1,partial.Item3,[Part("PIX",300),Part("Cartão de débito",301)],DateTime.Now,Admin),"soma acima do saldo bloqueada antes da primeira linha");Check(partial.Item2.Sales.Count==2,"excesso não aplica parcialmente");
        Reject(()=>PaymentPolicy.Validate([Part("PIX",1.001m)],100),"precisão monetária validada");
        Reject(()=>PaymentPolicy.Validate([Part("PIX",-1)],100),"pagamento negativo bloqueado");
        Check(PaymentPolicy.Fee(500,PaymentPolicy.Rate("Cartão de crédito 1x"))==19.95m,"taxa somente sobre R$ 500 no crédito");
        Check(PaymentPolicy.Rate("Cartão de crédito 2x")==2.69m&&PaymentPolicy.Rate("Cartão de crédito 12x")==16.69m,"taxas exatas 2x e 12x");
        var original=Copy(cash.Sales[0]);var edit=Copy(original);edit.CustomerName="Nome corrigido";edit.Contact="(11) 99836-7552";edit.Product="Descrição corrigida";edit.Notes="Observação corrigida";
        FinancialOperations.EditSale(data,original,edit,"",Admin);Check(cash.Sales.Count==3&&cash.Sales[0].CustomerName==edit.CustomerName&&cash.Sales[0].Contact=="11998367552"&&cash.Sales[0].Notes==edit.Notes,"informações corrigidas sem novo lançamento");
        original=Copy(cash.Sales[0]);edit=Copy(original);edit.PaymentType="Cartão de débito";FinancialOperations.EditSale(data,original,edit,"Forma informada incorretamente",Admin);Check(cash.Sales[0].EstimatedFee==6.36m&&order.Payments[0].PaymentType==edit.PaymentType&&order.EstimatedFees==36.70m,"Pix para débito recalcula somente a linha e sincroniza pedido");
        Check(data.AuditTrail.Last().OldValue.Contains("PIX")&&JsonSerializer.Deserialize<Sale>(data.AuditTrail.Last().NewValue)!.PaymentType=="Cartão de débito"&&data.AuditTrail.Last().UserName==Admin.UserName&&data.AuditTrail.Last().Reason.Length>0,"auditoria registra antes, depois, usuário, data e justificativa");
        var credit=Copy(cash.Sales[2]);edit=Copy(credit);edit.PaymentType="Cartão de crédito 1x";FinancialOperations.EditSale(data,credit,edit,"Conferência do comprovante",Admin);credit=Copy(cash.Sales[2]);edit=Copy(credit);edit.PaymentType="Cartão de crédito 3x";FinancialOperations.EditSale(data,credit,edit,"Parcelamento corrigido",Admin);Check(cash.Sales[2].EstimatedFee==27.16m,"crédito 1x para 3x");
        edit=Copy(cash.Sales[0]);edit.PaymentType="Dinheiro";Reject(()=>FinancialOperations.EditSale(data,Copy(cash.Sales[0]),edit,"",Admin),"correção financeira exige justificativa");Reject(()=>FinancialOperations.EditSale(data,Copy(cash.Sales[0]),edit,"Correção autorizada",new OperationContext("Operador","Operador")),"permissão financeira preservada");
        edit=Copy(cash.Sales[0]);edit.Units++;Reject(()=>FinancialOperations.EditSale(data,Copy(cash.Sales[0]),edit,"Corrigir quantidade",Admin),"quantidade histórica protegida");
        FinancialOperations.CloseCash(data,cash.Id,Copy(cash),cash.ExpectedCash,Admin);var v1=data.CashClosureVersions.Single().SnapshotJson;var before=Copy(data);
        original=Copy(cash.Sales[0]);edit=Copy(original);edit.PaymentType="Dinheiro";FinancialOperations.EditSale(data,original,edit,"Retificar comprovante no caixa",Admin);
        Check(data.CashClosureVersions.Count==2&&data.CashClosureVersions[0].SnapshotJson==v1&&cash.ExpectedCash==500,"caixa fechado mantém v1 e cria v2 com totais corrigidos");FinancialIntegrity.ValidateTransition(before,data);
        FinancialOperations.CorrectSale(data,cash.Sales[0].Id,250,"Estorno parcial confirmado",Admin);Check(cash.Sales[0].Total==400&&cash.Sales.Last().Total==-150&&order.PaidAmount==850,"estorno preserva valor original e saldo");
        if(Application.Current==null){var theme=new App();theme.InitializeComponent();theme.ShutdownMode=ShutdownMode.OnExplicitShutdown;}
        Check(new SaleRow(cash.Sales.Last()).TotalForeground==ThemeManager.Get("Danger")&&new HistorySaleRow(cash.Sales.Last()).TotalForeground==ThemeManager.Get("Danger"),"negativos vermelhos em lançamentos e fechamentos");
        original=Copy(cash.Sales[0]);edit=Copy(original);edit.TotalValue=250;edit.PaymentType="PIX";FinancialOperations.EditSale(data,original,edit,"Corrigir modalidade após estorno",Admin);Check(cash.ExpectedCash==100&&order.PaidAmount==850&&order.Payments.Where(p=>p.PaymentType=="PIX").Sum(p=>p.Amount)==250,"correção mantém estorno vinculado na mesma modalidade");
        var product1=new Product{Description="Caneca de Porcelana Branca",Price=32};var product2=new Product{Description="Banner",Price=70};var catalog=new ProductSearch([product1,product2]);Check(catalog.Find("caneca").Single().Id==product1.Id&&catalog.Find("porcelana").Single().Id==product1.Id,"autocomplete compartilhado busca palavras internas");
        var sale=new Sale{CustomerName="Cliente itens",Product="Canecas + banner + manual",Items=[new OrderItem{ProductId=product1.Id,Product=product1.Description,Quantity=2,UnitPrice=32},new OrderItem{ProductId=product2.Id,Product=product2.Description,Quantity=1,UnitPrice=70},new OrderItem{Product="Serviço especial cliente X",Quantity=1,UnitPrice=16}],PendingPayments=[Part("Dinheiro",50),Part("Cartão de crédito 2x",70)]};
        var fresh=Scenario();FinancialOperations.AddSale(fresh.Item1,sale,Admin);Check(fresh.Item2.Sales.Count==2&&fresh.Item2.Sales.Sum(x=>x.Total)==120&&fresh.Item1.ManualDebts.Single().Amount==150&&fresh.Item1.ManualDebts.Single().Remaining(fresh.Item1)==30,"lançamento: dois produtos, manual, múltiplas formas e saldo");
        Check(fresh.Item2.Sales.Sum(x=>x.EstimatedFee)==1.88m&&fresh.Item1.Products.Count==0,"taxa individual e item manual sem cadastro automático");Check(ProductOperations.IsUsed(fresh.Item1,product1.Id),"produto usado em item mantém proteção de histórico");
        FinancialOperations.AddSale(fresh.Item1,sale,Admin);Check(fresh.Item2.Sales.Count==2&&fresh.Item1.ManualDebts.Count==1,"reenvio de lançamento não duplica recebimentos ou dívida");
        var zero=Copy(sale);zero.Id=Guid.NewGuid();zero.PendingPayments=[];FinancialOperations.AddSale(fresh.Item1,zero,Admin);Check(fresh.Item1.ManualDebts.Last().Items.Count==3&&fresh.Item1.ManualDebts.Last().Remaining(fresh.Item1)==150,"lançamento sem pagamento preserva itens e saldo integral");
        var excess=Copy(sale);excess.Id=Guid.NewGuid();excess.PendingPayments=[Part("PIX",151)];Reject(()=>FinancialOperations.AddSale(fresh.Item1,excess,Admin),"lançamento acima do total bloqueado");
        Check(PhoneText.Format("11998367552")=="(11) 99836-7552"&&PhoneText.Format("1138367552")=="(11) 3836-7552","máscaras de celular e fixo");Check(PhoneText.Normalize("11 99836-7552")=="11998367552"&&PhoneText.Matches("(11) 99836-7552","998367552"),"normalização e busca sem máscara");Check(PhoneText.Format("7390")=="7390"&&PhoneText.Normalize("ramal 123")=="ramal 123","telefones antigos incompletos e ambíguos preservados");
        var immediate=Scenario();var draft=Copy(immediate.Item3);draft.ProductionBusinessDays=0;draft.DebtOriginDate=DateTime.Today.AddDays(-2);OrderOperations.Save(immediate.Item1,draft,Admin);var saved=immediate.Item1.ScheduledOrders.Single();Check(saved.DueDate==draft.DebtDate&&saved.Stage==OrderStages.Art&&!saved.FullyPaid,"Imediato usa data do pedido sem entregar ou quitar");
        saved.StartProductionDeadline(DateTime.Today);Check(saved.DueDate==draft.DebtDate,"aprovação não desloca Imediato");
        var savedMixed=Scenario();var mixedDraft=Copy(savedMixed.Item3);mixedDraft.PendingPayments=[Part("PIX",400),Part("Cartão de débito",200),Part("Cartão de crédito 3x",400)];OrderOperations.Save(savedMixed.Item1,mixedDraft,Admin);Check(savedMixed.Item1.ScheduledOrders.Single().PaidAmount==1000&&savedMixed.Item2.Sales.Count==3,"salvar pedido persiste exatamente as linhas confirmadas");
        var correctionDraft=Copy(savedMixed.Item1.ScheduledOrders.Single());correctionDraft.PaymentCorrectionType="Dinheiro";OrderOperations.Save(savedMixed.Item1,correctionDraft,Admin,savedMixed.Item2.Sales[0].Id,400,"Corrigir forma pelo pedido");Check(savedMixed.Item2.Sales.Count==3&&savedMixed.Item2.ExpectedCash==500,"correção de modalidade pelo pedido sem lançamento duplicado");
        var folder=Path.Combine(Path.GetTempPath(),"prompt6-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var store=new DataStore(folder);store.EnsureInitialized();store.Update(d=>{FinancialOperations.OpenCash(d,100,Admin);return 0;});
        store.Update(d=>FinancialOperations.AddSale(d,Copy(sale),Admin));var loaded=store.Load();Check(loaded.Sessions.Single().Sales.All(x=>x.Items.Count==3&&x.FeePercent!=null)&&loaded.ManualDebts.Single().Remaining(loaded)==30,"SQLite reaberto preserva itens, taxas, grupo e saldo");
        var legacy=new Sale{CustomerName="Antigo",Contact="7390",Product="Descrição antiga",Units=3,UnitValue=10,PaymentType="PIX"};store.Update(d=>FinancialOperations.AddSale(d,legacy,Admin));var old=store.Load().Sessions.Single().Sales.Single(x=>x.Id==legacy.Id);Check(old.Items.Count==0&&old.Total==30&&old.Product=="Descrição antiga"&&old.Contact=="7390","lançamento antigo sem migração destrutiva");
        var countBefore=store.Load().Sessions.Single().Sales.Count;Reject(()=>store.Update<int>(d=>{FinancialOperations.AddSale(d,new Sale{Product="teste rollback",Units=1,TotalValue=10,PaymentType="PIX"},Admin);throw new InvalidOperationException("falha simulada");}),"transação interrompida bloqueada");Check(store.Load().Sessions.Single().Sales.Count==countBefore,"rollback sem gravação parcial");
        var state=store.Load();Reject(()=>store.CommitRemote(new CommitRequest(Guid.NewGuid(),state.DataVersion,state,6)),"cliente antigo não descarta novos campos");
        store.Update(d=>{var persistedOrder=new ScheduledOrder{CustomerName="Teste persistência",Number=456,OrderTotal=1000,OriginalTotal=1000,PendingPayments=[Part("PIX",400),Part("Cartão de débito",200),Part("Cartão de crédito 3x",400)]};OrderOperations.Save(d,persistedOrder,Admin);return 0;});
        var persisted=store.Load().ScheduledOrders.Single();Check(persisted.PaidAmount==1000&&persisted.EstimatedFees==30.34m,"pagamentos e taxas do pedido após reabrir SQLite");
        store.Update(d=>{var session=d.Sessions.Single();FinancialOperations.CloseCash(d,session.Id,Copy(session),session.ExpectedCash,Admin);return 0;});
        var initialSnapshot=store.Load().CashClosureVersions.Single().SnapshotJson;
        store.Update(d=>{var payment=d.Sessions.Single().Sales.First(x=>x.Id==persisted.Payments[0].SaleId);var changed=Copy(payment);changed.PaymentType="Cartão de débito";FinancialOperations.EditSale(d,Copy(payment),changed,"Corrigir fechamento persistido",Admin);return 0;});
        var reread=store.Load();Check(reread.CashClosureVersions.Count==2&&reread.CashClosureVersions[0].SnapshotJson==initialSnapshot&&reread.ScheduledOrders.Single().EstimatedFees==36.70m,"retificação e auditoria persistem sem alterar snapshot original");
        UiTests(store,folder,product1);Console.WriteLine($"PROMPT 6: {count} verificações aprovadas. Artefatos de teste: {folder}");
    }
    static void UiTests(DataStore store,string folder,Product product)
    {
        var app=Application.Current as App??new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var editor=new PaymentEditor(false);editor.Add("PIX",400);editor.Add("Cartão de débito",200);editor.Add("Cartão de crédito 3x",400);Check(editor.Read(1000).Count==3&&editor.Fees==30.34m,"editor compartilhado calcula três linhas");
        var phone=new TextBox();PhoneInput.Attach(phone);phone.Text="11998367552";Check(phone.Text=="(11) 99836-7552","máscara durante digitação/colagem");
        var dialog=new ScheduledOrderDialog(store);((TextBox)dialog.FindName("NameBox")).Text="Rascunho preservado";((TextBox)dialog.FindName("ContactBox")).Text="11998367552";
        ProductOperations.Save(new AppData(),product,null,Admin);store.Update(d=>{ProductOperations.Save(d,product,null,Admin);return 0;});
        Check(((TextBox)dialog.FindName("NameBox")).Text=="Rascunho preservado"&&new ProductSearch(store.Load().Products).Find("porcelana").Any(),"salvar produto preserva rascunho e atualiza fonte do autocomplete");
        var tabs=(TabControl)dialog.FindName("OrderTabs");tabs.SelectedIndex=2;((StackPanel)dialog.FindName("MixedPaymentsHost")).Children.OfType<PaymentEditor>().First().Add("Cartão de crédito 12x",400);Render(dialog,Path.Combine(folder,"pedido-pagamentos.png"));
        var sale=new SaleWindow(store:store);Render(sale,Path.Combine(folder,"lancamento.png"));
        var receive=new PaymentReceivedWindow(new ScheduledOrder{CustomerName="Cliente Teste",OrderTotal=1000});Render(receive,Path.Combine(folder,"recebimento.png"));
        Check(true,"telas de pedido, lançamento e recebimento renderizadas");
    }
    static void Render(Window window,string path)
    {
        var content=(FrameworkElement)window.Content;window.Content=null;
        var frame=new Border{Background=new SolidColorBrush(Color.FromRgb(247,247,248)),Child=content,Width=window.Width-16,Height=window.Height-40};frame.Measure(new Size(frame.Width,frame.Height));frame.Arrange(new Rect(0,0,frame.Width,frame.Height));frame.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)frame.Width,(int)frame.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(frame);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);png.Save(file);
    }
}
