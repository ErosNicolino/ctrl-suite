namespace GraficaCaixa;
public sealed record AuditFilter(DateTime? From=null,DateTime? To=null,string? User=null,string? Module=null,string? Action=null,string? Entity=null,string? Id=null);
public static class AuditQuery
{
    public static IReadOnlyList<AuditEntry> Run(AppData data,AuditFilter filter)
    {
        var id=filter.Id??"";var resolved=data.ScheduledOrders.FirstOrDefault(x=>x.Number.ToString()==id)?.Id.ToString()??id;
        var related=data.ScheduledOrders.FirstOrDefault(x=>x.Id.ToString()==resolved)?.Payments.Where(x=>x.SaleId!=null).Select(x=>x.SaleId!.Value).ToHashSet()??[];
        var cash=data.Sessions.FirstOrDefault(x=>x.Id.ToString()==resolved);if(cash!=null)related.UnionWith(cash.Sales.Select(s=>s.Id));
        // A sangria guarda o caixa pelo horário de abertura ("Caixa dd/MM/yyyy HH:mm"); os registros mais antigos, pelo Id. Vale para os dois, sem alterar a auditoria.
        var opened=cash==null?null:$"Caixa {cash.OpenedAt:dd/MM/yyyy HH:mm}";
        bool OfCash(AuditEntry a)=>a.EntityType is "Sangria" or "Ajuste de caixa"&&(a.Description.Contains(resolved,StringComparison.Ordinal)||opened!=null&&a.Description.Contains(opened,StringComparison.Ordinal));
        return data.AuditTrail.Where(a=>(filter.From==null||a.Timestamp.ToUniversalTime()>=filter.From.Value.ToUniversalTime())&&(filter.To==null||a.Timestamp.ToUniversalTime()<=filter.To.Value.ToUniversalTime())
            && Match(a.UserName+" "+a.UserLogin+" "+a.UserId,filter.User)&&Match(Module(a),filter.Module)&&Match(a.Action,filter.Action)
            && (Match(a.EntityType,filter.Entity)||filter.Entity is "Pedido" or "Caixa" && a.SaleId.HasValue&&related.Contains(a.SaleId.Value)||filter.Entity=="Lançamento"&&a.SaleId?.ToString()==resolved||filter.Entity=="Caixa"&&OfCash(a))
            && (string.IsNullOrEmpty(id)||a.EntityId==resolved||a.SaleId?.ToString()==resolved||a.SaleId.HasValue&&related.Contains(a.SaleId.Value)||a.EntityId.Contains(id,StringComparison.OrdinalIgnoreCase)||cash!=null&&OfCash(a)))
            .OrderByDescending(a=>a.Timestamp).Take(1000).ToList();
    }
    public static string Module(AuditEntry a)=>a.EntityType switch {"Pedido"=>"Pedidos","Caixa" or "Sangria" or "Ajuste de caixa" or "Lançamento" or "Correção"=>"Financeiro","Segurança" or "Banco"=>"Sistema","Produto"=>"Cadastros",_=>a.EntityType};
    static bool Match(string value,string? search)=>string.IsNullOrWhiteSpace(search)||value.Contains(search,StringComparison.CurrentCultureIgnoreCase);
}
