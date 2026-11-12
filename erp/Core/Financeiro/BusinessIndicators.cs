using System.Text.Json;
namespace GraficaCaixa;
// Retrabalho: pedido refeito sem cobrar de novo. O custo entra no resultado gerencial do mês em que foi registrado; valor, etapa e prazo do pedido não mudam.
public sealed class OrderRework
{
    public Guid Id{get;set;}=Guid.NewGuid();
    public Guid OrderId{get;set;}
    public DateTime Date{get;set;}=DateTime.Now;
    public string Reason{get;set;}="";
    public decimal Cost{get;set;}
    public string UserName{get;set;}="";
}
// Serviço terceirizado: o que foi para fora e quando volta. O custo fica só registrado: não soma no pedido, porque o custo do item pode já incluir o serviço.
public sealed class OutsourcedService
{
    public Guid Id{get;set;}=Guid.NewGuid();
    public Guid OrderId{get;set;}
    public string Service{get;set;}="";
    public string Supplier{get;set;}="";
    public decimal Cost{get;set;}
    public DateTime SentAt{get;set;}=DateTime.Now;
    public DateTime? ExpectedBack{get;set;}
    public DateTime? ReturnedAt{get;set;}
}
public sealed record OrderExtrasView(List<OrderRework> Reworks,List<OutsourcedService> Outsourced,List<string> Suppliers,bool CanSeeCost);
public static class OrderExtras
{
    // Cópia: a leitura do servidor é compartilhada. Sem costs.edit o custo não sai daqui.
    public static OrderExtrasView View(AppData data,Guid orderId,Func<string,bool> can)
    {
        var canCost=can("costs.edit");
        var reworks=data.Prompt12.Reworks.Where(x=>x.OrderId==orderId).OrderByDescending(x=>x.Date).Select(x=>new OrderRework{Id=x.Id,OrderId=x.OrderId,Date=x.Date,Reason=x.Reason,UserName=x.UserName,Cost=canCost?x.Cost:0}).ToList();
        var outsourced=data.Prompt12.Outsourced.Where(x=>x.OrderId==orderId).OrderByDescending(x=>x.SentAt).Select(x=>new OutsourcedService{Id=x.Id,OrderId=x.OrderId,Service=x.Service,Supplier=x.Supplier,SentAt=x.SentAt,ExpectedBack=x.ExpectedBack,ReturnedAt=x.ReturnedAt,Cost=canCost?x.Cost:0}).ToList();
        var suppliers=can("view.purchases")?data.Prompt12.Suppliers.Select(x=>x.Name).Where(x=>!string.IsNullOrWhiteSpace(x)).OrderBy(x=>x).ToList():[];
        return new(reworks,outsourced,suppliers,canCost);
    }
    static ScheduledOrder Order(AppData d,Guid id)=>d.ScheduledOrders.FirstOrDefault(x=>x.Id==id)??throw new InvalidOperationException("Pedido não encontrado. Atualize a tela.");
    public static void SaveRework(AppData d,Guid orderId,OrderRework rework,bool canCost,OperationContext user)
    {
        var order=Order(d,orderId);var reason=(rework.Reason??"").Trim();
        if(reason.Length<3)throw new InvalidOperationException("Informe o motivo do retrabalho.");
        if(rework.Cost<0)throw new InvalidOperationException("O custo não pode ser negativo.");
        var old=d.Prompt12.Reworks.FirstOrDefault(x=>x.Id==rework.Id);
        if(old!=null&&old.OrderId!=orderId)throw new InvalidOperationException("Retrabalho de outro pedido.");
        var before=old==null?"":JsonSerializer.Serialize(old);
        var target=old??new OrderRework{Id=rework.Id,OrderId=orderId,Date=DateTime.Now,UserName=user.UserName};
        target.Reason=reason;if(canCost)target.Cost=rework.Cost;
        if(old==null)d.Prompt12.Reworks.Add(target);
        AuditService.Record(d,user,old==null?"Retrabalho registrado":"Retrabalho alterado","Retrabalho",target.Id.ToString(),before,JsonSerializer.Serialize(target),"",$"Pedido #{order.Number}");
    }
    public static void DeleteRework(AppData d,Guid orderId,Guid id,OperationContext user)
    {
        var order=Order(d,orderId);var old=d.Prompt12.Reworks.FirstOrDefault(x=>x.Id==id&&x.OrderId==orderId)??throw new InvalidOperationException("Retrabalho não encontrado. Atualize a tela.");
        d.Prompt12.Reworks.Remove(old);
        AuditService.Record(d,user,"Retrabalho excluído","Retrabalho",id.ToString(),JsonSerializer.Serialize(old),"","",$"Pedido #{order.Number}");
    }
    public static void SaveOutsourced(AppData d,Guid orderId,OutsourcedService service,bool canCost,OperationContext user)
    {
        var order=Order(d,orderId);var name=(service.Service??"").Trim();
        if(name.Length<2)throw new InvalidOperationException("Informe o serviço terceirizado.");
        if(service.Cost<0)throw new InvalidOperationException("O custo não pode ser negativo.");
        var old=d.Prompt12.Outsourced.FirstOrDefault(x=>x.Id==service.Id);
        if(old!=null&&old.OrderId!=orderId)throw new InvalidOperationException("Serviço de outro pedido.");
        var before=old==null?"":JsonSerializer.Serialize(old);
        var target=old??new OutsourcedService{Id=service.Id,OrderId=orderId,SentAt=DateTime.Now};
        target.Service=name;target.Supplier=(service.Supplier??"").Trim();target.ExpectedBack=service.ExpectedBack?.Date;if(canCost)target.Cost=service.Cost;
        if(old==null)d.Prompt12.Outsourced.Add(target);
        AuditService.Record(d,user,old==null?"Serviço terceirizado registrado":"Serviço terceirizado alterado","Terceirizado",target.Id.ToString(),before,JsonSerializer.Serialize(target),"",$"Pedido #{order.Number}");
    }
    public static void ReturnOutsourced(AppData d,Guid orderId,Guid id,OperationContext user)
    {
        var order=Order(d,orderId);var old=d.Prompt12.Outsourced.FirstOrDefault(x=>x.Id==id&&x.OrderId==orderId)??throw new InvalidOperationException("Serviço não encontrado. Atualize a tela.");
        if(old.ReturnedAt!=null)return;
        old.ReturnedAt=DateTime.Now;
        AuditService.Record(d,user,"Serviço terceirizado voltou","Terceirizado",id.ToString(),"",old.Service,"",$"Pedido #{order.Number}");
    }
    public static void DeleteOutsourced(AppData d,Guid orderId,Guid id,OperationContext user)
    {
        var order=Order(d,orderId);var old=d.Prompt12.Outsourced.FirstOrDefault(x=>x.Id==id&&x.OrderId==orderId)??throw new InvalidOperationException("Serviço não encontrado. Atualize a tela.");
        d.Prompt12.Outsourced.Remove(old);
        AuditService.Record(d,user,"Serviço terceirizado excluído","Terceirizado",id.ToString(),JsonSerializer.Serialize(old),"","",$"Pedido #{order.Number}");
    }
}
// Limite do MEI: duas visões, porque nem toda venda tem nota. A lei conta toda a receita; as notas são só as registradas no ERP.
public sealed record MeiStatus(int Year,decimal Limit,decimal Notes,int NotesCount,decimal Billed,decimal Received,DateTime? BilledReach,DateTime? NotesReach)
{
    public decimal WithoutNote=>Math.Max(0,Billed-Notes);
    public static decimal? Percent(decimal value,decimal limit)=>limit>0?value/limit*100:null;
    public static string Tone(decimal value,decimal limit)=>limit<=0?"COk":value>=limit*.9m?"CDanger":value>=limit*.7m?"CWarn":"COk";
    public static MeiStatus Of(AppData data,DateTime today,decimal limit)
    {
        today=today.Date;var start=new DateTime(today.Year,1,1);var overview=new FinancialOverview(data,start,today.AddDays(1));
        var notes=data.Prompt12.FiscalDocuments.Where(x=>x.Date.Year==today.Year&&x.Date.Date<=today).ToList();var total=notes.Sum(x=>x.Amount);
        return new(today.Year,limit,total,notes.Count,overview.Billed,overview.Receipts,Reach(overview.Billed,limit,start,today),Reach(total,limit,start,today));
    }
    // No ritmo do ano até hoje, o dia em que o valor chega ao limite; null se não chega neste ano.
    public static DateTime? Reach(decimal value,decimal limit,DateTime start,DateTime today)
    {
        if(limit<=0||value<=0)return null;if(value>=limit)return today;
        var perDay=value/((today-start).Days+1);var date=start.AddDays((double)Math.Ceiling(limit/perDay)-1);
        return date.Year==start.Year?date:null;
    }
}
public sealed record ReworkRow(DateTime Date,long Number,string Customer,string Reason,decimal Cost);
public sealed record OwnerIndicators(MeiStatus Mei,List<ReworkRow> Reworks)
{
    public static OwnerIndicators Of(AppData data,DateTime today)
    {
        var orders=data.ScheduledOrders.ToDictionary(x=>x.Id);
        var rows=data.Prompt12.Reworks.Where(x=>x.Date.Year==today.Year).OrderByDescending(x=>x.Date).Select(x=>{orders.TryGetValue(x.OrderId,out var o);return new ReworkRow(x.Date,o?.Number??0,o?.CustomerName??"",x.Reason,x.Cost);}).ToList();
        return new(MeiStatus.Of(data,today,BusinessRules.Current.MeiLimit),rows);
    }
}
public sealed record DayMethod(string Method,decimal Total);
public sealed record DaySummary(DateTime Date,decimal Billed,decimal Received,decimal Withdrawals,int OrdersCreated,int Ready,int Overdue,int DueNext,List<DayMethod> Methods);
public sealed record YearAgoComparison(DateTime From,DateTime To,decimal Billed,decimal Received,decimal CurrentBilled,decimal CurrentReceived);
// Indicadores de Relatórios, com a mesma conta da Visão financeira. Sem despesas nem lucro: quem vê Relatórios já vê estes valores.
public static class ReportIndicators
{
    public static YearAgoComparison YearAgo(AppData data,DateTime start,DateTime end)
    {
        var from=start.AddYears(-1);var to=end.AddYears(-1);var old=new FinancialOverview(data,from,to);var now=new FinancialOverview(data,start,end);
        return new(from,to.AddDays(-1),old.Billed,old.Receipts,now.Billed,now.Receipts);
    }
    public static DaySummary Day(AppData data,DateTime day,DateTime today)
    {
        day=day.Date;today=today.Date;var end=day.AddDays(1);var overview=new FinancialOverview(data,day,end);
        var methods=FinancialOverview.SalesByCashDay(data).Where(x=>x.CreatedAt>=day&&x.CreatedAt<end).GroupBy(x=>string.IsNullOrWhiteSpace(x.PaymentType)?"Não informado":x.PaymentType).Select(g=>new DayMethod(g.Key,g.Sum(x=>x.Total))).OrderByDescending(x=>x.Total).ToList();
        var running=data.ScheduledOrders.Where(x=>!x.IsUncollected&&!x.IsCancelled&&x.Stage!="Entregue"&&x.Stage!="Pronto para retirada"&&x.DeadlineStarted).Select(x=>BusinessCalendar.RemainingBusinessDays(today,x.DueDate)).ToList();
        return new(day,overview.Billed,overview.Receipts,overview.Withdrawals,data.ScheduledOrders.Count(x=>x.CreatedAt>=day&&x.CreatedAt<end&&!x.IsCancelled),data.ScheduledOrders.Count(x=>x.ReadyAt>=day&&x.ReadyAt<end&&!x.IsCancelled),running.Count(x=>x<0),running.Count(x=>x==1),methods);
    }
}
