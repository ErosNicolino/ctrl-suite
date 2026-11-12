namespace GraficaCaixa;

public sealed record CashFlowResult(decimal Receipts, decimal PaidExpenses, decimal PaidPurchases, decimal OtherOutflows, decimal Variation);
