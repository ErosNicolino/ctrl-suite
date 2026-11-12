using System;

namespace GraficaCaixa;

public sealed class Purchase
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public Guid NeedId { get; set; }

	public string Supplier { get; set; } = "";

	public Guid? SupplierId { get; set; }

	public decimal Quantity { get; set; }

	public decimal Amount { get; set; }

	public DateTime Date { get; set; }

	public string PaymentMethod { get; set; } = "";

	public string Notes { get; set; } = "";

	public bool Paid { get; set; }
}
