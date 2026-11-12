using GraficaCaixa;
using System.Text.Json;

// Pedido com desconto pago com acréscimo de cobrança (juros do cartão): o desconto é conferido contra o valor aplicado
// ao pedido, não contra o recebido bruto (caso do pedido #169: R$ 560 − R$ 28 = R$ 532; recebido R$ 541,45 com R$ 9,45 de juros).
public static class DiscountExcessTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    static T Clone<T>(T value)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    public static void Run()
    {
        var user=OperationContext.Administrator("Teste");
        var order=new ScheduledOrder{Number=169,CustomerName="Cliente",Stage="Pronto para retirada",OriginalTotal=560,FixedDiscount=28,OrderTotal=532,FullyPaid=true,Items=[new OrderItem{Product="Caneca",Quantity=14,UnitPrice=40}],
            Payments=[new OrderPayment{Amount=190,PaymentType="PIX",SaleId=Guid.NewGuid(),PaidAt=DateTime.Now.AddDays(-3)},
                new OrderPayment{Amount=351.45m,PaymentType="Cartão de crédito 2x",SaleId=Guid.NewGuid(),PaidAt=DateTime.Now,ExcessAmount=9.45m,ExcessKind=PaymentPolicy.CollectionSurcharge,ExcessConfirmed=true}]};
        var data=new AppData{ScheduledOrders=[order]};
        Check(order.PaidAmount==541.45m&&order.AppliedAmount==532&&order.RemainingAmount==0,"cenário: recebido bruto 541,45, aplicado 532, saldo zero");
        var deliver=Clone(order);deliver.Stage="Entregue";
        Check(!Throws(()=>OrderOperations.Save(data,deliver,user)),"concluir pedido com desconto pago com juros do cartão");
        Check(data.ScheduledOrders.Single().Stage=="Entregue"&&data.ScheduledOrders.Single().OrderTotal==532,"pedido fica Entregue com o mesmo total");
        var tooMuch=Clone(data.ScheduledOrders.Single());tooMuch.FixedDiscount=40;tooMuch.OrderTotal=520;
        Check(Throws(()=>OrderOperations.Save(data,tooMuch,user)),"desconto que deixa o total abaixo do valor aplicado continua recusado");
        // Lançamento em que o cliente deixa o troco: R$ 6,30 em itens, R$ 6,50 recebidos.
        var cash=new AppData{Sessions=[new CashSession()]};
        PaymentPart Part(string type,decimal amount)=>new(){PaymentType=type,Amount=amount,PaidAt=DateTime.Now};
        Sale Draft(params PaymentPart[] parts)=>new(){CustomerName="Elói",Product="7 Impressão A4",CreatedAt=DateTime.Now,Items=[new OrderItem{Product="Impressão A4",Quantity=7,UnitPrice=0.90m}],PendingPayments=[..parts]};
        Check(Throws(()=>FinancialOperations.AddSale(cash,Draft(Part("Dinheiro",6.50m)),user)),"lançamento acima do total sem confirmação continua recusado");
        var tip=Part("Dinheiro",6.50m);tip.ExcessConfirmed=true;tip.ExcessKind=PaymentPolicy.CustomerExtra;var draft=Draft(tip);FinancialOperations.AddSale(cash,draft,user);
        var debt=cash.ManualDebts.Single(x=>x.Id==draft.Id);
        Check(debt.Amount==6.30m&&debt.CustomerExtra==0.20m&&debt.Paid(cash)==6.50m&&debt.Remaining(cash)==0&&cash.Sessions[0].ExpectedCash==6.50m,"cliente deixa o troco: caixa com R$ 6,50, conta quitada e R$ 0,20 de valor adicional");
        var split=Draft(Part("PIX",5),Part("Cartão de débito",2));split.PendingPayments[1].ExcessConfirmed=true;split.PendingPayments[1].ExcessKind=PaymentPolicy.CollectionSurcharge;FinancialOperations.AddSale(cash,split,user);
        Check(cash.ManualDebts.Single(x=>x.Id==split.Id).CollectionSurcharge==0.70m,"excedente conta só a parte que passa do total");
        FinancialOperations.CorrectSale(cash,debt.SaleIds[0],6.30m,"troco devolvido",user);
        Check(debt.CustomerExtra==null&&debt.Remaining(cash)==0,"correção para o valor exato remove o excedente");
        Check(Throws(()=>FinancialOperations.CorrectSale(cash,debt.SaleIds[0],6.60m,"digitação",user)),"correção não cria excedente novo");
        Console.WriteLine($"TOTAL: {passed} verificações de desconto com acréscimo.");
    }
}
