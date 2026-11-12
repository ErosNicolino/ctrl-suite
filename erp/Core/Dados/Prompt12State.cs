using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GraficaCaixa;

public sealed class Prompt12State
{
	public int Version { get; set; } = 1;

	public OwnerPinCredential? OwnerPin { get; set; }

	// Senha de acesso do ERP (hash). Nula = ninguém entra até uma senha ser cadastrada.
	public OwnerPinCredential? LoginPassword { get; set; }

	public List<ReceivableLoss> Losses { get; set; } = new List<ReceivableLoss>();

	public List<Expense> Expenses { get; set; } = new List<Expense>();

	public List<PurchaseNeed> PurchaseNeeds { get; set; } = new List<PurchaseNeed>();

	public List<Purchase> Purchases { get; set; } = new List<Purchase>();

	public List<Supplier> Suppliers { get; set; } = new List<Supplier>();

	[JsonPropertyName("Quotes")]
	public List<JsonElement> RetiredProfessionalQuotes { get; set; } = new List<JsonElement>();

	public List<MaterialDefinition> Materials { get; set; } = new List<MaterialDefinition>();

	public List<KnownYield> KnownYields { get; set; } = new List<KnownYield>();

	public List<FiscalDocument> FiscalDocuments { get; set; } = new List<FiscalDocument>();

	public List<OrderRework> Reworks { get; set; } = new List<OrderRework>();

	public List<OutsourcedService> Outsourced { get; set; } = new List<OutsourcedService>();

	public List<CommunicationTemplate> CommunicationTemplates { get; set; } = new List<CommunicationTemplate>();

	public string FiscalIssuerUrl { get; set; } = "";
}
