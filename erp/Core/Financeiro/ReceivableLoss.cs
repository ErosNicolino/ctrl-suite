using System;

namespace GraficaCaixa;

public sealed class ReceivableLoss
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public Guid OrderId { get; set; }

	public decimal Amount { get; set; }

	public DateTime LossDate { get; set; }

	public string Reason { get; set; } = "";

	public string Notes { get; set; } = "";

	public LossStatus Status { get; set; }

	public DateTime? ReopenedAt { get; set; }

	public DateTime? RecoveredAt { get; set; }

	public decimal RecoveredAmount { get; set; }
}
