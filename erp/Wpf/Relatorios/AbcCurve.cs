namespace GraficaCaixa;

// Uma linha da Curva ABC: produto ou cliente, com o faturamento do período e a classe (A = até 80% do faturamento, B = até 95%, C = o resto).
public sealed class AbcLine
{
    public string Name {get;init;}="";
    public int Count {get;init;}
    public decimal Quantity {get;init;}
    public decimal Revenue {get;init;}
    // Custo só quando todas as vendas da linha têm custo informado; senão a margem fica em branco (custo pendente não é custo zero).
    public decimal? Cost {get;init;}
    public decimal Share {get;set;}
    public decimal Cumulative {get;set;}
    public string Class {get;set;}="C";
    public decimal? Margin=>Cost.HasValue&&Revenue>0?decimal.Round((Revenue-Cost.Value)/Revenue*100,1):null;
}

// Curva ABC do período, com a mesma base do faturamento dos Relatórios: pedidos (sem cancelados e sem "a definir"), lançamentos de balcão
// com itens e vendas diretas do caixa (sem sinal/parcela de pedido e sem recebimento avulso).
public static class AbcCurve
{
    public const decimal LimitA=80m, LimitB=95m;
    sealed class Acc{public string Name="";public int Count;public decimal Quantity;public decimal Revenue;public decimal Cost;public bool CostKnown=true;}
    static bool IsOrderPayment(Sale x)=>x.Product.StartsWith("Recebimento avulso:",StringComparison.OrdinalIgnoreCase)||System.Text.RegularExpressions.Regex.IsMatch(x.Product,@"^(Sinal|Parcela|Pagamento final) do pedido #",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static string Key(string text)=>string.Join(' ',text.Trim().ToLowerInvariant().Split(' ',StringSplitOptions.RemoveEmptyEntries));
    static string CustomerName(string name)=>string.IsNullOrWhiteSpace(name)?"Balcão (sem nome)":name.Trim();

    // cashDaySales: vendas já na data do caixa (FinancialOverview.SalesByCashDay), quando a tela já calculou.
    public static (List<AbcLine> Products,List<AbcLine> Customers) Of(AppData data,DateTime start,DateTime end,IEnumerable<Sale>? cashDaySales=null)
    {
        var products=new Dictionary<string,Acc>();var customers=new Dictionary<string,Acc>();
        var names=data.Products.ToDictionary(x=>x.Id,x=>x.Description);
        void Product(Guid? id,string name,decimal quantity,decimal revenue,decimal? cost,bool newSale)
        {
            if(revenue==0&&quantity==0)return;
            var label=id is Guid g&&names.TryGetValue(g,out var current)?current:(string.IsNullOrWhiteSpace(name)?"Não informado":name.Trim());
            // Pelo nome atual do cadastro: o mesmo produto vendido pelo pedido (com cadastro) e digitado no balcão vira uma linha só.
            var key=Key(label);
            if(!products.TryGetValue(key,out var acc))products[key]=acc=new Acc{Name=label};
            if(newSale)acc.Count++;acc.Quantity+=quantity;acc.Revenue+=revenue;if(cost is decimal c)acc.Cost+=c;else acc.CostKnown=false;
        }
        void Customer(Guid? id,string name,decimal revenue,decimal? cost)
        {
            var label=CustomerName(name);var key=id is Guid g?g.ToString("N"):"n:"+Key(label);
            if(!customers.TryGetValue(key,out var acc))customers[key]=acc=new Acc{Name=label};
            acc.Count++;acc.Revenue+=revenue;if(cost is decimal c)acc.Cost+=c;else acc.CostKnown=false;
        }
        foreach(var order in data.ScheduledOrders.Where(x=>!x.IsCancelled&&!x.PricePending&&x.CreatedAt>=start&&x.CreatedAt<end))
        {
            var seen=new HashSet<string>();
            foreach(var item in order.Items)
            {
                var cost=order.IgnoreCosts||item.CostIgnored?null:item.TotalCost;
                Product(item.ProductId,item.Product,item.Quantity,item.LineTotal??0,cost,seen.Add((item.ProductId?.ToString("N")??Key(item.Product))));
            }
            Customer(order.CustomerId,order.CustomerName,order.OrderTotal,order.HasCosts?order.TotalCost:null);
        }
        var hidden=Cancellations.HiddenSaleIds(data);
        // Lançamento que virou pedido já foi contado acima pelo pedido: a conta interna dele não conta de novo.
        var inOrders=data.ScheduledOrders.SelectMany(x=>x.Payments).Select(p=>p.SaleId).OfType<Guid>().ToHashSet();
        foreach(var debt in data.ManualDebts.Where(x=>x.Items.Count>0&&!x.SaleIds.Any(inOrders.Contains)&&x.CreatedAt>=start&&x.CreatedAt<end&&(x.SaleIds.Count==0||!x.SaleIds.All(hidden.Contains))))
        {
            foreach(var item in debt.Items)Product(item.ProductId,item.Product,item.Quantity,item.LineTotal??0,item.CostIgnored?null:item.TotalCost,true);
            Customer(debt.CustomerId,debt.CustomerName,debt.Amount,debt.Items.All(i=>i.TotalCost!=null)?debt.Items.Sum(i=>i.TotalCost!.Value):null);
        }
        var linked=data.ScheduledOrders.SelectMany(x=>x.Payments.Select(p=>p.SaleId).Concat(new[]{x.DepositSaleId,x.FinalSaleId})).OfType<Guid>().Concat(data.ManualDebts.SelectMany(x=>x.SaleIds)).ToHashSet();
        foreach(var sale in (cashDaySales??FinancialOverview.SalesByCashDay(data)).Where(x=>x.CreatedAt>=start&&x.CreatedAt<end&&!linked.Contains(x.Id)&&!hidden.Contains(x.Id)&&!IsOrderPayment(x)))
        {
            var cost=sale.SuggestedUnitCost is decimal unit?decimal.Round(unit*Math.Max(1,sale.Units),2):(decimal?)null;
            Product(sale.ProductId,sale.Product,Math.Max(1,sale.Units),sale.Total,cost,true);
            Customer(null,sale.CustomerName,sale.Total,cost);
        }
        return (Rank(products.Values),Rank(customers.Values));
    }

    static List<AbcLine> Rank(IEnumerable<Acc> values)
    {
        var lines=values.Where(x=>x.Revenue>0).OrderByDescending(x=>x.Revenue).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase)
            .Select(x=>new AbcLine{Name=x.Name,Count=x.Count,Quantity=x.Quantity,Revenue=x.Revenue,Cost=x.CostKnown?x.Cost:null}).ToList();
        var total=lines.Sum(x=>x.Revenue);var before=0m;
        foreach(var line in lines)
        {
            // Classe pelo acumulado ANTES da linha: o item que cruza os 80% ainda é A (assim sempre há pelo menos um A).
            line.Class=before<LimitA?"A":before<LimitB?"B":"C";
            line.Share=total==0?0:line.Revenue/total*100;before+=line.Share;line.Cumulative=Math.Min(100,before);
        }
        return lines;
    }
}
