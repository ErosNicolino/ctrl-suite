using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.Text.Json;

// Ajuste de caixa: corrige o dinheiro esperado sem mexer em vendas, recebimentos, lucro ou sangrias.
public static class CashAdjustmentTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    static readonly OperationContext Admin=OperationContext.Administrator("Teste ajuste");
    static CashAdjustment New(decimal amount,string reason="Sangria de ontem não registrada")=>new(){Amount=amount,Reason=reason};
    public static void Run()
    {
        var cash=new CashSession{OpenedAt=DateTime.Now.AddHours(-1),OpeningAmount=100,Sales=[new Sale{PaymentType="Dinheiro",TotalValue=50,CreatedAt=DateTime.Now.AddMinutes(-30)},new Sale{PaymentType="PIX",TotalValue=70,CreatedAt=DateTime.Now.AddMinutes(-20)}]};
        var data=new AppData{Sessions=[cash]};var before=new FinancialOverview(data,DateTime.Today,DateTime.Today.AddDays(1));
        var up=New(36);CashOperations.Adjust(data,cash.Id,up,Admin);
        Check(cash.ExpectedCash==186&&cash.AdjustmentsTotal==36,"ajuste para mais aumenta o dinheiro esperado");
        CashOperations.Adjust(data,cash.Id,New(-22),Admin);
        Check(cash.ExpectedCash==164&&cash.AdjustmentsTotal==14,"ajuste para menos diminui o dinheiro esperado");
        Check(cash.SalesTotal==120&&cash.Sales.Count==2&&cash.WithdrawalsTotal==0,"ajuste não vira lançamento, recebimento nem sangria");
        var after=new FinancialOverview(data,DateTime.Today,DateTime.Today.AddDays(1));
        Check(after.Withdrawals==before.Withdrawals&&after.GrossProfit==before.GrossProfit&&after.ConfirmedCosts==before.ConfirmedCosts,"ajuste não muda o resumo financeiro nem o lucro");
        CashOperations.Adjust(data,cash.Id,up,Admin);Check(cash.Adjustments.Count==2&&cash.ExpectedCash==164,"repetir a mesma gravação não duplica o ajuste");
        Check(up.UserName==Admin.UserName&&up.CreatedAt>=cash.OpenedAt,"ajuste guarda usuário e horário");
        var audit=data.AuditTrail.Last();Check(audit.Action=="Ajuste de caixa"&&audit.Reason.Length>0&&audit.Description.Contains(cash.Id.ToString())&&JsonDocument.Parse(audit.NewValue).RootElement.GetProperty("Amount").GetDecimal()==-22,"auditoria registra valor, motivo e caixa");
        Check(AuditQuery.Module(audit)=="Financeiro","auditoria classifica o ajuste como Financeiro");
        Check(Throws(()=>CashOperations.Adjust(data,cash.Id,New(0),Admin)),"ajuste zero recusado");
        Check(Throws(()=>CashOperations.Adjust(data,cash.Id,New(1.234m),Admin)),"mais de duas casas recusado");
        Check(Throws(()=>CashOperations.Adjust(data,cash.Id,New(5,"   "),Admin)),"motivo vazio recusado");
        Check(Throws(()=>CashOperations.Adjust(data,cash.Id,New(5,"abc"),Admin)),"motivo curto recusado");
        Check(Throws(()=>CashOperations.Adjust(data,cash.Id,New(-164.01m),Admin)),"esperado negativo recusado");
        CashOperations.Withdraw(data,cash.Id,new CashWithdrawal{Amount=164,Reason="Retirada de teste",CreatedAt=DateTime.Now},Admin);Check(cash.ExpectedCash==0,"sangria considera o dinheiro ajustado");
        FinancialOperations.CloseCash(data,cash.Id,JsonSerializer.Deserialize<CashSession>(JsonSerializer.Serialize(cash))!,0,Admin);
        Check(data.CashClosureVersions.Single().ExpectedCash==0&&cash.DeclaredClosingAmount==0,"fechamento confere com o esperado ajustado");
        Check(JsonSerializer.Deserialize<CashSession>(data.CashClosureVersions.Single().SnapshotJson)!.Adjustments.Count==2,"versão do fechamento guarda os ajustes");
        Check(Throws(()=>CashOperations.Adjust(data,cash.Id,New(5),Admin)),"caixa fechado não aceita ajuste");
        var old=JsonSerializer.Deserialize<CashSession>("{\"OpeningAmount\":10,\"Sales\":[{\"PaymentType\":\"Dinheiro\",\"TotalValue\":22}]}")!;
        Check(old.Adjustments.Count==0&&old.ExpectedCash==32,"caixa antigo, sem ajustes, mantém o mesmo esperado");

        // Histórico do caixa: sangrias e ajustes do próprio caixa aparecem, mesmo as sangrias já gravadas só com o horário de abertura.
        var first=new CashSession{OpenedAt=new DateTime(2026,10,1,8,0,0),ClosedAt=new DateTime(2026,10,1,18,0,0)};var second=new CashSession{OpenedAt=new DateTime(2026,10,2,8,5,0)};var history=new AppData{Sessions=[first,second]};
        history.AuditTrail.Add(new AuditEntry{EntityType="Sangria",EntityId=Guid.NewGuid().ToString(),Reason="Do primeiro",NewValue="10,50",Description="Caixa 01/10/2026 08:00"});
        history.AuditTrail.Add(new AuditEntry{EntityType="Sangria",EntityId=Guid.NewGuid().ToString(),Reason="Antiga por Id",Description=$"Caixa {first.Id}"});
        history.AuditTrail.Add(new AuditEntry{EntityType="Ajuste de caixa",EntityId=Guid.NewGuid().ToString(),Reason="Ajuste do primeiro",Description=$"Caixa 01/10/2026 08:00 · {first.Id}"});
        history.AuditTrail.Add(new AuditEntry{EntityType="Sangria",EntityId=Guid.NewGuid().ToString(),Reason="Do segundo",Description="Caixa 02/10/2026 08:05"});
        var ofFirst=AuditQuery.Run(history,new AuditFilter(Entity:"Caixa",Id:first.Id.ToString()));
        Check(ofFirst.Count==3&&ofFirst.All(x=>x.Reason!="Do segundo"),"histórico do caixa traz as sangrias e ajustes dele, e não os de outro caixa");
        Check(AuditQuery.Run(history,new AuditFilter(Entity:"Caixa",Id:second.Id.ToString())).Single().Reason=="Do segundo","sangria já gravada aparece no caixa certo");
        Check(AuditQuery.Run(history,new AuditFilter(Entity:"Caixa")).Count==4,"pesquisa geral por Caixa continua trazendo todas");
        Check(((string)typeof(AuditWindow).Assembly.GetType("GraficaCaixa.Wpf.AuditNames")!.GetMethod("Summary")!.Invoke(null,[ofFirst.Single(x=>x.Reason=="Do primeiro")])!).Contains("10,50"),"lista mostra o valor da sangria");

        var root=Path.Combine(Path.GetTempPath(),"erp-ajuste-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new DataStore(Path.Combine(root,"db"));store.EnsureInitialized();
            var open=new CashSession{OpeningAmount=50,OpenedAt=DateTime.Now.AddHours(-1)};store.Update(d=>{d.Sessions.Add(open);return 0;});
            Check(store.Load().Sessions.Single().Adjustments.Count==0,"banco sem ajustes carrega normalmente");
            store.Update(d=>{CashOperations.Adjust(d,open.Id,New(12.5m),Admin);return 0;});
            var saved=new DataStore(store.Folder).Load().Sessions.Single();
            Check(saved.Adjustments.Single().Amount==12.5m&&saved.ExpectedCash==62.5m&&saved.Adjustments.Single().UserName==Admin.UserName,"ajuste continua salvo ao reabrir o banco");
            var scoped=new AppData{Sessions=[saved]};ReportTotals.KeepPeriod(scoped,DateTime.Today,DateTime.Today.AddDays(1));
            Check(scoped.Sessions.Single().Adjustments.Count==1,"filtro de período mantém o caixa que só tem ajuste");
            var earlier=new AppData{Sessions=[saved]};ReportTotals.KeepPeriod(earlier,DateTime.Today.AddDays(-9),DateTime.Today.AddDays(-8));
            Check(earlier.Sessions.Count==0,"filtro de período não traz ajuste de outro dia");
            // Mesmo caminho do computador em rede: comando no servidor, com permissão e controle de versão.
            const string password="Teste-Ajuste!2026";store.Update(d=>{d.Prompt12.LoginPassword=LoginSecurity.Create(password);return 0;});
            var security=new IdentitySecurity(store);security.Migrate();var admin=security.Login("grafica",password,"teste")??throw new Exception("Login esperado.");
            security.SaveUser(admin.User,new UserEdit(null,"Atendente","atendente","Financeiro",true,"Senha-Forte!2026"),"teste");
            var employee=security.Login("atendente","Senha-Forte!2026","teste")??throw new Exception("Login do atendente esperado.");
            Check(security.Effective(admin.User)["cash.adjust"]&&!security.Effective(employee.User)["cash.adjust"],"por padrão só o Administrador faz ajuste de caixa");
            BusinessCommand Command(decimal amount,string? revision,Guid? id=null)=>new(Guid.NewGuid(),"cash.adjust",open.Id,revision,JsonSerializer.SerializeToElement(new CashAdjustmentArgs(new CashAdjustment{Id=id??Guid.NewGuid(),Amount=amount,Reason="Correção de falta na conta"})));
            string Revision()=>DataStore.DesktopRevision(store.Load().Sessions.Single());
            var denied=false;try{store.BusinessExecute(employee.User,Command(5,Revision()));}catch(Exception){denied=true;}
            Check(denied&&store.Load().Sessions.Single().Adjustments.Count==1,"usuário sem permissão não grava ajuste no servidor");
            var stale=Revision();store.BusinessExecute(admin.User,Command(-2.5m,Revision()));
            var server=store.Load().Sessions.Single();Check(server.Adjustments.Count==2&&server.ExpectedCash==60&&server.Adjustments.Last().UserName==admin.User.Name,"comando do servidor grava o ajuste com o usuário da sessão");
            var conflict=false;try{store.BusinessExecute(admin.User,Command(1,stale));}catch(WebRequestException ex) when(ex.Status==409){conflict=true;}
            Check(conflict&&store.Load().Sessions.Single().Adjustments.Count==2,"caixa alterado em outro computador recusa o ajuste");
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();try{Directory.Delete(root,true);}catch(IOException){}}
        Console.WriteLine($"AJUSTE DE CAIXA: {passed} testes aprovados");
    }
}
