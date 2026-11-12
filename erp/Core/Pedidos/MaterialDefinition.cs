using System;
using System.Collections.Generic;

namespace GraficaCaixa;

public sealed class MaterialDefinition
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "";

	public MaterialCostMode CostMode { get; set; }

	public decimal? Cost { get; set; }

	public decimal Width { get; set; }

	public decimal Height { get; set; }

	public decimal Margin { get; set; }

	public decimal Spacing { get; set; }

	public List<string> ValidTags { get; set; } = new List<string>();
}
