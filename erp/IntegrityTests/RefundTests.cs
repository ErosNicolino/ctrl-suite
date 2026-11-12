using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.Text.Json;

// Devolução ao cliente: original preservado, linha negativa no caixa aberto na forma da devolução (Dinheiro ou Pix).
public static class RefundTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    static readonly OperationContext Admin=OperationContext.Administrator("Teste devolução");
    static (AppData Data,CashSession Cash,Sale Sale) Scenario(string payment,decimal total=30,decimal opening=100)
    {
        var cash=new CashSession{OpeningAmount=opening,OpenedBy="Teste"};var sale=new Sale{CustomerName="Cliente",Product="Impressões",Units=1,TotalValue=total,PaymentType=payment,CreatedAt=DateTime.Now.AddMinutes(-30)};cash.Sales.Add(sale);
        return (new AppData{Sessions=[cash],FinancialIntegrityVersion=1,OrderDetailsVersion=4},cash,sale);
    }
    static decimal Revenue(AppData d)=>d.Sessions.SelectMany(x=>x.Sales).Sum(x=>x.Total);
    public static void Run()
    {
        var pix=Scenario("PIX");FinancialOperations.RefundSale(pix.Data,pix.Sale.Id,20,"PIX","Trocou para papel mais barato",Admin);
        var line=pix.Cash.Sales.Single(x=>x.Total<0);
        Check(pix.Sale.Total==30&&line.Total==-20&&line.PaymentType=="PIX","Pix pago, Pix devolvido: original de R$ 30 preservado e devolução de -R$ 20 em Pix");
        Check(Revenue(pix.Data)==10&&FinancialOperations.EffectiveAmount(pix.Data,pix.Sale.Id)==10,"faturamento e valor efetivo ficam R$ 10");
        Check(pix.Cash.ExpectedCash==100,"devolução por Pix não mexe na gaveta");
        Check(pix.Data.FinancialCorrections.Single().CorrectionType==FinancialOperations.RefundType&&pix.Data.AuditTrail.Any(x=>x.Action=="Devolução ao cliente"),"devolução registrada e auditada");

        var cash=Scenario("PIX");FinancialOperations.RefundSale(cash.Data,cash.Sale.Id,20,"Dinheiro","Trocou para papel mais barato",Admin);
        Check(cash.Cash.ExpectedCash==80&&cash.Sale.PaymentType=="PIX","Pix pago, devolvido em dinheiro: a gaveta esperada cai R$ 20 e o Pix original fica");

        var card=Scenario("Cartão de crédito 1x");
        Check(Throws(()=>FinancialOperations.RefundSale(card.Data,card.Sale.Id,20,"Cartão de crédito 1x","Trocou de papel",Admin)),"cartão não é forma de devolução");
        FinancialOperations.RefundSale(card.Data,card.Sale.Id,20,"PIX","Trocou de papel",Admin);Check(card.Cash.Sales.Any(x=>x.Total==-20&&x.PaymentType=="PIX"),"pago no cartão, devolvido por Pix");

        var limits=Scenario("Dinheiro");
        Check(Throws(()=>FinancialOperations.RefundSale(limits.Data,limits.Sale.Id,31,"PIX","Valor maior",Admin)),"não devolve mais do que foi pago");
        Check(Throws(()=>FinancialOperations.RefundSale(limits.Data,limits.Sale.Id,10,"PIX","abc",Admin)),"motivo obrigatório");
        FinancialOperations.RefundSale(limits.Data,limits.Sale.Id,20,"PIX","Primeira devolução",Admin);
        Check(Throws(()=>FinancialOperations.RefundSale(limits.Data,limits.Sale.Id,15,"PIX","Segunda devolução",Admin)),"segunda devolução respeita o que sobrou (R$ 10)");
        FinancialOperations.RefundSale(limits.Data,limits.Sale.Id,10,"PIX","Devolvido o resto",Admin);
        Check(FinancialOperations.EffectiveAmount(limits.Data,limits.Sale.Id)==0&&!Cancellations.HiddenSaleIds(limits.Data).Contains(limits.Sale.Id),"devolução total: lançamento continua visível (não vira cancelado escondido)");
        Check(Throws(()=>FinancialOperations.RefundSale(limits.Data,limits.Cash.Sales.First(x=>x.Total<0).Id,1,"PIX","Linha de devolução",Admin)),"não devolve uma linha de devolução");
        Check(Cancellations.List(limits.Data).Count(x=>x.Kind.StartsWith("Devolução"))==2,"devoluções aparecem nos Relatórios (Cancelados e devoluções)");

        var drawer=Scenario("PIX",30,5);
        Check(Throws(()=>FinancialOperations.RefundSale(drawer.Data,drawer.Sale.Id,20,"Dinheiro","Sem troco na gaveta",Admin)),"devolução em dinheiro não passa do que há na gaveta");
        var closed=Scenario("PIX");closed.Cash.ClosedAt=DateTime.Now;
        Check(Throws(()=>FinancialOperations.RefundSale(closed.Data,closed.Sale.Id,20,"PIX","Caixa fechado",Admin)),"sem caixa aberto não registra devolução");
        var yesterday=Scenario("PIX");yesterday.Cash.ClosedAt=DateTime.Now.AddHours(-2);var today=new CashSession{OpeningAmount=50};yesterday.Data.Sessions.Add(today);
        FinancialOperations.RefundSale(yesterday.Data,yesterday.Sale.Id,20,"Dinheiro","Cliente voltou no dia seguinte",Admin);
        Check(today.Sales.Single().Total==-20&&yesterday.Cash.Sales.Count==1&&today.ExpectedCash==30,"venda de um caixa fechado: devolução entra no caixa aberto de hoje");

        var orderCase=Scenario("PIX");var order=new ScheduledOrder{Number=1,CustomerName="Cliente",OrderTotal=30,OriginalTotal=30,Payments=[new(){Id=orderCase.Sale.Id,SaleId=orderCase.Sale.Id,Amount=30,PaymentType="PIX"}]};orderCase.Data.ScheduledOrders=[order];
        FinancialOperations.RefundSale(orderCase.Data,orderCase.Sale.Id,20,"PIX","Pedido trocado para papel mais barato",Admin);
        Check(order.PaidAmount==10&&order.Payments.Any(x=>x.Amount==-20&&x.PaymentType=="PIX"),"pagamento de pedido: total recebido do pedido cai para R$ 10");

        // Conferência do servidor: a devolução passa com as permissões de estorno e é recusada sem elas.
        var server=Scenario("PIX");var before=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(server.Data))!;
        FinancialOperations.RefundSale(server.Data,server.Sale.Id,20,"PIX","Trocou para papel mais barato",Admin);
        var actor=new UserIdentity(Guid.NewGuid(),"Teste","teste","Administrador",Guid.NewGuid());var all=Permissions.Catalog.ToDictionary(x=>x.Key,_=>true);
        bool Accepted(IReadOnlyDictionary<string,bool> p){try{ServerAuthorization.ValidateAndAudit(before,JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(server.Data))!,actor,p);return true;}catch(Exception ex) when(ex is InvalidOperationException or UnauthorizedAccessException){return false;}}
        Check(Accepted(all),"servidor aceita a devolução com permissão de correção e estorno");
        Check(!Accepted(all.ToDictionary(x=>x.Key,x=>x.Key!="finance.reverse"&&x.Value)),"servidor recusa devolução sem permissão de estorno");

        // Conciliação Pix: entrada de R$ 30 casa com o original e a saída de R$ 20 casa com a devolução.
        var bank=new List<BankEntry>{new(pix.Cash.Sales[0].CreatedAt.Date,30,"PIX RECEBIDO - CLIENTE","1","CREDIT"),new(DateTime.Today,-20,"PIX ENVIADO - CLIENTE","2","DEBIT"),new(DateTime.Today,-500,"PIX ENVIADO - FORNECEDOR","3","DEBIT")};
        var erp=BankReconciliation.FromSales(pix.Cash.Sales);var result=BankReconciliation.Reconcile(erp,bank,true);
        Check(erp.Count==2&&result.Rows.Count(x=>x.Tone=="ok")==2,"conciliação: entrada de R$ 30 e saída de R$ 20 conferidas");
        Check(!result.Rows.Any(x=>x.Bank?.Id=="3"),"pagamento a fornecedor (saída sem devolução no ERP) não vira divergência");
        Console.WriteLine($"DEVOLUÇÃO: {passed} testes aprovados");
    }
}
