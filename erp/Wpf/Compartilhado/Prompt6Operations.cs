using System.Text.Json;
namespace GraficaCaixa;
public static partial class FinancialOperations
{
    static void RequireRecentClosed(AppData data,CashSession session)
    {
        if(!session.IsOpen && !data.Sessions.Where(x=>!x.IsOpen).OrderByDescending(x=>x.ClosedAt).Take(5).Any(x=>x.Id==session.Id))
            throw new InvalidOperationException("Somente os cinco fechamentos mais recentes podem ser retificados.");
    }
    static Sale AddItemSale(AppData data,Sale draft,OperationContext context,Guid? closedSessionId,string reason)
    {
        context.Require(BusinessPermission.ReceivePayment);
        var repeated=data.Sessions.SelectMany(x=>x.Sales).FirstOrDefault(x=>x.PaymentGroupId==draft.Id);
        if(repeated!=null)return repeated;
        if(data.Sessions.SelectMany(x=>x.Sales).Any(x=>x.Id==draft.Id))throw new InvalidOperationException("Identificador de lançamento já utilizado.");
        if(data.ManualDebts.Any(x=>x.Id==draft.Id))return draft;
        if(draft.Items.Count==0 || draft.Items.Any(x=>x.Quantity<=0 || x.UnitPrice is null or <0 || string.IsNullOrWhiteSpace(x.Product)))throw new InvalidOperationException("Confira quantidade, descrição e preço dos itens.");
        var total=draft.Items.Sum(x=>x.LineTotal!.Value);
        if(total<=0)throw new InvalidOperationException("O total dos itens deve ser maior que zero.");
        PaymentPolicy.Validate(draft.PendingPayments,total,allowExcess:true);
        decimal surcharge=0,extra=0,running=total;
        foreach(var part in draft.PendingPayments)
        {
            var excess=part.Amount-Math.Min(part.Amount,running);running=Math.Max(0,running-part.Amount);if(excess==0)continue;
            if(!part.ExcessConfirmed||part.ExcessKind!=PaymentPolicy.CollectionSurcharge&&part.ExcessKind!=PaymentPolicy.CustomerExtra)throw new InvalidOperationException("Confirme e classifique o excedente do recebimento.");
            if(part.ExcessKind==PaymentPolicy.CollectionSurcharge)surcharge+=excess;else extra+=excess;
        }
        if(closedSessionId.HasValue){context.Require(BusinessPermission.RectifyCash);RequireReason(reason);RequireRecentClosed(data,RequireClosedSession(data,closedSessionId.Value));}
        else if(!data.Sessions.Any(x=>x.IsOpen))throw new InvalidOperationException("Abra o caixa antes de registrar o lançamento.");
        if(draft.PendingPayments.Any(p=>data.Sessions.SelectMany(x=>x.Sales).Any(x=>x.Id==p.Id)))throw new InvalidOperationException("Pagamento já registrado.");
        var debt=new ManualDebt{Id=draft.Id,CreatedAt=draft.CreatedAt,OriginDate=draft.CreatedAt.Date,CustomerName=draft.CustomerName,Contact=PhoneText.Normalize(draft.Contact),Description=draft.Product,Notes=draft.Notes,Items=JsonSerializer.Deserialize<List<OrderItem>>(JsonSerializer.Serialize(draft.Items))!,Amount=total,CollectionSurcharge=surcharge>0?surcharge:null,CustomerExtra=extra>0?extra:null};
        var index=0;Sale? first=null;
        foreach(var part in draft.PendingPayments)
        {
            var sale=JsonSerializer.Deserialize<Sale>(JsonSerializer.Serialize(draft))!;
            sale.Id=index++==0?draft.Id:part.Id;sale.PaymentGroupId=draft.Id;sale.ItemsTotal=total;sale.PendingPayments=[];
            sale.CreatedAt=part.PaidAt??draft.CreatedAt;sale.TotalValue=part.Amount;sale.PaymentType=part.PaymentType;sale.FeePercent=part.FeePercent??PaymentPolicy.Rate(part.PaymentType);
            AddSale(data,sale,context,closedSessionId,reason);debt.SaleIds.Add(sale.Id);first??=sale;
        }
        data.ManualDebts.Add(debt);
        AuditService.Record(data,context,"Itens e pagamentos registrados","Lançamento",draft.Id.ToString(),"",JsonSerializer.Serialize(new{draft.Items,Payments=draft.PendingPayments,Total=total,Saldo=debt.Remaining(data),Excedente=debt.Excess}),reason,draft.Product,first?.Id);
        return first??draft;
    }
    public static void EditSale(AppData data,Sale expected,Sale draft,string reason,OperationContext context)
    {
        var session=data.Sessions.FirstOrDefault(x=>x.Sales.Any(s=>s.Id==expected.Id))??throw new InvalidOperationException("Lançamento removido.");
        var sale=session.Sales.Single(x=>x.Id==expected.Id);DataRelations.RequireUnchanged(sale,expected);
        var debt=data.ManualDebts.FirstOrDefault(x=>x.SaleIds.Contains(sale.Id)&&x.Id==sale.PaymentGroupId);
        if(draft.Id!=sale.Id || draft.CreatedAt!=sale.CreatedAt || draft.UnitValue!=sale.UnitValue || draft.PaymentGroupId!=sale.PaymentGroupId ||
            !draft.Items.Select(x=>(x.Id,x.ProductId)).SequenceEqual(sale.Items.Select(x=>(x.Id,x.ProductId))))
            throw new InvalidOperationException("A identidade e a data do lançamento estão protegidas.");
        var itemValuesChanged=!draft.Items.Select(x=>(x.Quantity,x.UnitPrice)).SequenceEqual(sale.Items.Select(x=>(x.Quantity,x.UnitPrice)));
        if(debt!=null&&!itemValuesChanged&&(draft.Units!=sale.Units||draft.ItemsTotal!=sale.ItemsTotal))
            throw new InvalidOperationException("Os totais dos itens não correspondem ao lançamento original.");
        if((itemValuesChanged||draft.Units!=sale.Units||draft.ItemsTotal!=sale.ItemsTotal)&&debt==null)
            throw new InvalidOperationException("Quantidades e preços históricos sem vínculo de itens não podem ser alterados nesta tela.");
        if(debt!=null&&itemValuesChanged&&
            (draft.Items.Any(x=>x.Quantity<=0||x.UnitPrice is null or <0)||draft.Units!=draft.Items.Sum(x=>x.Quantity)||draft.ItemsTotal!=draft.Items.Sum(x=>x.LineTotal!.Value)))
            throw new InvalidOperationException("Confira quantidade, valor unitário e subtotal dos itens.");
        var typeChanged=sale.PaymentType!=draft.PaymentType;
        var effective=data.FinancialCorrections.Where(x=>x.OriginalSaleId==sale.Id).OrderBy(x=>x.CreatedAt).LastOrDefault()?.CorrectedAmount??sale.Total;
        var valueChanged=draft.Total!=effective;
        var newItemsTotal=debt!=null&&itemValuesChanged?draft.ItemsTotal!.Value:debt?.Amount??0;
        if(debt!=null&&itemValuesChanged&&newItemsTotal<=0)throw new InvalidOperationException("O total dos itens deve ser maior que zero.");
        if(debt!=null&&itemValuesChanged&&newItemsTotal+debt.Excess<debt.Paid(data)+(draft.Total-effective))
            throw new InvalidOperationException("O novo total dos itens ficaria abaixo do valor já recebido.");
        if((typeChanged||valueChanged)&&data.FinancialCorrections.Any(x=>x.AdjustmentSaleId==sale.Id))throw new InvalidOperationException("Corrija a linha original; esta linha é um ajuste vinculado.");
        if(typeChanged||valueChanged||itemValuesChanged){context.Require(BusinessPermission.CorrectFinancialEntry);RequireReason(reason);}
        if(typeChanged&&!PaymentPolicy.Rates.ContainsKey(draft.PaymentType))throw new InvalidOperationException("Forma de pagamento inválida.");
        var affectedSessions=itemValuesChanged&&debt!=null?data.Sessions.Where(x=>x.Sales.Any(y=>y.PaymentGroupId==debt.Id)).ToList():[session];
        foreach(var affected in affectedSessions.Where(x=>!x.IsOpen&&(typeChanged||valueChanged||itemValuesChanged)))
        {context.Require(BusinessPermission.RectifyCash);RequireReason(reason);RequireRecentClosed(data,affected);EnsureOriginalVersion(data,affected);}
        var before=JsonSerializer.Serialize(sale);
        sale.CustomerName=draft.CustomerName;sale.Contact=PhoneText.Normalize(draft.Contact);sale.Product=draft.Product;sale.Notes=draft.Notes;
        for(var i=0;i<sale.Items.Count;i++)sale.Items[i].Product=draft.Items[i].Product;
        if(debt!=null)
        {
            debt.CustomerName=draft.CustomerName;debt.Contact=PhoneText.Normalize(draft.Contact);
            if(itemValuesChanged)
            {
                var beforeDebt=JsonSerializer.Serialize(new{debt.Items,debt.Amount});
                debt.Items=JsonSerializer.Deserialize<List<OrderItem>>(JsonSerializer.Serialize(draft.Items))!;debt.Amount=newItemsTotal;debt.TrimExcess(debt.Paid(data));
                foreach(var related in data.Sessions.SelectMany(x=>x.Sales).Where(x=>x.PaymentGroupId==debt.Id))
                {related.Items=JsonSerializer.Deserialize<List<OrderItem>>(JsonSerializer.Serialize(draft.Items))!;related.Units=draft.Units;related.ItemsTotal=newItemsTotal;}
                AuditService.Record(data,context,"Itens do lançamento corrigidos","Lançamento",sale.Id.ToString(),beforeDebt,JsonSerializer.Serialize(new{debt.Items,debt.Amount}),reason,$"Total dos itens: {newItemsTotal:C2}; recebido: {debt.Paid(data):C2}",sale.Id);
            }
        }
        if(typeChanged)
        {
            var ids=data.FinancialCorrections.Where(x=>x.OriginalSaleId==sale.Id).Select(x=>x.AdjustmentSaleId).Append(sale.Id).ToHashSet();
            foreach(var movement in session.Sales.Where(x=>ids.Contains(x.Id))){movement.PaymentType=draft.PaymentType;movement.FeePercent=PaymentPolicy.Rate(draft.PaymentType);}
            foreach(var order in data.ScheduledOrders.Where(o=>o.Payments.Any(p=>p.SaleId is Guid id && ids.Contains(id))))
            {
                foreach(var p in order.Payments.Where(p=>p.SaleId is Guid id&&ids.Contains(id))){p.PaymentType=draft.PaymentType;p.FeePercent=PaymentPolicy.Rate(draft.PaymentType);}
                if(order.DepositSaleId is Guid dep&&ids.Contains(dep))order.DepositPaymentType=draft.PaymentType;
                if(order.FinalSaleId is Guid fin&&ids.Contains(fin))order.FinalPaymentType=draft.PaymentType;
                order.Revision++;
            }
        }
        var after=JsonSerializer.Serialize(sale);
        if(before!=after)AuditService.Record(data,context,typeChanged?"Forma de pagamento corrigida":"Informações do lançamento corrigidas","Lançamento",sale.Id.ToString(),before,after,reason,$"{expected.PaymentType} → {draft.PaymentType}; valores originais preservados",sale.Id);
        if(valueChanged)CorrectSale(data,sale.Id,draft.Total,reason,context);
        foreach(var affected in affectedSessions.Where(x=>!x.IsOpen&&(itemValuesChanged||typeChanged&&before!=after)&&(!valueChanged||x.Id!=session.Id)))AddRectifiedVersion(data,affected,context,reason);
    }
}
public static partial class OrderOperations
{
    public static void AddPayments(AppData data,ScheduledOrder order,IEnumerable<PaymentPart> payments,DateTime at,OperationContext context)
    {
        context.Require(BusinessPermission.ReceivePayment);
        var parts=payments.Where(x=>!order.Payments.Any(p=>p.Id==x.Id)).ToList();
        PaymentPolicy.Validate(parts,order.RemainingAmount,allowExcess:true);
        if(order.PricePending)throw new InvalidOperationException("Defina o valor total do pedido antes de receber.");
        if(!data.Sessions.Any(x=>x.IsOpen))throw new InvalidOperationException("Abra o caixa antes de registrar o pagamento.");
        if(parts.Any(p=>data.Sessions.SelectMany(x=>x.Sales).Any(x=>x.Id==p.Id)))throw new InvalidOperationException("Pagamento já registrado em outro lançamento.");
        var remaining=order.RemainingAmount;foreach(var part in parts){if(part.Amount>remaining&&(!part.ExcessConfirmed||part.ExcessKind!=PaymentPolicy.CollectionSurcharge&&part.ExcessKind!=PaymentPolicy.CustomerExtra))throw new InvalidOperationException("Confirme e classifique o excedente do recebimento.");remaining=Math.Max(0,remaining-part.Amount);}
        foreach(var part in parts)AddPayment(data,order,part.Amount,part.PaymentType,part.PaidAt??at,"Parcela",part.Id,context,feePercent:part.FeePercent,excessConfirmed:part.ExcessConfirmed,excessKind:part.ExcessKind);
    }
}
