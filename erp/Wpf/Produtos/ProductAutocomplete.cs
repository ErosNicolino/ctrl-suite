using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
public static class ProductAutocomplete
{
    public static void Attach(TextBox text,Func<ProductSearch?> search,Action<Product> selected,Func<DataStore?>? remoteStore=null)
    {
        void Select(SuggestionOption option){var product=(Product)option.Value;text.Text=product.Description;selected(product);}
        IReadOnlyList<SuggestionOption> Options(IEnumerable<Product> products)=>products.Select(p=>new SuggestionOption(p.Description,p.Category+(p.Price.HasValue?$" · {p.Price.Value:C2}":" · Preço por quantidade"),p)).ToList();
        if(remoteStore==null)SuggestionPopup.Attach(text,"Produtos e serviços",query=>Options(search()?.Find(query)??[]),Select);
        else SuggestionPopup.AttachAsync(text,"Produtos e serviços",async (query,ct)=>remoteStore()?.IsRemote==true?Options(await remoteStore()!.QueryRemoteAsync<List<Product>>("desktop/lookup/products?search="+Uri.EscapeDataString(query),ct)):Options(search()?.Find(query)??[]),Select);
    }
}