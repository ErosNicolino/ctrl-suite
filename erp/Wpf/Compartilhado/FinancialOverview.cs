namespace GraficaCaixa;
public sealed class FinancialOverview
{
    public decimal CollectionSurcharges {get;init;}
    public decimal CustomerExtras {get;init;}
    public decimal FinancialFees {get;init;}
    public string ReceiptComposition=>$"Recebimentos brutos: {Receipts:C2} • Acréscimos de cobrança: {CollectionSurcharges:C2} • Valores adicionais: {CustomerExtras:C2} • Taxas financeiras: {FinancialFees:C2}. Faturamento de produtos/serviços preservado separadamente.";
    public decimal ConfirmedCosts {get;init;}
    public decimal GrossProfit => Billed - ConfirmedCosts;
    public decimal? GrossMargin => Billed > 0 ? GrossProfit / Billed * 100 : null;
    public int CompletedCount {get;init;}
    public int ConfirmedCount {get;init;}
    public int PendingCount {get;init;}
    public int IgnoredCount {get;init;}
    public int UnfinishedCount {get;init;}
    public int UncostedDirectSales {get;init;}
    public decimal? Coverage => CompletedCount>0 ? ConfirmedCount*100m/CompletedCount : null;
    public bool CostsIncomplete => PendingCount+IgnoredCount+UnfinishedCount+UncostedDirectSales>0;
    public string CoverageText => $"Concluídos: {CompletedCount} • Conferidos: {ConfirmedCount} • Pendentes: {PendingCount} • Ignorados: {IgnoredCount} • Cobertura: "+(Coverage.HasValue?Coverage.Value.ToString("N1")+"%":"—")+$"\nPedidos em andamento: {UnfinishedCount} • Lançamentos diretos sem custo apurado: {UncostedDirectSales}";
    public List<(DateTime Date,decimal Value)> CostSeries {get;init;}
    public decimal Receipts {get;init;}
    public decimal Billed {get;init;}
    public decimal Withdrawals {get;init;}
    public decimal PreviousReceipts {get;init;}
    public decimal PreviousBilled {get;init;}
    public decimal PendingNow {get;init;}
    public int UnknownOrders {get;init;}
    public List<(DateTime Date,decimal Value)> ReceiptSeries {get;init;}
    public List<(DateTime Date,decimal Value)> BilledSeries {get;init;}
    // Dia contábil do lançamento: o dia em que o caixa foi aberto. Um caixa aberto em 08/10 e fechado em 09/10
    // mantém tudo em 08/10, mesmo o que foi digitado no dia seguinte; a hora real continua registrada.
    public static DateTime CashDay(CashSession session,Sale sale)=>sale.CreatedAt.Date>session.OpenedAt.Date?session.OpenedAt.Date.Add(sale.CreatedAt.TimeOfDay):sale.CreatedAt;
    // Resumo calculado no servidor (Relatórios): as séries são tuplas, por isso o JSON inclui campos (Item1/Item2).
    public static readonly System.Text.Json.JsonSerializerOptions Wire=new(){IncludeFields=true};
    [System.Text.Json.Serialization.JsonConstructor]
    public FinancialOverview(){CostSeries=[];ReceiptSeries=[];BilledSeries=[];}
    public static List<Sale> SalesByCashDay(AppData data)=>data.Sessions.SelectMany(s=>s.Sales.Select(x=>{var copy=System.Text.Json.JsonSerializer.Deserialize<Sale>(System.Text.Json.JsonSerializer.Serialize(x))!;copy.CreatedAt=CashDay(s,x);return copy;})).ToList();
    public FinancialOverview(AppData data,DateTime start,DateTime end,decimal? debtsRemaining=null)
    {
        var linked=data.ScheduledOrders.SelectMany(x=>x.Payments.Select(p=>p.SaleId).Concat(new[]{x.DepositSaleId,x.FinalSaleId}))
            .OfType<Guid>().Concat(data.ManualDebts.SelectMany(x=>x.SaleIds)).ToHashSet();
        var sales=SalesByCashDay(data);
        // Lançamento de balcão com itens fica num registro próprio (com os itens e as partes do pagamento): entra no faturamento pelo total
        // dos itens, uma vez só, e o custo dos itens (vindo do cadastro do produto ou digitado) entra nos custos conferidos.
        var hiddenSales=Cancellations.HiddenSaleIds(data);
        var directSales=data.ManualDebts.Where(x=>x.Items.Count>0&&(x.SaleIds.Count==0||!x.SaleIds.All(hiddenSales.Contains))).ToList();
        static decimal? DirectCost(ManualDebt x)=>x.Items.All(i=>i.TotalCost!=null)?x.Items.Sum(i=>i.TotalCost!.Value):null;
        var revenueEvents=data.ScheduledOrders.Where(x=>!x.PricePending&&!x.IsCancelled).Select(x=>(Date:x.CreatedAt,Value:x.OrderTotal))
            .Concat(sales.Where(x=>!linked.Contains(x.Id)&&!x.Product.StartsWith("Recebimento avulso:",StringComparison.OrdinalIgnoreCase)
                &&!System.Text.RegularExpressions.Regex.IsMatch(x.Product,@"^(Sinal|Parcela|Pagamento final) do pedido #",System.Text.RegularExpressions.RegexOptions.IgnoreCase)).Select(x=>(Date:x.CreatedAt,Value:x.Total))).Concat(directSales.Select(x=>(Date:x.CreatedAt,Value:x.Amount))).ToList();
        var periodOrders=data.ScheduledOrders.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end&&!x.IsCancelled).ToList();
        var completed=periodOrders.Where(OrderOperations.IsFinished).ToList();
        CompletedCount=completed.Count;ConfirmedCount=completed.Count(x=>x.HasCosts);PendingCount=completed.Count(x=>x.CostStatus=="Pendente");IgnoredCount=completed.Count(x=>x.IgnoreCosts);UnfinishedCount=periodOrders.Count-completed.Count;
        CostSeries=completed.Where(x=>x.HasCosts&&!x.PricePending).Select(x=>(x.CreatedAt,x.TotalCost)).Concat(directSales.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end&&DirectCost(x)!=null).Select(x=>(x.CreatedAt,DirectCost(x)!.Value))).ToList();ConfirmedCosts=CostSeries.Sum(x=>x.Value);
        UncostedDirectSales=sales.Count(x=>x.CreatedAt>=start&&x.CreatedAt<end&&!linked.Contains(x.Id)&&!x.Product.StartsWith("Recebimento avulso:",StringComparison.OrdinalIgnoreCase)&&!System.Text.RegularExpressions.Regex.IsMatch(x.Product,@"^(Sinal|Parcela|Pagamento final) do pedido #",System.Text.RegularExpressions.RegexOptions.IgnoreCase))+directSales.Count(x=>x.CreatedAt>=start&&x.CreatedAt<end&&DirectCost(x)==null);
        var previous=start-(end-start);
        var payments=data.ScheduledOrders.SelectMany(x=>x.Payments).Where(x=>x.SaleId!=null&&x.PaidAt>=start&&x.PaidAt<end).ToList();var debtExtras=data.ManualDebts.Where(x=>!x.IsCancelled&&x.CreatedAt>=start&&x.CreatedAt<end).ToList();CollectionSurcharges=payments.Where(x=>x.ExcessKind==PaymentPolicy.CollectionSurcharge).Sum(x=>x.ExcessAmount)+debtExtras.Sum(x=>x.CollectionSurcharge??0);CustomerExtras=payments.Where(x=>x.ExcessKind==PaymentPolicy.CustomerExtra).Sum(x=>x.ExcessAmount)+debtExtras.Sum(x=>x.CustomerExtra??0);FinancialFees=sales.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).Sum(x=>PaymentPolicy.Fee(x.Total,x.FeePercent??PaymentPolicy.HistoricalRate(x.PaymentType)));
        Receipts=sales.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).Sum(x=>x.Total);
        PreviousReceipts=sales.Where(x=>x.CreatedAt>=previous&&x.CreatedAt<start).Sum(x=>x.Total);
        Billed=revenueEvents.Where(x=>x.Date>=start&&x.Date<end).Sum(x=>x.Value);
        PreviousBilled=revenueEvents.Where(x=>x.Date>=previous&&x.Date<start).Sum(x=>x.Value);
        Withdrawals=data.Sessions.SelectMany(x=>x.Withdrawals).Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).Sum(x=>x.Amount);
        PendingNow=data.ScheduledOrders.Where(x=>!x.PricePending&&!x.IsCancelled).Sum(x=>x.RemainingAmount)+(debtsRemaining??data.ManualDebts.Sum(x=>x.Remaining(data)));
        UnknownOrders=data.ScheduledOrders.Count(x=>x.PricePending);
        ReceiptSeries=sales.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).Select(x=>(x.CreatedAt,x.Total)).ToList();
        BilledSeries=revenueEvents.Where(x=>x.Date>=start&&x.Date<end).ToList();
    }
    public static string Compare(decimal current,decimal previous)=>previous==0?(current==0?"Sem movimento nos dois períodos":"Sem base anterior"):$"{(current-previous)/previous:+0.0%;-0.0%;0.0%}";
}
// Relatórios (desktop remoto): o que não depende só do período sai pronto do servidor, calculado sobre os mesmos dados
// que a tela recebia; a tela recebe só os lançamentos, pedidos e fechamentos do período.
public sealed class ReportTotals
{
    public FinancialOverview Overview {get;init;}=new();
    public decimal ReceivableOrders {get;init;}
    public decimal ReceivableDebts {get;init;}
    public DateTime? FirstDate {get;init;}
    public static ReportTotals Of(AppData data,DateTime start,DateTime end)
    {
        var dates=data.Sessions.SelectMany(x=>x.Sales).Select(x=>x.CreatedAt).Concat(data.ScheduledOrders.Select(x=>x.CreatedAt)).ToList();
        // Mesmo resultado com menos lançamentos: o resumo só soma lançamentos do período e do período anterior (pelo dia de caixa)
        // e sangrias do período; o saldo das dívidas só olha os lançamentos ligados a elas (SaleIds e PaymentGroupId).
        var needed=Needed(data,start,end);
        var source=new AppData{ScheduledOrders=data.ScheduledOrders,ManualDebts=data.ManualDebts,FinancialCorrections=data.FinancialCorrections,
            Sessions=data.Sessions.Select(s=>new CashSession{Id=s.Id,OpenedAt=s.OpenedAt,ClosedAt=s.ClosedAt,Sales=s.Sales.Where(x=>needed(s,x)).ToList(),Withdrawals=s.Withdrawals.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).ToList()}).ToList()};
        var debts=DebtsRemaining(source);
        return new(){Overview=new FinancialOverview(source,start,end,debts),ReceivableOrders=data.ScheduledOrders.Where(x=>!x.PricePending).Sum(x=>x.RemainingAmount),ReceivableDebts=debts,FirstDate=dates.Count==0?null:dates.Min()};
    }
    static Func<CashSession,Sale,bool> Needed(AppData data,DateTime start,DateTime end)
    {
        var previous=start-(end-start);var debtIds=data.ManualDebts.Select(x=>x.Id).ToHashSet();var debtSales=data.ManualDebts.SelectMany(x=>x.SaleIds).ToHashSet();
        return (s,x)=>debtSales.Contains(x.Id)||x.PaymentGroupId is Guid g&&debtIds.Contains(g)||FinancialOverview.CashDay(s,x)>=previous&&FinancialOverview.CashDay(s,x)<end;
    }
    // No servidor, antes do filtro de permissões (que clona tudo): de cada caixa, só o que Of() e KeepPeriod() usam, mais o primeiro
    // lançamento (para a primeira data de "Tudo"); fechamentos do período inteiros. Mesmos caixas, então o filtro decide igual.
    public static void Prefilter(AppData data,DateTime start,DateTime end)
    {
        var needed=Needed(data,start,end);var closed=Closed(data,start,end);
        data.Sessions=data.Sessions.Select(s=>{if(closed.Contains(s.Id))return s;var first=s.Sales.OrderBy(x=>x.CreatedAt).FirstOrDefault();return Copy(s,s.Sales.Where(x=>ReferenceEquals(x,first)||needed(s,x)).ToList(),s.Withdrawals.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).ToList());}).ToList();
        data.CashClosureVersions=data.CashClosureVersions.Where(x=>closed.Contains(x.SessionId)).ToList();
    }
    static HashSet<Guid> Closed(AppData data,DateTime start,DateTime end)=>data.Sessions.Where(x=>!x.IsOpen&&x.ClosedAt>=start&&x.ClosedAt<end).Select(x=>x.Id).ToHashSet();
    static CashSession Copy(CashSession s,List<Sale> sales,List<CashWithdrawal> withdrawals)=>new(){Id=s.Id,OpenedAt=s.OpenedAt,ClosedAt=s.ClosedAt,OpeningAmount=s.OpeningAmount,DeclaredClosingAmount=s.DeclaredClosingAmount,OpenedBy=s.OpenedBy,ClosedBy=s.ClosedBy,Sales=sales,Withdrawals=withdrawals,Adjustments=s.Adjustments};
    // Soma de ManualDebt.Remaining(data), dívida a dívida, vendo só os lançamentos dela (pelo Id ou pelo PaymentGroupId):
    // a mesma regra sem varrer todos os lançamentos para cada dívida.
    static decimal DebtsRemaining(AppData data)
    {
        var sales=data.Sessions.SelectMany(x=>x.Sales).ToList();var byId=sales.ToLookup(x=>x.Id);var byGroup=sales.Where(x=>x.PaymentGroupId!=null).ToLookup(x=>x.PaymentGroupId!.Value);
        return data.ManualDebts.Sum(d=>d.Remaining(new AppData{FinancialCorrections=data.FinancialCorrections,Sessions=[new CashSession{Sales=d.SaleIds.Distinct().SelectMany(id=>byId[id]).Concat(byGroup[d.Id]).Distinct().ToList()}]}));
    }
    // Só o período: fechamentos do período completos (com todos os lançamentos e sangrias); dos outros caixas, os lançamentos
    // cujo dia de caixa cai no período e as sangrias do período. Pedidos criados no período; dívidas já vêm somadas.
    public static void KeepPeriod(AppData data,DateTime start,DateTime end)
    {
        var closed=Closed(data,start,end);
        data.Sessions=data.Sessions.Select(s=>closed.Contains(s.Id)?s:Copy(s,s.Sales.Where(x=>FinancialOverview.CashDay(s,x)>=start&&FinancialOverview.CashDay(s,x)<end).ToList(),s.Withdrawals.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).ToList()))
            .Where(s=>closed.Contains(s.Id)||s.Sales.Count>0||s.Withdrawals.Count>0||s.Adjustments.Any(x=>x.CreatedAt>=start&&x.CreatedAt<end)).ToList();
        data.ScheduledOrders=data.ScheduledOrders.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).ToList();
        // Lançamentos de balcão com itens do período ficam (Curva ABC por item); o "a receber" já vem pronto nos totais.
        data.ManualDebts=data.ManualDebts.Where(x=>x.Items.Count>0&&x.CreatedAt>=start&&x.CreatedAt<end).ToList();data.CashClosureVersions=data.CashClosureVersions.Where(x=>closed.Contains(x.SessionId)).ToList();
    }
}
