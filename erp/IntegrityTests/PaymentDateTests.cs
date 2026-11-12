using GraficaCaixa;
using System.Text.Json;

// Pagamento de pedido com data passada: Pix/cartão vão para o fechamento do dia do pagamento (retificação); dinheiro fica no caixa aberto.
public static class PaymentDateTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static readonly OperationContext Admin=OperationContext.Administrator("Teste data do pagamento");
    static (AppData Data,CashSession Past,CashSession Open,ScheduledOrder Order) Scenario()
    {
        var day=DateTime.Today.AddDays(-2);
        var past=new CashSession{OpeningAmount=100,OpenedAt=day.AddHours(8),ClosedAt=day.AddHours(18),DeclaredClosingAmount=100,ClosedBy="Teste"};
        var open=new CashSession{OpeningAmount=100,OpenedAt=DateTime.Today.AddHours(8)};
        var order=new ScheduledOrder{Number=80,CustomerName="Versa",OrderTotal=524.50m,Stage="Entregue",Items=[new(){Quantity=1,Product="Digitalização",UnitPrice=524.50m}]};
        return (new AppData{Sessions=[past,open],ScheduledOrders=[order],FinancialIntegrityVersion=1,OrderDetailsVersion=4},past,open,order);
    }
    public static void Run()
    {
        var pix=Scenario();var paidAt=pix.Past.OpenedAt.Date.AddHours(10);
        OrderOperations.AddPayment(pix.Data,pix.Order,524.50m,"PIX",paidAt,"Parcela",Guid.NewGuid(),Admin);
        Check(pix.Past.Sales.Count==1&&pix.Open.Sales.Count==0,"Pix com data de um dia fechado entra no fechamento daquele dia, não no caixa de hoje");
        Check(pix.Past.Sales[0].CreatedAt==paidAt&&pix.Order.Payments.Single().PaidAt==paidAt,"lançamento e pagamento ficam com a data informada");
        Check(pix.Data.CashClosureVersions.Count(x=>x.SessionId==pix.Past.Id&&!x.IsOriginal)==1&&pix.Data.CashClosureVersions.Any(x=>x.SessionId==pix.Past.Id&&x.IsOriginal),"fechamento ganha versão retificada e o original fica preservado");
        Check(pix.Data.AuditTrail.Any(x=>x.Action=="Lançamento incluído em retificação"&&x.Reason.Contains("pedido #80")),"retificação registrada com motivo automático citando o pedido");
        Check(pix.Order.FullyPaid&&pix.Order.RemainingAmount==0,"pedido fica quitado normalmente");

        var cash=Scenario();OrderOperations.AddPayment(cash.Data,cash.Order,100,"Dinheiro",cash.Past.OpenedAt.Date.AddHours(10),"Parcela",Guid.NewGuid(),Admin);
        Check(cash.Open.Sales.Count==1&&cash.Past.Sales.Count==0&&cash.Open.ExpectedCash==200,"dinheiro com data passada fica no caixa aberto (a nota está na gaveta hoje)");

        var noDay=Scenario();OrderOperations.AddPayment(noDay.Data,noDay.Order,100,"PIX",DateTime.Today.AddDays(-1).AddHours(10),"Parcela",Guid.NewGuid(),Admin);
        Check(noDay.Open.Sales.Count==1,"Pix de um dia sem caixa (ex.: domingo) entra no caixa aberto");

        var late=Scenario();OrderOperations.AddPayment(late.Data,late.Order,100,"Cartão de débito",late.Past.OpenedAt.Date.AddHours(6),"Parcela",Guid.NewGuid(),Admin);
        Check(late.Past.Sales.Single().CreatedAt==late.Past.OpenedAt.AddMinutes(1),"horário antes da abertura vira o primeiro minuto do caixa daquele dia");

        var today=Scenario();OrderOperations.AddPayment(today.Data,today.Order,100,"PIX",DateTime.Now,"Parcela",Guid.NewGuid(),Admin);
        Check(today.Open.Sales.Count==1&&today.Data.CashClosureVersions.Count==0,"pagamento de hoje continua no caixa aberto, sem retificação");

        // Servidor: aceita a retificação automática (motivo vem da auditoria) com permissão de retificar caixa.
        var server=Scenario();var before=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(server.Data))!;
        OrderOperations.AddPayment(server.Data,server.Order,524.50m,"PIX",server.Past.OpenedAt.Date.AddHours(10),"Parcela",Guid.NewGuid(),Admin);
        var actor=new UserIdentity(Guid.NewGuid(),"Teste","teste","Administrador",Guid.NewGuid());var all=Permissions.Catalog.ToDictionary(x=>x.Key,_=>true);
        var ok=true;try{ServerAuthorization.ValidateAndAudit(before,JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(server.Data))!,actor,all);}catch(Exception ex) when(ex is InvalidOperationException or UnauthorizedAccessException){ok=false;Console.WriteLine("   recusado: "+ex.Message);}
        Check(ok,"servidor aceita o pagamento retroativo no fechamento do dia");
        var limited=Scenario();var noRectify=new OperationContext("Vendedor","Vendedor"){Permissions=all.ToDictionary(x=>x.Key,x=>x.Key!="cash.rectify"&&x.Value)};
        OrderOperations.AddPayment(limited.Data,limited.Order,100,"PIX",limited.Past.OpenedAt.Date.AddHours(10),"Parcela",Guid.NewGuid(),noRectify);
        Check(limited.Open.Sales.Count==1&&limited.Past.Sales.Count==0,"quem não pode retificar caixa: pagamento retroativo entra no caixa aberto");
        Console.WriteLine($"DATA DO PAGAMENTO: {passed} testes aprovados");
    }
}
