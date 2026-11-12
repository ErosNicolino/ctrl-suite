using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace GraficaCaixa.Wpf;

// UX 2.0 (pacote 4): busca Ctrl+K em dropdown sob a barra de busca do topo — encontra telas, ações ("Novo pedido"), configurações e atalhos pelo nome,
// sem acento e em qualquer parte do texto. ↑/↓ escolhe, Enter abre, Esc fecha. Só lista o que o usuário pode abrir
// (a janela principal monta a lista com as mesmas permissões do menu). Não grava nada.
public sealed class CommandPalette : Window
{
    readonly IReadOnlyList<AppCommand> commands;readonly Dictionary<AppCommand,int> position=new(ReferenceEqualityComparer.Instance);
    readonly TextBox search=new(){Tag="Buscar telas, configurações e ações…",FontSize=16,Height=46,BorderThickness=new Thickness(0)};
    readonly ListBox list=new(){BorderThickness=new Thickness(0),Background=Brushes.Transparent,MaxHeight=420};
    readonly TextBlock empty=new(){Text="Nada encontrado. Tente outra palavra (ex.: senha, taxa, backup, devedores).",Margin=new Thickness(18,6,18,16),TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed};
    AppCommand? chosen;FrameworkElement? anchor;

    public CommandPalette(IReadOnlyList<AppCommand> commands)
    {
        this.commands=commands;for(var i=0;i<commands.Count;i++)position[commands[i]]=i;
        Title="Buscar";Width=640;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.Manual;
        var panel=new StackPanel();
        var top=new DockPanel{Margin=new Thickness(16,6,12,6)};var icon=new UiIcon{IconName="search",Width=20,Height=20,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center};icon.SetResourceReference(UiIcon.ForegroundProperty,"Muted");top.Children.Add(icon);
        var esc=new Border{Style=(Style)FindResource("Kbd"),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text="Esc"}};DockPanel.SetDock(esc,Dock.Right);top.Children.Add(esc);search.Background=Brushes.Transparent;top.Children.Add(search);panel.Children.Add(top);
        var line=new Border{Height=1};line.SetResourceReference(Border.BackgroundProperty,"Line");panel.Children.Add(line);
        list.Margin=new Thickness(8,8,8,8);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);panel.Children.Add(list);
        empty.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(empty);
        var foot=new TextBlock{Text="↑ ↓ escolher  ·  Enter abrir  ·  Esc fechar",FontSize=12,Margin=new Thickness(18,0,18,12)};foot.SetResourceReference(TextBlock.ForegroundProperty,"Muted");panel.Children.Add(foot);
        var card=new Border{CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1),Child=panel,Margin=new Thickness(24),Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=30,ShadowDepth=8,Direction=270,Opacity=.25}};
        card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Line");
        Content=card;
        search.TextChanged+=(_,_)=>Fill();
        PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.Escape){e.Handled=true;Dismiss();}
            else if(e.Key==Key.Down){Move(1);e.Handled=true;}
            else if(e.Key==Key.Up){Move(-1);e.Handled=true;}
            else if(e.Key==Key.Enter){Run();e.Handled=true;}
        };
        list.MouseLeftButtonUp+=(_,_)=>Run();
        // Clicar fora fecha; adiado para não fechar no meio do próprio evento de ativação.
        Deactivated+=(_,_)=>Dispatcher.BeginInvoke(Dismiss);
        Closing+=(_,_)=>closing=true;
        Loaded+=(_,_)=>{Place();Motion.Enter(card,0,-6,180);search.Focus();};
        Fill();
    }
    // Abre a busca; a ação escolhida roda depois que a busca fecha (para os diálogos abrirem sobre a janela principal).
    public static void Open(Window owner,IReadOnlyList<AppCommand> commands,FrameworkElement? anchor=null)
    {
        var palette=new CommandPalette(commands){Owner=owner,anchor=anchor};
        palette.ShowDialog();
        if(palette.chosen is AppCommand c)owner.Dispatcher.BeginInvoke(c.Run);
    }
    // Dropdown ancorado: o campo da busca fica exatamente sobre a barra de busca do cabeçalho (mesma esquerda e topo), e a lista abre logo abaixo.
    // Sem âncora (ex.: chamada de outro lugar), centraliza no topo da janela.
    void Place()
    {
        const double shadow=24;if(Owner==null)return;
        if(anchor is {IsVisible:true}&&PresentationSource.FromVisual(anchor)?.CompositionTarget is {} target)
        {
            var p=target.TransformFromDevice.Transform(anchor.PointToScreen(new Point(0,0)));Width=Math.Max(anchor.ActualWidth,560)+shadow*2;
            var area=SystemParameters.WorkArea;Left=Math.Min(p.X-shadow,area.Right-Width);Top=p.Y-shadow-(46+12-anchor.ActualHeight)/2;return;
        }
        Left=Owner.Left+(Owner.ActualWidth-ActualWidth)/2;Top=Owner.Top+Math.Min(110,Owner.ActualHeight*.12);
    }
    void Move(int step)
    {
        for(var i=list.SelectedIndex+step;i>=0&&i<list.Items.Count;i+=step)if(list.Items[i] is ListBoxItem{Tag:AppCommand}){list.SelectedIndex=i;list.ScrollIntoView(list.Items[i]);return;}
    }
    void Run(){if(list.SelectedItem is not ListBoxItem{Tag:AppCommand c})return;chosen??=c;Dismiss();}
    // Único caminho de fechamento (Esc, Enter, clique no item, clique fora): roda uma vez só.
    // Fechar perde o foco e dispara Deactivated; sem a flag, Close() era chamado de novo com a janela já fechando.
    bool closing;
    void Dismiss(){if(closing||!IsLoaded)return;closing=true;Close();}
    void Fill()
    {
        var term=search.Text.Trim();list.Items.Clear();
        // Ordem: título que começa com o termo, depois título que contém, depois o resto (dicas e palavras-chave).
        var folded=AppCommand.Fold(term);
        var found=commands.Where(c=>c.Matches(term)).OrderBy(c=>term.Length==0?0:AppCommand.Fold(c.Title).StartsWith(folded)?0:AppCommand.Fold(c.Title).Contains(folded)?1:2).ThenBy(c=>position[c]).Take(term.Length==0?14:40).ToList();
        string? group=null;
        foreach(var c in found)
        {
            if(term.Length==0&&c.Group!=group){group=c.Group;list.Items.Add(new ListBoxItem{Content=new TextBlock{Text=c.Group.ToUpperInvariant(),Style=(Style)FindResource("Eyebrow"),Margin=new Thickness(6,8,0,2)},IsEnabled=false,Focusable=false});}
            list.Items.Add(Item(c,term.Length==0));
        }
        list.SelectedIndex=list.Items.Cast<ListBoxItem>().ToList().FindIndex(x=>x.Tag is AppCommand);
        empty.Visibility=found.Count==0?Visibility.Visible:Visibility.Collapsed;list.Visibility=found.Count==0?Visibility.Collapsed:Visibility.Visible;
    }
    ListBoxItem Item(AppCommand c,bool grouped)
    {
        var row=new DockPanel{Margin=new Thickness(4,5,6,5)};
        var badge=new Border{Width=32,Height=32,CornerRadius=new CornerRadius(9),Margin=new Thickness(0,0,12,0),Child=new UiIcon{IconName=c.Icon,Width=17,Height=17}};badge.SetResourceReference(Border.BackgroundProperty,"SurfaceAlt");DockPanel.SetDock(badge,Dock.Left);row.Children.Add(badge);
        if(c.Shortcut.Length>0){var k=new Border{Style=(Style)FindResource("Kbd"),VerticalAlignment=VerticalAlignment.Center,Child=new TextBlock{Text=c.Shortcut}};DockPanel.SetDock(k,Dock.Right);row.Children.Add(k);}
        var texts=new StackPanel{VerticalAlignment=VerticalAlignment.Center};texts.Children.Add(new TextBlock{Text=c.Title,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis});
        var hint=new TextBlock{Text=grouped?c.Hint:c.Group+" · "+c.Hint,FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");texts.Children.Add(hint);row.Children.Add(texts);
        return new ListBoxItem{Content=row,Tag=c,Cursor=Cursors.Hand};
    }
}
