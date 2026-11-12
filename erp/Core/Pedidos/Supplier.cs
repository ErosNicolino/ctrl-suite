using System;
using System.Collections.Generic;
using System.Linq;

namespace GraficaCaixa;

// Fornecedor cadastrado (Compras › Fornecedores). Só o nome é obrigatório; inativo some da lista de escolha, o histórico fica.
public sealed class Supplier
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public string Name { get; set; } = "";

	public string LegalName { get; set; } = "";

	public string Document { get; set; } = "";

	public string ContactPerson { get; set; } = "";

	public string WhatsApp { get; set; } = "";

	public string Phone { get; set; } = "";

	public string Email { get; set; } = "";

	public string Address { get; set; } = "";

	public List<string> Supplies { get; set; } = new List<string>();

	public string LeadTime { get; set; } = "";

	public string PaymentTerms { get; set; } = "";

	public decimal? MinimumOrder { get; set; }

	public string Notes { get; set; } = "";

	public bool Active { get; set; } = true;

	public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed record SupplierPrice(string Material, string Unit, decimal UnitPrice, decimal? PreviousUnitPrice, DateTime Date, string Supplier);

public sealed record SupplierPurchase(DateTime Date, string Material, decimal Quantity, string Unit, decimal Amount, string PaymentMethod, string Supplier);

// Ligação compra ↔ fornecedor: pelo Id quando escolhido da lista; pelo nome quando digitado (compras antigas e nomes livres também contam).
public static class SupplierBook
{
	public static string Key(string? name) => string.Join(' ', (name ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

	public static Supplier? Find(Prompt12State state, Guid? id, string? name)
	{
		var list = state.Suppliers ?? [];
		if (id is Guid value && list.FirstOrDefault(x => x.Id == value) is Supplier byId) return byId;
		var key = Key(name);
		return key.Length == 0 ? null : list.FirstOrDefault(x => Key(x.Name) == key);
	}

	public static bool Owns(Supplier supplier, Guid? id, string? name) => id == supplier.Id || id == null && Key(name).Length > 0 && Key(name) == Key(supplier.Name);

	public static IEnumerable<SupplierPurchase> Purchases(Prompt12State state, Supplier? supplier = null)
	{
		var needs = (state.PurchaseNeeds ?? []).ToDictionary(x => x.Id);
		foreach (var purchase in state.Purchases ?? [])
		{
			if (supplier != null && !Owns(supplier, purchase.SupplierId, purchase.Supplier)) continue;
			needs.TryGetValue(purchase.NeedId, out var need);
			var name = Find(state, purchase.SupplierId, purchase.Supplier)?.Name ?? purchase.Supplier.Trim();
			yield return new SupplierPurchase(purchase.Date, need?.Material ?? "Material", purchase.Quantity, need?.Unit ?? "", purchase.Amount, purchase.PaymentMethod, name);
		}
	}

	static decimal Unit(SupplierPurchase x) => x.Quantity > 0 ? decimal.Round(x.Amount / x.Quantity, 2) : x.Amount;

	// Último preço unitário de cada material (e o anterior, para mostrar ▲/▼).
	public static List<SupplierPrice> LastPrices(Prompt12State state, Supplier supplier) =>
		Purchases(state, supplier).Where(x => x.Amount > 0).GroupBy(x => Key(x.Material))
			.Select(g => { var ordered = g.OrderByDescending(x => x.Date).ToList(); var last = ordered[0]; return new SupplierPrice(last.Material, last.Unit, Unit(last), ordered.Count > 1 ? Unit(ordered[1]) : null, last.Date, supplier.Name); })
			.OrderByDescending(x => x.Date).ToList();

	// Comparação na hora de comprar: último preço do material em cada fornecedor (cadastrado ou digitado).
	public static List<SupplierPrice> PricesFor(Prompt12State state, string material)
	{
		var key = Key(material);
		if (key.Length == 0) return [];
		return Purchases(state).Where(x => x.Amount > 0 && x.Supplier.Length > 0 && Key(x.Material) == key).GroupBy(x => Key(x.Supplier))
			.Select(g => { var last = g.OrderByDescending(x => x.Date).First(); return new SupplierPrice(last.Material, last.Unit, Unit(last), null, last.Date, last.Supplier); })
			.OrderBy(x => x.UnitPrice).ToList();
	}

	public static decimal Spend(Prompt12State state, Supplier supplier, DateTime from, DateTime to) =>
		Purchases(state, supplier).Where(x => x.Date >= from && x.Date < to).Sum(x => x.Amount);

	// Gasto por fornecedor no período (Relatórios); nomes livres sem cadastro aparecem como estão escritos.
	public static List<(string Supplier, int Count, decimal Amount)> SpendBySupplier(Prompt12State state, DateTime from, DateTime to) =>
		Purchases(state).Where(x => x.Date >= from && x.Date < to).GroupBy(x => x.Supplier.Length == 0 ? "Sem fornecedor" : x.Supplier, StringComparer.CurrentCultureIgnoreCase)
			.Select(g => (g.Key, g.Count(), g.Sum(x => x.Amount))).OrderByDescending(x => x.Item3).ToList();

	public static void Validate(Prompt12State state, Supplier supplier)
	{
		supplier.Name = (supplier.Name ?? "").Trim();
		if (supplier.Name.Length == 0) throw new InvalidOperationException("Informe o nome do fornecedor.");
		if (supplier.Name.Length > 120) throw new InvalidOperationException("Nome do fornecedor muito longo (máximo 120 caracteres).");
		foreach (var text in new[] { supplier.LegalName, supplier.Document, supplier.ContactPerson, supplier.WhatsApp, supplier.Phone, supplier.Email, supplier.Address, supplier.LeadTime, supplier.PaymentTerms })
			if ((text ?? "").Length > 200) throw new InvalidOperationException("Um dos campos do fornecedor passou de 200 caracteres.");
		if ((supplier.Notes ?? "").Length > 2000) throw new InvalidOperationException("Observação muito longa (máximo 2.000 caracteres).");
		if (supplier.MinimumOrder is < 0) throw new InvalidOperationException("Pedido mínimo não pode ser negativo.");
		supplier.Supplies = (supplier.Supplies ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).Take(20).ToList();
		if ((state.Suppliers ?? []).Any(x => x.Id != supplier.Id && Key(x.Name) == Key(supplier.Name))) throw new InvalidOperationException($"Já existe um fornecedor chamado \"{supplier.Name}\".");
	}

	// Grava (cria ou atualiza) e devolve o texto de auditoria (antes, depois).
	public static (string Before, string After) Save(Prompt12State state, Supplier supplier)
	{
		state.Suppliers ??= [];
		Validate(state, supplier);
		var old = state.Suppliers.FirstOrDefault(x => x.Id == supplier.Id);
		static string Text(Supplier? x) => x == null ? "" : $"{x.Name} | {x.Document} | {x.WhatsApp} | {(x.Active ? "ativo" : "inativo")}";
		var before = Text(old);
		if (old != null) { supplier.CreatedAt = old.CreatedAt; state.Suppliers.Remove(old); }
		state.Suppliers.Add(supplier);
		return (before, Text(supplier));
	}
}
