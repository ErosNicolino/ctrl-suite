using System.Text.Json.Serialization;
namespace GraficaCaixa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MobileCustomer(Guid? Id,string Name="",string Contact="",string Email="");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MobileItem(Guid Id,Guid? ProductId,int Quantity,string Description="",string Unit="un.",bool ManualPrice=false,decimal? UnitPrice=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MobilePayment(Guid Id,string PaymentType,decimal Amount,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]bool ExcessConfirmed=false,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? ExcessKind=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MobileDraft(MobileCustomer Customer,List<MobileItem> Items,int BusinessDays=3,string Notes="",string Reason="",bool ManualTotal=false,decimal? Total=null,bool PricePending=false,List<MobilePayment>? InitialPayments=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MobileOrderCommand([property:JsonRequired]Guid OperationId,[property:JsonRequired]MobileDraft Draft,long? ExpectedRevision=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MobileReceiveCommand([property:JsonRequired]Guid OperationId,[property:JsonRequired]long ExpectedRevision,[property:JsonRequired]List<MobilePayment> Payments,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]bool ExcessConfirmed=false,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? ExcessKind=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MobileDeliverCommand([property:JsonRequired]Guid OperationId,[property:JsonRequired]long ExpectedRevision,[property:JsonRequired]bool KeepReceivable);
public sealed record MobilePricedItem(Guid Id,Guid? ProductId,string Description,string Unit,int Quantity,decimal? UnitPrice,decimal? Subtotal,string Rule);
public sealed record MobileQuote(IReadOnlyList<MobilePricedItem> Items,decimal? Total,bool PricePending,int BusinessDays,string Deadline,string Customer,string Contact,decimal Received,decimal? Remaining);
public sealed record MobileReceiptPayment(Guid Id,DateTime PaidAt,string PaymentType,decimal Amount,decimal FeePercent,decimal Fee,decimal Net,decimal ExcessAmount=0,string ExcessKind="");
public sealed record MobileResult(Guid OperationId,Guid OrderId,long Number,long Revision,string Stage,MobileQuote Quote,IReadOnlyList<MobileReceiptPayment> Payments,DateTime AppliedAt,bool Replayed=false);
public static class MobileAccess
{
    public static void Require(IReadOnlyDictionary<string,bool> p,params string[] keys){if(keys.Any(k=>!p.GetValueOrDefault(k)))throw new UnauthorizedAccessException();}
    public static void Financial(IReadOnlyDictionary<string,bool> p)=>Require(p,"view.orders","orders.value","finance.receive");
    public static void Editable(ScheduledOrder o){if(o.IsUncollected||o.Stage=="Entregue")throw new WebRequestException(422,"Pedido arquivado ou entregue não permite edição estrutural pelo celular.");}
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PaymentSuggestionRequest(decimal DesiredNet,string PaymentType,decimal Amount);
