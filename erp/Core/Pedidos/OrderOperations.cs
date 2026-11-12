using System.Text.Json;

namespace GraficaCaixa;

public static partial class OrderOperations
{
    public static void SaveCosts(AppData data,ScheduledOrder draft,OperationContext context)
    {
        var old=data.ScheduledOrders.FirstOrDefault(x=>x.Id==draft.Id)??throw new InvalidOperationException("Pedido removido.");
        if(old.Revision!=draft.Revision)throw new InvalidOperationException("Pedido alterado em outro computador. Reabra o custo.");
        if(old.Items.Count!=draft.Items.Count || old.Items.Any(i=>!draft.Items.Any(n=>n.Id==i.Id&&n.Quantity==i.Quantity&&n.Product==i.Product)))throw new InvalidOperationException("Os itens mudaram. Reabra o pedido.");
        if(draft.FreightCost<0||draft.AdditionalCost<0||draft.Items.Any(x=>x.UnitCost<0||x.EnteredTotalCost<0||x.CostReviewed&&x.TotalCost==null))throw new InvalidOperationException("Confira os custos dos itens.");
        var before=CostSnapshot(old);
        if(before==CostSnapshot(draft))return;
        UpdateReviewedProductCosts(data,old,draft,context);
        old.ItemCostsEnabled=draft.ItemCostsEnabled;old.IgnoreCosts=draft.IgnoreCosts;old.FreightCost=draft.FreightCost;old.AdditionalCost=draft.AdditionalCost;old.CostNotes=draft.CostNotes;
        foreach(var item in old.Items){var cost=draft.Items.Single(x=>x.Id==item.Id);item.UnitCost=cost.UnitCost;item.EnteredTotalCost=cost.EnteredTotalCost;item.CostReviewed=cost.CostReviewed;item.CostIgnored=cost.CostIgnored;item.CostSource=cost.CostSource;}

        old.Revision++;
        AuditService.Record(data,context,"Custo do pedido alterado","Pedido",old.Id.ToString(),before,CostSnapshot(old),draft.CostChangeReason,$"Pedido #{old.Number}");
    }
    public static void UpdateReviewedProductCosts(AppData data,ScheduledOrder? before,ScheduledOrder after,OperationContext context)
    {
        if(after.IgnoreCosts||!after.ItemCostsEnabled)return;
        foreach(var item in after.Items)
        {
            var previous=before?.Items.FirstOrDefault(x=>x.Id==item.Id);
            if(!item.CostReviewed||item.CostIgnored||item.TotalCost is not decimal reviewed||item.ProductId is not Guid productId)continue;
            if(previous!=null&&previous.CostReviewed&&!previous.CostIgnored&&previous.TotalCost==reviewed&&previous.Quantity==item.Quantity&&previous.ProductId==item.ProductId&&before?.IgnoreCosts!=true)continue;
            var product=data.Products.FirstOrDefault(x=>x.Id==productId);if(product==null)continue;
            var registered=ProductCosting.Resolve(product,item.Quantity);
            if(registered.Kind==ProductCostKind.Standard&&(QuantityPricing.HasExactTable(product)||product.UseQuantityPricing))continue;
            if(registered.TotalCost==reviewed)continue;
            ProductCosting.UpdateFromReviewedOrder(product,item.Quantity,reviewed);
            AuditService.Record(data,context,"Custo do produto confirmado pelo pedido","Produto",product.Id.ToString(),JsonSerializer.Serialize(new{registered.Kind,registered.Label,registered.UnitCost,registered.TotalCost}),JsonSerializer.Serialize(new{registered.Kind,registered.Label,UnitCost=reviewed/item.Quantity,TotalCost=reviewed,OrderId=after.Id,OrderNumber=after.Number}),"Conferência do custo do item",$"{product.Description} • {registered.Kind} / {registered.Label} • Pedido #{after.Number}");
        }
    }
    // Valores sem zeros à direita: 39,0 e 39,00 são o mesmo custo (antes a comparação por texto pedia justificativa à toa).
    // Pedido antigo: "custo por item" desligado sem nenhum custo e valor original vazio (= total) são o mesmo pedido que o editor salva.
    static decimal? Plain(decimal? value)=>value.HasValue?value.Value/1.0000000000000000000000000000m:null;
    public static string CostSnapshot(ScheduledOrder o) => JsonSerializer.Serialize(new {ItemCostsEnabled=o.ItemCostsEnabled||o.Items.All(i=>i.UnitCost==null&&i.EnteredTotalCost==null&&!i.CostReviewed&&!i.CostIgnored),o.IgnoreCosts,FreightCost=Plain(o.FreightCost),AdditionalCost=Plain(o.AdditionalCost),o.CostNotes,Items=o.Items.Select(i=>new {i.Id,i.Quantity,UnitCost=Plain(i.UnitCost),EnteredTotalCost=Plain(i.EnteredTotalCost),i.CostReviewed,i.CostIgnored,i.CostSource})});
    public static bool IsImmediate(ScheduledOrder order)=>order.ProductionBusinessDays==0&&!order.IsUncollected&&!IsFinished(order);
    public static int OperationalPriority(ScheduledOrder order)=>IsImmediate(order)?0:order.IsUncollected||IsFinished(order)?4:order.Stage!="Pronto para retirada"&&order.DeadlineStarted?order.DueDate.Date<DateTime.Today?1:order.DueDate.Date==DateTime.Today?2:3:3;
    // Pedido pago: só pede justificativa quando o total novo fica abaixo do que já foi recebido (ou volta a "a definir"). Incluir item ou aumentar o valor fica só no histórico.
    public static bool RequiresPaidCorrection(ScheduledOrder before,ScheduledOrder after)=>before.AppliedAmount>0&&(after.PricePending&&!before.PricePending||!after.PricePending&&after.OrderTotal<before.AppliedAmount);
    public static bool IsFinished(ScheduledOrder order) => order.Stage == "Entregue";
    public static void Cancel(ScheduledOrder order,string reason,string user){if(order.IsCancelled)throw new InvalidOperationException("Este pedido já foi cancelado.");if((reason??"").Trim().Length<5)throw new InvalidOperationException("Informe o motivo do cancelamento (mínimo 5 caracteres).");if(order.AppliedAmount>0)throw new InvalidOperationException($"O pedido tem {order.AppliedAmount:C2} recebidos. Faça a devolução ao cliente (ou o estorno) antes de cancelar.");order.CancelledAt=DateTime.Now;order.CancelReason=reason!.Trim();order.CancelledBy=user;order.IsUncollected=true;order.ArchivedAsUncollectedAt??=DateTime.Now;order.Revision++;}
    public static bool IsInProduction(ScheduledOrder order) => order.Stage == "Produção";
    public static void RefreshPaymentState(ScheduledOrder order)
    {
        order.FullyPaid = !order.PricePending && order.RemainingAmount <= 0;
        if (!order.FullyPaid) { order.FinalSaleId = null; order.PaymentReceivedAt = null; }
    }
    public static List<Guid> Save(AppData data, ScheduledOrder draft, OperationContext context, Guid? correctionSaleId = null, decimal? correctedAmount = null, string correctionReason = "")
    {
        var old = data.ScheduledOrders.FirstOrDefault(x => x.Id == draft.Id);
        if (old != null && old.Revision != draft.Revision)
            throw new InvalidOperationException("Este pedido foi atualizado em outro computador. Abra-o novamente para aplicar suas alterações.");
        var order = JsonSerializer.Deserialize<ScheduledOrder>(JsonSerializer.Serialize(draft))!;
        if(order.FreightCost<0 || order.AdditionalCost<0 || order.Items.Any(x=>x.UnitCost<0 || x.EnteredTotalCost<0 || x.CostReviewed && x.TotalCost==null))
            throw new InvalidOperationException("Os custos não podem ser negativos.");
        if(order.UseItemPrices && !order.PricePending)
        {
            if(order.Items.Count==0 || order.Items.Any(x=>x.Quantity<=0 || x.UnitPrice is null or <0))
                throw new InvalidOperationException("Informe o valor unitário de todos os itens ou escolha Total manual.");
            order.OriginalTotal=order.Items.Sum(x=>x.LineTotal!.Value);
            order.OrderTotal=order.OriginalTotal.Value;
        }
        if(order.FixedDiscount is <0 || order.FixedDiscount.HasValue&&order.DiscountPercent!=0 || order.FixedDiscount.HasValue&&decimal.Round(order.FixedDiscount.Value,2)!=order.FixedDiscount.Value)throw new InvalidOperationException("Use somente uma modalidade de desconto e um valor em reais válido.");
        if(order.DiscountPercent < 0 || order.DiscountPercent > 100)
            throw new InvalidOperationException("O desconto deve estar entre 0% e 100%.");
        if(order.PricePending && order.DiscountAmount > 0)
            throw new InvalidOperationException("Defina o valor do pedido antes de aplicar um desconto.");
        if(!order.PricePending)
        {
            var original=order.OriginalTotal ?? order.OrderTotal;
            if(original<0)throw new InvalidOperationException("O valor original não pode ser negativo.");
            order.OriginalTotal=original;
            order.OrderTotal=original-order.DiscountAmount;
            // Compara com o valor aplicado ao pedido: acréscimo de cobrança (juros do cartão) e valor adicional do cliente não contam (pedido #169).
            if(order.DiscountAmount>0 && order.OrderTotal<(old?.AppliedAmount ?? order.AppliedAmount))
                throw new InvalidOperationException("O valor com desconto não pode ser menor que o valor já recebido. Nenhum recebimento será alterado.");
        }
        if (order.OrderTotal < 0) throw new InvalidOperationException("O valor não pode ser negativo.");
        if (old != null)
        {
            order.Payments = old.Payments.Select(x => JsonSerializer.Deserialize<OrderPayment>(JsonSerializer.Serialize(x))!).ToList();
            order.PdfGeneratedAt = old.PdfGeneratedAt;
        }
        // "Pronto desde": data do servidor, mantida enquanto o pedido continuar pronto para retirada.
        order.ReadyAt = order.Stage == OrderStages.Ready ? (old?.Stage == OrderStages.Ready ? old.ReadyAt ?? DateTime.Now : DateTime.Now) : null;
        var costChanged=old!=null && CostSnapshot(old)!=CostSnapshot(order);
        if(costChanged||old==null)UpdateReviewedProductCosts(data,old,order,context);
        if(costChanged) AuditService.Record(data,context,"Custo do pedido alterado","Pedido",order.Id.ToString(),CostSnapshot(old!),CostSnapshot(order),order.CostChangeReason,$"Pedido #{order.Number}");
        var changedClosed = new List<Guid>();
        if (correctionSaleId is Guid saleId)
        {
            var source=data.Sessions.SelectMany(x=>x.Sales).FirstOrDefault(x=>x.Id==saleId)??throw new InvalidOperationException("Pagamento removido.");
            if(old==null || !old.Payments.Any(x=>x.SaleId==saleId))throw new InvalidOperationException("Pagamento não pertence a este pedido.");
            var effective=data.FinancialCorrections.Where(x=>x.OriginalSaleId==saleId).OrderBy(x=>x.CreatedAt).LastOrDefault()?.CorrectedAmount??source.Total;
            var update=JsonSerializer.Deserialize<Sale>(JsonSerializer.Serialize(source))!;
            update.TotalValue=correctedAmount??effective;
            if(!string.IsNullOrWhiteSpace(order.PaymentCorrectionType))update.PaymentType=order.PaymentCorrectionType;
            FinancialOperations.EditSale(data,source,update,correctionReason,context);
            Guid? sessionId=data.Sessions.First(x=>x.Sales.Any(s=>s.Id==saleId)).Id;
            order.PaymentCorrectionType="";
            if (sessionId is Guid correctedSession && !data.Sessions.First(x => x.Id == correctedSession).IsOpen) changedClosed.Add(correctedSession);
            if (old != null) order.Payments = old.Payments.Select(x => JsonSerializer.Deserialize<OrderPayment>(JsonSerializer.Serialize(x))!).ToList();
        }
        // Pedido novo recebe o número antes dos pagamentos, para a parcela e a auditoria não saírem como "pedido #0".
        if(old==null&&order.Number<=0)order.Number=data.ScheduledOrders.Select(x=>x.Number).DefaultIfEmpty(0).Max()+1;
        if(order.LinkSaleIds.Count>0){LinkSales(data,order,order.LinkSaleIds,context);order.LinkSaleIds=[];}
        if(order.PendingPayments.Count>0)
        {
            if(order.PricePending)throw new InvalidOperationException("Defina o total antes de receber.");
            AddPayments(data,order,order.PendingPayments,DateTime.Now,context);
            order.PendingPayments=[];
        }
        var needsDeposit = order.DepositPaid && order.DepositSaleId == null;
        if (needsDeposit)
        {
            if (order.DepositAmount <= 0 || (!order.PricePending && order.DepositAmount > order.RemainingAmount))
                throw new InvalidOperationException("Confira o valor recebido como sinal.");
            var payment = AddPayment(data, order, order.DepositAmount, order.DepositPaymentType, DateTime.Now, "Sinal", Guid.NewGuid(), context);
            order.DepositSaleId = payment.SaleId;
        }
        var requestedPaid = draft.FullyPaid && (old == null || !old.FullyPaid);
        if (requestedPaid && order.PricePending) throw new InvalidOperationException("Defina o valor antes de marcar o pedido como quitado.");
        if (requestedPaid && order.RemainingAmount > 0)
            AddPayment(data, order, order.RemainingAmount, order.FinalPaymentType, draft.PaymentReceivedAt ?? DateTime.Now, "Pagamento final", Guid.NewGuid(), context);
        if (order.PricePending && order.Stage == "Entregue") order.PayAfterDelivery = true;
        if (order.Stage == "Entregue" && order.RemainingAmount > 0) order.PayAfterDelivery = true;
        var wasWaitingForApproval = old!=null&&OrderStages.BeforeApproval(old.Stage);
        var isApprovedOrLater = OrderStages.ApprovedOrLater(order.Stage);
        if (wasWaitingForApproval && isApprovedOrLater)
            order.ResetProductionDeadline();
        if (OrderStages.BeforeApproval(order.Stage))
            order.ResetProductionDeadline();
        if (OrderStages.ApprovedOrLater(order.Stage))
        {
            if (!order.DeadlineStarted) order.StartProductionDeadline(DateTime.Now);
        }
        if (OrderStages.ApprovedOrLater(order.Stage)) order.SentToProductionAt ??= DateTime.Now;
        if(order.ProductionBusinessDays==0)order.DueDate=order.DebtDate;
        order.Contact=PhoneText.Normalize(order.Contact);
        // Nome/telefone corrigidos no pedido também corrigem os pagamentos do caixa aberto (fechados ficam como histórico).
        if(old!=null&&(old.CustomerName!=order.CustomerName||old.Contact!=order.Contact))
        {
            var ids=order.Payments.Where(x=>x.SaleId.HasValue).Select(x=>x.SaleId!.Value).ToHashSet();
            foreach(var sale in data.Sessions.Where(x=>x.IsOpen).SelectMany(x=>x.Sales).Where(x=>ids.Contains(x.Id))){sale.CustomerName=order.CustomerName;sale.Contact=order.Contact;}
        }
        RefreshPaymentState(order);
        order.Revision = (old?.Revision ?? 0) + 1;
        if (old == null) data.ScheduledOrders.Add(order);
        else data.ScheduledOrders[data.ScheduledOrders.IndexOf(old)] = order;
        AuditService.Record(data, context, old == null ? "Pedido agendado" : "Pedido atualizado", "Pedido", order.Id.ToString(), old?.Stage ?? "", order.Stage, correctionReason, $"{order.CustomerName} — etapa {order.Stage}" + (order.PricePending ? " — valor a definir" : $" — {order.OrderTotal:C2}"));
        // Pagamento novo lançado num fechamento retificado (data passada): avisa a tela para atualizar o fechamento.
        var newSales = order.Payments.Where(p => old == null || old.Payments.All(o => o.Id != p.Id)).Select(p => p.SaleId).ToHashSet();
        changedClosed.AddRange(data.Sessions.Where(x => !x.IsOpen && x.Sales.Any(s => newSales.Contains(s.Id))).Select(x => x.Id).Where(id => !changedClosed.Contains(id)));
        return changedClosed;
    }
    // Pagamento que já entrou no caixa como lançamento (venda feita antes do pedido): vira pagamento do pedido sem criar outro lançamento.
    public static void LinkSales(AppData data,ScheduledOrder order,IEnumerable<Guid> ids,OperationContext context)
    {
        context.Require(BusinessPermission.ReceivePayment);
        if(order.PricePending)throw new InvalidOperationException("Defina o valor total do pedido antes de usar o lançamento do caixa.");
        var free=ReceivableViews.FreeCashSales(data).ToDictionary(x=>x.Id);
        foreach(var id in ids.Distinct().Where(id=>order.Payments.All(x=>x.SaleId!=id)))
        {
            if(!free.TryGetValue(id,out var sale))throw new InvalidOperationException("Este lançamento não está mais livre no caixa aberto (já foi ligado a um pedido ou o caixa foi fechado). Atualize a tela.");
            var amount=ReceivableViews.EffectiveTotal(data,sale);var balance=order.RemainingAmount;
            if(amount>balance)throw new InvalidOperationException($"O lançamento de {amount:C2} é maior que o saldo do pedido ({balance:C2}). Confira o valor do pedido.");
            order.Payments.Add(new OrderPayment{Id=Guid.NewGuid(),SaleId=sale.Id,Amount=amount,PaymentType=sale.PaymentType,FeePercent=sale.FeePercent,PaidAt=sale.CreatedAt,Note="Lançamento do caixa"});
            order.FinalPaymentType=sale.PaymentType;RefreshPaymentState(order);
            if(order.FullyPaid){order.FinalSaleId=sale.Id;order.PaymentReceivedAt=sale.CreatedAt;order.PayAfterDelivery=false;}
            order.Revision++;
            AuditService.Record(data,context,"Lançamento ligado ao pedido","Pedido",order.Id.ToString(),balance.ToString("0.00"),order.RemainingAmount.ToString("0.00"),"",$"Pedido #{order.Number} — {amount:C2} {sale.PaymentType} já lançado no caixa em {sale.CreatedAt:dd/MM HH:mm}; nenhum lançamento novo",sale.Id);
        }
    }
    // Fechamento do dia do pagamento (só entre os 5 mais recentes, que podem ser retificados) para Pix/cartão com data anterior ao caixa aberto.
    static CashSession? PastSession(AppData data, CashSession open, DateTime paidAt, string type, OperationContext context)
    {
        if (type == "Dinheiro" || paidAt.Date >= open.OpenedAt.Date || !context.Has(BusinessPermission.RectifyCash) || context.Permissions?.GetValueOrDefault("view.closed") == false) return null;
        return data.Sessions.Where(x => !x.IsOpen).OrderByDescending(x => x.ClosedAt).Take(5).Where(x => x.OpenedAt.Date == paidAt.Date).OrderByDescending(x => x.ClosedAt).FirstOrDefault();
    }
    public static OrderPayment AddPayment(AppData data, ScheduledOrder order, decimal amount, string type, DateTime paidAt, string note, Guid requestId, OperationContext context, long? expectedRevision = null, decimal? feePercent=null,bool excessConfirmed=false,string excessKind="")
    {
        context.Require(BusinessPermission.ReceivePayment);
        if (expectedRevision.HasValue && order.Revision != expectedRevision.Value)
            throw new InvalidOperationException("O pedido foi alterado em outro computador. Atualize a tela antes de registrar o pagamento.");
        var repeated = order.Payments.FirstOrDefault(x => x.Id == requestId);
        if (repeated != null){if(repeated.Amount!=amount||repeated.PaymentType!=type)throw new InvalidOperationException("Identificador de pagamento já utilizado com outro conteúdo.");return repeated;}
        if (order.PricePending) throw new InvalidOperationException("Defina o valor total do pedido antes de registrar este pagamento.");
        if(decimal.Round(amount,2)!=amount)throw new InvalidOperationException("Informe o pagamento com até duas casas decimais.");
        if (amount <= 0) throw new InvalidOperationException("Informe um pagamento maior que zero.");
        var balance = order.RemainingAmount;
        var excess=Math.Max(0,amount-balance);
        if(excess>0&&!excessConfirmed)throw new InvalidOperationException("Pagamento superior ao saldo. Confirme explicitamente o valor acima do saldo antes de receber.");
        if(excess>0&&excessKind!=PaymentPolicy.CollectionSurcharge&&excessKind!=PaymentPolicy.CustomerExtra)throw new InvalidOperationException("Confira a classificação do excedente.");
        var cash = data.Sessions.LastOrDefault(x => x.IsOpen) ?? throw new InvalidOperationException("Abra o caixa antes de registrar um pagamento.");
        var sale = new Sale { Id = requestId, CreatedAt = paidAt, CustomerName = order.CustomerName, Contact = order.Contact, Product = $"{note} do pedido #{order.Number}: {order.ItemsDescription}", Units = 1, PaymentType = type, FeePercent=PaymentPolicy.Rate(type), TotalValue = amount };
        if (data.Sessions.SelectMany(x => x.Sales).Any(x => x.Id == requestId)) throw new InvalidOperationException("Este pagamento já foi registrado.");
        // Pagamento com data de um dia já fechado (ex.: recebido 15/10, registrado 17/10): Pix e cartão entram no fechamento daquele dia
        // (retificação com versão nova e motivo automático); dinheiro fica no caixa aberto, porque a nota entrou na gaveta hoje.
        var past = PastSession(data, cash, paidAt, type, context);
        if (past != null) { sale.CreatedAt = paidAt < past.OpenedAt ? past.OpenedAt.AddMinutes(1) : past.ClosedAt is DateTime closedAt && paidAt > closedAt ? closedAt.AddMinutes(-1) : paidAt; FinancialOperations.AddSale(data, sale, context, past.Id, $"Pagamento do pedido #{order.Number} recebido em {paidAt:dd/MM/yyyy} e registrado em {DateTime.Now:dd/MM/yyyy}: lançado no fechamento do dia do pagamento."); }
        else cash.Sales.Add(sale);
        var payment = new OrderPayment { ExcessAmount=excess,ExcessConfirmed=excess>0&&excessConfirmed,ExcessKind=excess>0?excessKind:"",OperationId=requestId,Id = requestId, SaleId = sale.Id, Amount = amount, FeePercent=sale.FeePercent, PaymentType = type, PaidAt = paidAt, Note = note };
        order.Payments.Add(payment); order.FinalPaymentType = type;
        RefreshPaymentState(order);
        if (order.FullyPaid) { order.FinalSaleId = sale.Id; order.PaymentReceivedAt = paidAt; order.PayAfterDelivery = false; }
        order.Revision++;
        AuditService.Record(data, context, "Pagamento lançado", "Pedido", order.Id.ToString(), balance.ToString("0.00"), order.RemainingAmount.ToString("0.00"), "", $"Pedido #{order.Number} — bruto {amount:C2}; base aplicada {payment.AppliedAmount:C2}; excedente {excess:C2}; tipo {payment.ExcessKind}; forma {type}; taxa {payment.FeePercent}% / {payment.EstimatedFee:C2}; líquido {amount-payment.EstimatedFee:C2}; operationId {payment.OperationId}", sale.Id);
        return payment;
    }
    public static void AdvanceStage(AppData data, Guid orderId, long expectedRevision, string nextStage, OperationContext context, bool keepReceivable, decimal? paymentAmount = null, string paymentType = "PIX", DateTime? paidAt = null, Guid? requestId = null)
    {
        var order=data.ScheduledOrders.FirstOrDefault(x=>x.Id==orderId)??throw new InvalidOperationException("O pedido não existe mais.");
        if(order.Revision!=expectedRevision)throw new InvalidOperationException("O pedido foi alterado em outro computador. Atualize a lista antes de continuar.");
        if(order.IsUncollected)throw new InvalidOperationException("Restaure este pedido antes de avançar a etapa.");
        var previous=order.Stage;
        if(nextStage=="Entregue" && order.RemainingAmount>0)
        {
            if(paymentAmount.HasValue) AddPayment(data,order,paymentAmount.Value,paymentType,paidAt??DateTime.Now,"Pagamento na entrega",requestId??Guid.NewGuid(),context,expectedRevision);
            if(order.RemainingAmount>0 && !keepReceivable)throw new InvalidOperationException("O pedido ainda possui saldo. Registre o pagamento ou mantenha o saldo em Contas a Receber.");
            order.PayAfterDelivery=order.RemainingAmount>0;
        }
        order.Stage=nextStage;
        order.ReadyAt=order.Stage==OrderStages.Ready?(previous==OrderStages.Ready?order.ReadyAt??DateTime.Now:DateTime.Now):null;
        // A aprovação agora é a entrada em Produção: é nesse momento que o prazo de produção começa a contar (antes era a etapa "Aprovado").
        if(order.Stage==OrderStages.Production&&OrderStages.BeforeApproval(previous)){order.ResetProductionDeadline();order.StartProductionDeadline(DateTime.Now);}
        if(order.Stage=="Produção"&&order.SentToProductionAt==null)order.SentToProductionAt=DateTime.Now;
        order.Revision++;
        AuditService.Record(data,context,"Etapa do pedido alterada","Pedido",order.Id.ToString(),previous,order.Stage,"",$"Pedido #{order.Number}: {previous} → {order.Stage}");
    }
    public static void MarkPdfGenerated(AppData data, IEnumerable<Guid> ids, DateTime generatedAt)
    {
        foreach (var order in data.ScheduledOrders.Where(x => ids.Contains(x.Id)))
        // Não muda a revisão: gerar a ficha não é edição do pedido. Antes, o editor aberto ficava "desatualizado" e o
        // 2º PDF (ou Salvar alterações) era recusado. Ao salvar, o servidor mantém o PdfGeneratedAt gravado (OrderOperations.Save).
        { order.PdfGeneratedAt = generatedAt; AuditService.Record(data, OperationContext.Administrator("Sistema"), "PDF do pedido gerado", "Pedido", order.Id.ToString(), "", generatedAt.ToString("O"), "", $"Pedido #{order.Number}"); }
    }
}
