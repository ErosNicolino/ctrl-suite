namespace GraficaCaixa;
public static class MobileOrderPayments
{
    public static void Receive(AppData data,ScheduledOrder order,List<MobilePayment>? input,OperationContext context,bool excessConfirmed=false,string excessKind="")
    {
        if(order.IsUncollected)throw new WebRequestException(422,"Pedido arquivado não pode receber pagamento pelo celular.");
        if(input==null||input.Count is <1 or >12||input.Any(x=>x==null||x.Id==Guid.Empty||x.PaymentType==null))throw new WebRequestException(422,"Informe uma ou mais formas de pagamento válidas.");
        var parts=input.Select(x=>new PaymentPart{Id=x.Id,Amount=x.Amount,PaymentType=x.PaymentType,FeePercent=PaymentPolicy.Rate(x.PaymentType),ExcessConfirmed=excessConfirmed||x.ExcessConfirmed,ExcessKind=string.IsNullOrEmpty(excessKind)?string.IsNullOrEmpty(x.ExcessKind)?PaymentPolicy.DefaultExcessKind(x.PaymentType):x.ExcessKind:excessKind}).ToList();
        if(parts.Select(x=>x.Id).Distinct().Count()!=parts.Count||parts.Any(x=>order.Payments.Any(y=>y.Id==x.Id)||data.Sessions.SelectMany(s=>s.Sales).Any(y=>y.Id==x.Id)))throw new WebRequestException(422,"Identificador de pagamento já utilizado. Confira os recebimentos antes de continuar.");
        OrderOperations.AddPayments(data,order,parts,DateTime.Now,context);
    }
    public static void Deliver(AppData data,ScheduledOrder order,bool keep,OperationContext context)
    {
        MobileAccess.Editable(order);
        if(order.PricePending&&!keep)throw new WebRequestException(422,"O preço está a definir. Confirme que o pedido permanecerá em contas a receber.");
        OrderOperations.AdvanceStage(data,order.Id,order.Revision,"Entregue",context,keep);
        if(order.PricePending)order.PayAfterDelivery=true;
    }
}
