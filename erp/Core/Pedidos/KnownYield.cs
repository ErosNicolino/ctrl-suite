using System;

namespace GraficaCaixa;

public sealed class KnownYield
{
	public Guid MaterialId { get; set; }

	public ProductionShape Shape { get; set; }

	public decimal Width { get; set; }

	public decimal Height { get; set; }

	public int PerSheet { get; set; }
}
