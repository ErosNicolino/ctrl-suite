using GraficaCaixa;
using System.Text.Json;

// Gravação mais rápida (2026.10.21.1): a cópia de trabalho e a conferência da auditoria por Id mantêm exatamente as mesmas regras.
public static class AuditFastTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    static AppData Sample()
    {
        var data=new AppData{Products=[new(){Description="Cartão de visita",Price=0.45m,Cost=0.18m}]};
        for(var i=0;i<300;i++)data.AuditTrail.Add(new AuditEntry{Action="Registro alterado",Description="Evento "+i,EntityType="Produto",EntityId=data.Products[0].Id.ToString(),OldValue="{\"Price\":1}",NewValue="{\"Price\":2}",Timestamp=new DateTime(2026,10,1,8,0,0,DateTimeKind.Utc).AddMinutes(i),SaleId=i%2==0?Guid.NewGuid():null,UserName="Teste"});
        return data;
    }
    public static void Run()
    {
        var actor=new UserIdentity(Guid.NewGuid(),"Teste","teste","Administrador",Guid.NewGuid());var all=Permissions.Catalog.ToDictionary(x=>x.Key,_=>true);
        var current=Sample();var next=ServerAuthorization.CloneForWrite(current);
        Check(JsonSerializer.Serialize(next)==JsonSerializer.Serialize(JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(current))),"cópia para gravação igual à cópia por JSON");
        Check(current.AuditTrail.Count==300&&!ReferenceEquals(current.AuditTrail[0],next.AuditTrail[0]),"cópia não compartilha a auditoria e não esvazia a original");
        next.AuditTrail[5].Description="mudou";Check(current.AuditTrail[5].Description=="Evento 5","alterar a cópia não altera a original");
        Check(Throws(()=>ServerAuthorization.ValidateAndAudit(current,next,actor,all)),"auditoria antiga alterada continua recusada (servidor)");
        Check(Throws(()=>FinancialIntegrity.ValidateTransition(current,next)),"auditoria antiga alterada continua recusada (integridade)");
        next=ServerAuthorization.CloneForWrite(current);next.AuditTrail.RemoveAt(10);
        Check(Throws(()=>ServerAuthorization.ValidateAndAudit(current,next,actor,all)),"auditoria antiga apagada continua recusada (servidor)");
        Check(Throws(()=>FinancialIntegrity.ValidateTransition(current,next)),"auditoria antiga apagada continua recusada (integridade)");
        next=ServerAuthorization.CloneForWrite(current);next.AuditTrail[20].Timestamp=DateTime.SpecifyKind(next.AuditTrail[20].Timestamp,DateTimeKind.Local);
        Check(Throws(()=>FinancialIntegrity.ValidateTransition(current,next)),"mudar só o tipo da data também é detectado");
        next=ServerAuthorization.CloneForWrite(current);next.Products[0].Price=0.50m;
        ServerAuthorization.ValidateAndAudit(current,next,actor,all);FinancialIntegrity.ValidateTransition(current,next);
        Check(next.AuditTrail.Count==301&&next.AuditTrail[^1].Action=="Produto alterado","alteração válida passa e gera um só evento novo");
        var a=current.AuditTrail[0];Check(ServerAuthorization.SameAudit(a,ServerAuthorization.CopyAudit(a))&&JsonSerializer.Serialize(a)==JsonSerializer.Serialize(ServerAuthorization.CopyAudit(a)),"cópia do evento idêntica campo a campo e no JSON");
        var dup=new List<AuditEntry>{a,ServerAuthorization.CopyAudit(a)};dup[1].Description="segundo";Check(ServerAuthorization.FirstById(dup,x=>x.Id)[a.Id].Description==a.Description,"busca por Id devolve o primeiro, como FirstOrDefault");
        Console.WriteLine($"AuditFastTests: {passed} verificações OK");
    }
}
