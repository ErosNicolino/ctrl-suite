using System.Text.Json;
using System.Text;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace GraficaCaixa;

// A bounded read projection; this is never accepted as a commit or mutation base.
public sealed record ModulePage(JsonElement Data,int Page,int PageSize,long Total,Dictionary<string,long> Counts);
public sealed partial class DataStore
{
    public Task<ModulePage> ModuleViewAsync(UserIdentity actor,string module,HttpRequest request,CancellationToken cancellation)
        =>actor.DeviceId.HasValue
            ?WebReadAsync(actor,ModulePermission(module),(db,p)=>ModuleView(db,p,module,WebQuery.Parse(request,desktop:true),request,actor),cancellation)
            :Task.FromException<ModulePage>(new WebRequestException(403,"Esta projeção requer o dispositivo WPF pareado. Use a consulta padrão no navegador."));
    static string ModulePermission(string module)=>module switch{
        "bootstrap"=>"", "dashboard"=>"view.home", "pedidos" or "producao"=>"view.orders", "produtos"=>"view.products",
        "clientes"=>"view.customers", "possiveis-pedidos"=>"view.prospects", "devedores"=>"view.debtors", "lembretes"=>"view.reminders",
        "compras"=>"view.purchases", "orcamentos/configuracao"=>"view.budget", "fechamentos"=>"view.closed", "lancamentos"=>"view.cash", "relatorios/resumo"=>"view.reports",
        _=>throw new WebRequestException(404,"Consulta não encontrada.")};
    ModulePage ModuleView(SqliteConnection db,Dictionary<string,bool> p,string module,WebQuery q,HttpRequest request,UserIdentity actor)
    {
        // Legacy WPF presentation needs fields absent from the mobile summary. Select only its section,
        // before permission filtering/serialization, preserving existing financial and display rules.
        var source=ReadShared(db);var data=new AppData{DataVersion=source.DataVersion};var counts=new Dictionary<string,long>();long total=0;
        var keys=new List<string>{"DataVersion"};
        void Key(params string[] names)=>keys.AddRange(names);
        var sort=request.Query["sort"].ToString();var descending=request.Query["descending"]=="true";
        IOrderedEnumerable<T> Sort<T>(IEnumerable<T> rows,Func<T,IComparable?> key,Func<T,Guid> id)=> (descending?rows.OrderByDescending(key):rows.OrderBy(key)).ThenBy(id);
        static string Normalize(string value)=>string.Concat(value.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark&&char.IsLetterOrDigit(c))).ToLowerInvariant();
        bool SaleMatch(CashSession session,Sale sale){var text=$"{sale.CreatedAt:dd/MM/yyyy HH:mm} {DisplayText.Customer(sale)} {sale.Product} {sale.PaymentType} {sale.Total:C2} {sale.Total:0.00} "+(session.IsOpen?"Caixa aberto":$"Fechado em {session.ClosedAt:dd/MM/yyyy}");return Normalize(text).Contains(Normalize(q.Search))||PhoneText.Matches(sale.Contact,q.Search);}
        bool Match(string text)=>q.Search.Length==0||text.Contains(q.Search,StringComparison.CurrentCultureIgnoreCase);
        List<T> Page<T>(IEnumerable<T> items){var list=items.ToList();total=list.Count;return list.Skip((int)Math.Min(q.Offset,int.MaxValue)).Take(q.PageSize).ToList();}
        if(module=="bootstrap")
        {
            data.DocumentsRoot=source.DocumentsRoot;data.LogoPath=source.LogoPath;data.Company=source.Company;data.PaymentRates=source.PaymentRates;data.Rules=source.Rules;
            Key("DocumentsRoot","LogoPath","Company","PaymentRates","Rules");
        }
        else if(module=="produtos")
        {
            data.ProductCategories=ProductOperations.AllCategories(source).ToList();Key("Products","ProductCategories");
            var category=request.Query["category"].ToString();var includeInactive=request.Query["inactive"]=="true";
            var found=new ProductSearch(source.Products.OrderBy(x=>x.Description).ThenBy(x=>x.Id)).Find(q.Search,!includeInactive,int.MaxValue,category);
            // "Só sem custo": nem custo padrão, nem custo nas faixas / quantidades exatas.
            if(request.Query["nocost"]=="true")found=found.Where(x=>!ProductCostInfo.Of(x).HasCost).ToList();
            data.Products=Page(found);
        }
        else if(module=="pedidos"||module=="producao")
        {
            Key("ScheduledOrders");if(module=="pedidos")Key("Prompt12");var all=source.ScheduledOrders;var today=DateTime.Today;
            counts["active"]=all.Count(x=>!x.IsUncollected&&(module=="pedidos"?ReceivableViews.OrderBucket(source,x)=="active":!OrderOperations.IsFinished(x)));counts["unpaid"]=all.Count(x=>!x.IsUncollected&&ReceivableViews.OrderBucket(source,x)=="unpaid");counts["completed"]=all.Count(x=>!x.IsUncollected&&ReceivableViews.OrderBucket(source,x)=="completed");counts["ready"]=all.Count(x=>!x.IsUncollected&&ReceivableViews.OrderBucket(source,x)=="ready");counts["unprinted"]=all.Count(x=>!x.IsUncollected&&(ReceivableViews.OrderBucket(source,x) is "active" or "ready")&&x.PdfGeneratedAt==null);counts["archived"]=all.Count(x=>x.IsArchived);
            counts["urgent"]=all.Count(x=>!x.IsUncollected&&!OrderOperations.IsFinished(x)&&x.Stage!="Pronto para retirada"&&x.DeadlineStarted&&BusinessCalendar.RemainingBusinessDays(today,x.DueDate)<=BusinessRules.Current.PriorityDays);
            counts["production"]=all.Count(x=>!x.IsUncollected&&!OrderOperations.IsFinished(x)&&x.Stage=="Produção");counts["pending"]=all.Count(x=>!x.IsUncollected&&x.RemainingAmount>0&&ReceivableViews.ActiveLoss(source,x.Id)==null);
            var scope=request.Query["scope"].ToString();
            var filtered=all.Where(x=>scope=="all"||(scope=="archived"?x.IsArchived:!x.IsUncollected&&(module=="pedidos"?(scope=="unprinted"?ReceivableViews.OrderBucket(source,x) is "active" or "ready":ReceivableViews.OrderBucket(source,x)==(scope=="completed"?"completed":scope=="unpaid"?"unpaid":scope=="ready"?"ready":"active")):scope=="completed"?OrderOperations.IsFinished(x):!OrderOperations.IsFinished(x))))
                .Where(x=>scope!="unprinted"||x.PdfGeneratedAt==null).Where(x=>q.Stage==null||x.Stage==q.Stage).Where(x=>scope!="priority"||x.Stage!="Pronto para retirada"&&x.DeadlineStarted&&BusinessCalendar.RemainingBusinessDays(today,x.DueDate)<=BusinessRules.Current.PriorityDays)
                .Where(x=>Match($"{x.Number} {x.CustomerName} {x.Contact} {x.ItemsDescription} {x.Stage}")||PhoneText.Matches(x.Contact,q.Search))
                .OrderBy(x=>scope=="ready"?x.ReadyAt??x.CreatedAt:DateTime.MinValue).ThenBy(x=>OrderOperations.OperationalPriority(x)).ThenBy(x=>x.ProductionBusinessDays==0?x.CreatedAt:x.DeadlineStarted?x.DueDate.Date:DateTime.MaxValue.Date).ThenBy(x=>x.Number).ThenBy(x=>x.Id);
            Func<ScheduledOrder,IComparable?>? orderKey=sort switch {"NumberValue"=>x=>x.Number,"Urgency"=>x=>OrderOperations.OperationalPriority(x),"CreatedAt"=>x=>x.CreatedAt,"Customer"=>x=>x.CustomerName,"Stage"=>x=>x.Stage,"DueDate"=>x=>x.DueDate,"RemainingAmount"=>x=>p.GetValueOrDefault("orders.value")?x.RemainingAmount:0m,"ProfitValue"=>x=>p.GetValueOrDefault("costs.edit")&&p.GetValueOrDefault("orders.value")?x.Profit:0m,_=>null};
            data.ScheduledOrders=Page(orderKey==null?filtered:Sort(filtered,orderKey,x=>x.Id));
        }
        else if(module=="clientes")
        {
            Key("Customers");var incomplete=request.Query["incomplete"]=="true";counts["noFullPhone"]=source.Customers.Count(x=>!CustomerBasicOperations.HasFullPhone(x));var duplicates=request.Query["duplicates"]=="true";var repeated=duplicates?CustomerBasicOperations.PhoneDuplicates(source).SelectMany(g=>g).Select(x=>x.Id).ToHashSet():null;counts["duplicatePhones"]=CustomerBasicOperations.PhoneDuplicates(source).Count;var customers=source.Customers.Where(x=>!incomplete||CustomerBasicOperations.IsIncomplete(x)).Where(x=>repeated==null||repeated.Contains(x.Id)).Where(x=>Match(x.Name+" "+x.Contact)||PhoneText.Matches(x.Contact,q.Search));data.Customers=Page(Sort(customers,x=>duplicates?CustomerBasicOperations.PhoneKey(x.Contact)+" "+x.Name:sort=="Contact"?x.Contact:(x.Name.Any(char.IsLetter)?"":"￿")+x.Name,x=>x.Id)).Select(x=>new CustomerProfile{Id=x.Id,Name=x.Name,Contact=x.Contact,UpdatedAt=x.UpdatedAt,PriceType=x.PriceType}).ToList();
            // Totais por cliente só da página atual (chaves "orders:id", "received:id" e "pending:id" em centavos, "last:id" como aaaammdd), conforme as permissões.
            var owners=CustomerRecords.For(source);var orders=p.GetValueOrDefault("view.orders")?source.ScheduledOrders.Select(x=>(Owner:owners.Owner(x),Order:x)).Where(x=>x.Owner!=null).GroupBy(x=>x.Owner!.Value).ToDictionary(g=>g.Key,g=>g.Select(x=>x.Order).ToList()):new Dictionary<Guid,List<ScheduledOrder>>();
            var sales=source.Sessions.Where(s=>s.IsOpen?p.GetValueOrDefault("view.cash"):p.GetValueOrDefault("view.closed")).SelectMany(s=>s.Sales).Select(x=>(Owner:owners.Owner(x),Sale:x)).Where(x=>x.Owner!=null).GroupBy(x=>x.Owner!.Value).ToDictionary(g=>g.Key,g=>g.Sum(x=>x.Sale.Total));var money=p.GetValueOrDefault("orders.value");
            foreach(var c in data.Customers){var id=c.Id.ToString("N");if(p.GetValueOrDefault("view.orders")){var list=orders.GetValueOrDefault(c.Id)??[];counts["orders:"+id]=list.Count;if(list.Count>0)counts["last:"+id]=long.Parse(list.Max(x=>x.CreatedAt).ToString("yyyyMMdd"));if(money)counts["pending:"+id]=(long)Math.Round(list.Sum(x=>x.RemainingAmount)*100);}if(p.GetValueOrDefault("view.cash")||p.GetValueOrDefault("view.closed"))counts["received:"+id]=(long)Math.Round(sales.GetValueOrDefault(c.Id)*100);}
        }
        else if(module=="dashboard")
        {
            Key("Sessions","ScheduledOrders","Reminders","FinancialCorrections","DocumentsRoot","LogoPath","Company","PaymentRates");
            data.Sessions=source.Sessions.Where(x=>x.IsOpen).ToList();data.ScheduledOrders=source.ScheduledOrders.Where(x=>!x.IsUncollected&&x.Stage!="Entregue").ToList();
            counts["active"]=data.ScheduledOrders.Count(x=>x.Stage!="Pronto para retirada");counts["urgent"]=data.ScheduledOrders.Count(x=>x.Stage!="Pronto para retirada"&&x.DeadlineStarted&&BusinessCalendar.RemainingBusinessDays(DateTime.Today,x.DueDate)<=BusinessRules.Current.PriorityDays);
            counts["overdue"]=data.ScheduledOrders.Count(x=>x.Stage!="Pronto para retirada"&&x.DeadlineStarted&&BusinessCalendar.RemainingBusinessDays(DateTime.Today,x.DueDate)<0);counts["ready"]=data.ScheduledOrders.Count(x=>x.Stage=="Pronto para retirada");
            var orderSearch=request.Query["ordersSearch"].ToString();data.ScheduledOrders=data.ScheduledOrders.Where(x=>x.Stage!="Pronto para retirada").Where(x=>orderSearch.Length==0||$"{x.Number} {x.CustomerName} {x.Contact} {x.ItemsDescription} {x.Stage}".Contains(orderSearch,StringComparison.CurrentCultureIgnoreCase)).OrderBy(x=>OrderOperations.OperationalPriority(x)).ThenBy(x=>x.ProductionBusinessDays==0?x.CreatedAt:x.DeadlineStarted?x.DueDate:DateTime.MaxValue).ThenBy(x=>x.Number).Take(8).ToList();
            if(p.GetValueOrDefault("system.settings")&&JsonSerializer.Deserialize<BrowserCertificateStatus>(WebQueries.Metadata(db,BrowserCertificateRenewer.StatusKey,"null"))?.ExpiresAt is DateTime certificateEnd&&(certificateEnd-DateTime.UtcNow).TotalDays<=BrowserCertificateRenewer.RenewDays)counts["certificateDays"]=Math.Max(0,(long)Math.Ceiling((certificateEnd-DateTime.UtcNow).TotalDays));
            counts["today"]=TodayQueue.Build(source,k=>p.GetValueOrDefault(k),actor.UserId,DateTime.Now).Count;
            counts["purchaseNeeds"]=p.GetValueOrDefault("view.purchases")?source.Prompt12.PurchaseNeeds.Count(x=>x.Status==PurchaseNeedStatus.ToBuy):0;counts["reminders"]=p.GetValueOrDefault("view.reminders")?source.Reminders?.Count(x=>x.DoneAt==null&&!x.ReadByUsers.ContainsKey(actor.UserId))??0:0;data.Reminders=[];data.FinancialCorrections=source.FinancialCorrections.Where(x=>data.Sessions.Any(s=>s.Sales.Any(y=>y.Id==x.OriginalSaleId))).ToList();
            data.DocumentsRoot=source.DocumentsRoot;data.LogoPath=source.LogoPath;data.Company=source.Company;data.PaymentRates=source.PaymentRates;
        }
        else if(module=="possiveis-pedidos"){Key("Prospects");data.Prospects=source.Prospects;}
        else if(module=="lembretes"){Key("Reminders","Customers");data.Reminders=source.Reminders;counts["reminders"]=source.Reminders?.Count(x=>x.DoneAt==null&&!x.ReadByUsers.ContainsKey(actor.UserId))??0;data.Customers=source.Customers.Where(c=>source.Reminders?.Any(r=>r.Phone==c.Contact)==true).ToList();}
        else if(module=="compras"){Key("Prompt12");data.Prompt12=new(){PurchaseNeeds=source.Prompt12.PurchaseNeeds,Purchases=source.Prompt12.Purchases,Suppliers=source.Prompt12.Suppliers};}
        else if(module=="orcamentos/configuracao"){Key("BudgetRules","ProductionDeadlines","CalculatorSettingsJson");data.BudgetRules=source.BudgetRules;data.ProductionDeadlines=source.ProductionDeadlines;data.CalculatorSettingsJson=source.CalculatorSettingsJson;}
        else if(module=="lancamentos")
        {
            Key("Sessions");var hidden=Cancellations.HiddenSaleIds(source);var sales=source.Sessions.SelectMany(session=>session.Sales.Select(sale=>(session,sale))).Where(x=>!hidden.Contains(x.sale.Id)&&SaleMatch(x.session,x.sale));
            // Filtros opcionais: período (from/to, aaaa-mm-dd, inclusivos) e forma de pagamento ("Cartão de crédito" cobre todas as parcelas).
            if(DateTime.TryParse(request.Query["from"],System.Globalization.CultureInfo.InvariantCulture,out var fromDate))sales=sales.Where(x=>x.sale.CreatedAt>=fromDate.Date);
            if(DateTime.TryParse(request.Query["to"],System.Globalization.CultureInfo.InvariantCulture,out var toDate))sales=sales.Where(x=>x.sale.CreatedAt<toDate.Date.AddDays(1));
            var paymentFilter=request.Query["payment"].ToString();if(paymentFilter.Length>0)sales=sales.Where(x=>x.sale.PaymentType.StartsWith(paymentFilter,StringComparison.OrdinalIgnoreCase));
            Func<(CashSession session,Sale sale),IComparable?>? saleKey=sort switch {"Date"=>x=>x.sale.CreatedAt,"Customer"=>x=>DisplayText.Customer(x.sale),"Product"=>x=>x.sale.Product,"Quantity"=>x=>x.sale.Units,"Payment"=>x=>x.sale.PaymentType,"Total"=>x=>x.sale.Total,"SessionStatus"=>x=>x.session.ClosedAt??DateTime.MaxValue,_=>null};
            var pairs=Page(saleKey==null?sales.OrderByDescending(x=>x.sale.CreatedAt).ThenBy(x=>x.sale.Id):Sort(sales,saleKey,x=>x.sale.Id));
            data.Sessions=pairs.GroupBy(x=>x.session.Id).Select(g=>new CashSession{Id=g.Key,OpenedAt=g.First().session.OpenedAt,ClosedAt=g.First().session.ClosedAt,Sales=g.Select(x=>x.sale).ToList()}).ToList();
        }
        else if(module=="fechamentos")
        {
            Key("Sessions","CashClosureVersions");data.Sessions=Page(source.Sessions.Where(x=>!x.IsOpen).OrderByDescending(x=>x.ClosedAt).ThenBy(x=>x.Id));
            data.CashClosureVersions=source.CashClosureVersions.Where(x=>data.Sessions.Any(s=>s.Id==x.SessionId)).ToList();
        }
        else if(module=="devedores")
        {
            Key("ScheduledOrders","Sessions","Customers","ManualDebts","CollectionContacts","Prompt12");data.ScheduledOrders=source.ScheduledOrders.Where(x=>x.Stage=="Entregue").ToList();data.ManualDebts=source.ManualDebts;data.CollectionContacts=source.CollectionContacts;data.Customers=source.Customers.Where(c=>data.ScheduledOrders.Any(o=>o.CustomerName.Equals(c.Name,StringComparison.CurrentCultureIgnoreCase))||data.ManualDebts.Any(o=>o.CustomerName.Equals(c.Name,StringComparison.CurrentCultureIgnoreCase))).ToList();
            var ids=data.ManualDebts.SelectMany(x=>x.SaleIds).ToHashSet();data.Sessions=[new(){Sales=source.Sessions.SelectMany(x=>x.Sales).Where(x=>ids.Contains(x.Id)).ToList()}];data.Prompt12=new(){Losses=source.Prompt12.Losses};
        }
        else if(module=="relatorios/resumo")
        {
            OperationalQueries.RequireFinance(p);Key("Sessions","ScheduledOrders","ManualDebts","CashClosureVersions","Prompt12");
            // Comparisons and current receivables require earlier payment links. Audit, products, private
            // settings and documents are not part of a financial calculation source.
            data.Sessions=source.Sessions;data.ScheduledOrders=source.ScheduledOrders;data.ManualDebts=source.ManualDebts;data.CashClosureVersions=source.CashClosureVersions;
            // Gasto por fornecedor (aba Fornecedores): compras só chegam a quem pode ver Compras (ServerAuthorization.View limpa o resto).
            data.Prompt12=new(){PurchaseNeeds=source.Prompt12.PurchaseNeeds,Purchases=source.Prompt12.Purchases,Suppliers=source.Prompt12.Suppliers};
            if(request.Query["totais"]=="1"&&q.From is DateTime periodStart&&q.To is DateTime periodLast)ReportTotals.Prefilter(data,periodStart,periodLast.AddDays(1));
        }
        var selectedLosses=module=="pedidos"?source.Prompt12.Losses.Where(x=>data.ScheduledOrders.Any(o=>o.Id==x.OrderId)).Select(x=>new ReceivableLoss{Id=x.Id,OrderId=x.OrderId,Status=x.Status}).ToList():null;
        data=ServerAuthorization.View(data,p);if(selectedLosses!=null)data.Prompt12=new(){Losses=selectedLosses};
        // Desempenho: Relatórios e a lista de Fechamentos não usam a cópia completa do caixa guardada em cada versão (~700 KB);
        // o PDF e a retificação leem o fechamento pelo contexto próprio (desktop/edit/closed). View() já devolveu uma cópia.
        if(module is "relatorios/resumo" or "fechamentos")foreach(var version in data.CashClosureVersions)version.SnapshotJson="";
        if(!p.GetValueOrDefault("costs.edit"))foreach(var product in data.Products){product.Cost=null;foreach(var price in product.ExactQuantityPrices)price.TotalCost=null;foreach(var tier in product.QuantityPriceTiers)tier.UnitCost=null;}
        foreach(var order in data.ScheduledOrders){
            if(!p.GetValueOrDefault("costs.edit")){order.FreightCost=order.AdditionalCost=null;order.CostNotes="";foreach(var item in order.Items){item.UnitCost=item.EnteredTotalCost=null;}}
            if(!p.GetValueOrDefault("orders.value")){order.OrderTotal=0;order.OriginalTotal=null;order.DiscountPercent=0;order.FixedDiscount=null;order.DepositAmount=0;order.Payments=[];foreach(var item in order.Items)item.UnitPrice=null;}
        }
        if(!p.GetValueOrDefault("orders.value"))foreach(var product in data.Products){product.Price=null;product.ExactQuantityPrices=[];product.QuantityPriceTiers=[];}
        // Relatórios no computador (totais=1): resumo e "a receber" calculados aqui sobre os mesmos dados (já filtrados por permissão); vai só o período.
        ReportTotals? report=null;if(module=="relatorios/resumo"&&request.Query["totais"]=="1"&&q.From is DateTime reportStart&&q.To is DateTime reportLast){report=ReportTotals.Of(data,reportStart,reportLast.AddDays(1));ReportTotals.KeepPeriod(data,reportStart,reportLast.AddDays(1));}
        var encoded=JsonSerializer.SerializeToElement(data);var selected=keys.Distinct().ToDictionary(key=>key,key=>encoded.GetProperty(key));
        if(report!=null)selected["Report"]=JsonSerializer.SerializeToElement(report,FinancialOverview.Wire);
        return new(JsonSerializer.SerializeToElement(selected),q.Page,q.PageSize,total,counts);
    }
}
