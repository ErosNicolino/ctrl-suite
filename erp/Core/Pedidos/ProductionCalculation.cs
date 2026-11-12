namespace GraficaCaixa;

public sealed record ProductionCalculation(int Required, int Capacity, int TechnicalWaste, decimal UnitsOfMaterial, decimal? MaterialCost, bool UsedKnownYield, bool Rotated, bool IsComplete, string Description)
{
    public int? PerSheet { get; init; }
    public int? Columns { get; init; }
    public int? Rows { get; init; }
    public decimal? UsefulWidth { get; init; }
    public decimal? UsefulHeight { get; init; }
    public decimal? Gap { get; init; }
    public decimal? OccupiedWidth { get; init; }
    public decimal? OccupiedHeight { get; init; }
}
