using System.Text.Json;
namespace GraficaCaixa;
public sealed partial class DataStore
{
    public Task<object> MobileReceivePreviewAsync(UserIdentity actor,Guid id,MobileReceiveCommand command,CancellationToken ct)
        =>Task.Run<object>(()=>WithLock(()=>{ct.ThrowIfCancellationRequested();using var db=Open();var p=Permissions.Resolve(db,actor);MobileAccess.Financial(p);MobileAccess.Require(p,"view.cash");var data=ReadUnsafe();var order=MobileFind(data,id,command.ExpectedRevision);try{MobileOrderPayments.Receive(data,order,command.Payments,new(actor.Name,actor.Role){Permissions=p},true,command.ExcessKind??"");}catch(InvalidOperationException ex){throw new WebRequestException(422,ex.Message);}return (object)new{quote=MobileOrderDrafts.Quote(order,data),excess=order.Payments.Where(x=>command.Payments.Any(y=>y.Id==x.Id)).Sum(x=>x.ExcessAmount),excessMinAmount=BusinessRules.Current.ExcessMinAmount,excessMinPercent=BusinessRules.Current.ExcessMinPercent,balanceBefore=Math.Max(0,order.OrderTotal-order.Payments.Where(x=>!command.Payments.Any(y=>y.Id==x.Id)&&x.SaleId!=null).Sum(x=>x.AppliedAmount)),newPayments=order.Payments.Where(x=>command.Payments.Any(y=>y.Id==x.Id)).Select(x=>new MobileReceiptPayment(x.Id,x.PaidAt,x.PaymentType,x.Amount,x.FeePercent??0,x.EstimatedFee,x.Amount-x.EstimatedFee,x.ExcessAmount,x.ExcessKind))};}),ct);
    public Task<MobileQuote> MobilePreviewAsync(UserIdentity actor,MobileOrderCommand command,Guid? id,CancellationToken ct)
        =>Task.Run(()=>WithLock(()=>{ct.ThrowIfCancellationRequested();using var db=Open();var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,"view.orders",id.HasValue?"orders.edit":"orders.create","orders.value");if(id.HasValue)MobileAccess.Financial(p);var data=ReadUnsafe();var old=id.HasValue?MobileFind(data,id.Value,command.ExpectedRevision):null;var draft=MobileOrderDrafts.Build(data,command.Draft,p,old,preview:true);try{OrderOperations.Save(data,draft,new(actor.Name,actor.Role){Permissions=p},correctionReason:command.Draft.Reason);var saved=data.ScheduledOrders.Single(x=>x.Id==draft.Id);if(command.Draft.InitialPayments?.Count>0){MobileAccess.Financial(p);MobileAccess.Require(p,"view.cash");MobileOrderPayments.Receive(data,saved,command.Draft.InitialPayments,new(actor.Name,actor.Role){Permissions=p},true);}return MobileOrderDrafts.Quote(saved,data);}catch(InvalidOperationException ex){throw new WebRequestException(422,ex.Message);}}),ct);
    public Task<MobileResult> MobileSaveAsync(UserIdentity actor,MobileOrderCommand command,Guid? id,CancellationToken ct)
        =>MobileExecute(actor,command.OperationId,id,"order",command,ct,(data,p)=>{
            MobileAccess.Require(p,id.HasValue?"orders.edit":"orders.create","orders.value");
            var old=id.HasValue?MobileFind(data,id.Value,command.ExpectedRevision):null;
            if(!id.HasValue&&command.ExpectedRevision!=null)throw new WebRequestException(422,"Novo pedido não aceita revisão de outro pedido.");
            var draft=MobileOrderDrafts.Build(data,command.Draft,p,old);var context=new OperationContext(actor.Name,actor.Role){Permissions=p};
            OrderOperations.Save(data,draft,context,correctionReason:command.Draft.Reason);var saved=data.ScheduledOrders.Single(x=>x.Id==draft.Id);
            if(command.Draft.InitialPayments?.Count>0){MobileAccess.Financial(p);MobileAccess.Require(p,"view.cash");MobileOrderPayments.Receive(data,saved,command.Draft.InitialPayments,context);}return saved;
        });
    public Task<MobileResult> MobileReceiveAsync(UserIdentity actor,Guid id,MobileReceiveCommand command,CancellationToken ct)
        =>MobileExecute(actor,command.OperationId,id,"receive",command,ct,(data,p)=>{MobileAccess.Financial(p);MobileAccess.Require(p,"view.cash");var order=MobileFind(data,id,command.ExpectedRevision);MobileOrderPayments.Receive(data,order,command.Payments,new(actor.Name,actor.Role){Permissions=p},command.ExcessConfirmed,command.ExcessKind??"");return order;});
    public Task<MobileResult> MobileDeliverAsync(UserIdentity actor,Guid id,MobileDeliverCommand command,CancellationToken ct)
        =>MobileExecute(actor,command.OperationId,id,"deliver",command,ct,(data,p)=>{MobileAccess.Financial(p);MobileAccess.Require(p,"orders.deliver");var order=MobileFind(data,id,command.ExpectedRevision);MobileOrderPayments.Deliver(data,order,command.KeepReceivable,new(actor.Name,actor.Role){Permissions=p});return order;});
    Task<MobileResult> MobileExecute<T>(UserIdentity actor,Guid op,Guid? id,string kind,T command,CancellationToken ct,Func<AppData,Dictionary<string,bool>,ScheduledOrder> mutate)
        =>Task.Run(()=>WithLock(()=>{
            ct.ThrowIfCancellationRequested();if(op==Guid.Empty)throw new WebRequestException(400,"operationId obrigatório.");
            using var db=Open();var p=Permissions.Resolve(db,actor);MobileAccess.Require(p,"view.orders",kind=="receive"?"finance.receive":kind=="deliver"?"orders.deliver":id.HasValue?"orders.edit":"orders.create","orders.value");
            if(kind=="order")MobileAccess.Require(p,"view.customers","view.products");
            if(command is MobileOrderCommand orderCommand){if(orderCommand.Draft==null)throw new WebRequestException(422,"Informe os dados do pedido.");if(id.HasValue)MobileAccess.Financial(p);if(!id.HasValue&&orderCommand.Draft.Customer?.Id==null)MobileAccess.Require(p,"customers.edit");if(orderCommand.Draft.InitialPayments?.Count>0){MobileAccess.Financial(p);MobileAccess.Require(p,"view.cash");}}
            if(kind is "receive" or "deliver")MobileAccess.Financial(p);if(kind=="receive")MobileAccess.Require(p,"view.cash");
            var hash=CustomerRevision.Hash(JsonSerializer.Serialize(new{contract="mobile-8d-v1",kind,id,command}));
            using(var receipt=WebQueries.Command(db,"SELECT actor_id,payload_hash FROM remote_operations WHERE id=@id",("@id",op)))using(var r=receipt.ExecuteReader())if(r.Read()){
                if(r.GetString(0)!=actor.UserId.ToString()||r.GetString(1)!=hash)throw new WebRequestException(409,"Identificador de operação já utilizado com outro conteúdo.");r.Close();
                var original=JsonSerializer.Deserialize<MobileResult>(WebQueries.Metadata(db,"mobile_result_"+op.ToString("N"),"null"))??throw new WebRequestException(409,"Resultado original indisponível. Confira o pedido.");return original with{Replayed=true};
            }
            var current=ReadForWrite();var next=ServerAuthorization.CloneForWrite(current);
            ScheduledOrder order;
            try{order=mutate(next,p);foreach(var payment in order.Payments.Where(x=>current.ScheduledOrders.SelectMany(o=>o.Payments).All(y=>y.Id!=x.Id)))payment.OperationId=op;ServerAuthorization.ValidateAndAudit(current,next,actor,p);next=Normalize(next);FinancialIntegrity.ValidateTransition(current,next);}
            catch(InvalidOperationException ex){throw new WebRequestException(422,ex.Message);}
            foreach(var entry in next.AuditTrail.Where(x=>current.AuditTrail.All(y=>y.Id!=x.Id)))entry.Computer="Browser / dispositivo "+actor.DeviceId;
            var result=new MobileResult(op,order.Id,order.Number,order.Revision,order.Stage,MobileOrderDrafts.Quote(order,next),order.Payments.Where(x=>x.SaleId.HasValue).Select(x=>new MobileReceiptPayment(x.Id,x.PaidAt,x.PaymentType,x.Amount,x.FeePercent??PaymentPolicy.HistoricalRate(x.PaymentType),x.EstimatedFee,x.Amount-x.EstimatedFee,x.ExcessAmount,x.ExcessKind)).ToArray(),DateTime.UtcNow);
            ct.ThrowIfCancellationRequested();CreateRoutineBackupUnsafe();SaveUnsafe(next,op,actor.UserId.ToString(),hash,JsonSerializer.Serialize(result));return result;
        }),ct);
    static ScheduledOrder MobileFind(AppData data,Guid id,long? revision){var o=data.ScheduledOrders.FirstOrDefault(x=>x.Id==id)??throw new WebRequestException(404,"Pedido não encontrado.");if(revision==null||revision<0)throw new WebRequestException(400,"expectedRevision obrigatório.");if(o.Revision!=revision)throw new WebRequestException(409,"Este pedido foi alterado em outro dispositivo.");return o;}
}
