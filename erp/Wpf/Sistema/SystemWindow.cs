using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace GraficaCaixa.Wpf;

// Uma ação do sistema (abre uma tela existente). Usada na página Sistema e na busca Ctrl+K.
public sealed record AppCommand(string Group,string Title,string Hint,string Icon,string Keywords,Action Run,string Shortcut="")
{
    public bool Matches(string term)=>term.Length==0||Fold($"{Group} {Title} {Hint} {Keywords} {Shortcut}").Contains(Fold(term));
    public static string Fold(string text)
    {
        var normalized=text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(normalized.Where(c=>System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }
}

// UX 2.0 (pacote 4): página "Sistema" (padrão P7) — grupos à esquerda com busca, conteúdo à direita.
// Aparência é ajustada aqui mesmo; as demais opções abrem as mesmas telas de antes (mesmas permissões, que
// a janela principal já aplicou ao montar a lista). Nada de configuração foi removido: o que estava no menu
// "Sistema" está aqui e também na busca Ctrl+K.
public sealed class SystemWindow : Window
{
    public sealed record Group(string Name,string Icon,string Hint);
    static readonly Group Appearance=new("Aparência","palette","Tema, cores, tamanho do texto, frase do dia e movimento. Vale só para este computador.");
    readonly List<Group> groups;readonly IReadOnlyList<AppCommand> commands;
    readonly TextBox search=new(){Tag="Buscar configuração…"};
    readonly ListBox nav=new(){BorderThickness=new Thickness(0),Background=System.Windows.Media.Brushes.Transparent};
    readonly StackPanel body=new();readonly TextBlock heading=new(){FontSize=20,FontWeight=FontWeights.SemiBold},headingHint=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,14)};

    public SystemWindow(IReadOnlyList<AppCommand> commands,IEnumerable<Group> groups,string? start=null)
    {
        this.commands=commands;this.groups=[Appearance,..groups.Where(g=>commands.Any(c=>c.Group==g.Name))];
        Title="Sistema";Width=1180;Height=760;
        var root=new Grid{Margin=new Thickness(28,22,28,24)};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());
        var title=new StackPanel{Margin=new Thickness(0,0,0,16)};title.Children.Add(new TextBlock{Text="Sistema",Style=(Style)FindResource("Title")});
        var sub=new TextBlock{Text="Configurações, usuários, segurança e aparência num só lugar · Ctrl+K encontra qualquer opção",Margin=new Thickness(0,4,0,0)};sub.SetResourceReference(TextBlock.ForegroundProperty,"Muted");title.Children.Add(sub);root.Children.Add(title);
        var columns=new Grid();Grid.SetRow(columns,1);root.Children.Add(columns);
        columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(270)});columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new DockPanel();var searchBox=new Grid{Margin=new Thickness(0,0,0,10)};searchBox.Children.Add(search);DockPanel.SetDock(searchBox,Dock.Top);left.Children.Add(searchBox);left.Children.Add(nav);
        columns.Children.Add(new Border{Style=(Style)FindResource("Card"),Padding=new Thickness(12),Child=left});
        var content=new StackPanel();content.Children.Add(heading);headingHint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(headingHint);content.Children.Add(body);
        var right=new Border{Style=(Style)FindResource("Card"),Child=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=false}};Grid.SetColumn(right,2);columns.Children.Add(right);
        Content=root;
        foreach(var g in this.groups)nav.Items.Add(NavItem(g));
        nav.SelectionChanged+=(_,_)=>{if(nav.SelectedItem is ListBoxItem{Tag:Group g})Show(g);};
        search.TextChanged+=(_,_)=>Filter();
        search.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Down){nav.Focus();(nav.ItemContainerGenerator.ContainerFromIndex(Math.Max(0,nav.SelectedIndex)) as ListBoxItem)?.Focus();e.Handled=true;}};
        var first=this.groups.FirstOrDefault(g=>g.Name==start)??this.groups[0];
        nav.SelectedIndex=this.groups.IndexOf(first);
        Loaded+=(_,_)=>search.Focus();
    }

    ListBoxItem NavItem(Group g)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(6,9,6,9)};var icon=new UiIcon{IconName=g.Icon,Width=18,Height=18,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};row.Children.Add(icon);
        row.Children.Add(new TextBlock{Text=g.Name,VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold});
        return new ListBoxItem{Content=row,Tag=g,Margin=new Thickness(0,0,0,2)};
    }

    void Filter()
    {
        var term=search.Text.Trim();
        foreach(ListBoxItem item in nav.Items){var g=(Group)item.Tag;item.Visibility=term.Length==0||AppCommand.Fold(g.Name+" "+g.Hint).Contains(AppCommand.Fold(term))||commands.Any(c=>c.Group==g.Name&&c.Matches(term))||g==Appearance&&AppearanceMatches(term)?Visibility.Visible:Visibility.Collapsed;}
        if(term.Length==0){if(nav.SelectedItem is ListBoxItem{Tag:Group g})Show(g);return;}
        // Busca: mostra à direita todas as opções encontradas, de qualquer grupo.
        heading.Text=$"Resultado para \"{term}\"";body.Children.Clear();
        var found=commands.Where(c=>c.Matches(term)).ToList();
        if(AppearanceMatches(term)){headingHint.Text="";AppearancePanel();}
        foreach(var c in found)body.Children.Add(Row(c,true));
        headingHint.Text=found.Count==0&&!AppearanceMatches(term)?"Nada encontrado. Tente outra palavra (ex.: senha, taxa, backup, tema).":"";
    }
    static bool AppearanceMatches(string term)=>AppCommand.Fold("aparência tema claro escuro cores viva sóbria tamanho do texto fonte grande movimento animação frase do dia").Contains(AppCommand.Fold(term));

    void Show(Group g)
    {
        if(search.Text.Trim().Length>0)return;
        heading.Text=g.Name;headingHint.Text=g.Hint;body.Children.Clear();
        if(g==Appearance){AppearancePanel();return;}
        foreach(var c in commands.Where(c=>c.Group==g.Name))body.Children.Add(Row(c,false));
    }

    // Linha de opção: ícone, título, explicação e "Abrir". Clique na linha também abre.
    Border Row(AppCommand c,bool showGroup)
    {
        var card=new Border{CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Padding=new Thickness(16,12,12,12),Margin=new Thickness(0,0,0,10),Cursor=Cursors.Hand};card.SetResourceReference(Border.BorderBrushProperty,"Line");card.SetResourceReference(Border.BackgroundProperty,"Surface");
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var badge=new Border{Width=38,Height=38,CornerRadius=new CornerRadius(10),Margin=new Thickness(0,0,14,0),VerticalAlignment=VerticalAlignment.Center,Child=new UiIcon{IconName=c.Icon,Width=19,Height=19}};badge.SetResourceReference(Border.BackgroundProperty,"AccentSoft");((UiIcon)badge.Child).SetResourceReference(UiIcon.ForegroundProperty,"AccentInk");grid.Children.Add(badge);
        var texts=new StackPanel{VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(texts,1);grid.Children.Add(texts);
        texts.Children.Add(new TextBlock{Text=c.Title,FontWeight=FontWeights.SemiBold,FontSize=14.5});
        var hint=new TextBlock{Text=(showGroup?c.Group+" · ":"")+c.Hint,TextWrapping=TextWrapping.Wrap,FontSize=12.5,Margin=new Thickness(0,2,0,0)};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");texts.Children.Add(hint);
        var open=new Button{Content="Abrir",Style=(Style)FindResource("SoftButton"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0),ToolTip=c.Title};open.Click+=(_,e)=>{e.Handled=true;c.Run();};Grid.SetColumn(open,2);grid.Children.Add(open);
        card.Child=grid;
        card.MouseEnter+=(_,_)=>card.SetResourceReference(Border.BackgroundProperty,"Hover");card.MouseLeave+=(_,_)=>card.SetResourceReference(Border.BackgroundProperty,"Surface");
        card.MouseLeftButtonUp+=(_,e)=>{if(e.OriginalSource is DependencyObject d&&FindParent<Button>(d)!=null)return;c.Run();};
        return card;
    }
    static T? FindParent<T>(DependencyObject d) where T:DependencyObject{for(var x=d;x!=null;x=System.Windows.Media.VisualTreeHelper.GetParent(x)??LogicalTreeHelper.GetParent(x))if(x is T t)return t;return null;}

    // Aparência: aplica na hora (mesmo motor do menu antigo Tema / Tamanho do texto) e grava em aparencia.txt.
    void AppearancePanel()
    {
        void Apply(AppTheme? theme=null,double? scale=null,AppPalette? colors=null,bool? motionOff=null)=>ThemeManager.Set(theme??ThemeManager.Theme,scale??ThemeManager.TextScale,colors??ThemeManager.Colors,motionOff??ThemeManager.MotionOff,ThemeManager.DailyPhrase);
        body.Children.Add(Setting("Tema","Automático segue o Windows.",Segments(new[]{("Automático",ThemeManager.Theme==AppTheme.Automatico,(Action)(()=>Apply(theme:AppTheme.Automatico))),("Claro",ThemeManager.Theme==AppTheme.Claro,()=>Apply(theme:AppTheme.Claro)),("Escuro",ThemeManager.Theme==AppTheme.Escuro,()=>Apply(theme:AppTheme.Escuro))})));
        body.Children.Add(Setting("Cores","Viva usa uma cor por tipo de cadastro (lançamento azul, pedido verde, produto violeta…). Sóbria usa a cor da marca em tudo.",Segments(new[]{("Viva",ThemeManager.Colors==AppPalette.Viva,(Action)(()=>Apply(colors:AppPalette.Viva))),("Sóbria",ThemeManager.Colors==AppPalette.Sobria,()=>Apply(colors:AppPalette.Sobria))})));
        body.Children.Add(Setting("Tamanho do texto","Aumenta textos e janelas.",Segments(new[]{("Normal",Math.Abs(ThemeManager.TextScale-1)<.01,(Action)(()=>Apply(scale:1))),("Grande",Math.Abs(ThemeManager.TextScale-1.12)<.01,()=>Apply(scale:1.12)),("Muito grande",Math.Abs(ThemeManager.TextScale-1.25)<.01,()=>Apply(scale:1.25))})));
        var motion=new CheckBox{Style=(Style)FindResource("Switch"),Content="Sem movimento",IsChecked=ThemeManager.MotionOff};motion.Click+=(_,_)=>Apply(motionOff:motion.IsChecked==true);
        var phrase=new CheckBox{Style=(Style)FindResource("Switch"),Content="Mostrar",IsChecked=ThemeManager.DailyPhrase};phrase.Click+=(_,_)=>ThemeManager.Set(ThemeManager.Theme,ThemeManager.TextScale,ThemeManager.Colors,ThemeManager.MotionOff,phrase.IsChecked==true);
        body.Children.Add(Setting("Frase do dia no Início","Uma frase curta por dia, abaixo da saudação. Muda à meia-noite e não se repete por meses.",phrase));
        body.Children.Add(Setting("Animações","Desliga as transições curtas (entrada de diálogos, avisos e cards, contagem dos números nos resumos). O Windows com \"efeitos de animação\" desligados também desliga.",motion));
    }
    FrameworkElement Segments(IEnumerable<(string Label,bool Checked,Action Click)> options)
    {
        var bar=new Border{Style=(Style)FindResource("SegmentBar")};var row=new StackPanel{Orientation=Orientation.Horizontal};bar.Child=row;var name="g"+Guid.NewGuid().ToString("N");
        foreach(var (label,isChecked,click) in options){var r=new RadioButton{Content=label,Style=(Style)FindResource("Segment"),GroupName=name,IsChecked=isChecked};r.Checked+=(_,_)=>{if(IsLoaded)click();};row.Children.Add(r);}
        return bar;
    }
    Border Setting(string title,string hint,FrameworkElement control)
    {
        var card=new Border{CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Padding=new Thickness(16,14,16,14),Margin=new Thickness(0,0,0,10)};card.SetResourceReference(Border.BorderBrushProperty,"Line");
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var texts=new StackPanel{Margin=new Thickness(0,0,16,0),VerticalAlignment=VerticalAlignment.Center};texts.Children.Add(new TextBlock{Text=title,FontWeight=FontWeights.SemiBold,FontSize=14.5});var h=new TextBlock{Text=hint,TextWrapping=TextWrapping.Wrap,FontSize=12.5,Margin=new Thickness(0,2,0,0)};h.SetResourceReference(TextBlock.ForegroundProperty,"Muted");texts.Children.Add(h);grid.Children.Add(texts);
        control.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(control,1);grid.Children.Add(control);card.Child=grid;return card;
    }
}
