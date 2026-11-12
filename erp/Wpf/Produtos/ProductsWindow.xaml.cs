using System.Windows;
namespace GraficaCaixa.Wpf;
public partial class ProductsWindow : Window
{
    bool writeBusy;
    System.Windows.Controls.ItemsPanelTemplate? cardPanel;
    readonly System.Collections.ObjectModel.ObservableCollection<Product> remoteRows=[];readonly DataStore store; ProductSearch? search; readonly ModuleLoader remoteLoad; bool changing;
    public ProductsWindow(DataStore store){ProductThumb.Store=store;InitializeComponent();this.store=store;cardPanel=ProductsGrid.ItemsPanel;MigrateViewPreference();ProductsView.ModeChanged+=mode=>SetView(mode=="Lista");SetView(ProductsView.Mode=="Lista");RowActions.Attach(ProductsGrid,"Selecione um produto para criar pedido, editar ou inativar (ou clique com o botão direito).",CreateOrderButton,EditProductButton,ToggleProductButton,DeleteProductButton);remoteLoad=new(this,store,true,true);remoteLoad.Watch(ProductsGrid);remoteLoad.Cleared+=()=>{remoteRows.Clear();UpdateActionButtons();};if(store.IsRemote)ProductsGrid.ItemsSource=remoteRows;CategoryBox.ItemsSource=store.IsRemote?new[]{"Todas"}:new[]{"Todas"}.Concat(ProductOperations.AllCategories(store.Load()));CategoryBox.SelectedIndex=0;Loaded+=(_,_)=>Run(Refresh);}
    // A vista era gravada num arquivo próprio (produtos-<usuário>.txt); passa para o ViewToggle, como nas outras telas.
    void MigrateViewPreference(){var legacy=Path.Combine(AppProfile.Folder,"produtos-"+(store.AuthenticatedUser?.UserId.ToString("N")??"local")+".txt");try{if(!File.Exists(legacy))return;var old=File.ReadAllText(legacy).Trim();File.Delete(legacy);if(old is "Lista" or "Cards")ProductsView.Mode=old;}catch(IOException){}catch(UnauthorizedAccessException){}}
    void SetView(bool list){ListHeaders.Visibility=list?Visibility.Visible:Visibility.Collapsed;ProductsGrid.ItemTemplate=(System.Windows.DataTemplate)FindResource(list?"ProductList":"ProductCard");ProductsGrid.ItemContainerStyle=(Style)FindResource(list?"ProductListItem":"ProductItem");ProductsGrid.ItemsPanel=list?(System.Windows.Controls.ItemsPanelTemplate)FindResource("ProductListPanel"):cardPanel!;}
    async void Run(Func<Task> action){
        if(writeBusy)return;writeBusy=true;try{try{await action();}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
    void Run(Action action){try{action();}catch(Exception ex){AlertWindow.Info(this,"Produtos",ex.Message);}}
    async void Refresh(){
        if(store.IsRemote){var remoteCategory=CategoryBox.SelectedIndex>0?CategoryBox.SelectedItem?.ToString():null;
            var scoped=await remoteLoad.LoadAsync("produtos","&search="+ModuleLoader.Encode(SearchBox.Text)+"&category="+ModuleLoader.Encode(remoteCategory)+"&inactive="+(InactiveBox.IsChecked==true?"true":"false")+(NoCostBox.IsChecked==true?"&nocost=true":""),Refresh,true);if(scoped==null)return;
            changing=true;CategoryBox.ItemsSource=new[]{"Todas"}.Concat(scoped.ProductCategories??[]);CategoryBox.SelectedItem=remoteCategory??"Todas";changing=false;
            if(!remoteLoad.Appending)remoteRows.Clear();var ids=remoteRows.Select(x=>x.Id).ToHashSet();foreach(var product in scoped.Products)if(ids.Add(product.Id))remoteRows.Add(product);ResultCountText.Text=$"{remoteLoad.Result?.Total??scoped.Products.Count} itens";UpdateActionButtons();Restore();return;}
        var data=store.Load();if(data.Products.Any(x=>string.IsNullOrWhiteSpace(x.Category))){store.Update(d=>{var count=ProductOperations.FillMissingCategories(d.Products);if(count>0)AuditService.Record(d,App.CurrentUser,"Categorias de produtos preenchidas","Produto","categorias","",count.ToString(),"Somente categorias vazias",$"{count} produto(s) classificados; casos incertos marcados para Revisar.");return count;});data=store.Load();}search=new ProductSearch(data.Products.OrderBy(x=>x.Description));var category=CategoryBox.SelectedItem?.ToString();CategoryBox.ItemsSource=new[]{"Todas"}.Concat(ProductOperations.AllCategories(data));CategoryBox.SelectedItem=category??"Todas";if(CategoryBox.SelectedIndex<0)CategoryBox.SelectedIndex=0;Filter();}
    void Filter(){if(search==null)return;var results=search.Find(SearchBox.Text,InactiveBox.IsChecked!=true,int.MaxValue,CategoryBox.SelectedIndex>0?CategoryBox.SelectedItem?.ToString():null);if(NoCostBox.IsChecked==true)results=results.Where(x=>!ProductCostInfo.Of(x).HasCost).ToList();ProductsGrid.ItemsSource=results;ResultCountText.Text=results.Count==1?"1 item":$"{results.Count} itens";ProductsGrid.SelectedItem=null;UpdateActionButtons();Restore();}
    // Depois de editar, inativar ou excluir: volta para o mesmo produto (ou o de cima, se ele saiu da lista) em vez do topo.
    // Conectado ao servidor a lista vem de 30 em 30: carrega as próximas até achar.
    Guid[] keep=[];
    void Remember(Product? p){var list=ProductsGrid.Items.OfType<Product>().ToList();var i=p==null?-1:list.FindIndex(x=>x.Id==p.Id);keep=new Guid?[]{p?.Id,i>0?list[i-1].Id:i==0&&list.Count>1?list[1].Id:null}.OfType<Guid>().ToArray();}
    void Restore()
    {
        if(keep.Length==0)return;var items=ProductsGrid.Items.OfType<Product>().ToList();
        foreach(var id in keep){var found=items.FirstOrDefault(x=>x.Id==id);if(found==null)continue;keep=[];ProductsGrid.SelectedItem=found;ProductsGrid.ScrollIntoView(found);Dispatcher.BeginInvoke(()=>(ProductsGrid.ItemContainerGenerator.ContainerFromItem(found) as UIElement)?.Focus(),System.Windows.Threading.DispatcherPriority.Loaded);return;}
        if(store.IsRemote&&remoteLoad.HasMore){remoteLoad.LoadMore();return;}
        keep=[];
    }
    void Search_Changed(object s,RoutedEventArgs e){if(changing||remoteLoad==null)return;if(store.IsRemote){remoteLoad.Reset();Refresh();}else Filter();}
    void Product_DoubleClick(object s,System.Windows.Input.MouseButtonEventArgs e){if(System.Windows.Controls.ItemsControl.ContainerFromElement(ProductsGrid,e.OriginalSource as System.Windows.DependencyObject) is System.Windows.Controls.ListBoxItem { Content: Product p })Edit(p);}
    void Categories_Click(object s,RoutedEventArgs e)=>Run(()=>{new ProductCategoriesWindow(store){Owner=this}.ShowDialog();Refresh();});
    void New_Click(object s,RoutedEventArgs e)=>Edit(null);
    void Catalog_Click(object s,RoutedEventArgs e)=>Run(async ()=>{var all=await CatalogWindow.LoadProductsAsync(store);new CatalogWindow(store,all){Owner=this}.ShowDialog();});
    void Product_SelectionChanged(object s,System.Windows.Controls.SelectionChangedEventArgs e)=>UpdateActionButtons();
    void UpdateActionButtons(){var selected=ProductsGrid.SelectedItem as Product;var enabled=selected!=null;CreateOrderButton.IsEnabled=EditProductButton.IsEnabled=ToggleProductButton.IsEnabled=DeleteProductButton.IsEnabled=enabled;ToggleProductButton.Content=selected?.Active==false?"Ativar":"Inativar";}
    void CreateOrder_Click(object s,RoutedEventArgs e)=>Run(async ()=>{if(ProductsGrid.SelectedItem is not Product p)return;var dialog=new ScheduledOrderDialog(store,p){Owner=this};if(dialog.ShowDialog()!=true)return;await BusinessUi.SaveOrderAsync(store,dialog.Result!,dialog.CorrectionSaleId,dialog.CorrectedAmount,dialog.CorrectionReason);});
    void Edit_Click(object s,RoutedEventArgs e){if(ProductsGrid.SelectedItem is Product p)Edit(p);}
    void Edit(Product? p)=>Run(async ()=>{var dialog=new ProductEditor(p,DocumentPaths.TryRoot(store),store){Owner=this};if(dialog.ShowDialog()!=true)return;await ProductPhotos.SaveAsync(store,dialog.Result!,p,dialog.Photos,App.CurrentUser);ProductThumb.Forget(dialog.Result!.Id);Remember(p??dialog.Result);Refresh();});
    void Toggle_Click(object s,RoutedEventArgs e)=>Run(async ()=>{if(ProductsGrid.SelectedItem is not Product p)return;var copy=System.Text.Json.JsonSerializer.Deserialize<Product>(System.Text.Json.JsonSerializer.Serialize(p))!;copy.Active=!copy.Active;if(store.IsRemote)await store.RemoteDesktopWriteAsync("desktop/products",new DesktopProductCommand(Guid.NewGuid(),DataStore.DesktopRevision(p),copy));else store.Update(d=>{ProductOperations.Save(d,copy,p,App.CurrentUser);return 0;});Remember(p);Refresh();});
    void Delete_Click(object s,RoutedEventArgs e)=>Run(async ()=>{if(ProductsGrid.SelectedItem is not Product p)return;if(!AlertWindow.Confirm(this,"Excluir produto",$"Excluir {p.Description}? Produtos com vínculos serão preservados.",danger:true))return;await ProductPhotos.DeleteAsync(store,p,App.CurrentUser);Remember(p);Refresh();});
}
