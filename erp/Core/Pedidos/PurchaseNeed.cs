using System;

namespace GraficaCaixa;

public sealed class PurchaseNeed
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public string Material { get; set; } = "";

	public decimal Quantity { get; set; }

	public string Unit { get; set; } = "";

	public PurchasePriority Priority { get; set; } = PurchasePriority.Normal;

	public DateTime? BuyBy { get; set; }

	public string Notes { get; set; } = "";

	// Fornecedor de onde o material costuma vir (opcional); sugere o fornecedor ao marcar como comprado.
	public string Supplier { get; set; } = "";

	// Fornecedor escolhido do cadastro (nulo quando o nome foi digitado livremente).
	public Guid? SupplierId { get; set; }

	public string RequestedBy { get; set; } = "";

	public PurchaseNeedStatus Status { get; set; }
}
