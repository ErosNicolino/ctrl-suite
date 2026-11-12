namespace GraficaCaixa;

public sealed record ProductCostReference(ProductCostKind Kind, decimal? TotalCost, decimal? UnitCost, int Quantity, string Label);
