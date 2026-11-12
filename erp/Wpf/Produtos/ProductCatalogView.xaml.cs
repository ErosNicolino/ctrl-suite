using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;

public partial class ProductCatalogView : UserControl
{
    public DataStore? Store {get;set;}
    IReadOnlyList<ProductCatalogItem> catalog=[];bool remoteLoaded;
    bool ready, showAll, changing;CancellationTokenSource? loading;int page=1;long total;string? loadError;
    public bool StartWithProducts { get; set; }
    public ProductCatalogView()
    {
        InitializeComponent();
        Unloaded+=(_,_)=>loading?.Cancel();
        Loaded += async (_,_) =>
        {
            if (ready) return;
            try
            {
                if(Store?.IsRemote==true){await LoadRemote();ready=true;
                    if(Content is Grid root){root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var pager=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(pager,root.RowDefinitions.Count-1);root.Children.Add(pager);var previous=new Button{Content="Anterior"};var next=new Button{Content="Próxima"};pager.Children.Add(previous);pager.Children.Add(next);previous.Click+=async(_,_)=>{if(page<=1)return;page--;await LoadRemote();Refresh();};next.Click+=async(_,_)=>{if(page*30>=total)return;page++;await LoadRemote();Refresh();};}
                    Refresh();return;}
                catalog=ProductCatalog.FromProducts(Store?.Load().Products??[]);
                CategoryBox.ItemsSource = new[]{"Todas as categorias"}.Concat(catalog.Select(x=>x.Category).Distinct().OrderBy(x=>x)).ToList();
                CategoryBox.SelectedIndex=0;
                showAll=StartWithProducts;
                ready=true;
                Refresh();
            }
            catch(Exception ex) { DetailsText.Text=ex.Message; CategoriesPanel.Visibility=Visibility.Collapsed; ProductsPanel.Visibility=Visibility.Visible; EmptyText.Text="Não foi possível carregar o catálogo."; EmptyText.Visibility=Visibility.Visible; }
        };
    }
    async Task LoadRemote()
    {
        loading?.Cancel();var cancellation=loading=new();loadError=null;DetailsText.Text="Carregando…";
        try{
            if(remoteLoaded)await Task.Delay(350,cancellation.Token); // espera só para a digitação; a primeira carga sai na hora
            var result=await Store!.QueryRemoteAsync<ModulePage>($"api/v1/produtos?projection=desktop&page={page}&pageSize=30&search="+ModuleLoader.Encode(SearchBox.Text)+"&category="+ModuleLoader.Encode(CategoryBox.SelectedIndex>0?CategoryBox.SelectedItem?.ToString():null),cancellation.Token);
            var data=await Task.Run(()=>result.Data.Deserialize<AppData>()!,cancellation.Token);
            await DocumentPaths.EnsureBootstrapAsync(Store,data.DataVersion,cancellation.Token);cancellation.Token.ThrowIfCancellationRequested();
            catalog=ProductCatalog.FromProducts(data.Products);total=result.Total;remoteLoaded=true;changing=true;var selected=CategoryBox.SelectedItem;
            CategoryBox.ItemsSource=new[]{"Todas as categorias"}.Concat(data.ProductCategories??[]);CategoryBox.SelectedItem=selected??"Todas as categorias";changing=false;
        }catch(OperationCanceledException){}catch(Exception ex){if(ReferenceEquals(loading,cancellation))loadError=ex.Message;}
        finally{if(ReferenceEquals(loading,cancellation))loading=null;cancellation.Dispose();}
    }
    void Refresh()
    {
        if (!ready || changing) return;
        var category=CategoryBox.SelectedIndex>0 ? CategoryBox.SelectedItem?.ToString() : null;
        var rows=ProductCatalog.Find(catalog,SearchBox.Text,category);
        var list=Store?.IsRemote==true||showAll || category!=null || !string.IsNullOrWhiteSpace(SearchBox.Text);
        CategoriesPanel.Visibility=list?Visibility.Collapsed:Visibility.Visible;
        ProductsPanel.Visibility=list?Visibility.Visible:Visibility.Collapsed;
        CategoryCards.ItemsSource=rows.GroupBy(x=>x.Category).Select(x=>new ProductCategory(x.Key,x.Count())).OrderBy(x=>x.Name).ToList();
        ProductsGrid.ItemsSource=rows;
        ProductsGrid.SelectedIndex=-1;
        ListTitle.Text=category??"Todos os produtos";
        CountText.Text=Store?.IsRemote==true?$"Página {page} • {total} opções":$"{rows.Count} opções";
        EmptyText.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;
        DetailsText.Text=loadError??"Selecione uma opção para conferir suas observações.";
    }
    void Copy_Click(object s,RoutedEventArgs e)
    {
        if(ProductsGrid.SelectedItem is not ProductCatalogItem p)return;
        if(p.Price is decimal amount)Clipboard.SetText(BudgetSummary.Format(p.Name,amount));
    }
    async void Order_Click(object s,RoutedEventArgs e)
    {
        if(Store==null||ProductsGrid.SelectedItem is not ProductCatalogItem p)return;
        try{var product=(await Store.BusinessContextAsync("product",Guid.Parse(p.Id))).Products.FirstOrDefault(x=>x.Id.ToString()==p.Id&&x.Active)??throw new InvalidOperationException("Produto indisponível. Reabra o catálogo.");
        var draft=new ScheduledOrder{UseItemPrices=true,OrderTotal=product.Price??0,OriginalTotal=product.Price,PricePending=product.Price==null,ItemCostsEnabled=true,Items=[new OrderItem{ProductId=product.Id,Product=product.Description,Quantity=1,Unit=product.Unit,UnitPrice=product.Price,UnitCost=product.Cost,CostSource="Cadastro (sugerido)"}]};
        var dialog=new ScheduledOrderDialog(draft,Store){Owner=Window.GetWindow(this)};if(dialog.ShowDialog()==true)await BusinessUi.SaveOrderAsync(Store,dialog.Result!,dialog.CorrectionSaleId,dialog.CorrectedAmount,dialog.CorrectionReason);}
        catch(Exception ex){AlertWindow.Info(Window.GetWindow(this),"Orçamento / pedido",ex.Message);}
    }
    async void Filter_Changed(object s, EventArgs e){if(changing)return;if(Store?.IsRemote==true){page=1;await LoadRemote();}Refresh();}
    void Category_Click(object s,RoutedEventArgs e)=>CategoryBox.SelectedItem=((Button)s).Tag?.ToString();
    void All_Click(object s,RoutedEventArgs e){showAll=true;Refresh();}
    async void Reset(bool all)
    {
        changing=true;SearchBox.Text="";CategoryBox.SelectedIndex=0;showAll=all;changing=false;if(Store?.IsRemote==true){page=1;await LoadRemote();}Refresh();
    }
    void Back_Click(object s,RoutedEventArgs e)=>Reset(false);
    void Clear_Click(object s,RoutedEventArgs e)=>Reset(StartWithProducts);
    void Product_Selected(object s,SelectionChangedEventArgs e)
    {
        if(DetailsText!=null)DetailsText.Text=(ProductsGrid.SelectedItem as ProductCatalogItem)?.Details??"Selecione uma opção para conferir suas observações.";
    }
}
