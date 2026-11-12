using System.Text.Json;
namespace GraficaCaixa;
public sealed record OwnerSummary(ManagementResult Management,CashFlowResult Cash,decimal ActiveReceivable);
public sealed partial class DataStore
{
    public AppData BusinessContext(UserIdentity actor,string kind,Guid id)=>ReadWork(db=>{
        var p=Permissions.Resolve(db,actor);
        string permission=kind switch{"cash" or "lost"=>"view.cash","sale"=>"","closed"=>"view.closed","customer"=>"view.customers","debt" or "collection"=>"view.debtors","prospect"=>"view.prospects","settings" or "templates"=>"system.settings","documents"=>"view.products","budget"=>"view.budget","product"=>"view.products","purchase" or "supplier"=>"view.purchases",_=>throw new WebRequestException(404,"Contexto inexistente.")};
        if(permission.Length>0)MobileAccess.Require(p,permission);
        var source=ReadShared(db);if(kind=="sale")MobileAccess.Require(p,source.Sessions.FirstOrDefault(x=>x.Sales.Any(s=>s.Id==id))?.IsOpen==false?"view.closed":"view.cash");var data=new AppData{DataVersion=source.DataVersion,DocumentsRoot=source.DocumentsRoot,LogoPath=source.LogoPath,Company=source.Company,Backup=source.Backup,PaymentRates=source.PaymentRates};
        switch(kind){
        case "settings":data.Rules=source.Rules;break;
        case "documents":break;
        case "templates":data.Prompt12.CommunicationTemplates=source.Prompt12.CommunicationTemplates;break;
        case "budget":data.BudgetRules=source.BudgetRules;data.ProductionDeadlines=source.ProductionDeadlines;data.CalculatorSettingsJson=source.CalculatorSettingsJson;data.Prompt12.CommunicationTemplates=source.Prompt12.CommunicationTemplates;break;
        case "customer":data.Customers=source.Customers.Where(x=>x.Id==id).ToList();break;
        case "product":data.Products=source.Products.Where(x=>x.Id==id).Select(x=>BusinessVisibleProduct(x,p)).ToList();data.ProductCategories=ProductOperations.AllCategories(source).ToList();break;
        case "purchase":data.Prompt12.PurchaseNeeds=source.Prompt12.PurchaseNeeds.Where(x=>x.Id==id).ToList();data.Prompt12.Suppliers=source.Prompt12.Suppliers;break;
        case "supplier":data.Prompt12.Suppliers=source.Prompt12.Suppliers.Where(x=>x.Id==id).ToList();break;
        case "prospect":data.Prospects=source.Prospects.Where(x=>x.Id==id).ToList();break;
        case "debt":case "collection":
            data.ManualDebts=source.ManualDebts.Where(x=>x.Id==id).ToList();data.ScheduledOrders=source.ScheduledOrders.Where(x=>x.Id==id).ToList();data.CollectionContacts=source.CollectionContacts.Where(x=>x.DebtId==id).ToList();
            var ids=data.ManualDebts.SelectMany(x=>x.SaleIds).ToHashSet();
            data.Sessions=source.Sessions.Where(x=>x.Sales.Any(s=>ids.Contains(s.Id))).Select(x=>JsonSerializer.Deserialize<CashSession>(JsonSerializer.Serialize(x))!).ToList();foreach(var session in data.Sessions)session.Sales=session.Sales.Where(x=>ids.Contains(x.Id)).ToList();
            data.Prompt12.Losses=source.Prompt12.Losses.Where(x=>x.OrderId==id).ToList();
            var names=data.ManualDebts.Select(x=>x.CustomerName).Concat(data.ScheduledOrders.Select(x=>x.CustomerName)).ToHashSet();data.Customers=source.Customers.Where(x=>names.Contains(x.Name)).ToList();if(kind=="collection")data.Prompt12.CommunicationTemplates=source.Prompt12.CommunicationTemplates;break;
        case "cash":case "closed":case "sale":
            data.Sessions=source.Sessions.Where(x=>kind=="sale"?x.Sales.Any(s=>s.Id==id):id!=Guid.Empty?x.Id==id:x.IsOpen).ToList();
            var saleIds=data.Sessions.SelectMany(x=>x.Sales).Select(x=>x.Id).ToHashSet();
            data.FinancialCorrections=source.FinancialCorrections.Where(x=>saleIds.Contains(x.OriginalSaleId)).ToList();
            data.ManualDebts=source.ManualDebts.Where(x=>x.SaleIds.Any(saleIds.Contains)).ToList();
            data.ScheduledOrders=source.ScheduledOrders.Where(x=>x.Payments.Any(s=>s.SaleId is Guid sid&&saleIds.Contains(sid))).ToList();
            data.CashClosureVersions=source.CashClosureVersions.Where(x=>data.Sessions.Any(s=>s.Id==x.SessionId)).GroupBy(x=>x.SessionId).Select(x=>x.OrderBy(v=>v.Version).Last()).ToList();break;
        case "lost":data.LostEntries=source.LostEntries.Where(x=>id==Guid.Empty||x.Id==id).ToList();break;
        }
        data.AuditTrail=[];data.Prompt12.OwnerPin=null;data.Prompt12.LoginPassword=null;
        return data;
    });
    public OwnerSummary BusinessOwnerSummary(UserIdentity actor,DateTime from,DateTime to)=>ReadWork(db=>{
        RequireBusinessOwner(db,actor);var data=ReadUnsafe(db,includeAudit:false);var session=new OwnerSession(TimeSpan.FromMinutes(1));
        return new OwnerSummary(PrivateFinance.Result(data,from,to,session),PrivateFinance.CashFlow(data,from,to,session),data.ScheduledOrders.Where(x=>LossOperations.IsNormalReceivable(data,x.Id)).Sum(x=>x.RemainingAmount));
    });
    void RequireBusinessOwner(Microsoft.Data.Sqlite.SqliteConnection db,UserIdentity actor){MobileAccess.Require(Permissions.Resolve(db,actor),"system.owner");using var q=WebQueries.Command(db,"SELECT owner_until FROM auth_sessions WHERE id=@s",("@s",actor.SessionId));if(Convert.ToInt64(q.ExecuteScalar())<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new UnauthorizedAccessException("Sessão do proprietário expirada.");}
    // Fluxo previsto (Área do proprietário): só com a sessão do proprietário liberada, como o resumo e as despesas.
    public CashForecast BusinessOwnerForecast(UserIdentity actor)=>ReadWork(db=>{RequireBusinessOwner(db,actor);return CashForecast.Build(ReadUnsafe(db,includeAudit:false),DateTime.Today);});
    public IReadOnlyList<Expense> BusinessExpenses(UserIdentity actor,int offset,string filter,string search)=>ReadWork(db=>{
        RequireBusinessOwner(db,actor);var state=ReadUnsafe(db,includeAudit:false).Prompt12;
        return ExpenseList.Filter(state,filter,search??"").Skip(Math.Max(0,offset)).Take(50).ToList();
    });
    public IReadOnlyList<Product> BusinessProductLookup(UserIdentity actor,string search)=>ReadWork(db=>{var p=Permissions.Resolve(db,actor);if(!p.GetValueOrDefault("view.orders")&&!p.GetValueOrDefault("view.cash")&&!p.GetValueOrDefault("view.budget")&&!p.GetValueOrDefault("view.products"))throw new UnauthorizedAccessException();return new ProductSearch(ReadShared(db).Products).Find(search).Select(x=>BusinessVisibleProduct(x,p)).ToList();});
    public IReadOnlyList<CustomerProfile> BusinessCustomerLookup(UserIdentity actor,string search)=>ReadWork<IReadOnlyList<CustomerProfile>>(db=>{var p=Permissions.Resolve(db,actor);if(!p.GetValueOrDefault("view.customers"))return Array.Empty<CustomerProfile>();var words=ProductOperations.Normalize(search).Split(' ',StringSplitOptions.RemoveEmptyEntries);var digits=PhoneText.Digits(search);return ReadShared(db).Customers.Where(c=>words.All(w=>ProductOperations.Normalize(c.Name).Contains(w))||digits.Length>=2&&PhoneText.Matches(c.Contact,search)).OrderBy(c=>ProductOperations.Normalize(c.Name).StartsWith(ProductOperations.Normalize(search))?0:1).ThenBy(c=>c.Name).Take(20).ToList();});
    // Mais vendidos do Novo lançamento (últimos 90 dias); custo só para quem pode ver custos.
    public IReadOnlyList<RankedProduct> BusinessTopProducts(UserIdentity actor)=>ReadWork(db=>{var p=Permissions.Resolve(db,actor);if(!p.GetValueOrDefault("view.cash")&&!p.GetValueOrDefault("view.orders"))throw new UnauthorizedAccessException();return (IReadOnlyList<RankedProduct>)ProductRanking.Top(ReadShared(db)).Select(x=>x with{Product=BusinessVisibleProduct(x.Product,p)}).ToList();});
    public OwnerIndicators BusinessOwnerIndicators(UserIdentity actor)=>ReadWork(db=>{RequireBusinessOwner(db,actor);return OwnerIndicators.Of(ReadShared(db),DateTime.Today);});
    public OrderExtrasView BusinessOrderExtras(UserIdentity actor,Guid id)=>ReadWork(db=>{var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,"view.orders");return OrderExtras.View(ReadShared(db),id,k=>p.GetValueOrDefault(k));});
    public YearAgoComparison ReportYearAgoOn(Microsoft.Data.Sqlite.SqliteConnection db,DateTime from,DateTime end)=>ReportIndicators.YearAgo(ReadShared(db),from,end);
    public DaySummary ReportDayOn(Microsoft.Data.Sqlite.SqliteConnection db,DateTime day)=>ReportIndicators.Day(ReadShared(db),day,DateTime.Today);
    public YearAgoComparison BusinessReportYearAgo(UserIdentity actor,DateTime from,DateTime to)=>ReadWork(db=>{var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,"view.reports");OperationalQueries.RequireFinance(p);return ReportYearAgoOn(db,from.Date,to.Date.AddDays(1));});
    public DaySummary BusinessReportDay(UserIdentity actor,DateTime date)=>ReadWork(db=>{var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,"view.reports");OperationalQueries.RequireFinance(p);return ReportDayOn(db,date);});
    public List<TodayItem> TodayOn(Microsoft.Data.Sqlite.SqliteConnection db,Dictionary<string,bool> p,UserIdentity actor)=>TodayQueue.Build(ReadUnsafe(db,includeAudit:false),k=>p.GetValueOrDefault(k),actor.UserId,DateTime.Now);
    public List<TodayItem> BusinessToday(UserIdentity actor)=>ReadWork(db=>{var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,"view.home");return TodayOn(db,p,actor);});
    public Guid? BusinessLastOrder(UserIdentity actor,Guid customerId)=>ReadWork(db=>{MobileAccess.Require(Permissions.Resolve(db,actor),"view.orders");return OrderRepeat.LastOf(ReadUnsafe(db,includeAudit:false),customerId)?.Id;});
    public IReadOnlyList<AuditEntry> BusinessAuditBrief(UserIdentity actor,int offset)=>ReadWork(db=>{MobileAccess.Require(Permissions.Resolve(db,actor),"system.audit");return ReadUnsafe(db).AuditTrail.OrderByDescending(x=>x.Timestamp).Skip(Math.Max(0,offset)).Take(100).Select(x=>new AuditEntry{Id=x.Id,Timestamp=x.Timestamp,Action=x.Action,Description=x.Description,Computer=x.Computer,UserName=x.UserName,EntityType=x.EntityType,EntityId=x.EntityId,NewValue=string.IsNullOrWhiteSpace(x.Description)?x.NewValue:"",OldValue=string.IsNullOrWhiteSpace(x.Description)&&string.IsNullOrWhiteSpace(x.NewValue)?x.OldValue:""}).ToList();});
    public Task<T> RemoteReadPostAsync<T>(string path,object body,CancellationToken cancellation=default)=>remote?.ReadPostAsync<T>(path,body,cancellation)??throw new InvalidOperationException("Leitura remota exige conexão.");
    public IReadOnlyList<CancelledRecord> BusinessCancelled(UserIdentity actor)=>ReadWork<IReadOnlyList<CancelledRecord>>(db=>{var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,"view.reports");
        // Desempenho (pacote 11): leitura compartilhada + só a auditoria dos pedidos arquivados (antes, o banco inteiro com toda a auditoria).
        var shared=ReadShared(db);var archived=shared.ScheduledOrders.Where(x=>x.IsCancelled).Select(x=>x.Id.ToString()).ToList();
        return Cancellations.List(new AppData{Sessions=shared.Sessions,FinancialCorrections=shared.FinancialCorrections,ScheduledOrders=shared.ScheduledOrders,AuditTrail=archived.Count==0?[]:ReadAudit(db,archived)});});
    public Task<AppData> BusinessContextAsync(string kind,Guid id=default,CancellationToken cancellation=default)=>IsRemote?QueryRemoteAsync<AppData>($"desktop/edit/{kind}/{id}",cancellation):Task.Run(Load,cancellation);
}

