using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.Text.Json;

// Limite do MEI, Comparar com o ano passado, Resumo do dia, Retrabalho e Serviço terceirizado.
public static class IndicatorsTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    static readonly OperationContext Admin=OperationContext.Administrator("Teste indicadores");
    public static void Run()
    {
        var today=new DateTime(2026,7,2);
        ScheduledOrder Order(DateTime created,decimal total)=>new(){CreatedAt=created,OrderTotal=total,CustomerName="Cliente",Stage=OrderStages.Production};
        var thisYear=Order(new DateTime(2026,3,10),40000);var lastYear=Order(new DateTime(2025,3,10),9000);var cancelled=Order(new DateTime(2026,3,11),5000);cancelled.CancelledAt=today;
        var data=new AppData{ScheduledOrders=[thisYear,lastYear,cancelled]};
        data.Prompt12.FiscalDocuments.AddRange([new FiscalDocument{OrderId=thisYear.Id,Number="1",Date=new DateTime(2026,3,12),Amount=10000},new FiscalDocument{OrderId=lastYear.Id,Number="2",Date=new DateTime(2025,3,12),Amount=7000}]);
        var mei=MeiStatus.Of(data,today,81000);
        Check(mei.Notes==10000&&mei.NotesCount==1,"limite: só soma notas registradas neste ano");
        Check(mei.Billed==40000,"limite: faturamento total do ano, sem ano passado nem cancelado");
        Check(mei.WithoutNote==30000,"limite: mostra o que foi faturado sem nota");
        Check(MeiStatus.Reach(45000,81000,new DateTime(2026,1,1),today)==new DateTime(2026,11,26),"limite: projeta o dia em que chega ao limite no ritmo atual");
        Check(mei.BilledReach==null&&mei.NotesReach==null,"limite: ritmo baixo não chega ao limite neste ano");
        Check(MeiStatus.Reach(90000,81000,new DateTime(2026,1,1),today)==today,"limite: acima do limite aponta hoje");
        Check(MeiStatus.Tone(50000,81000)=="COk"&&MeiStatus.Tone(57000,81000)=="CWarn"&&MeiStatus.Tone(73000,81000)=="CDanger","limite: avisos em 70% e 90%");
        Check(MeiStatus.Of(data,today,0).BilledReach==null&&MeiStatus.Percent(1,0)==null,"limite zerado não quebra");
        var rules=new BusinessRules{MeiLimit=-1};Check(Throws(rules.Validate),"limite negativo é recusado nas Regras");Check(new BusinessRules().MeiLimit==81000,"limite padrão é R$ 81 mil");

        var compare=ReportIndicators.YearAgo(data,new DateTime(2026,3,1),new DateTime(2026,4,1));
        Check(compare.Billed==9000&&compare.CurrentBilled==40000,"ano passado: compara o mesmo período");
        Check(compare.From==new DateTime(2025,3,1)&&compare.To==new DateTime(2025,3,31),"ano passado: datas do período anterior");
        Check(ReportIndicators.YearAgo(data,new DateTime(2024,2,29),new DateTime(2024,3,1)).From==new DateTime(2023,2,28),"ano passado: 29 de fevereiro não quebra");

        var day=new DateTime(2026,7,1);var dayOrder=Order(day.AddHours(10),300);dayOrder.ReadyAt=day.AddHours(15);var late=Order(day.AddDays(-20),100);late.ProductionBusinessDays=3;late.ArtworkApprovedAt=day.AddDays(-20);late.DueDate=day.AddDays(-5);var soon=Order(day.AddDays(-1),100);soon.ProductionBusinessDays=3;soon.ArtworkApprovedAt=day.AddDays(-1);soon.DueDate=BusinessCalendar.AddBusinessDays(today,1);
        var cash=new CashSession{OpenedAt=day.AddHours(8),Sales=[new Sale{CreatedAt=day.AddHours(9),PaymentType="PIX",TotalValue=70},new Sale{CreatedAt=day.AddHours(11),PaymentType="Dinheiro",TotalValue=30}],Withdrawals=[new CashWithdrawal{CreatedAt=day.AddHours(12),Amount=20,Reason="Troco"}]};
        var book=new AppData{ScheduledOrders=[dayOrder,late,soon],Sessions=[cash]};var summary=ReportIndicators.Day(book,day,today);
        Check(summary.Received==100&&summary.Withdrawals==20,"resumo: recebido e sangrias do dia");
        Check(summary.Billed==summary.Received+300,"resumo: faturamento usa a conta da Visão financeira");
        Check(summary.Methods.Count==2&&summary.Methods[0].Method=="PIX"&&summary.Methods[0].Total==70,"resumo: recebido por forma de pagamento");
        Check(summary.OrdersCreated==1&&summary.Ready==1,"resumo: pedidos criados e prontos no dia");
        Check(summary.Overdue>=1&&summary.DueNext==1,"resumo: atrasados e os que vencem no próximo dia útil");

        var order=Order(new DateTime(2026,7,1),500);order.ItemCostsEnabled=true;var finance=new AppData{ScheduledOrders=[order]};var session=(OwnerSession)Activator.CreateInstance(typeof(OwnerSession),System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance,null,[TimeSpan.FromMinutes(1)],null)!;
        var rework=new OrderRework{Reason="Corte torto",Cost=200};OrderExtras.SaveRework(finance,order.Id,rework,true,Admin);
        Check(finance.Prompt12.Reworks.Single().Cost==200&&finance.Prompt12.Reworks[0].UserName==Admin.UserName,"retrabalho: registra motivo, custo e quem lançou");
        Check(order.OrderTotal==500&&order.Stage==OrderStages.Production,"retrabalho: valor e etapa do pedido não mudam");
        var month=PrivateFinance.Result(finance,DateTime.Today.AddDays(-1),DateTime.Today.AddDays(1),session);
        Check(month.Rework==200,"retrabalho: conta no mês em que foi registrado, mesmo com pedido de outro mês");
        var clean=PrivateFinance.Result(new AppData{ScheduledOrders=[order]},DateTime.Today.AddDays(-1),DateTime.Today.AddDays(1),session);Check(month.Result==clean.Result-200,"retrabalho: o custo diminui o resultado");
        Check(PrivateFinance.Result(finance,DateTime.Today.AddYears(-5),DateTime.Today.AddYears(-4),session).Rework==0,"retrabalho: fora do período não conta");
        Check(Throws(()=>OrderExtras.SaveRework(finance,order.Id,new OrderRework{Reason="x",Cost=1},true,Admin)),"retrabalho: exige motivo");
        Check(Throws(()=>OrderExtras.SaveRework(finance,order.Id,new OrderRework{Reason="Motivo",Cost=-1},true,Admin)),"retrabalho: recusa custo negativo");
        OrderExtras.SaveRework(finance,order.Id,new OrderRework{Id=rework.Id,Reason="Corte errado",Cost=999},false,Admin);
        Check(finance.Prompt12.Reworks.Single().Cost==200&&finance.Prompt12.Reworks[0].Reason=="Corte errado","retrabalho: sem permissão de custo o valor não muda");
        Check(OrderExtras.View(finance,order.Id,_=>false).Reworks[0].Cost==0&&finance.Prompt12.Reworks[0].Cost==200,"retrabalho: sem permissão a leitura não traz o custo");
        Check(OwnerIndicators.Of(finance,DateTime.Today).Reworks.Single().Cost==200,"retrabalho aparece na Área do proprietário");
        Check(OwnerIndicators.Of(finance,DateTime.Today.AddYears(1)).Reworks.Count==0,"retrabalho: lista só do ano");
        OrderExtras.DeleteRework(finance,order.Id,rework.Id,Admin);Check(finance.Prompt12.Reworks.Count==0,"retrabalho: exclui");
        Check(finance.AuditTrail.Count(x=>x.EntityType=="Retrabalho")==3,"retrabalho: tudo fica na auditoria");

        var now=new DateTime(2026,7,10,10,0,0);var user=Guid.NewGuid();var running=Order(now.AddDays(-5),100);running.CustomerName="Com terceiro";running.ProductionBusinessDays=20;running.ArtworkApprovedAt=now.AddDays(-5);running.DueDate=now.AddDays(30);
        var queue=new AppData{ScheduledOrders=[running]};var service=new OutsourcedService{Service="Laminação",Supplier="Fornecedor X",Cost=80,ExpectedBack=now.AddDays(-1)};
        OrderExtras.SaveOutsourced(queue,running.Id,service,true,Admin);
        Check(running.TotalCost==0&&queue.Prompt12.Outsourced.Single().Cost==80,"terceirizado: custo não soma no pedido");
        var items=TodayQueue.Build(queue,_=>true,user,now);
        Check(items.Count==1&&items[0].Label=="Terceirizado"&&items[0].Tone=="Danger"&&items[0].Id==running.Id,"terceirizado: atraso aparece na fila Hoje e abre o pedido");
        Check(!items[0].Detail.Contains("R$")&&!items[0].Title.Contains("R$"),"terceirizado: fila sem valores");
        Check(TodayQueue.Build(queue,k=>k!="view.orders",user,now).Count==0,"terceirizado: sem ver pedidos, fica de fora");
        queue.Prompt12.Outsourced[0].ExpectedBack=now.Date;Check(TodayQueue.Build(queue,_=>true,user,now)[0].Tone=="Warning","terceirizado: volta hoje avisa sem atraso");
        queue.Prompt12.Outsourced[0].ExpectedBack=now.AddDays(2);Check(TodayQueue.Build(queue,_=>true,user,now).Count==0,"terceirizado: volta futura não entra");
        queue.Prompt12.Outsourced[0].ExpectedBack=null;Check(TodayQueue.Build(queue,_=>true,user,now).Count==0,"terceirizado: sem data não entra");
        queue.Prompt12.Outsourced[0].ExpectedBack=now.AddDays(-1);OrderExtras.ReturnOutsourced(queue,running.Id,service.Id,Admin);Check(queue.Prompt12.Outsourced[0].ReturnedAt!=null&&TodayQueue.Build(queue,_=>true,user,now).Count==0,"terceirizado: quando volta sai da fila");
        Check(Throws(()=>OrderExtras.SaveOutsourced(queue,Guid.NewGuid(),new OutsourcedService{Service="Corte"},true,Admin)),"terceirizado: pedido inexistente é recusado");

        // Mesmo caminho do computador em rede: comando no servidor, com permissão, controle de versão e gravação no banco.
        var root=Path.Combine(Path.GetTempPath(),"erpi"+Guid.NewGuid().ToString("N")[..6]);
        try
        {
            Directory.CreateDirectory(root);var store=new DataStore(root);store.EnsureInitialized();const string password="Teste-Indicadores!2026";
            var saved=new ScheduledOrder{Number=7,CustomerName="Cliente servidor",Stage=OrderStages.Production,OrderTotal=100};
            store.Update(d=>{d.ScheduledOrders.Add(saved);d.Prompt12.LoginPassword=LoginSecurity.Create(password);return 0;});
            var security=new IdentitySecurity(store);security.Migrate();var admin=security.Login("grafica",password,"teste")??throw new Exception("Login esperado.");
            security.SaveUser(admin.User,new UserEdit(null,"Atendente","atendente","Financeiro",true,"Senha-Forte!2026"),"teste");
            var employee=security.Login("atendente","Senha-Forte!2026","teste")??throw new Exception("Login do atendente esperado.");
            BusinessCommand Command(string action,object args)=>new(Guid.NewGuid(),action,saved.Id,DataStore.DesktopRevision(store.Load().ScheduledOrders.Single()),JsonSerializer.SerializeToElement(args));
            var serverRework=new OrderRework{Reason="Refeito no servidor",Cost=55};var serverService=new OutsourcedService{Service="Corte especial",Supplier="Fornecedor",Cost=30,ExpectedBack=DateTime.Today};
            store.BusinessExecute(admin.User,Command("rework.save",new ReworkArgs(serverRework)));store.BusinessExecute(admin.User,Command("outsourced.save",new OutsourcedArgs(serverService)));
            var reopened=new DataStore(store.Folder).Load();
            Check(reopened.Prompt12.Reworks.Single().Cost==55&&reopened.Prompt12.Reworks[0].UserName==admin.User.Name,"servidor grava o retrabalho com o usuário da sessão");
            Check(reopened.Prompt12.Outsourced.Single().Cost==30,"terceirizado continua salvo ao reabrir o banco");
            Check(store.BusinessOrderExtras(admin.User,saved.Id).Reworks.Single().Cost==55,"leitura do pedido traz o retrabalho");
            Check(store.BusinessToday(admin.User).Any(x=>x.Label=="Terceirizado"),"fila Hoje do servidor mostra o terceirizado");
            var rights=security.Effective(employee.User);
            if(rights.GetValueOrDefault("view.orders")&&!rights.GetValueOrDefault("costs.edit"))Check(store.BusinessOrderExtras(employee.User,saved.Id).Reworks.Single().Cost==0,"sem permissão de custo, a leitura do servidor esconde o valor");
            var denied=false;try{store.BusinessExecute(employee.User,Command("rework.delete",new OrderExtraArgs(serverRework.Id)));}catch(Exception){denied=true;}
            Check(rights.GetValueOrDefault("orders.edit")||denied&&store.Load().Prompt12.Reworks.Count==1,"sem editar pedidos, o servidor recusa");
            var conflict=false;var stale=Command("outsourced.return",new OrderExtraArgs(serverService.Id));store.Update(d=>{d.ScheduledOrders.Single().Notes="Mudou em outro computador";return 0;});
            try{store.BusinessExecute(admin.User,stale);}catch(WebRequestException ex) when(ex.Status==409){conflict=true;}
            Check(conflict&&store.Load().Prompt12.Outsourced.Single().ReturnedAt==null,"pedido alterado em outro computador recusa a gravação");
            store.BusinessExecute(admin.User,Command("outsourced.return",new OrderExtraArgs(serverService.Id)));Check(store.Load().Prompt12.Outsourced.Single().ReturnedAt!=null&&!store.BusinessToday(admin.User).Any(x=>x.Label=="Terceirizado"),"marcar que voltou tira da fila");
            store.BusinessExecute(admin.User,Command("rework.delete",new OrderExtraArgs(serverRework.Id)));store.BusinessExecute(admin.User,Command("outsourced.delete",new OrderExtraArgs(serverService.Id)));
            Check(store.Load().Prompt12.Reworks.Count==0&&store.Load().Prompt12.Outsourced.Count==0,"servidor exclui retrabalho e terceirizado");
            Check(store.BusinessReportDay(admin.User,DateTime.Today).OrdersCreated==1&&store.BusinessReportYearAgo(admin.User,DateTime.Today,DateTime.Today).CurrentBilled==100,"resumo do dia e ano passado respondem no servidor");
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();try{Directory.Delete(root,true);}catch(IOException){}}
        Console.WriteLine($"INDICADORES: {passed} testes aprovados");
    }
}
