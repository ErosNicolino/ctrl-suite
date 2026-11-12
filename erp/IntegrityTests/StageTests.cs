using GraficaCaixa;
using Microsoft.Data.Sqlite;

// Pacote 9: fases do pedido (Arte → Aguardando aprovação → Produção → Pronto para retirada → Entregue) e migração reversível.
public static class StageTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        Check(OrderStages.All.SequenceEqual(["Arte","Aguardando aprovação","Produção","Pronto para retirada","Entregue"]),"cinco fases na ordem combinada");
        foreach(var (old,expected) in new[]{("Agendado","Arte"),("Arte / criação","Arte"),("Aprovado","Produção"),("Acabamento","Produção"),("Aguardando aprovação","Aguardando aprovação"),("Produção","Produção"),("Pronto para retirada","Pronto para retirada"),("Entregue","Entregue")})
            Check(OrderStages.Normalize(old)==expected,$"{old} vira {expected}");
        Check(new ScheduledOrder{Stage="Aprovado"}.Stage=="Produção"&&new ScheduledOrder().Stage=="Arte","modelo converte o nome antigo ao receber");
        Check(!new ScheduledOrder{Stage="Agendado",ProductionBusinessDays=5}.DeadlineStarted&&new ScheduledOrder{Stage="Aprovado",ProductionBusinessDays=5,ArtworkApprovedAt=DateTime.Now}.DeadlineStarted&&new ScheduledOrder{Stage="Acabamento",ProductionBusinessDays=5,ArtworkApprovedAt=DateTime.Now}.DeadlineStarted,"prazo só conta depois da aprovação (Produção em diante), como antes");
        Check(new ScheduledOrder{Stage="Arte",ProductionBusinessDays=0}.DeadlineStarted,"Imediato continua com prazo iniciado em qualquer fase");
        // Avançar de Aguardando aprovação para Produção inicia o prazo (antes: etapa Aprovado).
        var admin=OperationContext.Administrator("Teste");var data=new AppData();var order=new ScheduledOrder{Number=1,CustomerName="Cliente",Description="x",Stage="Aguardando aprovação",ProductionBusinessDays=5,OrderTotal=10,OriginalTotal=10};data.ScheduledOrders.Add(order);
        Check(!order.DeadlineStarted,"antes da aprovação o prazo não conta");
        OrderOperations.AdvanceStage(data,order.Id,order.Revision,"Produção",admin,false);
        Check(order.Stage=="Produção"&&order.DeadlineStarted&&order.SentToProductionAt!=null,"entrar em Produção inicia o prazo e marca o envio");
        // Migração: banco com nomes antigos → novos, contagem confere, nada além da fase muda, e desfaz.
        var folder=Path.Combine(Path.GetTempPath(),"erp-fases-"+Guid.NewGuid().ToString("N"));var store=new DataStore(folder);store.EnsureInitialized();
        store.Update(d=>{foreach(var (stage,i) in new[]{"Arte","Arte","Aguardando aprovação","Produção","Produção","Pronto para retirada","Entregue"}.Select((s,i)=>(s,i)))d.ScheduledOrders.Add(new ScheduledOrder{Number=100+i,CustomerName="C"+i,Description="d",Stage=stage,OrderTotal=50+i,OriginalTotal=50+i});return 0;});
        using var c=new SqliteConnection($"Data Source={Path.Combine(folder,"caixa_dados.db")};Pooling=False");c.Open();
        void Raw(string sql){using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.ExecuteNonQuery();}
        Raw("UPDATE scheduled_orders SET stage='Agendado' WHERE order_number=100");Raw("UPDATE scheduled_orders SET stage='Arte / criação' WHERE order_number=101");Raw("UPDATE scheduled_orders SET stage='Aprovado' WHERE order_number=103");Raw("UPDATE scheduled_orders SET stage='Acabamento' WHERE order_number=104");Raw("DELETE FROM metadata WHERE key='order_stages_v2'");
        decimal Total(){using var cmd=c.CreateCommand();cmd.CommandText="SELECT SUM(CAST(order_total AS REAL)) FROM scheduled_orders";return Convert.ToDecimal(cmd.ExecuteScalar());}
        var before=StageMigration.Count(c);var totalBefore=Total();
        var report=StageMigration.Apply(c,Path.Combine(folder,"Backups"));
        Check(report!=null&&report.Converted==4&&File.Exists(report.BackupPath),"migração converte só os pedidos com nome antigo e grava backup verificado");
        var after=StageMigration.Count(c);
        Check(after.Values.Sum()==before.Values.Sum()&&after.GetValueOrDefault("Arte")==2&&after.GetValueOrDefault("Produção")==2&&!after.Keys.Any(OrderStages.Legacy.ContainsKey),"contagem total igual e por fase certa depois");
        Check(Total()==totalBefore,"valores dos pedidos não mudam");
        Check(StageMigration.Apply(c,Path.Combine(folder,"Backups"))==null,"segunda execução não faz nada");
        var restored=StageMigration.Rollback(c);var back=StageMigration.Count(c);
        Check(restored==4&&back.GetValueOrDefault("Agendado")==1&&back.GetValueOrDefault("Aprovado")==1&&back.GetValueOrDefault("Acabamento")==1&&back.GetValueOrDefault("Arte / criação")==1,"desfazer devolve cada pedido à fase que tinha");
        Check(StageMigration.Apply(c,Path.Combine(folder,"Backups"))==null,"depois de desfazer, não converte de novo sozinho ao reiniciar");
        Check(store.Load().ScheduledOrders.All(x=>OrderStages.All.Contains(x.Stage)),"mesmo com nomes antigos no banco, o sistema lê as fases já convertidas");
        Check(StageMigration.Reapply(c,Path.Combine(folder,"Backups"))?.Converted==4,"reconverter manualmente funciona");
        var pdf=Path.Combine(folder,"ficha-servidor.pdf");Task.Run(()=>ReportWriter.WriteOrderSheet([order],pdf)).Wait();Check(File.Exists(pdf)&&new FileInfo(pdf).Length>1000,"ficha em PDF gerada fora da thread de tela (como no servidor)");
        SqliteConnection.ClearAllPools();
        Console.WriteLine("TOTAL: "+passed+" testes de fases aprovados");
    }
}