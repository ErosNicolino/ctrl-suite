using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace GraficaCaixa.Wpf;

public sealed class ProductCatalogItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string SourceSheet { get; set; } = "";
    public string SourceCell { get; set; } = "";
    public string AlsoListedIn { get; set; } = "";
    public decimal? Quantity { get; set; }
    public decimal? Price { get; set; }
    public string Basis { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Artwork { get; set; } = "";
    public string Review { get; set; } = "";
    public string PriceText => Price.HasValue?Price.Value.ToString(Price == decimal.Round(Price.Value,2) ? "C2" : "C4", CultureInfo.GetCultureInfo("pt-BR")):Notes.StartsWith("Tabela")||Notes.StartsWith("Faixas")?"Ver tabela":"Sem preço";
    public string SourceText => $"{SourceSheet} • {SourceCell}";
    public string StatusText => Review.Length > 0 ? "Conferir preço" : Artwork;
    public string StatusColor => Review.Length > 0 ? "#996719" : "#686E75";
    public string Details => string.Join("\n", new[]{Notes, Artwork, Review, Category}.Where(x => !string.IsNullOrWhiteSpace(x)));
}
public sealed class ProductCatalogDocument
{
    public string SourceFile { get; set; } = "";
    public string ImportedOn { get; set; } = "";
    public List<ProductCatalogItem> Items { get; set; } = [];
}
public sealed record ProductCategory(string Name, int Count)
{
    public string CountText => $"{Count} opções e faixas de preço";
}
public static class ProductCatalog
{
    public static IReadOnlyList<ProductCatalogItem> FromProducts(IEnumerable<Product> products) => products.Where(x=>x.Active).Select(x=>new ProductCatalogItem{Id=x.Id.ToString(),Name=x.Description,Category=x.Category,Price=QuantityPricing.BasePrice(x),Basis=x.Unit,Notes=PriceTable(x)}).ToList();
    // Resumo da tabela por quantidade ou das faixas, para aparecer junto do produto no catálogo.
    static string PriceTable(Product x)
    {
        var pt=CultureInfo.GetCultureInfo("pt-BR");
        if(QuantityPricing.HasExactTable(x))return "Tabela: "+string.Join(" • ",x.ExactQuantityPrices.OrderBy(e=>e.Quantity).Select(e=>string.Format(pt,"{0:N0} un {1:C2}",e.Quantity,e.TotalPrice)));
        if(x.UseQuantityPricing&&x.QuantityPriceTiers.Count>0)return "Faixas: "+string.Join(" • ",x.QuantityPriceTiers.OrderBy(f=>f.From).Select(f=>f.Label));
        return "";
    }
    public static string Normalize(string value) => Regex.Replace(new string(value.ToLowerInvariant()
        .Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)
        .ToArray()), @"[^a-z0-9]+", " ").Trim();
    public static List<ProductCatalogItem> Find(IEnumerable<ProductCatalogItem> items, string query, string? category = null)
    {
        var words=Normalize(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        return items.Where(x=>(category==null || x.Category==category) &&
            words.All(w=>Normalize($"{x.Name} {x.Category} {x.Basis} {x.Notes}").Contains(w)))
            .OrderBy(x=>x.Name,StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"),true))
            .ThenBy(x=>x.Quantity).ThenBy(x=>x.SourceSheet).ToList();
    }
}
