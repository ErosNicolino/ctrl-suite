using System;

namespace GraficaCaixa;

public sealed class FiscalDocument
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public Guid OrderId { get; set; }

	public string Number { get; set; } = "";

	public DateTime Date { get; set; }

	public decimal Amount { get; set; }

	public string Type { get; set; } = "";

	public string Notes { get; set; } = "";

	public string PdfPath { get; set; } = "";

	public string XmlPath { get; set; } = "";
}
