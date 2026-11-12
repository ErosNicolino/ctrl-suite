using System;

namespace GraficaCaixa;

public sealed class Expense
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public string Description { get; set; } = "";

	public string Category { get; set; } = "Outras";

	public decimal Amount { get; set; }

	public DateTime Competence { get; set; }

	public DateTime DueDate { get; set; }

	public ExpenseStatus Status { get; set; }

	public DateTime? PaidAt { get; set; }

	public string PaymentMethod { get; set; } = "";

	public string Notes { get; set; } = "";

	public bool Recurring { get; set; }

	public string RecurrenceKey { get; set; } = "";

	public bool Active { get; set; } = true;

	// Repetição em meses: 1 mensal, 2 bimestral, 3 trimestral, 6 semestral, 12 anual. Antigas "recorrentes" sem valor = mensal.
	public int RecurrenceMonths { get; set; }

	[System.Text.Json.Serialization.JsonIgnore]
	public int EveryMonths => Recurring ? (RecurrenceMonths > 0 ? RecurrenceMonths : 1) : 0;

	[System.Text.Json.Serialization.JsonIgnore]
	public string RecurrenceLabel => EveryMonths switch { 0 => "Não", 1 => "Mensal", 2 => "Bimestral", 3 => "Trimestral", 6 => "Semestral", 12 => "Anual", var m => $"A cada {m} meses" };

	// Linha só de leitura montada a partir de Compras (material comprado); não é gravada como despesa.
	public bool FromPurchase { get; set; }
}
