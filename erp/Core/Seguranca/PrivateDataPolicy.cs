using System.Linq;
using System.Text.Json;

namespace GraficaCaixa;

public static class PrivateDataPolicy
{
	public static AppData EmployeeView(AppData source)
	{
		AppData appData = JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(source))!;
		Prompt12State prompt = appData.Prompt12;
		foreach (FiscalDocument fiscalDocument in prompt.FiscalDocuments)
		{
			fiscalDocument.PdfPath = "";
			fiscalDocument.XmlPath = "";
		}
		appData.Prompt12 = new Prompt12State
		{
			Version = prompt.Version,
			PurchaseNeeds = prompt.PurchaseNeeds,
			Materials = prompt.Materials.Select((MaterialDefinition x) => new MaterialDefinition
			{
				Id = x.Id,
				Name = x.Name,
				CostMode = x.CostMode,
				Width = x.Width,
				Height = x.Height,
				Margin = x.Margin,
				Spacing = x.Spacing,
				ValidTags = x.ValidTags
			}).ToList(),
			KnownYields = prompt.KnownYields,
			FiscalDocuments = prompt.FiscalDocuments,
			FiscalIssuerUrl = prompt.FiscalIssuerUrl,
			CommunicationTemplates = prompt.CommunicationTemplates
		};
		return appData;
	}

	public static void MergeEmployeeChanges(AppData current, AppData incoming)
	{
		Prompt12State prompt12State = incoming.Prompt12 ?? new Prompt12State();
		foreach (FiscalDocument document in prompt12State.FiscalDocuments)
		{
			FiscalDocument? fiscalDocument = current.Prompt12.FiscalDocuments.FirstOrDefault((FiscalDocument x) => x.Id == document.Id);
			if (fiscalDocument != null)
			{
				document.PdfPath = fiscalDocument.PdfPath;
				document.XmlPath = fiscalDocument.XmlPath;
			}
		}
		current.Prompt12.PurchaseNeeds = prompt12State.PurchaseNeeds;
		// Compras registradas em qualquer computador (valor pago, fornecedor) acompanham a lista de compras.
		current.Prompt12.Purchases = prompt12State.Purchases ?? current.Prompt12.Purchases;
		current.Prompt12.Materials = prompt12State.Materials.Select((MaterialDefinition x) =>
		{
			MaterialDefinition? materialDefinition = current.Prompt12.Materials.FirstOrDefault((MaterialDefinition y) => y.Id == x.Id);
			if (materialDefinition != null)
			{
				x.Cost = materialDefinition.Cost;
			}
			return x;
		}).ToList();
		current.Prompt12.KnownYields = prompt12State.KnownYields;
		current.Prompt12.FiscalDocuments = prompt12State.FiscalDocuments;
		current.Prompt12.FiscalIssuerUrl = prompt12State.FiscalIssuerUrl;
		current.Prompt12.CommunicationTemplates = prompt12State.CommunicationTemplates;
		incoming.Prompt12 = current.Prompt12;
	}
}
