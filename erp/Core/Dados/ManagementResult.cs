namespace GraficaCaixa;

public sealed record ManagementResult(decimal Sales, decimal DirectCosts, decimal GrossProfit, decimal OperatingExpenses, decimal Losses, decimal Result, bool IsComplete, decimal Rework = 0);
