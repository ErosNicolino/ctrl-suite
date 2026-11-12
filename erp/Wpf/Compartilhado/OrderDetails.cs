namespace GraficaCaixa;
public sealed class OrderDetails
{
    public Guid? CustomerId { get; set; }
    public bool ItemCostsEnabled { get; set; }
    public bool IgnoreCosts { get; set; }
    public DateTime? DebtOriginDate { get; set; }
    public DateTime? ExpectedPaymentDate { get; set; }
    public Dictionary<Guid,OrderPayment>? PaymentSnapshots { get; set; }
    public Dictionary<Guid,OrderItem> ItemSnapshots { get; set; } = [];
    public decimal? OriginalTotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal? FixedDiscount { get; set; }
    public bool UseItemPrices { get; set; }
    public decimal? FreightCost { get; set; }
    public decimal? AdditionalCost { get; set; }
    public string CostNotes { get; set; } = "";
    public DateTime? ReadyAt { get; set; }
    public Dictionary<Guid,decimal?> UnitPrices { get; set; } = [];
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = "";
    public string CancelledBy { get; set; } = "";
    public OrderDetails() {}
    public OrderDetails(ScheduledOrder x)
    {
        CustomerId=x.CustomerId;PaymentSnapshots=x.Payments.ToDictionary(p=>p.Id,p=>p);ItemCostsEnabled=x.ItemCostsEnabled;IgnoreCosts=x.IgnoreCosts;DebtOriginDate=x.DebtOriginDate;ExpectedPaymentDate=x.ExpectedPaymentDate;ItemSnapshots=x.Items.ToDictionary(i=>i.Id,i=>i);
        OriginalTotal=x.OriginalTotal;DiscountPercent=x.DiscountPercent;FixedDiscount=x.FixedDiscount;UseItemPrices=x.UseItemPrices;FreightCost=x.FreightCost;
        AdditionalCost=x.AdditionalCost;CostNotes=x.CostNotes;ReadyAt=x.ReadyAt;CancelledAt=x.CancelledAt;CancelReason=x.CancelReason;CancelledBy=x.CancelledBy;UnitPrices=x.Items.ToDictionary(i=>i.Id,i=>i.UnitPrice);
    }
    public void ApplyPayments(ScheduledOrder x){if(PaymentSnapshots==null)return;foreach(var p in x.Payments)if(PaymentSnapshots.TryGetValue(p.Id,out var saved)){p.ExcessAmount=saved.ExcessAmount;p.ExcessKind=saved.ExcessKind;p.ExcessConfirmed=saved.ExcessConfirmed;p.OperationId=saved.OperationId;p.FeePercent=saved.FeePercent;}}
    public void Apply(ScheduledOrder x)
    {
        x.CustomerId=CustomerId;x.ItemCostsEnabled=ItemCostsEnabled;x.IgnoreCosts=IgnoreCosts;x.DebtOriginDate=DebtOriginDate;x.ExpectedPaymentDate=ExpectedPaymentDate;
        foreach(var item in x.Items)if(ItemSnapshots.TryGetValue(item.Id,out var saved)){item.ProductId=saved.ProductId;item.Unit=saved.Unit;item.UnitCost=saved.UnitCost;item.EnteredTotalCost=saved.EnteredTotalCost;item.CostReviewed=saved.CostReviewed;item.CostIgnored=saved.CostIgnored;item.CostSource=saved.CostSource;}
        x.OriginalTotal=OriginalTotal;x.DiscountPercent=DiscountPercent;x.FixedDiscount=FixedDiscount;x.UseItemPrices=UseItemPrices;x.FreightCost=FreightCost;
        x.AdditionalCost=AdditionalCost;x.CostNotes=CostNotes;x.ReadyAt=ReadyAt;x.CancelledAt=CancelledAt;x.CancelReason=CancelReason??"";x.CancelledBy=CancelledBy??"";
        foreach(var item in x.Items)if(UnitPrices.TryGetValue(item.Id,out var price))item.UnitPrice=price;
    }
}
