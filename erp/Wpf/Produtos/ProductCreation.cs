using System.Windows;
namespace GraficaCaixa.Wpf;
public static class ProductCreation
{
    // description/price: já preenche o cadastro com o que foi digitado no pedido ou lançamento.
    public static async Task<Product?> CreateAsync(Window owner,DataStore store,string? description=null,decimal? price=null)
    {
        var dialog=new ProductEditor(null,DocumentPaths.TryRoot(store),store){Owner=owner};
        if(description!=null)dialog.Prefill(description,price);
        if(dialog.ShowDialog()!=true)return null;
        await ProductPhotos.SaveAsync(store,dialog.Result!,null,dialog.Photos,App.CurrentUser);
        return dialog.Result!;
    }
}
