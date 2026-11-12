using System.Text.Json;
namespace GraficaCaixa;

// Read classification only: payment, cash and loss domain operations are unchanged.
public static class ReceivableViews
{
    public static ReceivableLoss? ActiveLoss(AppData data,Guid id)=>data.Prompt12.Losses.FirstOrDefault(x=>x.OrderId==id&&x.Status==LossStatus.Loss);
    // Abas de Pedidos: "ready" é o pronto esperando o cliente retirar, fora de Em andamento (só depende do cliente).
    public static string OrderBucket(AppData data,ScheduledOrder order)=>ActiveLoss(data,order.Id)!=null?"completed":order.Stage!="Entregue"?(order.Stage==OrderStages.Ready?"ready":"active"):order.PricePending||order.RemainingAmount>0?"unpaid":"completed";
    internal static readonly string[] CounterNames=["não informado","cliente balcão","balcão"];
    // Lançamentos do caixa aberto que ainda não pertencem a pedido nem a devedor: um pagamento que já entrou no caixa
    // pode ser ligado ao pedido em vez de lançado de novo (evita faturamento em dobro, ex.: pedido #237).
    public static decimal EffectiveTotal(AppData data,Sale sale)=>data.FinancialCorrections.Where(x=>x.OriginalSaleId==sale.Id).OrderBy(x=>x.CreatedAt).LastOrDefault()?.CorrectedAmount??sale.Total;
    public static List<Sale> FreeCashSales(AppData data)
    {
        var open=data.Sessions.Where(x=>x.IsOpen).SelectMany(x=>x.Sales).ToList();var openIds=open.Select(x=>x.Id).ToHashSet();
        // Todo lançamento com itens gera uma conta interna: quitada e inteira no caixa aberto, ela não prende o lançamento
        // (antes prendia sempre, e quase nenhum lançamento de balcão ficava livre). Conta com saldo ou cancelada continua presa.
        var used=data.ScheduledOrders.SelectMany(o=>o.Payments.Select(p=>p.SaleId).Append(o.DepositSaleId).Append(o.FinalSaleId)).OfType<Guid>()
            .Concat(data.ManualDebts.Where(d=>d.IsCancelled||!d.SaleIds.All(openIds.Contains)||d.Remaining(data)>0).SelectMany(x=>x.SaleIds)).Concat(data.FinancialCorrections.Select(x=>x.AdjustmentSaleId)).ToHashSet();
        return open.Where(s=>!used.Contains(s.Id)&&EffectiveTotal(data,s)>0).OrderByDescending(s=>s.CreatedAt).ToList();
    }
    // Do mesmo cliente: telefone completo igual ou mesmo nome (cliente de balcão/sem nome nunca casa pelo nome).
    public static List<Sale> FreeCashSales(AppData data,string name,string contact)
    {
        var key=ProductOperations.Normalize(name.Trim());var phone=PhoneText.Digits(contact);
        if(CounterNames.Contains(name.Trim().ToLowerInvariant()))key="";
        return FreeCashSales(data).Where(s=>phone.Length>=10&&PhoneText.Digits(s.Contact)==phone||key.Length>0&&ProductOperations.Normalize(s.CustomerName.Trim())==key).ToList();
    }
    // Lançamento com pagamento dividido (mesmo grupo): todas as partes livres juntas.
    public static List<Sale> FreeGroup(AppData data,Sale sale)=>sale.PaymentGroupId is Guid g?FreeCashSales(data).Where(x=>x.PaymentGroupId==g).OrderBy(x=>x.CreatedAt).ToList():[sale];
    public static CustomerProfile? Customer(AppData data,Guid? id,string name,string contact)
    {
        if(id.HasValue)return data.Customers.SingleOrDefault(x=>x.Id==id.Value);
        if(!FullPhone(contact)||CounterNames.Contains(name.Trim().ToLowerInvariant()))return null;
        var matches=data.Customers.Where(x=>x.Name.Equals(name,StringComparison.CurrentCultureIgnoreCase)&&FullPhone(x.Contact)&&PhoneText.Normalize(x.Contact)==PhoneText.Normalize(contact)).ToList();
        return matches.Count==1?matches[0]:null;
    }
    public static List<AgeBand> Bands(IEnumerable<ReceivableRow> pending)
    {
        var open=pending.Where(x=>!x.Unknown&&x.Balance>0).Select(x=>(Days:(DateTime.Today-x.Sort.Date).Days,x.Balance)).ToList();
        AgeBand Band(string label,Func<int,bool> test){var l=open.Where(x=>test(x.Days)).ToList();return new(label,l.Count,l.Sum(x=>x.Balance));}
        // Faixas em Sistema › Configurações › Regras (padrão: 90 e 365 dias).
        var recent=BusinessRules.Current.DebtRecentDays;var old=BusinessRules.Current.DebtOldDays;
        return [Band($"Até {recent} dias",d=>d<=recent),Band($"{recent+1} a {old} dias",d=>d>recent&&d<=old),Band(OldLabel(),d=>d>old)];
    }
    public static string OldLabel()=>BusinessRules.Current.DebtOldDays==365?"Mais de 1 ano":$"Mais de {BusinessRules.Current.DebtOldDays} dias";
    public static bool FullPhone(string? contact)=>PhoneText.Digits(contact??"").Length is >=10 and <=15;
    public static string Contact(AppData data,Guid? id,string name,string historical)
    {
        var customer=Customer(data,id,name,historical);
        return PhoneText.Format(FullPhone(customer?.Contact)?customer!.Contact:historical);
    }
    public static void Link(AppData data,ScheduledOrder order)=>order.CustomerId=Customer(data,order.CustomerId,order.CustomerName,order.Contact)?.Id;
    public static void Link(AppData data,ManualDebt debt)=>debt.CustomerId=Customer(data,debt.CustomerId,debt.CustomerName,debt.Contact)?.Id;
    public static List<ReceivableRow> Rows(AppData data,bool losses)
    {
        var rows=new List<ReceivableRow>();var debts=new DebtBalances(data);var customers=new CustomerIndex(data);
        string Status(Guid id,bool order)=>order?CollectionControl.Status(data,id,true):debts.Status(id);
        void Add(Guid id,bool order,string name,string contact,Guid? customerId,string description,decimal balance,bool unknown,DateTime origin,bool canDelete)
        {
            var loss=ActiveLoss(data,id);if(losses!=(loss!=null))return;
            var profile=customers.Find(customerId,name,contact);var current=PhoneText.Format(FullPhone(profile?.Contact)?profile!.Contact:contact);
            rows.Add(new(id,order,profile?.Name??name,current,description,losses?loss!.Amount:balance,unknown,losses?loss!.LossDate:origin,loss?.Id,loss?.Reason??"",loss?.Notes??"",losses?"Perda":Status(id,order),canDelete&&data.Prompt12.Losses.All(x=>x.OrderId!=id),profile?.Id,0,0));
        }
        foreach(var o in data.ScheduledOrders.Where(x=>!x.IsUncollected&&x.Stage=="Entregue"||losses&&ActiveLoss(data,x.Id)!=null))Add(o.Id,true,o.CustomerName,o.Contact,o.CustomerId,$"Pedido #{o.Number} • {o.ItemsDescription}",o.RemainingAmount,o.PricePending,o.DebtDate,false);
        foreach(var d in data.ManualDebts.Where(x=>!debts.Closed(x)))Add(d.Id,false,d.CustomerName,d.Contact,d.CustomerId,d.Description,debts.Remaining(d),false,d.DebtDate,d.SaleIds.Count==0);
        if(losses)foreach(var loss in data.Prompt12.Losses.Where(x=>x.Status==LossStatus.Loss&&rows.All(r=>r.Id!=x.OrderId)))rows.Add(new(loss.OrderId,false,"Referência histórica","",$"Registro {loss.OrderId}",loss.Amount,false,loss.LossDate,loss.Id,loss.Reason,loss.Notes,"Perda",false,null,0,0){CanEdit=false});
        string Key(ReceivableRow row)=>row.CustomerId?.ToString()??row.Customer+"\t"+row.Contact;var totals=rows.GroupBy(Key).ToDictionary(g=>g.Key,g=>(Total:g.Where(x=>!x.Unknown&&x.Balance>0).Sum(x=>x.Balance),Unknown:g.Count(x=>x.Unknown)));
        return rows.Select(r=>r with {CustomerTotal=totals[Key(r)].Total,UnknownCount=totals[Key(r)].Unknown}).ToList();
    }
}
// Desempenho (pacote 11): clientes indexados por Id e por telefone completo. Mesma regra de ReceivableViews.Customer/Contact:
// por Id (SingleOrDefault) ou por nome + telefone completo normalizado, só quando há exatamente um.
public sealed class CustomerIndex
{
    readonly Dictionary<Guid,List<CustomerProfile>> byId=[];
    readonly Dictionary<string,List<CustomerProfile>> byPhone=[];
    public CustomerIndex(AppData data)
    {
        static void Add<TKey>(Dictionary<TKey,List<CustomerProfile>> map,TKey key,CustomerProfile c) where TKey:notnull{if(!map.TryGetValue(key,out var list))map[key]=list=[];list.Add(c);}
        foreach(var c in data.Customers){Add(byId,c.Id,c);if(ReceivableViews.FullPhone(c.Contact))Add(byPhone,PhoneText.Normalize(c.Contact),c);}
    }
    public CustomerProfile? Find(Guid? id,string name,string contact)
    {
        if(id.HasValue)return !byId.TryGetValue(id.Value,out var same)?null:same.Count==1?same[0]:throw new InvalidOperationException("Sequence contains more than one matching element");
        if(!ReceivableViews.FullPhone(contact)||ReceivableViews.CounterNames.Contains(name.Trim().ToLowerInvariant()))return null;
        if(!byPhone.TryGetValue(PhoneText.Normalize(contact),out var candidates))return null;
        var matches=candidates.Where(x=>x.Name.Equals(name,StringComparison.CurrentCultureIgnoreCase)).Take(2).ToList();
        return matches.Count==1?matches[0]:null;
    }
}
// Desempenho (pacote 11): saldos das dívidas avulsas calculados uma vez por consulta, com índices.
// Mesma regra de ManualDebt.Paid/WasReversed/Remaining/IsClosedByCancellation e de CollectionControl.Status
// (antes cada dívida varria todas as vendas 3 vezes). A igualdade é conferida nos testes (--debtors).
public sealed class DebtBalances
{
    readonly AppData data;
    readonly Dictionary<Guid,decimal> saleTotals=[];
    readonly Dictionary<Guid,List<Guid>> groups=[];
    readonly Dictionary<Guid,FinancialCorrection> lastCorrection=[];
    readonly Dictionary<Guid,ManualDebt> firstDebt=[];
    readonly Dictionary<ManualDebt,(decimal Paid,bool Reversed)> facts=new(ReferenceEqualityComparer.Instance);
    public DebtBalances(AppData data)
    {
        this.data=data;
        var adjustments=data.FinancialCorrections.Select(c=>c.AdjustmentSaleId).ToHashSet();
        foreach(var sale in data.Sessions.SelectMany(x=>x.Sales))
        {
            saleTotals[sale.Id]=saleTotals.GetValueOrDefault(sale.Id)+sale.Total;
            if(sale.PaymentGroupId is Guid group&&!adjustments.Contains(sale.Id)){if(!groups.TryGetValue(group,out var ids))groups[group]=ids=[];ids.Add(sale.Id);}
        }
        // OrderBy(CreatedAt).LastOrDefault(): a de maior data; no empate, a última na ordem da lista.
        foreach(var c in data.FinancialCorrections)if(!lastCorrection.TryGetValue(c.OriginalSaleId,out var current)||c.CreatedAt>=current.CreatedAt)lastCorrection[c.OriginalSaleId]=c;
        foreach(var debt in data.ManualDebts)firstDebt.TryAdd(debt.Id,debt);
    }
    (decimal Paid,bool Reversed) Facts(ManualDebt debt)
    {
        if(facts.TryGetValue(debt,out var known))return known;
        var paid=debt.SaleIds.Distinct().Sum(id=>saleTotals.GetValueOrDefault(id));
        var reversed=groups.TryGetValue(debt.Id,out var group)&&group.Count>0&&paid==0&&group.All(id=>lastCorrection.TryGetValue(id,out var c)&&c.CorrectedAmount==0);
        return facts[debt]=(paid,reversed);
    }
    public decimal Paid(ManualDebt debt)=>Facts(debt).Paid;
    public bool Closed(ManualDebt debt)=>debt.IsCancelled||Facts(debt).Reversed;
    public decimal Remaining(ManualDebt debt)=>Closed(debt)?0:Math.Max(0,debt.Amount-Paid(debt));
    public string Status(Guid id)
    {
        var debt=firstDebt.TryGetValue(id,out var found)?found:throw new InvalidOperationException("Dívida não encontrada.");
        return Remaining(debt)==0?"Pago":CollectionControl.History(data,id,false).FirstOrDefault()?.Status??debt.InitialCollectionStatus;
    }
}
public sealed record ReceivableRow(Guid Id,bool Order,string Customer,string Contact,string Description,decimal Balance,bool Unknown,DateTime Sort,Guid? LossId,string Reason,string Notes,string CollectionStatus,bool CanDelete,Guid? CustomerId,decimal CustomerTotal,int UnknownCount)
{
    public bool CanEdit {get;init;}=true;
    public bool IsLoss=>LossId.HasValue;
    public bool OverThirty=>!IsLoss&&Balance>0&&(DateTime.Today-Sort.Date).Days>30;
    public string Status=>IsLoss?"PERDA":Unknown?"Valor a definir":Balance>0?(Order?"Entregue • pendente":"Avulso • pendente"):"Quitado";
    public string Detail=>Unknown?"A definir":Balance.ToString("C2");
    public string Age=>$"{Sort:dd/MM/yyyy} • {Math.Max(0,(DateTime.Today-Sort.Date).Days)} dias";
    public string LossNotes=>string.Join(" • ",new[]{Reason,Notes}.Where(x=>x.Length>0));
}
// Faixas de idade das pendências (só leitura, para o painel de Devedores): faixas configuráveis em Regras (padrão até 90 dias, 91 a 365 dias, mais de 1 ano).
public sealed record AgeBand(string Label,int Count,decimal Total);
public sealed record ReceivablePage(List<ReceivableRow> Items,int Page,int PageSize,int Total,int PendingCount,decimal PendingTotal,int LossCount,decimal LossTotal,long Revision,List<AgeBand>? Bands=null);
public sealed partial class DataStore
{
    public ReceivablePage ReceivablesView(UserIdentity actor,bool losses,string search,string status,DateTime? from,DateTime? to,string sort,string filter,int page)=>ReadWork<ReceivablePage>(db=>{
        var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,"view.debtors");if(!actor.DeviceId.HasValue)throw new UnauthorizedAccessException();
        // Leitura compartilhada é só leitura: quem não vê pedidos/valores recebe uma cópia antes do ajuste.
        var data=ReadShared(db);if(!p.GetValueOrDefault("view.orders")||!p.GetValueOrDefault("orders.value"))data=JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data))!;if(!p.GetValueOrDefault("view.orders"))data.ScheduledOrders=[];
        if(!p.GetValueOrDefault("orders.value"))foreach(var order in data.ScheduledOrders){order.OrderTotal=0;order.Payments=[];order.PricePending=true;}
        var pending=ReceivableViews.Rows(data,false);var lost=ReceivableViews.Rows(data,true);
        var rows=(losses?lost:pending).Where(x=>losses||filter=="all"||filter=="paid"&&!x.Unknown&&x.Balance==0||filter=="old"&&x.OverThirty||filter=="pending"&&(x.Unknown||x.Balance>0))
            .Where(x=>status.Length==0||x.CollectionStatus==status).Where(x=>(from==null||x.Sort.Date>=from.Value.Date)&&(to==null||x.Sort.Date<=to.Value.Date))
            .Where(x=>search.Length==0||$"{x.Customer} {x.Contact} {x.Description} {x.Reason} {x.Notes}".Contains(search,StringComparison.CurrentCultureIgnoreCase)||PhoneText.Matches(x.Contact,search));
        rows=sort=="value"?rows.OrderByDescending(x=>x.Balance).ThenBy(x=>x.Id):sort=="recent"?rows.OrderByDescending(x=>x.Sort).ThenBy(x=>x.Id):rows.OrderBy(x=>x.Sort).ThenBy(x=>x.Id);
        var list=rows.ToList();page=Math.Max(1,page);return new(list.Skip((page-1)*30).Take(30).ToList(),page,30,list.Count,pending.Count(x=>x.Unknown||x.Balance>0),pending.Where(x=>!x.Unknown&&x.Balance>0).Sum(x=>x.Balance),lost.Count,lost.Sum(x=>x.Balance),data.DataVersion,ReceivableViews.Bands(pending));
    });
}

