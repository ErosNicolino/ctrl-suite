using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace GraficaCaixa;

public static class OperationalApi
{
    public static void Map(WebApplication app, DataStore store, WebApiOptions options)
    {
        var api=app.MapGroup("/api/v1").RequireCors("web-v1");
        UserIdentity Actor(HttpContext c)=>(UserIdentity)c.Items["identity"]!;
        Task<IResult> Read(HttpContext c,string permission,Func<SqliteConnection,Dictionary<string,bool>,IResult> query)
            =>store.WebReadAsync(Actor(c),permission,query,c.RequestAborted);
        async Task<IResult> Module(HttpContext c,string module)=>Results.Ok(await store.ModuleViewAsync(Actor(c),module,c.Request,c.RequestAborted));
        void Get(string path,Delegate handler)=>api.MapGet(path,handler);
        Get("/clientes/{id:guid}/resumo",(Delegate)(async(HttpContext c,Guid id)=>Results.Ok(await store.CustomerSummaryAsync(Actor(c),id,c.RequestAborted))));
        Get("/bootstrap",(HttpContext c)=>Module(c,"bootstrap"));
        Get("/orcamentos/configuracao",(HttpContext c)=>Module(c,"orcamentos/configuracao"));
        Get("/lancamentos",(HttpContext c)=>Module(c,"lancamentos"));
        Get("/etapas",(HttpContext c)=>Read(c,"view.orders",(db,p)=>Results.Ok(new{stages=WebApiV1.Stages})));
        Get("/pedidos/{id:guid}/etapas",(HttpContext c,Guid id)=>Read(c,"view.orders",(db,p)=>{
            var order=WebQueries.ReadOrders(db," WHERE id=@id",("@id",id)).FirstOrDefault();
            return order==null?Results.NotFound():Results.Ok(new{current=order.Stage,allowed=OrderStages.MobileAllowed(order,p)});
        }));
        Get("/producao",(HttpContext c)=>c.Request.Query["projection"]=="desktop"?Module(c,"producao"):Read(c,"view.orders",(db,p)=>Results.Ok(WebQueries.Orders(db,p,WebQuery.Parse(c.Request) with{Scope="production"}))));
        Get("/acompanhamento",(HttpContext c)=>Read(c,"view.home",(db,p)=>Results.Ok(OperationalQueries.Overview(db,p,WebQuery.Parse(c.Request)))));
        Get("/relatorios/operacional",(HttpContext c)=>Read(c,"view.reports",(db,p)=>{
            if(!p.GetValueOrDefault("view.orders"))throw new UnauthorizedAccessException();
            return Results.Ok(OperationalQueries.Overview(db,p,WebQuery.Parse(c.Request)));
        }));
        Get("/relatorios/resumo",(HttpContext c)=>c.Request.Query["projection"]=="desktop"?Module(c,"relatorios/resumo"):Read(c,"view.reports",(db,p)=>{
            OperationalQueries.RequireFinance(p);return Results.Ok(ReportQueries.Summary(db,p,WebQuery.Parse(c.Request)));
        }));
        Get("/relatorios/dia",(HttpContext c)=>Read(c,"view.reports",(db,p)=>{
            OperationalQueries.RequireFinance(p);return Results.Ok(store.ReportDayOn(db,DateTime.TryParse(c.Request.Query["data"],out var day)?day.Date:DateTime.Today));
        }));
        Get("/relatorios/ano-passado",(HttpContext c)=>Read(c,"view.reports",(db,p)=>{
            OperationalQueries.RequireFinance(p);var(from,end)=OperationalQueries.Period(WebQuery.Parse(c.Request));return Results.Ok(store.ReportYearAgoOn(db,from,end));
        }));
        Get("/pedidos/{id:guid}/historico",(HttpContext c,Guid id)=>Read(c,"view.orders",(db,p)=>
            !WebQueries.Exists(db,"scheduled_orders",id)?Results.NotFound():Results.Ok(OperationalQueries.History(db,p,WebQuery.Parse(c.Request),c.Request,id))));
        Get("/historico",(HttpContext c)=>Read(c,"system.audit",(db,p)=>Results.Ok(OperationalQueries.History(db,p,WebQuery.Parse(c.Request),c.Request))));
        Get("/clientes/{id:guid}/pedidos",(HttpContext c,Guid id)=>Read(c,"view.customers",(db,p)=>{
            if(!p.GetValueOrDefault("view.orders"))throw new UnauthorizedAccessException();
            return OperationalQueries.CustomerOrders(db,p,id,WebQuery.Parse(c.Request));
        }));
        Get("/possiveis-pedidos",(HttpContext c)=>c.Request.Query["projection"]=="desktop"?Module(c,"possiveis-pedidos"):Read(c,"view.prospects",(db,p)=>Results.Ok(OperationalQueries.Prospects(db,p,WebQuery.Parse(c.Request)))));
        Get("/lembretes",(HttpContext c)=>c.Request.Query["projection"]=="desktop"?Module(c,"lembretes"):Read(c,"view.reminders",(db,p)=>Results.Ok(OperationalQueries.Reminders(db,p,WebQuery.Parse(c.Request),c.Request.Query["status"].ToString()))));
        Get("/compras",(HttpContext c)=>c.Request.Query["projection"]=="desktop"?Module(c,"compras"):Read(c,"view.purchases",(db,p)=>Results.Ok(OperationalQueries.Purchases(db,WebQuery.Parse(c.Request),c.Request.Query["status"].ToString()))));
        Get("/fornecedores",(HttpContext c)=>Read(c,"view.purchases",(db,p)=>Results.Ok(OperationalQueries.Suppliers(db,WebQuery.Parse(c.Request)))));
        Get("/caixa/atual",(HttpContext c)=>Read(c,"view.cash",(db,p)=>Results.Ok(ReportQueries.CurrentCash(db))));
        Get("/caixa/atual/lancamentos",(HttpContext c)=>Read(c,"view.cash",(db,p)=>Results.Ok(ReportQueries.CurrentCashSales(db))));
        Get("/fechamentos",(HttpContext c)=>c.Request.Query["projection"]=="desktop"?Module(c,"fechamentos"):Read(c,"view.closed",(db,p)=>Results.Ok(ReportQueries.Closures(db,p,Actor(c),options,WebQuery.Parse(c.Request)))));
        Get("/orcamentos",(HttpContext c)=>Read(c,"view.budget",(db,p)=>{
            if(!p.GetValueOrDefault("view.orders"))throw new UnauthorizedAccessException();
            return Results.Ok(ReportQueries.Quotes(db,p,Actor(c),options,WebQuery.Parse(c.Request)));
        }));
        api.MapPatch("/clientes/{id:guid}/basico",(Delegate)(async(HttpContext c,Guid id,WebCustomerCommand command)=>
            Results.Ok(await store.WebCustomerCommandAsync(Actor(c),id,command,c.RequestAborted))));
        // Orçamento e calculadora no celular (somente leitura, permissão Orçamento). Mesmo cálculo do desktop, feito aqui.
        Get("/orcamento/regras",(HttpContext c)=>Read(c,"view.budget",(db,p)=>Results.Ok(MobileBudget.Rules(db).Select(x=>new{id=x.Id,product=x.Product}))));
        Get("/orcamento/prazos",(HttpContext c)=>Read(c,"view.budget",(db,p)=>MobileBudget.Deadlines(db)));
        Get("/pedidos/{id:guid}/pdf",(HttpContext c,Guid id)=>Read(c,"view.orders",(db,p)=>MobileBudget.OrderPdf(db,p,id)));
        Get("/orcamento/calcular",(HttpContext c)=>Read(c,"view.budget",(db,p)=>MobileBudget.Quote(db,c.Request.Query,p)));
        Get("/calculadora",(HttpContext c)=>Read(c,"view.budget",(db,p)=>MobileBudget.Production(db,c.Request.Query)));
        // Prévia de orçamento em PDF para compartilhar: gerada sob demanda e não fica salva no servidor.
        api.MapPost("/orcamento/previa",(HttpContext c,MobileBudget.PreviewRequest request)=>Read(c,"view.budget",(db,p)=>MobileBudget.Preview(db,request)));
        // WPF polls a revision, never AppData. Authentication is still checked every time.
        Get("/revision",(HttpContext c)=>Read(c,"",(db,p)=>Results.Ok(new{revision=WebQueries.Metadata(db,"data_version","0"),permissions=p})));
    }
}

public static class OrderStages
{
    // Fases do pedido (UX 2.0, pacote 9): Arte → Aguardando aprovação → Produção → Pronto para retirada → Entregue.
    // Os nomes antigos continuam sendo aceitos e viram o novo nome (Agendado e "Arte / criação" → Arte; Aprovado e Acabamento → Produção).
    public const string Art="Arte",Approval="Aguardando aprovação",Production="Produção",Ready="Pronto para retirada",Delivered="Entregue";
    public static readonly string[] All=[Art,Approval,Production,Ready,Delivered];
    public static readonly IReadOnlyDictionary<string,string> Legacy=new Dictionary<string,string>{["Agendado"]=Art,["Arte / criação"]=Art,["Aprovado"]=Production,["Acabamento"]=Production};
    public static string Normalize(string? stage)=>string.IsNullOrWhiteSpace(stage)?Art:Legacy.TryGetValue(stage.Trim(),out var current)?current:stage.Trim();
    public static bool BeforeApproval(string? stage)=>Normalize(stage) is Art or Approval;
    public static bool ApprovedOrLater(string? stage)=>Normalize(stage) is Production or Ready or Delivered;
    public static string[] MobileAllowed(ScheduledOrder order,IReadOnlyDictionary<string,bool> permissions)
        =>order.IsUncollected||order.Stage=="Entregue"||!permissions.GetValueOrDefault("orders.stage")?[]:All.Where(x=>x!="Entregue"&&x!=order.Stage).ToArray();
}
