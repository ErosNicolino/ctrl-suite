namespace GraficaCaixa;
// Fila "Hoje" do Início: junta o que pede atenção no dia e hoje está em cinco telas. Só leitura e sem valores em dinheiro;
// cada grupo depende da permissão de ver a tela de origem, e o item sai da fila quando é resolvido lá.
public sealed record TodayItem(string Kind,string Label,string Tone,string Title,string Detail,Guid? Id);
public static class TodayQueue
{
    public const int MaxItems=60;
    public static List<TodayItem> Build(AppData data,Func<string,bool> can,Guid userId,DateTime now)
    {
        var today=now.Date;var result=new List<TodayItem>();
        var orders=data.ScheduledOrders.Where(x=>!x.IsUncollected&&!x.IsCancelled&&x.Stage!="Entregue").ToList();
        static string Who(ScheduledOrder x)=>$"#{x.Number} • {(string.IsNullOrWhiteSpace(x.CustomerName)?"Cliente não informado":x.CustomerName.Trim())}";
        static string Days(int days)=>days==1?"1 dia":$"{days} dias";
        if(can("view.orders"))
        {
            var running=orders.Where(x=>x.Stage!="Pronto para retirada"&&x.DeadlineStarted).Select(x=>(Order:x,Days:BusinessCalendar.RemainingBusinessDays(today,x.DueDate))).ToList();
            foreach(var x in running.Where(x=>x.Days<0).OrderBy(x=>x.Days))result.Add(new("order","Atrasado","Danger",Who(x.Order),$"Há {Days(-x.Days)} úteis · {x.Order.ItemsDescription}",x.Order.Id));
            foreach(var x in running.Where(x=>x.Days==0).OrderBy(x=>x.Order.Number))result.Add(new("order","Para hoje","Warning",Who(x.Order),x.Order.ItemsDescription,x.Order.Id));
        }
        if(can("view.orders"))
        {
            var open=orders.ToDictionary(x=>x.Id);
            foreach(var x in (data.Prompt12.Outsourced??[]).Where(x=>x.ReturnedAt==null&&x.ExpectedBack?.Date<=today&&open.ContainsKey(x.OrderId)).OrderBy(x=>x.ExpectedBack))
            {
                var late=x.ExpectedBack!.Value.Date<today;var from=string.IsNullOrWhiteSpace(x.Supplier)?x.Service:$"{x.Service} · {x.Supplier}";
                result.Add(new("order","Terceirizado",late?"Danger":"Warning",Who(open[x.OrderId]),late?$"{from} · volta prevista em {x.ExpectedBack:dd/MM}":$"{from} · volta hoje",x.OrderId));
            }
        }
        if(can("view.purchases"))
            foreach(var x in data.Prompt12.PurchaseNeeds.Where(x=>x.Status==PurchaseNeedStatus.ToBuy&&x.BuyBy?.Date<=today).OrderBy(x=>x.BuyBy))
                result.Add(new("purchase","Compra",x.BuyBy!.Value.Date<today?"Danger":"Warning",x.Material,x.BuyBy.Value.Date<today?$"Atrasada há {Days((today-x.BuyBy.Value.Date).Days)}":"Comprar hoje",x.Id));
        if(can("view.debtors"))
            foreach(var x in orders.Concat(data.ScheduledOrders.Where(o=>o.Stage=="Entregue"&&!o.IsUncollected&&!o.IsCancelled)).Where(x=>x.PayAfterDelivery&&!x.PricePending&&x.RemainingAmount>0&&x.ExpectedPaymentDate?.Date<=today).OrderBy(x=>x.ExpectedPaymentDate))
                result.Add(new("debt","Cobrança",x.ExpectedPaymentDate!.Value.Date<today?"Danger":"Warning",Who(x),x.ExpectedPaymentDate.Value.Date<today?$"Pagamento previsto em {x.ExpectedPaymentDate:dd/MM}":"Pagamento previsto para hoje",x.Id));
        if(can("view.prospects"))
            foreach(var x in data.Prospects.Where(x=>x.Status!="Convertido em pedido"&&x.Status!="Não fechou"&&x.NextContactAt?.Date<=today).OrderBy(x=>x.NextContactAt))
                result.Add(new("prospect","Orçamento","Info",string.IsNullOrWhiteSpace(x.CustomerName)?PhoneText.Format(x.Contact):x.CustomerName.Trim(),x.Description,x.Id));
        if(can("view.reminders"))
            foreach(var x in (data.Reminders??[]).Where(x=>x.DoneAt==null&&!x.ReadByUsers.ContainsKey(userId)&&(x.DueAt==null||x.DueAt<=now)).OrderBy(x=>x.DueAt??x.CreatedAt))
            {
                var name=new[]{x.Title,x.Name,PhoneText.Format(x.Phone)}.FirstOrDefault(t=>!string.IsNullOrWhiteSpace(t))??"Lembrete";
                result.Add(new("reminder","Lembrete","Info",name.Trim(),x.Description,x.Id));
            }
        if(can("view.orders"))
            foreach(var x in orders.Where(x=>x.Stage=="Pronto para retirada").OrderBy(x=>x.ReadyAt??x.CreatedAt))
                result.Add(new("order","Retirada","Ok",Who(x),x.ReadyAt is DateTime ready&&(today-ready.Date).Days>0?$"Pronto há {Days((today-ready.Date).Days)} · {x.ItemsDescription}":x.ItemsDescription,x.Id));
        return result;
    }
}
// Repetir pedido: rascunho novo com o mesmo cliente, itens, preços e prazo. Sem número, datas, etapa, pagamentos nem custos conferidos;
// nada é gravado até a pessoa conferir e salvar na tela do pedido.
public static class OrderRepeat
{
    public static ScheduledOrder Draft(ScheduledOrder source)=>new()
    {
        CustomerId=source.CustomerId,CustomerName=source.CustomerName,Contact=source.Contact,Description=source.Description,Notes=source.Notes,
        Items=source.Items.Select(x=>new OrderItem{ProductId=x.ProductId,Product=x.Product,Quantity=x.Quantity,Unit=x.Unit,UnitPrice=x.UnitPrice,UnitCost=x.UnitCost,EnteredTotalCost=x.EnteredTotalCost,CostIgnored=x.CostIgnored,CostSource=x.CostSource}).ToList(),
        UseItemPrices=source.UseItemPrices,OrderTotal=source.OrderTotal,OriginalTotal=source.OriginalTotal,DiscountPercent=source.DiscountPercent,FixedDiscount=source.FixedDiscount,PricePending=source.PricePending,
        ProductionBusinessDays=source.ProductionBusinessDays,ItemCostsEnabled=source.ItemCostsEnabled,IgnoreCosts=source.IgnoreCosts
    };
    // Último pedido do cliente: pelo vínculo do pedido; sem vínculo, pelo telefone completo. Nunca pelo nome, que pode ser de outra pessoa.
    public static ScheduledOrder? LastOf(AppData data,Guid customerId)
    {
        var phone=PhoneText.Digits(data.Customers.FirstOrDefault(x=>x.Id==customerId)?.Contact??"");
        return data.ScheduledOrders.Where(x=>!x.IsCancelled&&(x.CustomerId==customerId||x.CustomerId==null&&phone.Length>=10&&PhoneText.Digits(x.Contact)==phone)).OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Number).FirstOrDefault();
    }
}
