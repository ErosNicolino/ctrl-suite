using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
// UX 2.0 (pacote 3): diálogo simples violeta. Mesma regra: só cria categorias (até 100 caracteres, sem repetir).
public sealed class ProductCategoriesWindow:Window
{
    bool writeBusy;
    readonly DataStore store;readonly TextBox name=new(){Tag="Nome da nova categoria"};readonly ListBox categories=new();readonly TextBlock feedback=new(){TextWrapping=TextWrapping.Wrap};readonly TextBox filter=new(){Tag="Procurar na lista…"};
    IReadOnlyList<string> all=[];
    public string? CreatedCategory{get;private set;}
    public ProductCategoriesWindow(DataStore store)
    {
        this.store=store;Title="Categorias de produtos e serviços";Width=620;Height=Math.Min(660,SystemParameters.WorkArea.Height-32);MinWidth=460;MinHeight=460;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var panel=new Grid();foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star)})panel.RowDefinitions.Add(new RowDefinition{Height=height});
        var create=new Grid();create.ColumnDefinitions.Add(new ColumnDefinition());create.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});panel.Children.Add(create);name.ToolTip="Nome da nova categoria";create.Children.Add(name);
        var add=new Button{Content="Criar categoria",Style=(Style)FindResource("DialogPrimaryButton"),MinHeight=40,Padding=new Thickness(16,6,16,6),Margin=new Thickness(10,0,0,0)};Grid.SetColumn(add,1);create.Children.Add(add);add.Click+=(_,_)=>CreateCategory();
        name.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;CreateCategory();}};
        feedback.Margin=new Thickness(0,8,0,8);Grid.SetRow(feedback,1);panel.Children.Add(feedback);
        filter.Margin=new Thickness(0,4,0,8);Grid.SetRow(filter,2);panel.Children.Add(filter);filter.TextChanged+=(_,_)=>ApplyFilter();
        var categoryText=new FrameworkElementFactory(typeof(TextBlock));categoryText.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding());categoryText.SetValue(FrameworkElement.MarginProperty,new Thickness(12,9,12,9));categoryText.SetValue(TextBlock.FontSizeProperty,14d);categories.ItemTemplate=new DataTemplate{VisualTree=categoryText};categories.BorderThickness=new Thickness(1);categories.SetResourceReference(Control.BorderBrushProperty,"Line");categories.Padding=new Thickness(4);
        Loaded+=async (_,_)=>{try{all=ProductOperations.AllCategories(await store.BusinessContextAsync("product")).ToList();ApplyFilter();}catch(Exception ex){feedback.Text=ex.Message;}name.Focus();};Grid.SetRow(categories,3);panel.Children.Add(categories);
        var footer=new StackPanel{Orientation=Orientation.Horizontal};var close=new Button{Content="Concluir",Style=(Style)FindResource("SoftButton")};close.Click+=(_,_)=>Close();footer.Children.Add(close);
        Content=new DialogFrame{Kind="Produto",IconName="tag",Title="Categorias",Subtitle="Crie grupos para organizar produtos e serviços · Esc fecha",Content=panel,Footer=footer,ScrollBody=false};
    }
    void ApplyFilter(){var term=filter.Text.Trim();categories.ItemsSource=term.Length==0?all:all.Where(x=>x.Contains(term,StringComparison.CurrentCultureIgnoreCase)).ToList();if(CreatedCategory!=null)categories.SelectedItem=CreatedCategory;}
    async void CreateCategory()
    {
        if(writeBusy)return;writeBusy=true;try{
        try{CreatedCategory=await BusinessUi.ExecuteAsync(store,"product.category",Guid.Empty,null,new CategoryArgs(name.Text),()=>store.Update(d=>ProductOperations.CreateCategory(d,name.Text,App.CurrentUser)));var data=await store.BusinessContextAsync("product");if(data.ProductCategories?.Contains(CreatedCategory)!=true)throw new InvalidOperationException("Atualize o ERP do computador principal para salvar as novas categorias.");all=ProductOperations.AllCategories(data).ToList();filter.Clear();ApplyFilter();categories.ScrollIntoView(CreatedCategory);name.Clear();feedback.SetResourceReference(TextBlock.ForegroundProperty,"AccentInk");feedback.Text=$"Categoria “{CreatedCategory}” criada.";Toast.Show(this,$"Categoria “{CreatedCategory}” criada.",tone:"ok");}
        catch(Exception ex){feedback.SetResourceReference(TextBlock.ForegroundProperty,"Danger");feedback.Text=ex.Message;Ui.SetHasError(name,true);name.Focus();}
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
}
