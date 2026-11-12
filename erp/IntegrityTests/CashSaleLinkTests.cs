using GraficaCaixa;

// Lançamento do caixa ligado ao pedido (Virar pedido / "Este pagamento já está no caixa?"): o pagamento entra no pedido sem criar outro lançamento.
public static class CashSaleLinkTests
{
    static int passed;
    static readonly OperationContext Admin=OperationContext.Administrator("Teste vínculo");
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static void Rejects(Action action,string name){try{action();}catch(Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException){Check(true,name);return;}Check(false,name);}
    public static void Run()
    {
        var sale=new Sale{CustomerName="Maria Silva",Contact="11987653917",Product="Adesivo",Units=1,PaymentType="PIX",TotalValue=340.80m,CreatedAt=DateTime.Now.AddMinutes(-10)};
        var other=new Sale{CustomerName="João",Contact="",Product="Cartão",Units=1,PaymentType="Dinheiro",TotalValue=50,CreatedAt=DateTime.Now};
        var counter=new Sale{CustomerName="Não informado",Contact="",Product="Cópia",Units=1,PaymentType="Dinheiro",TotalValue=2,CreatedAt=DateTime.Now};
        var data=new AppData{Sessions=[new CashSession{OpenedAt=DateTime.Today,Sales=[sale,other,counter]}]};
        Check(ReceivableViews.FreeCashSales(data,"maria silva","").Select(x=>x.Id).SequenceEqual([sale.Id]),"acha o lançamento livre pelo nome (sem acento/maiúscula)");
        Check(ReceivableViews.FreeCashSales(data,"Outro nome","(11) 98765-3917").Count==1,"acha pelo telefone completo");
        Check(ReceivableViews.FreeCashSales(data,"Não informado","").Count==0,"cliente sem nome nunca casa pelo nome");

        var order=new ScheduledOrder{CustomerName="Maria Silva",Contact="11987653917",OrderTotal=340.80m,LinkSaleIds=[sale.Id]};
        OrderOperations.Save(data,order,Admin);
        var saved=data.ScheduledOrders.Single();
        Check(data.Sessions[0].Sales.Count==3,"não cria outro lançamento no caixa");
        Check(saved.Payments.Count==1&&saved.Payments[0].SaleId==sale.Id&&saved.RemainingAmount==0,"pedido fica quitado pelo lançamento existente");
        Check(saved.LinkSaleIds.Count==0,"lista de vínculo não fica gravada no pedido");
        Check(ReceivableViews.FreeCashSales(data).All(x=>x.Id!=sale.Id),"lançamento ligado sai da lista de livres");

        var second=new ScheduledOrder{CustomerName="Maria Silva",OrderTotal=340.80m,LinkSaleIds=[sale.Id]};
        Rejects(()=>OrderOperations.Save(data,second,Admin),"o mesmo lançamento não paga dois pedidos");
        var small=new ScheduledOrder{CustomerName="João",OrderTotal=30,LinkSaleIds=[other.Id]};
        Rejects(()=>OrderOperations.Save(data,small,Admin),"lançamento maior que o saldo do pedido é recusado");
        var pending=new ScheduledOrder{CustomerName="João",PricePending=true,LinkSaleIds=[other.Id]};
        Rejects(()=>OrderOperations.Save(data,pending,Admin),"pedido com valor a definir não usa lançamento");
        Rejects(()=>OrderOperations.LinkSales(data,saved,[other.Id],new OperationContext("Operador","Operador")),"sem permissão de receber não liga");

        data.Sessions[0].ClosedAt=DateTime.Now;
        var closed=new ScheduledOrder{CustomerName="João",OrderTotal=50,LinkSaleIds=[other.Id]};
        Rejects(()=>OrderOperations.Save(data,closed,Admin),"lançamento de caixa fechado não é ligado");

        // Lançamento com itens (o caso comum no balcão): cria conta interna quitada e mesmo assim precisa ficar livre.
        var items=new AppData{Sessions=[new CashSession{OpenedAt=DateTime.Today}]};
        var draft=new Sale{CustomerName="Ana Souza",Contact="11912345086",Product="2 Banner",Units=2,PaymentType="PIX",Items=[new OrderItem{Product="Banner",Quantity=2,UnitPrice=40}],PendingPayments=[new PaymentPart{Amount=80,PaymentType="PIX"}]};
        FinancialOperations.AddSale(items,draft,Admin);
        Check(items.ManualDebts.Count==1&&ReceivableViews.FreeCashSales(items,"Ana Souza","").Count==1,"lançamento com itens quitado fica livre");
        OrderOperations.Save(items,new ScheduledOrder{CustomerName="Ana Souza",Contact="11912345086",OrderTotal=80,LinkSaleIds=[draft.Id]},Admin);
        Check(items.ScheduledOrders.Single().RemainingAmount==0&&items.Sessions[0].Sales.Count==1,"lançamento com itens vira pedido quitado sem lançar de novo");
        Check(AbcCurve.Of(items,DateTime.Today,DateTime.Today.AddDays(1)).Customers.Sum(x=>x.Revenue)==80,"Curva ABC não conta o lançamento e o pedido juntos");
        var owing=new Sale{CustomerName="Bia Lima",Contact="11955554444",Product="Faixa",Units=1,PaymentType="PIX",Items=[new OrderItem{Product="Faixa",Quantity=1,UnitPrice=100}],PendingPayments=[new PaymentPart{Amount=60,PaymentType="PIX"}]};
        FinancialOperations.AddSale(items,owing,Admin);
        Check(ReceivableViews.FreeCashSales(items,"Bia Lima","").Count==0,"lançamento com saldo a receber continua preso à conta");
        Console.WriteLine($"VÍNCULO LANÇAMENTO-PEDIDO: {passed} testes aprovados");
    }
}
