using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace GraficaCaixa.Wpf;

// Mostra uma janela de módulo (Pedidos, Clientes, Produtos…) como área dentro da janela principal.
// O conteúdo da janela é transferido para a área da direita. A janela original continua existindo,
// invisível, transparente e sem ícone na barra de tarefas, posicionada sobre essa área: assim os diálogos
// abertos pelo módulo (Owner=this) continuam funcionando e aparecem centralizados sobre a área.
public sealed class PageNavigator
{
    readonly Window main;readonly Border area;readonly ContentControl host;
    public Window? Current{get;private set;}
    public event Action? Closed;
    public PageNavigator(Window main,Border area,ContentControl host)
    {
        this.main=main;this.area=area;this.host=host;
        main.LocationChanged+=(_,_)=>Sync();main.SizeChanged+=(_,_)=>Sync();main.StateChanged+=(_,_)=>Sync();area.SizeChanged+=(_,_)=>Sync();
    }
    public void Show(Window page)
    {
        Close();
        var content=page.Content as UIElement;page.Content=null;
        // Telas embutidas aproveitam a altura toda: a barra "Atualizado" da janela principal já separa do rodapé.
        if(content is FrameworkElement root&&root.Margin.Bottom>8)root.Margin=new Thickness(root.Margin.Left,root.Margin.Top,root.Margin.Right,8);
        var resources=new ResourceDictionary();foreach(var key in page.Resources.Keys)resources[key]=page.Resources[key];host.Resources=resources;
        host.FontSize=page.FontSize;host.FontFamily=page.FontFamily;
        // Cores do tema (a janela do módulo ainda não recebeu o tema neste momento), salvo se o módulo definiu as suas.
        if(page.ReadLocalValue(Control.ForegroundProperty)==DependencyProperty.UnsetValue)host.SetResourceReference(Control.ForegroundProperty,"Ink");else host.Foreground=page.Foreground;
        if(page.ReadLocalValue(Control.BackgroundProperty)==DependencyProperty.UnsetValue)area.SetResourceReference(Border.BackgroundProperty,"Paper");else area.Background=page.Background;
        host.Content=content;area.Visibility=Visibility.Visible;Motion.Enter(host,0,4,180);
        page.WindowStyle=WindowStyle.None;page.AllowsTransparency=true;page.Background=Brushes.Transparent;page.ShowInTaskbar=false;page.ShowActivated=false;
        page.ResizeMode=ResizeMode.NoResize;page.SizeToContent=SizeToContent.Manual;page.WindowState=WindowState.Normal;page.WindowStartupLocation=WindowStartupLocation.Manual;
        page.MinWidth=page.MinHeight=0;page.MaxWidth=page.MaxHeight=double.PositiveInfinity;page.IsHitTestVisible=false;page.Focusable=false;page.Owner=main;
        // Ao fechar um diálogo do módulo, o Windows ativa a janela invisível; devolve o foco para a principal.
        page.Activated+=(_,_)=>main.Dispatcher.BeginInvoke(()=>{if(main.IsVisible&&main.WindowState!=WindowState.Minimized)main.Activate();});
        page.Closed+=(_,_)=>{if(Current!=page)return;Current=null;host.Content=null;area.Visibility=Visibility.Collapsed;Closed?.Invoke();};
        // Dentro do menu, "Fechar" não faz sentido (o menu lateral troca de área): esconde esses botões.
        if(content!=null)foreach(var close in Descendants(content).OfType<Button>().Where(b=>b.Content is string s&&s.Trim()=="Fechar"))close.Visibility=Visibility.Collapsed;
        Current=page;main.UpdateLayout();Sync();page.Show();
        if(content is FrameworkElement element)element.Focus();
    }
    public void Close()
    {
        if(Current is not Window page)return;
        Current=null;host.Content=null;area.Visibility=Visibility.Collapsed;page.Close();
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root){foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()){yield return child;foreach(var d in Descendants(child))yield return d;}}
    void Sync()
    {
        if(Current is not Window page||!area.IsVisible||PresentationSource.FromVisual(area)?.CompositionTarget is not { } target)return;
        var topLeft=target.TransformFromDevice.Transform(area.PointToScreen(new Point(0,0)));
        page.Left=topLeft.X;page.Top=topLeft.Y;page.Width=Math.Max(1,area.ActualWidth);page.Height=Math.Max(1,area.ActualHeight);
    }
}
