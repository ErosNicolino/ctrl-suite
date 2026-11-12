using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace GraficaCaixa.Wpf;

public partial class ComponentCatalogWindow:Window
{
    public ComponentCatalogWindow()
    {
        InitializeComponent();
        foreach(var key in ThemeManager.TokenKeys)
        {
            var swatch=new Border{Width=34,Height=34,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,8,0)};
            swatch.SetResourceReference(Border.BackgroundProperty,key);swatch.SetResourceReference(Border.BorderBrushProperty,"Line");
            var item=new StackPanel{Orientation=Orientation.Horizontal,Width=170,Margin=new Thickness(0,0,8,8)};item.Children.Add(swatch);item.Children.Add(new TextBlock{Text=key,VerticalAlignment=VerticalAlignment.Center,FontSize=12.5});
            Swatches.Children.Add(item);
        }
        foreach(var name in UiIconData.Paths.Keys.OrderBy(x=>x))
        {
            var icon=new UiIcon{IconName=name,Width=22,Height=22};icon.SetResourceReference(UiIcon.ForegroundProperty,"Ink");
            var cell=new StackPanel{Width=110,Margin=new Thickness(0,0,6,12)};cell.Children.Add(icon);
            var label=new TextBlock{Text=name,FontSize=11,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,4,0,0),TextTrimming=TextTrimming.CharacterEllipsis};label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");cell.Children.Add(label);
            Icons.Children.Add(cell);
        }
        SampleGrid.ItemsSource=new[]{new{Hora="13:15",Cliente="Cliente Exemplo",Pagamento="PIX",Total="R$ 60,00"},new{Hora="13:04",Cliente="Outra Cliente",Pagamento="Dinheiro",Total="R$ 37,70"},new{Hora="12:40",Cliente="Mais Um Cliente",Pagamento="Crédito 1x",Total="R$ 120,00"}};
        Loaded+=(_,_)=>{int i=0;foreach(UIElement child in Body.Children)Motion.Enter(child,Math.Min(160,40*i++));};
    }
    void Theme_Click(object s,RoutedEventArgs e)=>ThemeManager.Set(ThemeManager.IsDark?AppTheme.Claro:AppTheme.Escuro,ThemeManager.TextScale,ThemeManager.Colors,ThemeManager.MotionOff,ThemeManager.DailyPhrase);
    void Palette_Click(object s,RoutedEventArgs e)=>ThemeManager.Set(ThemeManager.Theme,ThemeManager.TextScale,ThemeManager.Colors==AppPalette.Viva?AppPalette.Sobria:AppPalette.Viva,ThemeManager.MotionOff,ThemeManager.DailyPhrase);
    void Motion_Click(object s,RoutedEventArgs e){ThemeManager.Set(ThemeManager.Theme,ThemeManager.TextScale,ThemeManager.Colors,!ThemeManager.MotionOff,ThemeManager.DailyPhrase);Toast.Show(this,ThemeManager.MotionOff?"Sem movimento: animações desligadas.":"Movimento leve ligado.");}
    void Toast_Click(object s,RoutedEventArgs e)=>Toast.Show(this,"Pedido #116 salvo.",tone:"ok");
    void ToastUndo_Click(object s,RoutedEventArgs e)=>Toast.Show(this,"Pedido #116 avançou para Produção.","Desfazer",()=>Toast.Show(this,"Etapa desfeita."),"ok");
    void Dialog_Click(object s,RoutedEventArgs e)=>new SampleDialog{Owner=this}.ShowDialog();
}

// Exemplo do diálogo central (só para o catálogo; não grava nada).
sealed class SampleDialog:Window
{
    public SampleDialog()
    {
        Title="Novo pedido (exemplo)";Width=980;Height=680;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.CanResizeWithGrip;
        var form=new StackPanel();
        form.Children.Add(new SectionCard{Number="1",Header="Cliente",Hint="recentes ou busca",IsDone=true,Content=new TextBox{Text="Cliente Exemplo"}});
        form.Children.Add(new SectionCard{Number="2",Header="Itens",Hint="atalhos dos mais usados",Content=new TextBox{Tag="Produto ou serviço"}});
        form.Children.Add(new SectionCard{Number="3",Header="Prazo e observações",Content=new TextBox{Tag="Observações para a ficha"}});
        var summary=new Border{Style=(Style)FindResource("SummaryCard"),Child=new StackPanel{Children={new TextBlock{Text="TOTAL",Style=(Style)FindResource("Eyebrow"),Margin=new Thickness(0)},new TextBlock{Text="R$ 120,00",Style=(Style)FindResource("SummaryValue")}}}};
        var footer=new StackPanel{Orientation=Orientation.Horizontal};
        var cancel=new Button{Content="Cancelar",Style=(Style)FindResource("GhostButton"),Margin=new Thickness(0,0,8,0),IsCancel=true};
        var save=new Button{Content="Salvar pedido",Style=(Style)FindResource("DialogPrimaryButton")};save.Click+=(_,_)=>{DialogResult=true;};
        footer.Children.Add(cancel);footer.Children.Add(save);
        Content=new DialogFrame{Kind="Pedido",IconName="clipboard-list",Title="Novo pedido",Subtitle="Enter avança · Ctrl+Enter salva · Esc fecha",Content=form,Aside=summary,Footer=footer};
    }
}
