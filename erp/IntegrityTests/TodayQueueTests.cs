using GraficaCaixa;

// Fila "Hoje" e Repetir pedido.
public static class TodayQueueTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var now=new DateTime(2026,11,4,10,0,0);var today=now.Date;var user=Guid.NewGuid();
        ScheduledOrder Running(string name,DateTime due)=>new(){CustomerName=name,Stage=OrderStages.Production,ProductionBusinessDays=3,ArtworkApprovedAt=due.AddDays(-5),DueDate=due,OrderTotal=100,Items=[new OrderItem{Product="Cartão",Quantity=100}]};
        var late=Running("Atrasado",today.AddDays(-3));var due=Running("Hoje",today);var later=Running("Depois",today.AddDays(5));
        var ready=new ScheduledOrder{CustomerName="Pronto",Stage="Pronto para retirada",ReadyAt=today.AddDays(-2),OrderTotal=50};
        var delivered=new ScheduledOrder{CustomerName="Deve",Stage="Entregue",PayAfterDelivery=true,ExpectedPaymentDate=today.AddDays(-1),OrderTotal=80};
        var paidLater=new ScheduledOrder{CustomerName="Promessa futura",Stage="Entregue",PayAfterDelivery=true,ExpectedPaymentDate=today.AddDays(4),OrderTotal=80};
        var archived=Running("Arquivado",today.AddDays(-3));archived.IsUncollected=true;var cancelled=Running("Cancelado",today.AddDays(-3));cancelled.CancelledAt=now;
        var data=new AppData{ScheduledOrders=[later,due,ready,late,delivered,paidLater,archived,cancelled]};
        data.Prompt12.PurchaseNeeds.AddRange([new PurchaseNeed{Material="Papel atrasado",BuyBy=today.AddDays(-2),Status=PurchaseNeedStatus.ToBuy},new PurchaseNeed{Material="Tinta futura",BuyBy=today.AddDays(3),Status=PurchaseNeedStatus.ToBuy},new PurchaseNeed{Material="Sem data",Status=PurchaseNeedStatus.ToBuy}]);
        data.Prospects.AddRange([new Prospect{CustomerName="Cobrar hoje",NextContactAt=today},new Prospect{CustomerName="Cobrar depois",NextContactAt=today.AddDays(2)},new Prospect{CustomerName="Fechou",Status="Convertido em pedido",NextContactAt=today}]);
        var seen=new Reminder{Title="Já lido",ReadByUsers=new(){[user]=now}};
        data.Reminders=[new Reminder{Name="Rápido"},new Reminder{Title="Vencido",DueAt=now.AddHours(-1)},new Reminder{Title="Mais tarde",DueAt=now.AddDays(1)},new Reminder{Title="Feito",DoneAt=now},seen];
        var all=TodayQueue.Build(data,_=>true,user,now);var titles=all.Select(x=>x.Title).ToList();
        Check(all.Count==8,"fila traz só o que pede atenção hoje");
        Check(all[0].Label=="Atrasado"&&all[0].Tone=="Danger"&&all[1].Label=="Para hoje"&&all[^1].Label=="Retirada","atrasados primeiro, retiradas por último");
        Check(!titles.Any(x=>x.Contains("Depois")||x.Contains("Arquivado")||x.Contains("Cancelado")||x.Contains("Promessa")),"pedido futuro, arquivado ou cancelado fica de fora");
        Check(titles.Contains("Papel atrasado")&&!titles.Contains("Tinta futura")&&!titles.Contains("Sem data"),"compra só entra com data vencida ou de hoje");
        Check(titles.Contains("Cobrar hoje")&&!titles.Contains("Cobrar depois")&&!titles.Contains("Fechou"),"orçamento só entra na data de cobrar e em aberto");
        Check(titles.Contains("Vencido")&&titles.Contains("Rápido")&&!titles.Contains("Mais tarde")&&!titles.Contains("Feito")&&!titles.Contains("Já lido"),"lembrete pendente entra; futuro, feito ou já lido não");
        Check(all.Any(x=>x.Kind=="debt"&&x.Title.Contains("Deve")),"pagamento previsto vencido entra como cobrança");
        Check(!all.Any(x=>x.Detail.Contains("R$")||x.Title.Contains("R$")),"fila não mostra valores em dinheiro");
        delivered.ExpectedPaymentDate=null;Check(!TodayQueue.Build(data,_=>true,user,now).Any(x=>x.Kind=="debt"),"sem data prevista não vira cobrança");delivered.ExpectedPaymentDate=today.AddDays(-1);
        foreach(var (key,kind) in new[]{("view.purchases","purchase"),("view.debtors","debt"),("view.prospects","prospect"),("view.reminders","reminder"),("view.orders","order")})
            Check(!TodayQueue.Build(data,k=>k!=key,user,now).Any(x=>x.Kind==kind),$"sem {key}, a fila esconde {kind}");
        Check(TodayQueue.Build(data,_=>false,user,now).Count==0,"sem nenhuma permissão a fila fica vazia");
        Check(TodayQueue.Build(new AppData{Reminders=null},_=>true,user,now).Count==0,"banco sem dados não quebra a fila");

        var customer=new CustomerProfile{Name="Alfa Pragas",Contact="11999998888"};
        var source=new ScheduledOrder{Number=77,CustomerId=customer.Id,CustomerName=customer.Name,Contact=customer.Contact,Notes="Mesma arte",Stage="Entregue",ProductionBusinessDays=5,ArtworkApprovedAt=today,DueDate=today,OrderTotal=90,OriginalTotal=100,DiscountPercent=10,UseItemPrices=true,FullyPaid=true,PaymentReceivedAt=now,ReadyAt=now,PdfGeneratedAt=now,Revision=9,DepositPaid=true,DepositAmount=50,PayAfterDelivery=true,ExpectedPaymentDate=today,FreightCost=12,
            Payments=[new OrderPayment{Amount=90}],Items=[new OrderItem{Product="Adesivo",Quantity=1000,UnitPrice=0.1m,UnitCost=0.05m,CostReviewed=true,ProductId=Guid.NewGuid(),Unit="un"}]};
        var draft=OrderRepeat.Draft(source);
        Check(draft.Id!=source.Id&&draft.Number==0&&draft.Revision==0,"repetir cria pedido novo, sem número");
        Check(draft.CustomerId==customer.Id&&draft.CustomerName==customer.Name&&draft.Contact==customer.Contact&&draft.Notes=="Mesma arte","mantém cliente, telefone e observações");
        Check(draft.Items.Single().Product=="Adesivo"&&draft.Items[0].Quantity==1000&&draft.Items[0].UnitPrice==0.1m&&draft.Items[0].ProductId==source.Items[0].ProductId&&draft.Items[0].Id!=source.Items[0].Id,"mantém itens e preços, com identificação nova");
        Check(draft.OrderTotal==90&&draft.OriginalTotal==100&&draft.DiscountPercent==10&&draft.ProductionBusinessDays==5,"mantém total, desconto e prazo");
        Check(draft.Stage==new ScheduledOrder().Stage&&draft.ArtworkApprovedAt==null&&!draft.DeadlineStarted&&draft.ReadyAt==null&&draft.PdfGeneratedAt==null,"etapa e datas recomeçam");
        Check(draft.Payments.Count==0&&!draft.FullyPaid&&!draft.DepositPaid&&draft.DepositAmount==0&&draft.PaymentReceivedAt==null&&!draft.PayAfterDelivery&&draft.ExpectedPaymentDate==null&&draft.RemainingAmount==90,"nada de pagamento vem junto: saldo igual ao total");
        Check(!draft.Items[0].CostReviewed&&draft.FreightCost==null,"custo volta para conferência e frete não é copiado");
        draft.Items[0].Quantity=5;Check(source.Items[0].Quantity==1000,"alterar o rascunho não muda o pedido original");

        var other=new ScheduledOrder{CustomerName=customer.Name,Contact="11911112222",Number=90,CreatedAt=now};
        var byPhone=new ScheduledOrder{CustomerName="Outro nome",Contact="(11) 99999-8888",Number=80,CreatedAt=now.AddDays(-1)};
        var gone=new ScheduledOrder{CustomerId=customer.Id,Number=95,CreatedAt=now,CancelledAt=now};
        source.CreatedAt=now.AddDays(-10);var book=new AppData{Customers=[customer],ScheduledOrders=[source,other,byPhone,gone]};
        Check(OrderRepeat.LastOf(book,customer.Id)?.Number==80,"último pedido vem do vínculo ou do telefone, sem cancelado nem homônimo");
        Check(OrderRepeat.LastOf(book,Guid.NewGuid())==null,"cliente sem pedido não traz pedido de outra pessoa");
        Console.WriteLine($"FILA HOJE E REPETIR: {passed} testes aprovados");
    }
}
