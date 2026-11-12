using System;
using System.Linq;

namespace GraficaCaixa;

public static class LossOperations
{
	public static ReceivableLoss Declare(AppData data, Guid orderId, string reason, DateTime date, string notes, OperationContext user)
	{
		// The existing reference also supports an avulsa debt ID; no storage change is required.
        var scheduledOrder = data.ScheduledOrders.SingleOrDefault(x => x.Id == orderId);
        var debt = data.ManualDebts.SingleOrDefault(x => x.Id == orderId);
        if (scheduledOrder == null && debt == null) throw new InvalidOperationException("Dívida não encontrada.");
        if (scheduledOrder?.PricePending == true) throw new InvalidOperationException("Defina o valor do pedido antes de declarar perda.");
        var balance = scheduledOrder?.RemainingAmount ?? debt!.Remaining(data);
		if (balance <= 0m)
		{
			throw new InvalidOperationException("O pedido não possui saldo a receber.");
		}
		if (string.IsNullOrWhiteSpace(reason))
		{
			throw new InvalidOperationException("Informe o motivo da perda.");
		}
		if (data.Prompt12.Losses.Any((ReceivableLoss x) => x.OrderId == orderId && x.Status != LossStatus.Reopened))
		{
			throw new InvalidOperationException("Este saldo já está declarado como perda.");
		}
		ReceivableLoss receivableLoss = new ReceivableLoss
		{
			OrderId = orderId,
			Amount = balance,
			LossDate = date.Date,
			Reason = reason.Trim(),
			Notes = notes.Trim()
		};
		data.Prompt12.Losses.Add(receivableLoss);
		AuditService.Record(data, user, "Dívida declarada como perda", "Perda", receivableLoss.Id.ToString(), "", receivableLoss.Amount.ToString("0.00"), receivableLoss.Reason, scheduledOrder != null ? $"Pedido #{scheduledOrder.Number}" : $"Dívida avulsa: {debt!.Description}");
		return receivableLoss;
	}

	public static void Reopen(AppData data, Guid lossId, OperationContext user)
	{
		ReceivableLoss receivableLoss = Find(data, lossId);
		if (receivableLoss.Status != LossStatus.Loss) throw new InvalidOperationException("Somente perdas abertas podem ser reabertas.");
		receivableLoss.Status = LossStatus.Reopened;
		receivableLoss.ReopenedAt = DateTime.Now;
		AuditService.Record(data, user, "Cobrança reaberta", "Perda", receivableLoss.Id.ToString(), "PERDA", "REABERTA", "", "");
	}

	public static void Recover(AppData data, Guid lossId, decimal amount, DateTime date, OperationContext user)
	{
		ReceivableLoss receivableLoss = Find(data, lossId);
		if (receivableLoss.Status != LossStatus.Loss) throw new InvalidOperationException("Somente perdas abertas podem ser recuperadas.");
		if (amount <= 0m || amount > receivableLoss.Amount)
		{
			throw new InvalidOperationException("Valor de recuperação inválido.");
		}
		receivableLoss.Status = LossStatus.Recovered;
		receivableLoss.RecoveredAmount = amount;
		receivableLoss.RecoveredAt = date;
		AuditService.Record(data, user, "Recuperação de perda", "Perda", receivableLoss.Id.ToString(), receivableLoss.Amount.ToString("0.00"), amount.ToString("0.00"), "", "");
	}

	public static bool IsNormalReceivable(AppData data, Guid orderId)
	{
		return !data.Prompt12.Losses.Any((ReceivableLoss x) => x.OrderId == orderId && x.Status != LossStatus.Reopened);
	}

	private static ReceivableLoss Find(AppData data, Guid id)
	{
		return data.Prompt12.Losses.SingleOrDefault((ReceivableLoss x) => x.Id == id) ?? throw new InvalidOperationException("Perda não encontrada.");
	}
}
