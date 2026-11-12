using System;
using System.Collections.Generic;
using System.Linq;

namespace GraficaCaixa;

public static class PrivateFinance
{
	public static void AddExpense(AppData data, Expense expense, OwnerSession session, OperationContext user)
	{
		OwnerSecurity.Require(session);
		if (string.IsNullOrWhiteSpace(expense.Description) || expense.Amount < 0m)
		{
			throw new InvalidOperationException("Despesa inválida.");
		}
		if (expense.Recurring && !string.IsNullOrWhiteSpace(expense.RecurrenceKey) && data.Prompt12.Expenses.Any((Expense x) => x.Recurring && x.RecurrenceKey == expense.RecurrenceKey && x.Competence.Year == expense.Competence.Year && x.Competence.Month == expense.Competence.Month))
		{
			throw new InvalidOperationException("Despesa recorrente já gerada para esta competência.");
		}
		data.Prompt12.Expenses.Add(expense);
		AuditService.Record(data, user, "Despesa incluída", "Despesa", expense.Id.ToString(), "", expense.Amount.ToString("0.00"), "", expense.Description);
	}

	public static Purchase CompletePurchase(AppData data, Guid needId, Purchase purchase, OwnerSession session, OperationContext user)
	{
		OwnerSecurity.Require(session);
		PurchaseNeed purchaseNeed = data.Prompt12.PurchaseNeeds.Single((PurchaseNeed x) => x.Id == needId);
		purchase.NeedId = needId;
		data.Prompt12.Purchases.Add(purchase);
		purchaseNeed.Status = PurchaseNeedStatus.Purchased;
		AuditService.Record(data, user, "Compra registrada", "Compra", purchase.Id.ToString(), "", purchase.Amount.ToString("0.00"), "", purchaseNeed.Material);
		return purchase;
	}

	public static ManagementResult Result(AppData data, DateTime from, DateTime through, OwnerSession session)
	{
		OwnerSecurity.Require(session);
		List<ScheduledOrder> source = data.ScheduledOrders.Where((ScheduledOrder x) => x.CreatedAt.Date >= from.Date && x.CreatedAt.Date <= through.Date && !x.PricePending && !x.IsCancelled).ToList();
		decimal num = source.Sum((ScheduledOrder x) => x.OrderTotal);
		decimal num2 = source.Sum((ScheduledOrder x) => x.ItemsCost);
		bool isComplete = source.All((ScheduledOrder x) => x.HasCosts);
		// Lançamentos de balcão com itens também são faturamento (e o custo dos itens entra nos custos diretos).
		var hidden = Cancellations.HiddenSaleIds(data);
		var direct = data.ManualDebts.Where(x => x.Items.Count > 0 && x.CreatedAt.Date >= from.Date && x.CreatedAt.Date <= through.Date && (x.SaleIds.Count == 0 || !x.SaleIds.All(hidden.Contains))).ToList();
		num += direct.Sum(x => x.Amount);
		num2 += direct.Sum(x => x.Items.Sum(i => i.TotalCost ?? 0m));
		isComplete = isComplete && direct.All(x => x.Items.All(i => i.TotalCost != null));
		decimal num3 = data.Prompt12.Expenses.Where((Expense x) => x.Active && x.Competence.Date >= from.Date && x.Competence.Date <= through.Date).Sum((Expense x) => x.Amount);
		decimal num4 = data.Prompt12.Losses.Where((ReceivableLoss x) => x.LossDate >= from.Date && x.LossDate <= through.Date && x.Status == LossStatus.Loss).Sum((ReceivableLoss x) => x.Amount);
		// Retrabalho conta no mês em que foi registrado, mesmo que o pedido seja de outro mês.
		decimal rework = data.Prompt12.Reworks.Where(x => x.Date.Date >= from.Date && x.Date.Date <= through.Date).Sum(x => x.Cost);
		return new ManagementResult(num, num2, num - num2, num3, num4, num - num2 - num3 - num4 - rework, isComplete, rework);
	}

	public static CashFlowResult CashFlow(AppData data, DateTime from, DateTime through, OwnerSession session)
	{
		OwnerSecurity.Require(session);
		decimal num = (from x in data.Sessions.SelectMany((CashSession x) => x.Sales)
			where x.CreatedAt.Date >= @from.Date && x.CreatedAt.Date <= through.Date
			select x).Sum((Sale x) => x.Total);
		decimal num2 = data.Prompt12.Expenses.Where((Expense x) => x.Active && x.Status == ExpenseStatus.Paid && x.PaidAt?.Date >= from.Date && x.PaidAt?.Date <= through.Date).Sum((Expense x) => x.Amount);
		decimal num3 = data.Prompt12.Purchases.Where((Purchase x) => x.Paid && x.Date.Date >= from.Date && x.Date.Date <= through.Date).Sum((Purchase x) => x.Amount);
		decimal num4 = (from x in data.Sessions.SelectMany((CashSession x) => x.Withdrawals)
			where x.CreatedAt.Date >= @from.Date && x.CreatedAt.Date <= through.Date
			select x).Sum((CashWithdrawal x) => x.Amount);
		return new CashFlowResult(num, num2, num3, num4, num - num2 - num3 - num4);
	}
}

// Lista de despesas da Área do proprietário: despesas lançadas + compras de material (Compras › Comprado), que aparecem
// como despesas pagas só para consulta. No resultado gerencial as compras NÃO entram de novo (o custo do material usado
// já está no custo dos pedidos); no resultado financeiro (caixa) elas já saem como "Compras".
public static class ExpenseList
{
	public const string PurchaseCategory = "Compras de material";

	public static IEnumerable<Expense> Purchases(Prompt12State state)
	{
		var needs = (state.PurchaseNeeds ?? []).ToDictionary(x => x.Id);
		foreach (var p in state.Purchases ?? [])
		{
			needs.TryGetValue(p.NeedId, out var need);
			var supplier = SupplierBook.Find(state, p.SupplierId, p.Supplier)?.Name ?? p.Supplier;
			yield return new Expense { Id = p.Id, Description = need?.Material ?? "Compra", Category = PurchaseCategory, Amount = p.Amount, Competence = new DateTime(p.Date.Year, p.Date.Month, 1), DueDate = p.Date.Date, Status = ExpenseStatus.Paid, PaidAt = p.Date, PaymentMethod = p.PaymentMethod, Notes = supplier.Length > 0 ? "Fornecedor: " + supplier : "", Active = true, FromPurchase = true };
		}
	}

	public static IEnumerable<Expense> Filter(Prompt12State state, string filter, string search)
	{
		var expenses = (state.Expenses ?? []).AsEnumerable();
		var rows = filter switch
		{
			"pending" => expenses.Where(x => x.Active && x.Status == ExpenseStatus.Pending),
			"paid" => expenses.Where(x => x.Active && x.Status == ExpenseStatus.Paid).Concat(Purchases(state)),
			"overdue" => expenses.Where(x => x.Active && x.Status == ExpenseStatus.Pending && x.DueDate < DateTime.Today),
			"recurring" => expenses.Where(x => x.Active && x.Recurring),
			"inactive" => expenses.Where(x => !x.Active),
			"purchases" => Purchases(state),
			"expenses" => expenses,
			_ => expenses.Concat(Purchases(state))
		};
		search ??= "";
		return rows.Where(x => search.Length == 0 || $"{x.Description} {x.Category} {x.PaymentMethod} {x.Notes}".Contains(search, StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(x => x.Competence).ThenBy(x => x.DueDate);
	}

	// Próximas despesas recorrentes que já deveriam existir até o mês atual (uma por mês devido de cada série).
	public static List<Expense> DueRecurring(Prompt12State state, DateTime today)
	{
		var month = new DateTime(today.Year, today.Month, 1); var result = new List<Expense>();
		foreach (var series in (state.Expenses ?? []).Where(x => x.Active && x.Recurring).GroupBy(x => x.RecurrenceKey.Length > 0 ? x.RecurrenceKey : x.Description.Trim().ToLowerInvariant()))
		{
			var last = series.OrderByDescending(x => x.Competence).First(); var every = last.EveryMonths;
			for (var next = last.Competence.AddMonths(every); next <= month && result.Count < 200; next = next.AddMonths(every))
			{
				if ((state.Expenses ?? []).Any(x => x.Recurring && (x.RecurrenceKey.Length > 0 ? x.RecurrenceKey : x.Description.Trim().ToLowerInvariant()) == series.Key && x.Competence == next)) continue;
				var shift = (next.Year - last.Competence.Year) * 12 + next.Month - last.Competence.Month;
				result.Add(new Expense { Description = last.Description, Category = last.Category, Amount = last.Amount, Competence = next, DueDate = last.DueDate.AddMonths(shift), Status = ExpenseStatus.Pending, PaymentMethod = last.PaymentMethod, Notes = last.Notes, Recurring = true, RecurrenceMonths = every, RecurrenceKey = series.Key, Active = true });
			}
		}
		return result.OrderBy(x => x.Competence).ThenBy(x => x.Description).ToList();
	}
}
