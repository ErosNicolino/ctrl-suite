using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;

// Ao salvar pedido ou lançamento com item digitado que não está no cadastro, pergunta o que fazer:
// cadastrar como produto novo (já preenchido), escolher/editar um produto existente ou manter só como texto.
public static class UnregisteredItems
{
    public sealed record Pending(string Text,int Quantity,decimal? UnitPrice);

    // Uma resposta por item (null = manter como texto). Retorna null se o usuário preferir voltar ao formulário.
    public static async Task<List<Product?>?> ResolveAsync(Window owner,DataStore store,IReadOnlyList<Pending> items)
    {
        var chosen=new List<Product?>();
        for(var i=0;i<items.Count;)
        {
            var item=items[i];
            var ask=new UnregisteredItemDialog(item,i+1,items.Count){Owner=owner};
            if(ask.ShowDialog()!=true)return null;
            Product? product=null;
            try
            {
                if(ask.Selected==UnregisteredItemDialog.Choice.Create)
                {
                    product=await ProductCreation.CreateAsync(owner,store,item.Text,item.UnitPrice);
                    if(product==null)continue; // fechou o cadastro: pergunta de novo o mesmo item
                }
                else if(ask.Selected==UnregisteredItemDialog.Choice.Existing)
                {
                    var picker=new ProductPickerDialog(store,item.Text,item.Quantity){Owner=owner};
                    if(picker.ShowDialog()!=true||picker.Selected==null)continue;
                    product=picker.Selected;
                    if(picker.EditFirst)
                    {
                        var editor=new ProductEditor(product,DocumentPaths.TryRoot(store),store){Owner=owner};
                        if(editor.ShowDialog()!=true)continue;
                        await ProductPhotos.SaveAsync(store,editor.Result!,product,editor.Photos,App.CurrentUser);
                        product=editor.Result!;
                    }
                }
            }
            catch(Exception ex){AlertWindow.Info(owner,"Produto",ex.Message);continue;}
            chosen.Add(product);i++;
        }
        return chosen;
    }

    // Busca no cadastro: no PC conectado consulta o servidor; no principal, o banco local.
    public static async Task<List<Product>> FindAsync(DataStore store,string query)
    {
        query=query.Trim();
        if(store.IsRemote)return await store.QueryRemoteAsync<List<Product>>("desktop/lookup/products?search="+Uri.EscapeDataString(query));
        var products=store.Load().Products;
        IEnumerable<Product> found=query.Length==0?products.Where(x=>x.Active).OrderBy(x=>x.Description):new ProductSearch(products).Find(query);
        return found.Take(40).ToList();
    }
}

public sealed class UnregisteredItemDialog:Window
{
    public enum Choice{Keep,Create,Existing}
    public Choice Selected{get;private set;}=Choice.Keep;
    public UnregisteredItemDialog(UnregisteredItems.Pending item,int number,int total)
    {
        // UX 2.0 (pacote 3): diálogo P5 com opções em cartões (ícone + título + explicação). Mesmas 3 escolhas e "Voltar".
        Title="Item fora do cadastro";Width=Math.Min(600,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var panel=new StackPanel();
        var price=item.UnitPrice is decimal p?$" • {p.ToString("C2",CultureInfo.GetCultureInfo("pt-BR"))}/un.":"";
        var itemBox=new Border{CornerRadius=new CornerRadius(10),Padding=new Thickness(14,10,14,10),Margin=new Thickness(0,0,0,10)};itemBox.SetResourceReference(Border.BackgroundProperty,"SurfaceAlt");
        itemBox.Child=new TextBlock{Text=$"{item.Quantity} × \"{item.Text}\"{price}",FontSize=15,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};panel.Children.Add(itemBox);
        panel.Children.Add(DialogKit.Hint("Ligar o item a um produto do cadastro traz o preço e o custo certos e entra nos relatórios por produto.",0));
        var options=new StackPanel{Margin=new Thickness(0,14,0,0)};panel.Children.Add(options);
        Button Option(string icon,string text,string hint,Choice choice,bool primary=false)
        {
            var row=new DockPanel{LastChildFill=true};var badge=new UiIcon{IconName=icon,Width=20,Height=20,Margin=new Thickness(0,0,14,0),VerticalAlignment=VerticalAlignment.Center};badge.SetResourceReference(UiIcon.ForegroundProperty,primary?"KindProduto":"Muted");row.Children.Add(badge);
            var go=new UiIcon{IconName="chevron-right",Width=16,Height=16,VerticalAlignment=VerticalAlignment.Center};go.SetResourceReference(UiIcon.ForegroundProperty,"Muted");DockPanel.SetDock(go,Dock.Right);row.Children.Add(go);
            var texts=new StackPanel();texts.Children.Add(new TextBlock{Text=text,FontWeight=FontWeights.SemiBold,FontSize=14});var h=new TextBlock{Text=hint,FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,0)};h.SetResourceReference(TextBlock.ForegroundProperty,"Muted");texts.Children.Add(h);row.Children.Add(texts);
            var b=new Button{Content=row,Style=(Style)FindResource("SoftButton"),Margin=new Thickness(0,0,0,8),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(16,12,16,12)};
            b.SizeChanged+=(_,_)=>row.Width=Math.Max(0,b.ActualWidth-b.Padding.Left-b.Padding.Right-2);
            if(primary){b.SetResourceReference(BorderBrushProperty,"KindProduto");b.BorderThickness=new Thickness(1.5);}
            b.Click+=(_,_)=>{Selected=choice;DialogResult=true;};options.Children.Add(b);return b;
        }
        var create=Option("package","Cadastrar como produto novo","Abre o cadastro já preenchido com o nome e o preço.",Choice.Create,true);create.IsDefault=true;
        Option("search","Escolher ou editar um produto existente","Procura no cadastro; pode ajustar antes de usar.",Choice.Existing);
        Option("file-text","Manter só aqui, sem cadastrar","O item fica só como texto neste lançamento/pedido.",Choice.Keep);
        var footer=new StackPanel{Orientation=Orientation.Horizontal};var back=new Button{Content="Voltar ao formulário",Style=(Style)FindResource("GhostButton"),ToolTip="Esc"};back.Click+=(_,_)=>DialogResult=false;footer.Children.Add(back);
        Content=new DialogFrame{Kind="Produto",IconName="package",Title=total>1?$"Item {number} de {total} fora do cadastro":"Item fora do cadastro",Subtitle="O que fazer com este item?",Content=panel,Footer=footer,ScrollBody=false};
        Loaded+=(_,_)=>create.Focus();
    }
}

public sealed class ProductPickerDialog:Window
{
    // Sem preço unitário para esta quantidade: mostra a tabela do produto como na tela Produtos ("R$ 80,00 por 1.224 un", "a partir de…").
    static readonly ProductCardText cardText=new();
    static string TablePrice(Product p){var price=cardText.Convert(p,typeof(string),"price",CultureInfo.CurrentCulture) as string;return price is null or "Sem preço"?"sem preço":$"{price} {cardText.Convert(p,typeof(string),"basis",CultureInfo.CurrentCulture)}";}
    readonly DataStore store;readonly int quantity;readonly TextBox search=new();readonly ListBox list=new(){MinHeight=260,MaxHeight=380};readonly TextBlock status=new(){Margin=new Thickness(0,6,0,0)};
    int generation;
    public Product? Selected{get;private set;} public bool EditFirst{get;private set;}
    public sealed record Row(Product Product,string Name,string Detail){public override string ToString()=>Name+"  •  "+Detail;}
    public ProductPickerDialog(DataStore store,string query,int quantity)
    {
        // UX 2.0 (pacote 3): diálogo violeta com busca e lista; mesmas ações (Usar este / Editar e usar / Cancelar).
        this.store=store;this.quantity=quantity;Title="Escolher produto do cadastro";Width=Math.Min(680,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var panel=new StackPanel();search.Tag="Nome, categoria ou parte da descrição";
        panel.Children.Add(DialogKit.LabeledField("Pesquisar",search).Panel);
        list.ItemTemplate=(DataTemplate)System.Windows.Markup.XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Margin='6,5,6,5'><TextBlock Text='{Binding Name}' FontWeight='SemiBold' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding Detail}' FontSize='12' Margin='0,2,0,0' Foreground='{DynamicResource Muted}' TextTrimming='CharacterEllipsis'/></StackPanel></DataTemplate>");ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);list.BorderThickness=new Thickness(0);list.Background=System.Windows.Media.Brushes.Transparent;
        var frame=new Border{Child=list,CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1),Padding=new Thickness(4)};frame.SetResourceReference(Border.BorderBrushProperty,"Line");panel.Children.Add(frame);
        status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");status.FontSize=12;status.Margin=new Thickness(2,8,0,0);panel.Children.Add(status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};
        void Button(string text,string style,Action click){var b=new Button{Content=text,Style=(Style)FindResource(style),Margin=new Thickness(8,0,0,0)};b.Click+=(_,_)=>click();buttons.Children.Add(b);}
        Button("Cancelar","GhostButton",()=>DialogResult=false);
        Button("Editar e usar","SoftButton",()=>Pick(true));
        Button("Usar este","DialogPrimaryButton",()=>Pick(false));
        Content=new DialogFrame{Kind="Produto",IconName="search",Title="Escolher produto do cadastro",Subtitle="Duplo clique ou Enter usa o produto selecionado",Content=panel,Footer=buttons,ScrollBody=false};
        list.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Pick(false);e.Handled=true;}};
        search.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Down&&list.Items.Count>0){list.Focus();(list.ItemContainerGenerator.ContainerFromIndex(Math.Max(0,list.SelectedIndex)) as ListBoxItem)?.Focus();e.Handled=true;}};
        list.MouseDoubleClick+=(_,_)=>Pick(false);
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};timer.Tick+=(_,_)=>{timer.Stop();Search();};
        search.TextChanged+=(_,_)=>{timer.Stop();timer.Start();};
        search.Text=query;Loaded+=(_,_)=>{search.Focus();search.SelectAll();Search();};
    }
    async void Search()
    {
        var ticket=++generation;status.Text="Procurando…";
        try
        {
            var found=await UnregisteredItems.FindAsync(store,search.Text);if(ticket!=generation)return;
            var pt=CultureInfo.GetCultureInfo("pt-BR");
            list.ItemsSource=found.Select(p=>new Row(p,p.Description,(QuantityPricing.UnitPrice(p,quantity) is decimal v?$"{v.ToString("C2",pt)}/un.":TablePrice(p))+(p.Cost is decimal c?$"  •  custo {c.ToString("C2",pt)}":"")+(p.Active?"":"  •  inativo"))).ToList();
            list.SelectedIndex=found.Count>0?0:-1;status.Text=found.Count==0?"Nada encontrado. Tente outra palavra ou cadastre como produto novo.":$"{found.Count} produto(s).";
        }
        catch(Exception ex){if(ticket==generation)status.Text="Não foi possível pesquisar: "+ex.Message;}
    }
    void Pick(bool edit){if(list.SelectedItem is not Row row){status.Text="Selecione um produto da lista.";return;}Selected=row.Product;EditFirst=edit;DialogResult=true;}
}
