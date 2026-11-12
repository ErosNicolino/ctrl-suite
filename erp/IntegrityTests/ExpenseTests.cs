using GraficaCaixa;

// Área do proprietário: recorrência (mensal … anual), compras aparecendo como despesa (sem contar duas vezes) e faturamento com balcão.
public static class ExpenseTests
{
    static int passed;
    static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        var state=new Prompt12State();
        state.Expenses.Add(new Expense{Description="Alvará",Category="Impostos",Amount=300,Competence=new DateTime(2026,4,1),DueDate=new DateTime(2026,4,20),Recurring=true,RecurrenceMonths=6,RecurrenceKey="alvará"});
        state.Expenses.Add(new Expense{Description="Aluguel da Gráfica",Amount=3100,Competence=new DateTime(2026,8,1),DueDate=new DateTime(2026,8,23),Recurring=true,RecurrenceKey="aluguel da gráfica"});
        state.Expenses.Add(new Expense{Description="Aluguel da Gráfica",Amount=3100,Competence=new DateTime(2026,9,1),DueDate=new DateTime(2026,9,23),Recurring=true,RecurrenceKey="aluguel da gráfica",Status=ExpenseStatus.Paid,PaidAt=new DateTime(2026,9,22)});
        state.Expenses.Add(new Expense{Description="Conserto",Amount=80,Competence=new DateTime(2026,10,1),DueDate=new DateTime(2026,10,5)});
        var due=ExpenseList.DueRecurring(state,new DateTime(2026,10,19));
        Check(due.Count==2&&due.Any(x=>x.Description=="Alvará"&&x.Competence==new DateTime(2026,10,1)&&x.DueDate==new DateTime(2026,10,20)&&x.RecurrenceLabel=="Semestral"),"semestral: abril gera a próxima em outubro, com vencimento no mesmo dia");
        Check(due.Any(x=>x.Description=="Aluguel da Gráfica"&&x.Competence==new DateTime(2026,10,1)&&x.Status==ExpenseStatus.Pending&&x.RecurrenceLabel=="Mensal"),"recorrente antiga (sem meses) continua mensal: gera outubro a partir de setembro");
        Check(!due.Any(x=>x.Description=="Conserto"),"despesa que não repete não gera nada");
        Check(ExpenseList.DueRecurring(state,new DateTime(2026,9,19)).All(x=>x.Description!="Alvará"),"semestral ainda não vence em setembro");
        foreach(var x in due)state.Expenses.Add(x);
        Check(ExpenseList.DueRecurring(state,new DateTime(2026,10,19)).Count==0,"depois de lançadas, não gera de novo no mesmo mês");

        var need=new PurchaseNeed{Material="Papel couché 300g",Unit="pct",Quantity=2,Status=PurchaseNeedStatus.Received};state.PurchaseNeeds.Add(need);
        state.Purchases.Add(new Purchase{NeedId=need.Id,Supplier="Lavoro",Amount=179.80m,Quantity=2,Date=new DateTime(2026,10,14),PaymentMethod="PIX",Paid=true});
        var all=ExpenseList.Filter(state,"all","").ToList();var bought=all.Single(x=>x.FromPurchase);
        Check(bought.Description=="Papel couché 300g"&&bought.Category==ExpenseList.PurchaseCategory&&bought.Status==ExpenseStatus.Paid&&bought.Notes.Contains("Lavoro"),"compra de material aparece na lista de despesas como paga, com fornecedor");
        Check(ExpenseList.Filter(state,"purchases","").Count()==1&&ExpenseList.Filter(state,"expenses","").All(x=>!x.FromPurchase)&&ExpenseList.Filter(state,"pending","").All(x=>!x.FromPurchase),"filtros: só compras, só despesas e pendentes (compra nunca fica pendente)");
        Check(ExpenseList.Filter(state,"all","lavoro").Single().FromPurchase,"busca encontra a compra pelo fornecedor");

        var data=new AppData{FinancialIntegrityVersion=1,OrderDetailsVersion=4};data.Prompt12=state;
        data.ManualDebts.Add(new ManualDebt{CreatedAt=new DateTime(2026,10,10),OriginDate=new DateTime(2026,10,10),CustomerName="Balcão",Amount=40,Items=[new(){Quantity=10,Product="Impressão A4",UnitPrice=4m,EnteredTotalCost=6m}]});
        data.ScheduledOrders.Add(new ScheduledOrder{Number=1,CustomerName="Pedido",OrderTotal=100,CreatedAt=new DateTime(2026,10,11),Stage="Entregue",ItemCostsEnabled=true,Items=[new(){Quantity=1,Product="Banner",UnitPrice=100m,UnitCost=30m,CostReviewed=true}]});
        var result=PrivateFinance.Result(data,new DateTime(2026,10,1),new DateTime(2026,10,31),OwnerSecurity.Authenticate(OwnerSecurity.Create("246813"),"246813"));
        Check(result.Sales==140&&result.DirectCosts==36,"faturamento do proprietário inclui o balcão (R$ 100 + R$ 40) e o custo dos itens (R$ 30 + R$ 6)");
        Check(result.OperatingExpenses==3100+300+80,"despesas do resultado gerencial não somam as compras de material (sem contar duas vezes)");
        var overview=new FinancialOverview(data,new DateTime(2026,10,1),new DateTime(2026,11,1));
        Check(overview.Billed==140&&overview.ConfirmedCosts==36,"Relatórios: faturamento e custos conferidos incluem o lançamento de balcão com custo");
        // Auditoria de listas: alterar 1 de 40 dívidas avulsas grava só aquele item (antes gravava as 40, antes e depois).
        var big=new AppData{FinancialIntegrityVersion=1,OrderDetailsVersion=4};for(var i=0;i<40;i++)big.ManualDebts.Add(new ManualDebt{CustomerName=$"Cliente {i}",Description=$"Dívida {i}",Amount=10+i,CreatedAt=new DateTime(2026,10,1),OriginDate=new DateTime(2026,10,1)});
        var changed=System.Text.Json.JsonSerializer.Deserialize<AppData>(System.Text.Json.JsonSerializer.Serialize(big))!;changed.ManualDebts[7].Description="Dívida 7 corrigida";
        var actor=new UserIdentity(Guid.NewGuid(),"Teste","teste","Administrador",Guid.NewGuid());ServerAuthorization.ValidateAndAudit(big,changed,actor,Permissions.Catalog.ToDictionary(x=>x.Key,_=>true));
        var ev=changed.AuditTrail.Single(x=>x.Action=="Cadastro / configuração alterado");
        Check(System.Text.Json.JsonDocument.Parse(ev.NewValue).RootElement.GetArrayLength()==1&&System.Text.Json.JsonDocument.Parse(ev.NewValue).RootElement[0].GetProperty("Description").GetString()=="Dívida 7 corrigida"&&System.Text.Json.JsonDocument.Parse(ev.OldValue).RootElement.GetArrayLength()==1,"auditoria de lista guarda só o item alterado (1 de 40), antes e depois");
        // Cancelado ≠ arquivado: cancelado sai de Arquivados, do faturamento e do a receber; só aparece em Relatórios › Cancelados.
        var cancelData=new AppData{FinancialIntegrityVersion=1,OrderDetailsVersion=4};
        var toCancel=new ScheduledOrder{Number=108,CustomerName="Danilo",OrderTotal=10,CreatedAt=new DateTime(2026,10,7),Stage="Arte",IsUncollected=true,ArchivedAsUncollectedAt=new DateTime(2026,10,10)};
        var archivedOnly=new ScheduledOrder{Number=93,CustomerName="José",OrderTotal=195,CreatedAt=new DateTime(2026,10,5),Stage="Pronto para retirada",IsUncollected=true};
        var paidOrder=new ScheduledOrder{Number=50,CustomerName="Tays",OrderTotal=100,CreatedAt=new DateTime(2026,10,6),Stage="Produção",Payments=[new(){Amount=40,SaleId=Guid.NewGuid()}]};
        cancelData.ScheduledOrders.AddRange([toCancel,archivedOnly,paidOrder]);
        Check(Throws(()=>OrderOperations.Cancel(paidOrder,"Cliente desistiu","Teste")),"pedido com valor recebido não cancela (devolver antes)");
        Check(Throws(()=>OrderOperations.Cancel(toCancel,"ab","Teste")),"cancelar exige motivo");
        OrderOperations.Cancel(toCancel,"Cliente desistiu do pedido","Teste");
        Check(toCancel.IsCancelled&&!toCancel.IsArchived&&archivedOnly.IsArchived&&!archivedOnly.IsCancelled,"cancelado sai de Arquivados; arquivado continua em Arquivados");
        var cov=new FinancialOverview(cancelData,new DateTime(2026,10,1),new DateTime(2026,11,1));
        Check(cov.Billed==195+100,"faturamento não conta o pedido cancelado (conta o arquivado e o ativo)");
        var cancelledList=Cancellations.List(cancelData);
        Check(cancelledList.Any(x=>x.Description.Contains("Pedido #108")||x.Customer=="Danilo")&&!cancelledList.Any(x=>x.Customer=="José"),"Relatórios › Cancelados lista o cancelado e não lista o arquivado");
        Check(Throws(()=>OrderOperations.Cancel(toCancel,"De novo cancelado","Teste")),"não cancela duas vezes");
        // Compactação única da auditoria antiga: cópia conferida antes, só os itens alterados ficam, eventos pequenos não mudam.
        var folder=Path.Combine(Path.GetTempPath(),"audit-compact-"+Guid.NewGuid().ToString("N"));new DataStore(folder).EnsureInitialized();
        var debtRows=Enumerable.Range(1,400).Select(i=>(object)new{Id=$"d{i}",Description=$"Dívida {i} "+new string('x',80),Amount=i}).ToList();
        var bigBefore=System.Text.Json.JsonSerializer.Serialize(debtRows);var bigAfter=System.Text.Json.JsonSerializer.Serialize(debtRows.Select((x,i)=>i==8?(object)new{Id="d9",Description="Dívida 9 corrigida",Amount=9}:x));
        using(var c=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(folder,"caixa_dados.db")};Pooling=False"))
        {
            c.Open();void Exec(string sql,params (string,object)[] p){using var cmd=c.CreateCommand();cmd.CommandText=sql;foreach(var (k,v) in p)cmd.Parameters.AddWithValue(k,v);cmd.ExecuteNonQuery();}
            Exec("DELETE FROM metadata WHERE key='audit_compact_v1'");
            Exec("INSERT INTO audit_trail(id,timestamp,action,description,computer,old_value,new_value) VALUES('big',@t,'Cadastro / configuração alterado','Dívidas avulsas','PC',@o,@n)",("@t",(object)DateTime.Now.ToString("O")),("@o",(object)bigBefore),("@n",(object)bigAfter));
            Exec("INSERT INTO audit_trail(id,timestamp,action,description,computer,old_value,new_value) VALUES('small',@t,'Pedido alterado','Pedido #1','PC','{\"a\":1}','{\"a\":2}')",("@t",(object)DateTime.Now.ToString("O")));
            var message=AuditCompaction.Apply(c,Path.Combine(folder,"Backups"));
            string Value(string id,string col){using var cmd=c.CreateCommand();cmd.CommandText=$"SELECT {col} FROM audit_trail WHERE id=@id";cmd.Parameters.AddWithValue("@id",id);return cmd.ExecuteScalar()?.ToString()??"";}
            var after=System.Text.Json.JsonDocument.Parse(Value("big","new_value")).RootElement;var beforeItems=System.Text.Json.JsonDocument.Parse(Value("big","old_value")).RootElement;
            Check(message!=null&&after.GetArrayLength()==1&&after[0].GetProperty("Description").GetString()=="Dívida 9 corrigida"&&beforeItems.GetArrayLength()==1&&beforeItems[0].GetProperty("Id").GetString()=="d9","compactação: evento antigo de lista guarda só o item que mudou, antes e depois");
            Check(Value("small","new_value")=="{\"a\":2}","compactação não mexe em eventos pequenos");
            var copies=Directory.GetFiles(Path.Combine(folder,"Backups"),"auditoria-original-*.db");
            Check(copies.Length==1&&File.ReadAllBytes(copies[0]).Length>bigBefore.Length,"compactação guarda a cópia original completa antes de alterar");
            Check(AuditCompaction.Apply(c,Path.Combine(folder,"Backups"))==null,"compactação roda uma única vez");
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Console.WriteLine($"DESPESAS: {passed} testes aprovados");
    }
}
