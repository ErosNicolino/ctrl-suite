using GraficaCaixa;

// Abas de Pedidos: "Aguardando retirada" separada de "Em andamento".
public static class OrderTabsTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var data=new AppData();
        ScheduledOrder Order(string stage,decimal total=100,decimal paid=0){var o=new ScheduledOrder{CustomerName="Cliente",Stage=stage,OrderTotal=total};if(paid>0)o.Payments.Add(new OrderPayment{Amount=paid,PaidAt=DateTime.Today,SaleId=Guid.NewGuid()});data.ScheduledOrders.Add(o);return o;}
        foreach(var stage in new[]{OrderStages.Art,OrderStages.Approval,OrderStages.Production})Check(ReceivableViews.OrderBucket(data,Order(stage))=="active",$"{stage} fica em Em andamento");
        Check(ReceivableViews.OrderBucket(data,Order(OrderStages.Ready))=="ready","pronto para retirada vai para Aguardando retirada");
        Check(ReceivableViews.OrderBucket(data,Order(OrderStages.Ready,100,100))=="ready","pronto e quitado também espera a retirada");
        Check(ReceivableViews.OrderBucket(data,Order(OrderStages.Delivered,100,40))=="unpaid","entregue com saldo continua em Falta pagamento");
        Check(ReceivableViews.OrderBucket(data,Order(OrderStages.Delivered,100,100))=="completed","entregue quitado continua em Concluídos");
        Console.WriteLine($"ABAS DE PEDIDOS: {passed} testes aprovados");
    }
}
