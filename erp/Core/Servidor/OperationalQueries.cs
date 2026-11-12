using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace GraficaCaixa;

// Desempenho (pacote 11): order_details_v1 lido uma vez por consulta, em vez de um json_each por pedido.
// Mesma busca (chave exata = Id do pedido, primeira ocorrência) e mesma desserialização do texto da entrada.
public sealed class OrderDetailsIndex
{
    readonly Dictionary<string,JsonElement> entries=new(StringComparer.Ordinal);
    public OrderDetailsIndex(SqliteConnection db)
    {
        var text=WebQueries.Metadata(db,"order_details_v1","");if(text.Length==0)return;
        using var document=JsonDocument.Parse(text);if(document.RootElement.ValueKind!=JsonValueKind.Object)return;
        foreach(var entry in document.RootElement.EnumerateObject())entries.TryAdd(entry.Name,entry.Value.Clone());
    }
    public OrderDetails? For(Guid id)=>entries.TryGetValue(id.ToString(),out var value)&&value.ValueKind!=JsonValueKind.Null?JsonSerializer.Deserialize<OrderDetails>(value.GetRawText()):null;
}
public static class OperationalQueries
{
    public static void RequireFinance(IReadOnlyDictionary<string,bool> p)
    {foreach(var key in new[]{"view.orders","view.cash","view.debtors","orders.value","finance.receive"})if(!p.GetValueOrDefault(key))throw new UnauthorizedAccessException();}
    public static (DateTime From,DateTime End) Period(WebQuery q)
    {
        var from=q.From??new DateTime(DateTime.Today.Year,DateTime.Today.Month,1);var end=(q.To??DateTime.Today).AddDays(1);
        if(end<=from||(end-from).TotalDays>366)throw new WebRequestException(400,"Escolha um período de até 366 dias.");
        return(from,end);
    }
    public static void Details(OrderDetailsIndex details,ScheduledOrder order)=>details.For(order.Id)?.Apply(order);
    public static void Details(SqliteConnection db,ScheduledOrder order)
    {
        using var cmd=WebQueries.Command(db,"SELECT j.value FROM metadata m,json_each(m.value) j WHERE m.key='order_details_v1' AND j.key=@id",("@id",order.Id));
        if(cmd.ExecuteScalar() is string text)JsonSerializer.Deserialize<OrderDetails>(text)?.Apply(order);
    }
    // Desempenho (pacote 11): mesmo resultado de CustomerId para muitas linhas, com uma leitura só
    // (igualdade exata como a coluna TEXT/BINARY; exatamente um cliente com aquele nome e contato).
    public static Func<string,string,Guid?> CustomerIds(SqliteConnection db)
    {
        var map=new Dictionary<(string,string),(Guid Id,int Count)>();
        using(var cmd=WebQueries.Command(db,"SELECT id,name,contact FROM customers"))using(var r=cmd.ExecuteReader())
            while(r.Read()){var key=(r.GetString(1),r.GetString(2));map[key]=map.TryGetValue(key,out var seen)?(seen.Id,seen.Count+1):(Guid.Parse(r.GetString(0)),1);}
        return (name,contact)=>map.TryGetValue((name,contact),out var hit)&&hit.Count==1?hit.Id:null;
    }
    public static Guid? CustomerId(SqliteConnection db,string name,string contact)
    {
        using var cmd=WebQueries.Command(db,"SELECT id FROM customers WHERE name=@n AND contact=@c LIMIT 2",("@n",name),("@c",contact));
        using var r=cmd.ExecuteReader();if(!r.Read())return null;var id=Guid.Parse(r.GetString(0));return r.Read()?null:id;
    }
    public static object Overview(SqliteConnection db,Dictionary<string,bool> p,WebQuery q)
    {
        var (from,end)=Period(q);object? operational=null,financial=null;
        if(p.GetValueOrDefault("view.orders"))
        {
            var orders=WebQueries.ReadOrders(db," WHERE is_uncollected=0");var today=DateTime.Today;
            var active=orders.Where(x=>x.Stage!="Entregue").ToList();var working=active.Where(x=>x.Stage!="Pronto para retirada"&&x.DeadlineStarted).ToList();
            var upcoming=active.OrderBy(OrderOperations.OperationalPriority).ThenBy(x=>OrderOperations.IsImmediate(x)?x.CreatedAt:x.DueDate).ThenBy(x=>x.Number).Take(5).ToList();if(p.GetValueOrDefault("orders.value")&&p.GetValueOrDefault("finance.receive"))foreach(var order in upcoming)WebQueries.Payments(db,order);
            operational=new{nextOrders=upcoming.Select(x=>WebQueries.OrderDto(x,p)).ToArray(),immediate=active.Count(OrderOperations.IsImmediate),active=active.Count(x=>x.Stage!="Pronto para retirada"),overdue=working.Count(x=>BusinessCalendar.RemainingBusinessDays(today,x.DueDate)<0),today=working.Count(x=>x.DueDate.Date==today),ready=active.Count(x=>x.Stage=="Pronto para retirada"),waiting=active.Count(x=>x.Stage is "Agendado" or "Arte / criação" or "Aguardando aprovação"),created=orders.Count(x=>x.CreatedAt>=from&&x.CreatedAt<end),completed=orders.Count(x=>x.CreatedAt>=from&&x.CreatedAt<end&&x.Stage=="Entregue"),byStage=OrderStages.All.Select(stage=>new{stage,count=orders.Count(x=>x.Stage==stage)}),noDeadline=active.Count(x=>!x.DeadlineStarted),completedContract="Concluídos: estado atual dos pedidos criados no período; não é a data da entrega."};
        }
        if(p.GetValueOrDefault("view.reports")&&new[]{"view.orders","view.cash","view.debtors","orders.value","finance.receive"}.All(x=>p.GetValueOrDefault(x)))financial=ReportQueries.Summary(db,p,q);
        int? reminders=p.GetValueOrDefault("view.reminders")?WebQueries.Json<List<Reminder>>(db,"reminders_v1").Count(x=>x.DoneAt==null):null;
        int? overdueReturns=p.GetValueOrDefault("view.prospects")?WebQueries.Json<List<Prospect>>(db,"prospects").Count(x=>x.NextContactAt?.Date<DateTime.Today&&x.OrderId==null):null;
        return new{from,to=end.AddDays(-1),operational,financial,reminders,overdueReturns,updatedAt=DateTime.Now};
    }
    public static IResult CustomerOrders(SqliteConnection db,Dictionary<string,bool> p,Guid id,WebQuery q)
    {
        var c=WebQueries.Customer(db,p,id);if(c==null)return Results.NotFound();
        if(CustomerId(db,c.Name,c.Contact)!=id)return Results.Ok(new{ambiguous=true,items=Array.Empty<WebOrder>(),page=q.Page,pageSize=q.PageSize,total=0});
        var source=WebQueries.ReadOrders(db," WHERE customer_name=@n AND contact=@c ORDER BY order_number DESC",("@n",c.Name),("@c",c.Contact));
        var selected=source.Skip((int)Math.Min(q.Offset,int.MaxValue)).Take(q.PageSize).ToList();
        foreach(var o in selected)WebQueries.Payments(db,o);
        return Results.Ok(new{ambiguous=false,items=selected.Select(x=>WebQueries.OrderDto(x,p)),page=q.Page,pageSize=q.PageSize,total=source.Count});
    }
    public static object Prospects(SqliteConnection db,Dictionary<string,bool> p,WebQuery q)
    {
        var page=WebQueries.Page(WebQueries.Json<List<Prospect>>(db,"prospects").Where(x=>(q.From==null||x.CreatedAt>=q.From)&&(q.To==null||x.CreatedAt<q.To.Value.AddDays(1))).OrderBy(x=>x.NextContactAt??DateTime.MaxValue).ThenBy(x=>x.Id),q,x=>$"{x.CustomerName} {x.Contact} {x.Description} {x.Status}");
        return new{items=page.Items.Select(x=>new{x.Id,x.CustomerName,x.Contact,x.Description,x.Notes,x.Status,x.Amount,x.CreatedAt,x.LastContactAt,x.NextContactAt,orderId=p.GetValueOrDefault("view.orders")?x.OrderId:null,customerId=p.GetValueOrDefault("view.customers")?CustomerId(db,x.CustomerName,x.Contact):null,returnStatus=x.NextContactAt==null?"Sem retorno agendado":x.NextContactAt.Value.Date<DateTime.Today?"Atrasado":x.NextContactAt.Value.Date==DateTime.Today?"Hoje":"Futuro"}).ToArray(),page.Page,page.PageSize,page.Total};
    }
    // status: "pendentes", "concluidos" ou vazio/"todos". Sem nome gravado, usa o nome do cliente cadastrado com o mesmo telefone.
    public static object Reminders(SqliteConnection db,Dictionary<string,bool> p,WebQuery q,string status="")
    {
        var customers=new List<(string Name,string Digits)>();
        using(var cmd=WebQueries.Command(db,"SELECT name,contact FROM customers"))using(var r=cmd.ExecuteReader())while(r.Read()){var d=PhoneText.Digits(r.GetString(1));if(d.Length>=8)customers.Add((r.GetString(0),d));}
        string NameOf(Reminder x){if(x.Name.Trim().Length>0)return x.Name.Trim();var digits=PhoneText.Digits(x.Phone);return digits.Length<8?"":customers.FirstOrDefault(c=>c.Digits.EndsWith(digits)||digits.EndsWith(c.Digits)).Name??"";}
        var page=WebQueries.Page(WebQueries.Json<List<Reminder>>(db,"reminders_v1").Where(x=>status switch{"pendentes"=>x.DoneAt==null,"concluidos"=>x.DoneAt!=null,_=>true}).Where(x=>(q.From==null||x.CreatedAt>=q.From)&&(q.To==null||x.CreatedAt<q.To.Value.AddDays(1))).OrderBy(x=>x.DoneAt!=null).ThenByDescending(x=>x.CreatedAt),q,x=>$"{x.Name} {x.Phone} {x.Title} {x.Description}");
        return new{items=page.Items.Select(x=>{var name=NameOf(x);return new{x.Id,customerName=x.Title.Length>0?x.Title:name.Length>0?name:PhoneText.Format(x.Phone),contact=x.Phone,description=x.Title.Length>0&&name.Length>0?$"{name} · {x.Description}":x.Description,x.CreatedAt,x.DoneAt,nextContactAt=x.DueAt,status=x.DoneAt==null?(x.DueAt is DateTime due&&due<DateTime.Now?"Atrasado":"Pendente"):"Concluído",customerId=p.GetValueOrDefault("view.customers")?CustomerId(db,name,x.Phone):null};}).ToArray(),page.Page,page.PageSize,page.Total};
    }
    // Fornecedores para o celular (Mais › Fornecedores): só cadastrados ativos, contatos e a última compra; mesma permissão de Compras.
    public static object Suppliers(SqliteConnection db,WebQuery q)
    {
        using var cmd=WebQueries.Command(db,"SELECT json_extract(value,'$.Suppliers'),json_extract(value,'$.PurchaseNeeds'),json_extract(value,'$.Purchases') FROM metadata WHERE key='prompt12_v1'");
        var state=new Prompt12State();
        using(var r=cmd.ExecuteReader())if(r.Read()){state.Suppliers=JsonSerializer.Deserialize<List<Supplier>>(r.IsDBNull(0)?"[]":r.GetString(0))??[];state.PurchaseNeeds=JsonSerializer.Deserialize<List<PurchaseNeed>>(r.IsDBNull(1)?"[]":r.GetString(1))??[];state.Purchases=JsonSerializer.Deserialize<List<Purchase>>(r.IsDBNull(2)?"[]":r.GetString(2))??[];}
        var page=WebQueries.Page(state.Suppliers.Where(x=>x.Active).OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase),q,x=>$"{x.Name} {x.LegalName} {x.ContactPerson} {x.WhatsApp} {x.Phone} {string.Join(' ',x.Supplies)}");
        return new{items=page.Items.Select(x=>{var last=SupplierBook.Purchases(state,x).OrderByDescending(p=>p.Date).FirstOrDefault();return new{x.Id,name=x.Name,contactPerson=x.ContactPerson,contact=x.WhatsApp.Length>0?x.WhatsApp:x.Phone,x.Email,supplies=string.Join(", ",x.Supplies),leadTime=x.LeadTime,paymentTerms=x.PaymentTerms,lastPurchase=last==null?null:$"{last.Date:dd/MM} · {last.Material} · {last.Amount.ToString("C2",System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}"};}),page.Page,page.PageSize,page.Total};
    }
    // status: "abertos" (a comprar), "comprados" (comprados ou recebidos) ou vazio/"todos".
    public static object Purchases(SqliteConnection db,WebQuery q,string status="")
    {
        // Read only these two fields; owner credentials and other private configuration stay outside the query.
        using var cmd=WebQueries.Command(db,"SELECT json_extract(value,'$.PurchaseNeeds'),json_extract(value,'$.Purchases') FROM metadata WHERE key='prompt12_v1'");
        List<PurchaseNeed> needs=[];List<Purchase> purchases=[];
        using(var r=cmd.ExecuteReader())if(r.Read()){needs=JsonSerializer.Deserialize<List<PurchaseNeed>>(r.IsDBNull(0)?"[]":r.GetString(0))??[];purchases=JsonSerializer.Deserialize<List<Purchase>>(r.IsDBNull(1)?"[]":r.GetString(1))??[];}
        var page=WebQueries.Page(needs.Where(x=>status switch{"abertos"=>x.Status==PurchaseNeedStatus.ToBuy,"comprados"=>x.Status is PurchaseNeedStatus.Purchased or PurchaseNeedStatus.Received,_=>true}).Where(x=>q.From==null&&q.To==null||purchases.Any(y=>y.NeedId==x.Id&&(q.From==null||y.Date>=q.From)&&(q.To==null||y.Date<q.To.Value.AddDays(1)))).OrderBy(x=>x.Status).ThenBy(x=>x.BuyBy),q,x=>$"{x.Material} {x.Notes} {x.Supplier} {string.Join(' ',purchases.Where(y=>y.NeedId==x.Id).Select(y=>y.Supplier))}");
        return new{items=page.Items.Select(x=>new{x.Id,description=x.Material,x.Quantity,x.Unit,x.BuyBy,x.Notes,supplier=x.Supplier,status=x.Status switch{PurchaseNeedStatus.ToBuy=>"A comprar",PurchaseNeedStatus.Purchased=>"Comprado",PurchaseNeedStatus.Received=>"Recebido",_=>"Cancelado"},purchases=purchases.Where(y=>y.NeedId==x.Id).Select(y=>new{y.Supplier,y.Amount,y.Date,y.Paid,y.PaymentMethod})}),page.Page,page.PageSize,page.Total};
    }
    public static object History(SqliteConnection db,Dictionary<string,bool> p,WebQuery q,HttpRequest request,Guid? orderId=null)
    {
        string Filter(string key){var value=request.Query[key].ToString().Trim();if(value.Length>200)throw new WebRequestException(400,"Filtro muito longo.");return value;}
        var (from,end)=orderId.HasValue?(q.From??new DateTime(1900,1,1),(q.To??DateTime.Today).AddDays(1)):Period(q);var user=Filter("user");var module=Filter("module");var action=Filter("operation");var entity=Filter("entity");
        const string where=" WHERE (@id IS NULL OR (entity_type='Pedido' AND entity_id=@id) OR sale_id IN (SELECT sale_id FROM order_payments WHERE order_id=@id)) AND timestamp>=@from AND timestamp<@end AND (@user='' OR instr(lower(user_name||' '||user_login||' '||user_id),lower(@user))>0) AND (@module='' OR instr(lower(CASE entity_type WHEN 'Pedido' THEN 'Pedidos' WHEN 'Caixa' THEN 'Financeiro' WHEN 'Lançamento' THEN 'Financeiro' ELSE entity_type END),lower(@module))>0) AND (@action='' OR instr(lower(action),lower(@action))>0) AND (@entity='' OR instr(lower(entity_type||' '||entity_id),lower(@entity))>0)";
        (string,object?)[] values=[("@id",orderId),("@from",from.ToString("O")),("@end",end.ToString("O")),("@user",user),("@module",module),("@action",action),("@entity",entity),("@limit",q.PageSize),("@offset",q.Offset)];
        using var count=WebQueries.Command(db,"SELECT COUNT(*) FROM audit_trail"+where,values);var total=Convert.ToInt64(count.ExecuteScalar());
        using var cmd=WebQueries.Command(db,"SELECT id,timestamp,user_name,action,entity_type,entity_id,old_value,new_value FROM audit_trail"+where+" ORDER BY timestamp DESC,id LIMIT @limit OFFSET @offset",values);
        var list=new List<object>();using var r=cmd.ExecuteReader();
        while(r.Read())
        {
            var changes=new List<string>();var type=r.GetString(4);
            string? Field(string json,string field){try{using var doc=JsonDocument.Parse(json);return doc.RootElement.ValueKind==JsonValueKind.Object&&doc.RootElement.TryGetProperty(field,out var v)?v.ToString():null;}catch(JsonException){return null;}}
            if(type=="Pedido"&&p.GetValueOrDefault("view.orders"))
            {
                var a=Field(r.GetString(6),"Stage");var b=Field(r.GetString(7),"Stage");if(a!=null&&b!=null&&a!=b)changes.Add($"Etapa: {a} → {b}");
                if(Field(r.GetString(6),"Notes")!=Field(r.GetString(7),"Notes"))changes.Add("Observação atualizada");
            }
            // Never send raw old/new snapshots: they can contain costs, identity configuration or unrelated customers.
            list.Add(new{id=r.GetString(0),timestamp=r.GetString(1),user=r.GetString(2),action=r.GetString(3),entityType=type,entityId=r.GetString(5),changes});
        }
        return new{items=list,page=q.Page,pageSize=q.PageSize,total};
    }
}
