using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace GraficaCaixa;

public static class ReportQueries
{
    static DateTime Date(string text)=>DateTime.Parse(text,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);
    static decimal Money(string text)=>decimal.Parse(text,CultureInfo.InvariantCulture);
    public static AppData FinancialSource(SqliteConnection db,DateTime from,DateTime end)
    {
        var data=new AppData{ScheduledOrders=WebQueries.ReadOrders(db,""),ManualDebts=WebQueries.Json<List<ManualDebt>>(db,"manual_debts")};
        var orders=data.ScheduledOrders.ToDictionary(x=>x.Id);
        using(var cmd=WebQueries.Command(db,"SELECT order_id,id,paid_at,amount,payment_type,sale_id FROM order_payments"))
        using(var r=cmd.ExecuteReader())while(r.Read())if(orders.TryGetValue(Guid.Parse(r.GetString(0)),out var o))o.Payments.Add(new(){Id=Guid.Parse(r.GetString(1)),PaidAt=Date(r.GetString(2)),Amount=Money(r.GetString(3)),PaymentType=r.GetString(4),SaleId=r.IsDBNull(5)?null:Guid.Parse(r.GetString(5))});
        var snapshots=WebQueries.Json<Dictionary<Guid,OrderDetails>>(db,"order_details_v1","{}");foreach(var order in data.ScheduledOrders)if(snapshots.TryGetValue(order.Id,out var snapshot))snapshot.ApplyPayments(order);
        // All minimal payment links are needed to exclude receipts from billed sales. Item costs only for the requested period.
        using(var cmd=WebQueries.Command(db,"SELECT i.order_id,i.id,i.quantity,i.product FROM order_items i JOIN scheduled_orders o ON o.id=i.order_id WHERE o.created_at>=@from AND o.created_at<@end AND o.stage='Entregue'",("@from",from.ToString("O")),("@end",end.ToString("O"))))
        using(var r=cmd.ExecuteReader())while(r.Read())if(orders.TryGetValue(Guid.Parse(r.GetString(0)),out var o))o.Items.Add(new(){Id=Guid.Parse(r.GetString(1)),Quantity=r.GetInt32(2),Product=r.GetString(3)});
        var details=new OrderDetailsIndex(db);foreach(var o in data.ScheduledOrders.Where(x=>x.CreatedAt>=from&&x.CreatedAt<end&&x.Stage=="Entregue"))OperationalQueries.Details(details,o);
        var session=new CashSession();data.Sessions.Add(session);
        using(var cmd=WebQueries.Command(db,"SELECT id,created_at,product,units,payment_type,unit_value,total_value FROM sales WHERE (created_at>=@from AND created_at<@end) OR id IN (SELECT links.value FROM metadata m,json_each(m.value) d,json_each(d.value,'$.SaleIds') links WHERE m.key='manual_debts')",("@from",from.ToString("O")),("@end",end.ToString("O"))))
        using(var r=cmd.ExecuteReader())while(r.Read())session.Sales.Add(new(){Id=Guid.Parse(r.GetString(0)),CreatedAt=Date(r.GetString(1)),Product=r.GetString(2),Units=r.GetInt32(3),PaymentType=r.GetString(4),UnitValue=Money(r.GetString(5)),TotalValue=r.IsDBNull(6)?null:Money(r.GetString(6))});
        var saleSnapshots=WebQueries.Json<Dictionary<Guid,Sale>>(db,"sale_products_v1","{}");foreach(var sale in session.Sales)if(saleSnapshots.TryGetValue(sale.Id,out var snapshot))sale.FeePercent=snapshot.FeePercent;
        return data;
    }
    public static object Summary(SqliteConnection db,Dictionary<string,bool> p,WebQuery q)
    {
        var(from,end)=OperationalQueries.Period(q);var data=FinancialSource(db,from,end);var overview=new FinancialOverview(data,from,end);
        var dict=new Dictionary<string,object?>{["from"]=from,["to"]=end.AddDays(-1),["billed"]=overview.Billed,["received"]=overview.Receipts,["receivableOrders"]=data.ScheduledOrders.Where(x=>!x.PricePending).Sum(x=>x.RemainingAmount),["receivableDebts"]=data.ManualDebts.Sum(x=>x.Remaining(data)),["receivable"]=overview.PendingNow,["undefinedOrders"]=overview.UnknownOrders,["orders"]=data.ScheduledOrders.Count(x=>x.CreatedAt>=from&&x.CreatedAt<end),["payments"]=data.Sessions.SelectMany(x=>x.Sales).Where(x=>x.CreatedAt>=from&&x.CreatedAt<end).GroupBy(x=>x.PaymentType).Select(x=>new{method=x.Key,total=x.Sum(y=>y.Total)}).ToArray(),["contracts"]=new{billed="Pedidos com preço definido pela criação + lançamentos diretos sem vínculo, pela data do lançamento. Recebimentos de pedidos e dívidas não são faturados novamente.",received="Lançamentos efetivamente registrados no caixa no período, incluindo ajustes existentes.",receivable="Saldo atual de todos os períodos: pedidos com preço definido + dívidas avulsas. Valores indefinidos não entram na soma.",result="Resultado bruto: faturamento menos custo de pedidos concluídos criados no período e com custo conferido. Não representa lucro líquido."},["collectionSurcharges"]=overview.CollectionSurcharges,["customerExtras"]=overview.CustomerExtras,["financialFees"]=overview.FinancialFees,["receiptComposition"]=overview.ReceiptComposition,["updatedAt"]=DateTime.Now};
        if(p.GetValueOrDefault("costs.edit"))
        {dict["confirmedCosts"]=overview.ConfirmedCosts;dict["grossResult"]=overview.GrossProfit;dict["margin"]=overview.GrossMargin;dict["partial"]=overview.CostsIncomplete;dict["costCoverage"]=new{completed=overview.CompletedCount,confirmed=overview.ConfirmedCount,pending=overview.PendingCount,ignored=overview.IgnoredCount,unfinished=overview.UnfinishedCount,directUncosted=overview.UncostedDirectSales};}
        return dict;
    }
    public static CashSession? Cash(SqliteConnection db,Guid? id=null)
    {
        using var cmd=WebQueries.Command(db,"SELECT id,opened_at,closed_at,opening_amount,declared_closing_amount,opened_by,closed_by FROM cash_sessions WHERE (@id IS NULL AND closed_at IS NULL) OR id=@id ORDER BY opened_at DESC LIMIT 1",("@id",id));
        CashSession cash;
        using(var r=cmd.ExecuteReader()){if(!r.Read())return null;cash=new(){Id=Guid.Parse(r.GetString(0)),OpenedAt=Date(r.GetString(1)),ClosedAt=r.IsDBNull(2)?null:Date(r.GetString(2)),OpeningAmount=Money(r.GetString(3)),DeclaredClosingAmount=r.IsDBNull(4)?null:Money(r.GetString(4)),OpenedBy=r.GetString(5),ClosedBy=r.GetString(6)};}
        using(var sales=WebQueries.Command(db,"SELECT id,created_at,payment_type,units,unit_value,total_value FROM sales WHERE session_id=@id",("@id",cash.Id)))
        using(var r=sales.ExecuteReader())while(r.Read())cash.Sales.Add(new(){Id=Guid.Parse(r.GetString(0)),CreatedAt=Date(r.GetString(1)),PaymentType=r.GetString(2),Units=r.GetInt32(3),UnitValue=Money(r.GetString(4)),TotalValue=r.IsDBNull(5)?null:Money(r.GetString(5))});
        using(var withdrawals=WebQueries.Command(db,"SELECT j.value FROM metadata m,json_each(m.value) j WHERE m.key='cash_withdrawals_v1' AND j.key=@id",("@id",cash.Id)))
            if(withdrawals.ExecuteScalar() is string text)cash.Withdrawals=JsonSerializer.Deserialize<List<CashWithdrawal>>(text)??[];
        using(var adjustments=WebQueries.Command(db,"SELECT j.value FROM metadata m,json_each(m.value) j WHERE m.key='cash_adjustments_v1' AND j.key=@id",("@id",cash.Id)))
            if(adjustments.ExecuteScalar() is string text)cash.Adjustments=JsonSerializer.Deserialize<List<CashAdjustment>>(text)??[];
        return cash;
    }
    static object CashDto(CashSession c,string? documentUrl=null)=>new{c.Id,status=c.IsOpen?"Aberto":"Fechado",c.OpenedAt,c.ClosedAt,c.OpenedBy,c.ClosedBy,c.OpeningAmount,entries=c.SalesTotal,withdrawals=c.WithdrawalsTotal,adjustments=c.AdjustmentsTotal,c.ExpectedCash,c.DeclaredClosingAmount,difference=c.DeclaredClosingAmount-c.ExpectedCash,payments=c.Sales.GroupBy(x=>x.PaymentType).Select(x=>new{method=x.Key,total=x.Sum(y=>y.Total)}),documentUrl,contract="Entradas são recebimentos registrados; sangrias são saídas. Dinheiro esperado = abertura + recebimentos em Dinheiro − sangrias ± ajustes de caixa. Compras não são saídas deste caixa automaticamente."};
    // Lançamentos do caixa aberto (celular › Caixa): mais recentes primeiro; ajustes de cancelamento aparecem com valor negativo, como no computador.
    public static object CurrentCashSales(SqliteConnection db)
    {
        using var open=WebQueries.Command(db,"SELECT id FROM cash_sessions WHERE closed_at IS NULL ORDER BY opened_at DESC LIMIT 1");
        if(open.ExecuteScalar() is not string id)return new{items=Array.Empty<object>()};
        var items=new List<object>();
        using var cmd=WebQueries.Command(db,"SELECT created_at,customer_name,contact,product,units,payment_type,unit_value,total_value FROM sales WHERE session_id=@id ORDER BY created_at DESC,sort_index DESC",("@id",id));
        using var r=cmd.ExecuteReader();
        while(r.Read()){var units=r.GetInt32(4);var total=r.IsDBNull(7)?units*Money(r.GetString(6)):Money(r.GetString(7));items.Add(new{createdAt=Date(r.GetString(0)),customer=r.GetString(1),contact=PhoneText.Format(r.GetString(2)),product=r.GetString(3),units,payment=r.GetString(5),total});}
        return new{items};
    }
    public static object CurrentCash(SqliteConnection db)=>Cash(db) is {} cash?CashDto(cash):new{status="Fechado",message="Nenhum caixa aberto.",updatedAt=DateTime.Now};
    public static object Closures(SqliteConnection db,Dictionary<string,bool> p,UserIdentity actor,WebApiOptions options,WebQuery q)
    {
        const string where=" WHERE closed_at IS NOT NULL AND (@from IS NULL OR closed_at>=@from) AND (@to IS NULL OR closed_at<@to) AND (@search='' OR instr(lower(opened_by||' '||closed_by),lower(@search))>0)";
        (string,object?)[] values=[("@from",q.From?.ToString("O")),("@to",q.To?.AddDays(1).ToString("O")),("@search",q.Search),("@limit",q.PageSize),("@offset",q.Offset)];
        using var count=WebQueries.Command(db,"SELECT COUNT(*) FROM cash_sessions"+where,values);var total=Convert.ToInt64(count.ExecuteScalar());
        var ids=new List<Guid>();using(var cmd=WebQueries.Command(db,"SELECT id FROM cash_sessions"+where+" ORDER BY closed_at DESC,id LIMIT @limit OFFSET @offset",values))using(var r=cmd.ExecuteReader())while(r.Read())ids.Add(Guid.Parse(r.GetString(0)));
        return new{items=ids.Select(id=>CashDto(Cash(db,id)!,DocumentUrl(db,p,actor,options,"fechamento",id))).ToArray(),page=q.Page,pageSize=q.PageSize,total};
    }
    public static string? DocumentUrl(SqliteConnection db,Dictionary<string,bool> p,UserIdentity actor,WebApiOptions options,string type,Guid id)
    {try{var path=WebFiles.Document(db,p,actor,type,id,options);return path!=null&&File.Exists(path)?$"/api/v1/documentos/{type}/{id}":null;}catch(UnauthorizedAccessException){return null;}catch(WebRequestException){return null;}}
    public static object Quotes(SqliteConnection db,Dictionary<string,bool> p,UserIdentity actor,WebApiOptions options,WebQuery q)
    {
        var page=WebQueries.Page(options.Documents.Where(x=>x.Type=="orcamento").Select(x=>new{order=WebQueries.Order(db,p,x.EntityId),url=DocumentUrl(db,p,actor,options,"orcamento",x.EntityId)}).Where(x=>x.order!=null&&x.url!=null&&(q.From==null||x.order.Order.CreatedAt>=q.From)&&(q.To==null||x.order.Order.CreatedAt<q.To.Value.AddDays(1))).OrderByDescending(x=>x.order!.Order.Number),q,x=>$"{x.order!.Order.Number} {x.order.Order.CustomerName}");
        return new{items=page.Items.Select(x=>new{x.order!.Order.Id,x.order.Order.Number,x.order.Order.CustomerName,x.order.Order.CreatedAt,status="Documento associado ao pedido",x.order.Order.Finance,documentUrl=x.url}),page.Page,page.PageSize,page.Total,contract="Orçamentos que viraram pedido. Os calculados e não salvos ficam só no computador."};
    }
}
