using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace GraficaCaixa.Wpf;

// UX 2.0 (pacote 2): menu "+ Novo" e atalhos L (novo lançamento) e N (novo pedido).
// Cada opção chama exatamente o mesmo fluxo que já existia (mesmas telas, validações e gravações).
public partial class MainWindow
{
    Popup? newMenu;
    void NewMenu_Click(object s,RoutedEventArgs e)=>ShowNewMenu((UIElement)s);
    void ShowNewMenu(UIElement target)
    {
        if(newMenu?.IsOpen==true){newMenu.IsOpen=false;return;}
        var list=new StackPanel{Margin=new Thickness(6)};
        list.Children.Add(MenuEntry("Novo lançamento","Venda paga na hora, entra no caixa","L","wallet","Accent",()=>Add_Click(this,new RoutedEventArgs())));
        list.Children.Add(MenuEntry("Novo pedido","Produção com prazo, sinal e saldo","N","clipboard-list","KPed",NewOrder));
        list.Children.Add(new TextBlock{Text="CADASTROS",Style=(Style)FindResource("Eyebrow"),Margin=new Thickness(12,10,0,4)});
        var grid=new UniformGrid{Columns=2};
        if(Can("view.products")||!store.IsRemote)grid.Children.Add(SmallEntry("Produto","package","KProd",NewProduct));
        if(Can("view.purchases")||!store.IsRemote)grid.Children.Add(SmallEntry("Compra","shopping-cart","KBuy",()=>{PurchaseNeeds_Click(this,new RoutedEventArgs());(pages.Current as PurchaseNeedsWindow)?.StartNew();}));
        if(Can("view.reminders")||!store.IsRemote)grid.Children.Add(SmallEntry("Lembrete","bell","KRem",()=>{Reminders_Click(this,new RoutedEventArgs());(pages.Current as RemindersWindow)?.StartNew();}));
        grid.Children.Add(SmallEntry("Recibo avulso","receipt","KRec",()=>Receipt_Click(this,new RoutedEventArgs())));
        list.Children.Add(grid);
        var card=new Border{CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Width=390,Child=list,Margin=new Thickness(0,4,12,14),
            Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=24,ShadowDepth=6,Direction=270,Opacity=.18}};
        card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Line");
        newMenu=new Popup{PlacementTarget=target,Placement=PlacementMode.Bottom,HorizontalOffset=-(390-(target as FrameworkElement)?.ActualWidth??0),StaysOpen=false,AllowsTransparency=true,Child=card,PopupAnimation=ThemeManager.MotionEnabled?PopupAnimation.Fade:PopupAnimation.None};
        card.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;newMenu.IsOpen=false;}};
        newMenu.IsOpen=true;
        newMenu.Opened+=(_,_)=>list.Children.OfType<Button>().FirstOrDefault()?.Focus();
    }
    Button MenuEntry(string title,string subtitle,string key,string icon,string color,Action action)
    {
        var badge=new Border{Width=36,Height=36,CornerRadius=new CornerRadius(10),Margin=new Thickness(0,0,12,0),Child=new UiIcon{IconName=icon,Width=19,Height=19,Plain=true}};
        badge.SetResourceReference(Border.BackgroundProperty,color);((UiIcon)badge.Child).SetResourceReference(UiIcon.ForegroundProperty,"OnK");
        var texts=new StackPanel{VerticalAlignment=VerticalAlignment.Center};texts.Children.Add(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold});var sub=new TextBlock{Text=subtitle,FontSize=12.5};sub.SetResourceReference(TextBlock.ForegroundProperty,"Muted");texts.Children.Add(sub);
        var kbd=new Border{Style=(Style)FindResource("Kbd"),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text=key}};
        var row=new DockPanel();DockPanel.SetDock(badge,Dock.Left);DockPanel.SetDock(kbd,Dock.Right);row.Children.Add(badge);row.Children.Add(kbd);row.Children.Add(texts);
        return Entry(row,color,4,new Thickness(10,9,12,9),action);
    }
    Button SmallEntry(string title,string icon,string color,Action action)
    {
        var badge=new Border{Width=26,Height=26,CornerRadius=new CornerRadius(8),Margin=new Thickness(0,0,8,0),Child=new UiIcon{IconName=icon,Width=15,Height=15,Plain=true}};
        badge.SetResourceReference(Border.BackgroundProperty,color);((UiIcon)badge.Child).SetResourceReference(UiIcon.ForegroundProperty,"OnK");
        var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(badge);row.Children.Add(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        return Entry(row,color,3,new Thickness(8,7,8,7),action);
    }
    Button Entry(UIElement content,string color,double stripe,Thickness padding,Action action)
    {
        var button=new Button{Content=content,Cursor=Cursors.Hand,Margin=new Thickness(0,2,0,2),HorizontalContentAlignment=HorizontalAlignment.Stretch,FocusVisualStyle=(Style)FindResource("FocusRingVisual")};
        var factory=new FrameworkElementFactory(typeof(Border),"B");factory.SetValue(Border.CornerRadiusProperty,new CornerRadius(10));factory.SetValue(Border.BorderThicknessProperty,new Thickness(stripe,0,0,0));factory.SetValue(Border.PaddingProperty,padding);
        factory.SetResourceReference(Border.BorderBrushProperty,color);factory.SetValue(Border.BackgroundProperty,Brushes.Transparent);
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));factory.AppendChild(presenter);
        var template=new ControlTemplate(typeof(Button)){VisualTree=factory};
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BackgroundProperty,new DynamicResourceExtension("SurfaceAlt"),"B"));template.Triggers.Add(hover);
        var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BackgroundProperty,new DynamicResourceExtension("SurfaceAlt"),"B"));template.Triggers.Add(focus);
        button.Template=template;button.SetResourceReference(ForegroundProperty,"Ink");
        button.Click+=(_,_)=>{if(newMenu!=null)newMenu.IsOpen=false;action();};
        return button;
    }
    // Novo pedido pelo menu ou pela tecla N: mesmo diálogo e mesma gravação do botão "＋ Novo pedido" da tela de Pedidos.
    internal async void NewOrder()
    {
        if(writeBusy)return;writeBusy=true;
        try
        {
            var dialog=new ScheduledOrderDialog(store){Owner=this};
            if(dialog.ShowDialog()==true){await SaveOrder(dialog);Toast.Show(this,"Pedido salvo.",tone:"ok");if(pages.Current is ScheduledOrdersWindow or OrderBoardWindow)OpenOrders();else RefreshData(false);}
        }
        catch(OperationCanceledException){}
        catch(Exception ex){AlertWindow.Info(this,"Não foi possível salvar o pedido",ex.Message);}
        finally{writeBusy=false;}
    }
    async void NewProduct()
    {
        try{var product=await ProductCreation.CreateAsync(this,store);if(product!=null)Toast.Show(this,$"Produto \"{product.Description}\" cadastrado.",tone:"ok");}
        catch(Exception ex){AlertWindow.Info(this,"Novo produto",ex.Message);}
    }
    // L e N só valem fora de campos de texto, sem modificadores e sem diálogo aberto (protótipo).
    static bool Typing()=>Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox or ComboBox{IsEditable:true};
}
