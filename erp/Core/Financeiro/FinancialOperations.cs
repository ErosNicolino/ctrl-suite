using System.Text.Json;

namespace GraficaCaixa;

public static class AuditService
{
    public static AuditEntry Record(AppData data, OperationContext context, string action, string entityType, string entityId,
        string oldValue, string newValue, string reason, string description, Guid? saleId = null)
    {
        data.AuditTrail ??= [];
        var entry=new AuditEntry
        {
            Action=action,Description=description,SaleId=saleId,UserName=context.UserName,
            EntityType=entityType,EntityId=entityId,OldValue=oldValue,NewValue=newValue,Reason=reason
        };
        data.AuditTrail.Add(entry);
        return entry;
    }
}

public static partial class FinancialOperations
{
    public static CashSession OpenCash(AppData data, decimal openingAmount, OperationContext context)
    {
        if(data.Sessions.Any(x=>x.IsOpen))throw new InvalidOperationException("Já existe um caixa aberto.");
        if(openingAmount<0)throw new InvalidOperationException("O fundo inicial não pode ser negativo.");
        var session=new CashSession{OpeningAmount=openingAmount,OpenedBy=context.UserName};data.Sessions.Add(session);
        AuditService.Record(data,context,"Caixa aberto","Caixa",session.Id.ToString(),"",openingAmount.ToString("0.00"),"",$"Fundo inicial: {openingAmount:C2}");
        return session;
    }

    public static Sale AddSale(AppData data, Sale sale, OperationContext context, Guid? closedSessionId=null, string reason="")
    {
        if(sale.Items.Count>0 && sale.ItemsTotal==null)return AddItemSale(data,sale,context,closedSessionId,reason);
        sale.FeePercent ??= PaymentPolicy.Rate(sale.PaymentType);
        sale.Contact=PhoneText.Normalize(sale.Contact);
        var session=closedSessionId.HasValue?RequireClosedSession(data,closedSessionId.Value):data.Sessions.LastOrDefault(x=>x.IsOpen)??throw new InvalidOperationException("Abra o caixa antes de registrar um lançamento.");
        if(data.Sessions.SelectMany(x=>x.Sales).Any(x=>x.Id==sale.Id))return data.Sessions.SelectMany(x=>x.Sales).First(x=>x.Id==sale.Id);
        ValidateSale(sale);
        if(!session.IsOpen){context.Require(BusinessPermission.RectifyCash);RequireReason(reason);EnsureOriginalVersion(data,session);}
        session.Sales.Add(sale);
        AuditService.Record(data,context,session.IsOpen?"Lançamento criado":"Lançamento incluído em retificação","Lançamento",sale.Id.ToString(),"",sale.Total.ToString("0.00"),reason,$"{sale.Product} — {sale.Total:C2}",sale.Id);
        if(!session.IsOpen)AddRectifiedVersion(data,session,context,reason);
        return sale;
    }

    public static Guid? CorrectSale(AppData data, Guid originalSaleId, decimal correctedAmount, string reason, OperationContext context, Guid? expectedOrderId=null)
    {
        context.Require(BusinessPermission.CorrectFinancialEntry);RequireReason(reason);
        if(correctedAmount<0||decimal.Round(correctedAmount,2)!=correctedAmount)throw new InvalidOperationException("O valor corrigido deve ser igual ou maior que zero, com até duas casas decimais.");
        var session=data.Sessions.FirstOrDefault(x=>x.Sales.Any(s=>s.Id==originalSaleId))??throw new InvalidOperationException("O lançamento original não existe mais.");
        var original=session.Sales.First(x=>x.Id==originalSaleId);
        if(IsAdjustmentLine(original))throw new InvalidOperationException("Selecione o lançamento original, não uma linha de ajuste ou de devolução.");
        var prior=data.FinancialCorrections.Where(x=>x.OriginalSaleId==originalSaleId).OrderBy(x=>x.CreatedAt).LastOrDefault();
        var currentAmount=prior?.CorrectedAmount??original.Total;
        var difference=correctedAmount-currentAmount;
        if(difference==0)throw new InvalidOperationException("O novo valor é igual ao valor financeiro atual.");
        var order=data.ScheduledOrders.FirstOrDefault(x=>x.Payments.Any(p=>p.SaleId==originalSaleId));
        if(expectedOrderId.HasValue&&order?.Id!=expectedOrderId)throw new InvalidOperationException("O lançamento não pertence ao pedido informado.");
        if(order!=null&&difference>0&&order.PaidAmount+difference>order.OrderTotal)throw new InvalidOperationException($"A correção deixaria o pedido com pagamento superior ao saldo em {order.PaidAmount+difference-order.OrderTotal:C2}.");
        var debt=data.ManualDebts.FirstOrDefault(x=>x.SaleIds.Contains(originalSaleId));
        if(debt!=null && (debt.Paid(data)+difference>debt.Amount+debt.Excess || debt.Paid(data)+difference<0))throw new InvalidOperationException("A correção ultrapassa o saldo da conta a receber.");
        if(order!=null&&order.PaidAmount+difference<0)throw new InvalidOperationException("A correção deixaria o total recebido negativo.");
        if(!session.IsOpen){context.Require(BusinessPermission.RectifyCash);RequireRecentClosed(data,session);EnsureOriginalVersion(data,session);}
        var adjustment=new Sale{CreatedAt=DateTime.Now,CustomerName=original.CustomerName,Contact=original.Contact,Product=$"Ajuste do lançamento {originalSaleId:N}: {reason}",Units=1,PaymentType=original.PaymentType,FeePercent=original.FeePercent??PaymentPolicy.HistoricalRate(original.PaymentType),PaymentGroupId=original.PaymentGroupId,TotalValue=difference};
        session.Sales.Add(adjustment);
        if(debt!=null){debt.SaleIds.Add(adjustment.Id);debt.TrimExcess(debt.Paid(data));}
        if(order!=null)
        {
            var originalPayment=order.Payments.First(p=>p.SaleId==originalSaleId);var priorExtra=order.Payments.Where(p=>p.SaleId==originalSaleId||data.FinancialCorrections.Any(c=>c.OriginalSaleId==originalSaleId&&c.AdjustmentSaleId==p.SaleId)).Sum(p=>p.ExcessAmount);var extraAdjustment=difference<0?-Math.Min(priorExtra,-difference):0;
            order.Payments.Add(new OrderPayment{ExcessAmount=extraAdjustment,ExcessKind=extraAdjustment!=0?originalPayment.ExcessKind:"",ExcessConfirmed=extraAdjustment!=0,Id=adjustment.Id,SaleId=adjustment.Id,Amount=difference,FeePercent=adjustment.FeePercent,PaymentType=original.PaymentType,PaidAt=adjustment.CreatedAt,Note=$"Ajuste: {reason}"});
            RefreshOrderPaymentState(order);order.Revision++;
        }
        var correction=new FinancialCorrection{OriginalSaleId=originalSaleId,AdjustmentSaleId=adjustment.Id,OrderId=order?.Id,UserName=context.UserName,Reason=reason,OriginalAmount=currentAmount,CorrectedAmount=correctedAmount};
        data.FinancialCorrections.Add(correction);
        AuditService.Record(data,context,"Lançamento financeiro corrigido","Lançamento",originalSaleId.ToString(),currentAmount.ToString("0.00"),correctedAmount.ToString("0.00"),reason,$"Original preservado; ajuste {difference:C2} vinculado ao lançamento {originalSaleId}",originalSaleId);
        if(!session.IsOpen)AddRectifiedVersion(data,session,context,reason);
        return session.Id;
    }

    public const string RefundPrefix="Devolução do lançamento",RefundType="Devolução";
    public static readonly string[] RefundMethods=["Dinheiro","PIX"];
    public static bool IsAdjustmentLine(Sale sale)=>sale.Product.StartsWith("Ajuste do lançamento",StringComparison.OrdinalIgnoreCase)||sale.Product.StartsWith(RefundPrefix,StringComparison.OrdinalIgnoreCase);

    // Devolução ao cliente (ex.: pagou R$ 30, trocou para um serviço de R$ 10 e recebe R$ 20 de volta).
    // O pagamento original fica como foi (R$ 30 no Pix/cartão/dinheiro); a devolução é uma linha negativa no caixa ABERTO,
    // na forma em que o dinheiro sai (Dinheiro da gaveta ou Pix), ligada ao original por uma correção do tipo "Devolução".
    // Cartão: a maquininha não estorna aqui, a devolução sai em Dinheiro ou Pix e a taxa do cartão sobre o original continua.
    public static Guid RefundSale(AppData data,Guid originalSaleId,decimal amount,string method,string reason,OperationContext context)
    {
        context.Require(BusinessPermission.CorrectFinancialEntry);context.Require(BusinessPermission.CancelFinancialEntry);RequireReason(reason);
        if(!RefundMethods.Contains(method))throw new InvalidOperationException("A devolução é feita em Dinheiro ou Pix.");
        if(amount<=0||decimal.Round(amount,2)!=amount)throw new InvalidOperationException("Informe o valor a devolver, maior que zero e com até duas casas decimais.");
        var open=data.Sessions.LastOrDefault(x=>x.IsOpen)??throw new InvalidOperationException("Abra o caixa para registrar a devolução: o valor sai do caixa de hoje.");
        var original=data.Sessions.SelectMany(x=>x.Sales).FirstOrDefault(x=>x.Id==originalSaleId)??throw new InvalidOperationException("O lançamento original não existe mais.");
        if(IsAdjustmentLine(original))throw new InvalidOperationException("Selecione o lançamento original, não uma linha de ajuste ou de devolução.");
        var currentAmount=data.FinancialCorrections.Where(x=>x.OriginalSaleId==originalSaleId).OrderBy(x=>x.CreatedAt).LastOrDefault()?.CorrectedAmount??original.Total;
        if(amount>currentAmount)throw new InvalidOperationException($"A devolução não pode passar do valor pago ({currentAmount:C2}).");
        if(method=="Dinheiro"&&amount>open.ExpectedCash)throw new InvalidOperationException($"Não há dinheiro suficiente na gaveta ({open.ExpectedCash:C2}). Devolva por Pix ou registre depois.");
        var order=data.ScheduledOrders.FirstOrDefault(x=>x.Payments.Any(p=>p.SaleId==originalSaleId));
        if(order!=null&&order.PaidAmount-amount<0)throw new InvalidOperationException("A devolução deixaria o total recebido do pedido negativo.");
        // Lançamento que virou pagamento de pedido: quem responde pelo saldo é o pedido, não a conta interna do lançamento.
        var debt=order!=null?null:data.ManualDebts.FirstOrDefault(x=>x.SaleIds.Contains(originalSaleId));
        if(debt!=null&&debt.Paid(data)-amount<0)throw new InvalidOperationException("A devolução passa do que foi pago nesta conta a receber.");
        var refund=new Sale{CreatedAt=DateTime.Now,CustomerName=original.CustomerName,Contact=original.Contact,Product=$"{RefundPrefix} {originalSaleId:N}: {reason}",Units=1,PaymentType=method,FeePercent=0,TotalValue=-amount};
        open.Sales.Add(refund);
        if(debt!=null){debt.SaleIds.Add(refund.Id);debt.TrimExcess(debt.Paid(data));}
        if(order!=null)
        {
            var originalPayment=order.Payments.First(p=>p.SaleId==originalSaleId);var priorExtra=order.Payments.Where(p=>p.SaleId==originalSaleId||data.FinancialCorrections.Any(c=>c.OriginalSaleId==originalSaleId&&c.AdjustmentSaleId==p.SaleId)).Sum(p=>p.ExcessAmount);var extraAdjustment=-Math.Min(priorExtra,amount);
            order.Payments.Add(new OrderPayment{ExcessAmount=extraAdjustment,ExcessKind=extraAdjustment!=0?originalPayment.ExcessKind:"",ExcessConfirmed=extraAdjustment!=0,Id=refund.Id,SaleId=refund.Id,Amount=-amount,FeePercent=0,PaymentType=method,PaidAt=refund.CreatedAt,Note=$"Devolução: {reason}"});
            RefreshOrderPaymentState(order);order.Revision++;
        }
        data.FinancialCorrections.Add(new FinancialCorrection{OriginalSaleId=originalSaleId,AdjustmentSaleId=refund.Id,OrderId=order?.Id,UserName=context.UserName,Reason=reason,CorrectionType=RefundType,OriginalAmount=currentAmount,CorrectedAmount=currentAmount-amount});
        AuditService.Record(data,context,"Devolução ao cliente","Lançamento",originalSaleId.ToString(),currentAmount.ToString("0.00"),(currentAmount-amount).ToString("0.00"),reason,$"{amount:C2} devolvidos em {method}; pagamento original ({original.PaymentType}) preservado",originalSaleId);
        return open.Id;
    }

    // Cancelar um lançamento com itens cancela o lançamento inteiro: zera todos os pagamentos do grupo e encerra a
    // conta a receber gerada por ele. Antes, só o pagamento era zerado e a conta voltava a aparecer em Devedores.
    public static void CancelSale(AppData data, Guid saleId, string reason, OperationContext context)
    {
        var sale=data.Sessions.SelectMany(x=>x.Sales).FirstOrDefault(x=>x.Id==saleId)??throw new InvalidOperationException("O lançamento original não existe mais.");
        var debt=sale.PaymentGroupId is Guid group?data.ManualDebts.FirstOrDefault(x=>x.Id==group&&!x.IsCancelled):null;
        if(debt==null){CorrectSale(data,saleId,0,reason,context);return;}
        context.Require(BusinessPermission.CancelFinancialEntry);
        var adjustments=data.FinancialCorrections.Select(c=>c.AdjustmentSaleId).ToHashSet();
        foreach(var id in data.Sessions.SelectMany(x=>x.Sales).Where(x=>x.PaymentGroupId==debt.Id&&!adjustments.Contains(x.Id)).Select(x=>x.Id).ToList())
            if(EffectiveAmount(data,id)!=0)CorrectSale(data,id,0,reason,context);
        if(debt.Paid(data)!=0)throw new InvalidOperationException($"Esta conta tem outros recebimentos ({debt.Paid(data):C2}) fora deste lançamento. Cancele esses recebimentos antes.");
        debt.CancelledAt=DateTime.Now;debt.CancelReason=reason.Trim();
        AuditService.Record(data,context,"Lançamento cancelado","Lançamento",debt.Id.ToString(),debt.Amount.ToString("0.00"),"0.00",reason,$"{debt.CustomerName} • {debt.Description} • conta a receber encerrada",saleId);
    }
    public static decimal EffectiveAmount(AppData data,Guid saleId)=>data.FinancialCorrections.Where(x=>x.OriginalSaleId==saleId).OrderBy(x=>x.CreatedAt).LastOrDefault()?.CorrectedAmount??data.Sessions.SelectMany(x=>x.Sales).First(x=>x.Id==saleId).Total;

    public static CashSession CloseCash(AppData data, Guid sessionId, CashSession expected, decimal declaredAmount, OperationContext context)
    {
        context.Require(BusinessPermission.CloseCash);
        if(declaredAmount<0)throw new InvalidOperationException("O dinheiro contado não pode ser negativo.");
        var session=data.Sessions.FirstOrDefault(x=>x.Id==sessionId)??throw new InvalidOperationException("O caixa não existe mais.");
        if(!session.IsOpen)throw new InvalidOperationException("O caixa já foi fechado.");
        DataRelations.RequireUnchanged(session,expected);
        session.ClosedAt=DateTime.Now;session.DeclaredClosingAmount=declaredAmount;session.ClosedBy=context.UserName;
        AddClosureVersion(data,session,context,"Fechamento original",true);
        AuditService.Record(data,context,"Caixa fechado","Caixa",session.Id.ToString(),"Aberto",$"Fechado; contado {declaredAmount:0.00}","",$"Vendas: {session.SalesTotal:C2}; contado: {declaredAmount:C2}; diferença: {(declaredAmount-session.ExpectedCash):C2}");
        return session;
    }

    public static void RectifySessionValues(AppData data, Guid sessionId, CashSession expected, decimal openingAmount, decimal declaredAmount, DateTime openedAt, DateTime closedAt, string openedBy, string closedBy, string reason, OperationContext context)
    {
        context.Require(BusinessPermission.RectifyCash);RequireReason(reason);
        var session=RequireClosedSession(data,sessionId);DataRelations.RequireUnchanged(session,expected);EnsureOriginalVersion(data,session);
        var before=$"{session.OpenedAt:O}|{session.ClosedAt:O}|{session.OpeningAmount:0.00}|{session.DeclaredClosingAmount:0.00}|{session.OpenedBy}|{session.ClosedBy}";
        session.OpeningAmount=openingAmount;session.DeclaredClosingAmount=declaredAmount;session.OpenedAt=openedAt;session.ClosedAt=closedAt;session.OpenedBy=openedBy;session.ClosedBy=closedBy;
        var after=$"{session.OpenedAt:O}|{session.ClosedAt:O}|{session.OpeningAmount:0.00}|{session.DeclaredClosingAmount:0.00}|{session.OpenedBy}|{session.ClosedBy}";
        AuditService.Record(data,context,"Fechamento retificado","Caixa",session.Id.ToString(),before,after,reason,$"Dados do fechamento de {session.OpenedAt:dd/MM/yyyy} retificados");
        AddRectifiedVersion(data,session,context,reason);
    }

    static CashSession RequireClosedSession(AppData data,Guid id)=>data.Sessions.FirstOrDefault(x=>x.Id==id&&!x.IsOpen)??throw new InvalidOperationException("O fechamento não existe mais ou ainda está aberto.");
    static void ValidateSale(Sale sale){if(sale.Total<=0||decimal.Round(sale.Total,2)!=sale.Total)throw new InvalidOperationException("Informe um lançamento maior que zero, com até duas casas decimais.");if(string.IsNullOrWhiteSpace(sale.Product))throw new InvalidOperationException("Informe o produto ou a descrição do lançamento.");}
    static void RequireReason(string reason){if(string.IsNullOrWhiteSpace(reason)||reason.Trim().Length<5)throw new InvalidOperationException("Informe uma justificativa com pelo menos 5 caracteres.");}
    static void RefreshOrderPaymentState(ScheduledOrder order){OrderOperations.RefreshPaymentState(order);if(order.FullyPaid){var last=order.Payments.LastOrDefault(x=>x.Amount>0);order.FinalSaleId=last?.SaleId;order.PaymentReceivedAt=last?.PaidAt;order.PayAfterDelivery=false;}}
    static void EnsureOriginalVersion(AppData data,CashSession session){if(data.CashClosureVersions.Any(x=>x.SessionId==session.Id))return;AddClosureVersion(data,session,OperationContext.Administrator(session.ClosedBy.Length>0?session.ClosedBy:"Sistema"),"Fechamento original preservado antes da primeira retificação",true);}
    static void AddRectifiedVersion(AppData data,CashSession session,OperationContext context,string reason)=>AddClosureVersion(data,session,context,reason,false);
    static void AddClosureVersion(AppData data,CashSession session,OperationContext context,string reason,bool original)
    {
        var version=data.CashClosureVersions.Where(x=>x.SessionId==session.Id).Select(x=>x.Version).DefaultIfEmpty(0).Max()+1;
        data.CashClosureVersions.Add(new CashClosureVersion{SessionId=session.Id,Version=version,UserName=context.UserName,Reason=reason,IsOriginal=original,OpenedAt=session.OpenedAt,ClosedAt=session.ClosedAt,OpeningAmount=session.OpeningAmount,DeclaredClosingAmount=session.DeclaredClosingAmount,SalesTotal=session.SalesTotal,ExpectedCash=session.ExpectedCash,SnapshotJson=JsonSerializer.Serialize(session)});
    }
}

// Cancelados ficam guardados (histórico e auditoria), mas fora das telas do dia a dia.
public sealed record CancelledRecord(DateTime CancelledAt,string Kind,string Customer,string Description,decimal Amount,string Reason,string User);
public static class Cancellations
{
    // Lançamento cancelado (valor corrigido para zero) e as linhas de ajuste que o zeraram.
    public static HashSet<Guid> HiddenSaleIds(AppData data)
    {
        var hidden=new HashSet<Guid>();
        foreach(var group in data.FinancialCorrections.GroupBy(x=>x.OriginalSaleId))
        {
            // Com devolução ao cliente as duas linhas continuam visíveis (o dinheiro saiu de fato do caixa).
            if(group.OrderBy(x=>x.CreatedAt).Last().CorrectedAmount!=0||group.Any(x=>x.CorrectionType==FinancialOperations.RefundType))continue;
            hidden.Add(group.Key);foreach(var c in group)hidden.Add(c.AdjustmentSaleId);
        }
        return hidden;
    }
    public static List<CancelledRecord> List(AppData data)
    {
        var rows=new List<CancelledRecord>();var sales=data.Sessions.SelectMany(x=>x.Sales).ToDictionary(x=>x.Id);
        // Devoluções ao cliente (parciais ou totais) aparecem uma a uma, com o valor devolvido e a forma.
        foreach(var refund in data.FinancialCorrections.Where(x=>x.CorrectionType==FinancialOperations.RefundType))
            if(sales.TryGetValue(refund.OriginalSaleId,out var paid)&&sales.TryGetValue(refund.AdjustmentSaleId,out var line))
                rows.Add(new(refund.CreatedAt,"Devolução ("+line.PaymentType+")",DisplayText.Customer(paid),paid.Product,-line.Total,refund.Reason,refund.UserName));
        foreach(var group in data.FinancialCorrections.GroupBy(x=>x.OriginalSaleId))
        {
            var last=group.OrderBy(x=>x.CreatedAt).Last();if(last.CorrectedAmount!=0||last.CorrectionType==FinancialOperations.RefundType||!sales.TryGetValue(group.Key,out var sale))continue;
            rows.Add(new(last.CreatedAt,"Lançamento",DisplayText.Customer(sale),sale.Product,sale.Total,last.Reason,last.UserName));
        }
        // Só pedidos cancelados de verdade (arquivado é outra coisa: pode voltar e não entra aqui).
        foreach(var order in data.ScheduledOrders.Where(x=>x.IsCancelled))
        {
            var audit=data.AuditTrail.Where(a=>a.EntityId==order.Id.ToString()&&a.Action.Contains("cancelado",StringComparison.OrdinalIgnoreCase)).OrderBy(a=>a.Timestamp).LastOrDefault();
            rows.Add(new(order.CancelledAt??audit?.Timestamp??order.CreatedAt,"Pedido #"+order.Number,order.CustomerName,order.ItemsDescription,order.OrderTotal,order.CancelReason.Length>0?order.CancelReason:audit?.Reason??"",order.CancelledBy.Length>0?order.CancelledBy:audit?.UserName??""));
        }
        return rows.OrderByDescending(x=>x.CancelledAt).ToList();
    }
}

public static class FinancialIntegrity
{
    public static void ValidateState(AppData data)
    {
        if(data.PaymentRates.Any(x=>!PaymentPolicy.DefaultRates.ContainsKey(x.Key)||x.Value<0||x.Value>100||decimal.Round(x.Value,4)!=x.Value))throw new InvalidOperationException("Taxa de pagamento inválida.");
        if(data.Products.Any(x=>string.IsNullOrWhiteSpace(x.Description)||x.Cost<0||x.Price<0)||data.Products.Select(x=>x.Id).Distinct().Count()!=data.Products.Count)throw new InvalidOperationException("Cadastro de produtos inválido.");
        foreach(var order in data.ScheduledOrders)
        {
            if(order.FixedDiscount.HasValue&&(order.DiscountPercent!=0||order.FixedDiscount<0||decimal.Round(order.FixedDiscount.Value,2)!=order.FixedDiscount||order.FixedDiscount>(order.OriginalTotal??order.OrderTotal)||order.PricePending&&order.FixedDiscount>0))throw new InvalidOperationException("Desconto em reais inválido.");
            if(order.Items.Any(x=>x.UnitCost<0||x.CostReviewed&&x.TotalCost==null)||order.Items.Select(x=>x.Id).Distinct().Count()!=order.Items.Count)throw new InvalidOperationException("Custos ou identificadores de itens inválidos.");
            if(order.Payments.Any(p=>decimal.Round(p.ExcessAmount,2)!=p.ExcessAmount||(p.Amount>=0?(p.ExcessAmount<0||p.ExcessAmount>p.Amount):(p.ExcessAmount>0||p.ExcessAmount<p.Amount))||p.ExcessAmount!=0&&(!p.ExcessConfirmed||(p.ExcessKind!=PaymentPolicy.CollectionSurcharge&&p.ExcessKind!=PaymentPolicy.CustomerExtra)))||order.CollectionSurcharge<0||order.CustomerExtra<0)throw new InvalidOperationException("Excedente financeiro inválido.");
            if(order.PaidAmount<0)throw new InvalidOperationException($"O pedido #{order.Number} possui recebimento negativo.");
            var ids=order.Payments.Select(x=>x.Id).ToList();if(ids.Count!=ids.Distinct().Count())throw new InvalidOperationException($"O pedido #{order.Number} possui pagamentos duplicados.");
        }
        var saleIds=data.Sessions.SelectMany(x=>x.Sales).Select(x=>x.Id).ToList();if(saleIds.Count!=saleIds.Distinct().Count())throw new InvalidOperationException("Há identificadores duplicados em lançamentos financeiros.");
        foreach(var correction in data.FinancialCorrections)if(!saleIds.Contains(correction.OriginalSaleId)||!saleIds.Contains(correction.AdjustmentSaleId)||string.IsNullOrWhiteSpace(correction.Reason))throw new InvalidOperationException("Uma correção financeira perdeu o vínculo com o lançamento original, o ajuste ou a justificativa.");
    }

    public static void ValidateTransition(AppData current,AppData incoming)
    {
        ValidateState(incoming);
        foreach(var order in incoming.ScheduledOrders.Where(x=>!x.PricePending&&x.PaidAmount>x.OrderTotal))
        {
            var old=current.ScheduledOrders.FirstOrDefault(x=>x.Id==order.Id);
            if((old==null||old.PaidAmount<=old.OrderTotal||old.PaidAmount!=order.PaidAmount||old.OrderTotal!=order.OrderTotal)&&!(order.Payments.Where(p=>p.SaleId!=null).Sum(p=>p.ExcessAmount)>=order.PaidAmount-order.OrderTotal&&order.AppliedAmount<=order.OrderTotal&&order.Payments.Where(p=>p.ExcessAmount>0).All(p=>p.ExcessConfirmed&&(p.ExcessKind==PaymentPolicy.CollectionSurcharge||p.ExcessKind==PaymentPolicy.CustomerExtra))))
                throw new InvalidOperationException($"O pedido #{order.Number} ficaria com pagamento superior ao total.");
        }
        // Desempenho: busca por Id em vez de varrer a lista para cada registro (mesmo resultado de FirstOrDefault).
        var incomingAudit=ServerAuthorization.FirstById(incoming.AuditTrail,x=>x.Id);
        foreach(var audit in current.AuditTrail)
        {
            var next=incomingAudit.GetValueOrDefault(audit.Id)??throw new InvalidOperationException("O histórico de auditoria não pode ser excluído.");
            if(!ServerAuthorization.SameAudit(audit,next)&&JsonSerializer.Serialize(audit)!=JsonSerializer.Serialize(next))throw new InvalidOperationException("O histórico de auditoria existente não pode ser alterado.");
        }
        foreach(var correction in current.FinancialCorrections)
        {
            var next=incoming.FinancialCorrections.FirstOrDefault(x=>x.Id==correction.Id)??throw new InvalidOperationException("Correções financeiras registradas não podem ser excluídas.");
            if(JsonSerializer.Serialize(correction)!=JsonSerializer.Serialize(next))throw new InvalidOperationException("Correções financeiras registradas não podem ser alteradas.");
        }
        foreach(var version in current.CashClosureVersions)
        {
            var next=incoming.CashClosureVersions.FirstOrDefault(x=>x.Id==version.Id)??throw new InvalidOperationException("Versões de fechamento não podem ser excluídas.");
            if(JsonSerializer.Serialize(version)!=JsonSerializer.Serialize(next))throw new InvalidOperationException("Versões de fechamento existentes não podem ser alteradas.");
        }
        foreach(var oldSession in current.Sessions.Where(x=>!x.IsOpen))
        {
            var next=incoming.Sessions.FirstOrDefault(x=>x.Id==oldSession.Id)??throw new InvalidOperationException("Fechamentos concluídos não podem ser excluídos.");
            if(JsonSerializer.Serialize(oldSession)!=JsonSerializer.Serialize(next))
            {
                var versions=incoming.CashClosureVersions.Where(x=>x.SessionId==oldSession.Id).OrderBy(x=>x.Version).ToList();
                if(versions.Count<2||!versions[0].IsOriginal)throw new InvalidOperationException("Uma alteração em caixa fechado exige preservação do original e nova versão de retificação.");
            }
        }
    }
}
