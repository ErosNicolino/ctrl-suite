using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace GraficaCaixa.Wpf;

// UX 2.0 (pacote 5): Início do protótipo — saudação com o nome de quem entrou, frase do dia, indicadores com cor de
// significado (recebido com a divisão por forma de pagamento, dinheiro esperado, pedidos que pedem atenção, prontos).
// Só apresentação: os valores são os mesmos que o painel já calculava (nenhuma fórmula nova).
public partial class MainWindow
{
    void SetupDashboard()
    {
        var first=App.CurrentUser.UserName.Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        GreetingText.Text=string.IsNullOrEmpty(first)?"Início":$"Bom trabalho, {first}";
        ApplyPhrase();ThemeManager.Changed+=ApplyPhrase;Closed+=(_,_)=>ThemeManager.Changed-=ApplyPhrase;
        if(ThemeManager.MotionEnabled)Loaded+=(_,_)=>{if(PhraseBox.IsVisible)Motion.Enter(PhraseBox,120);Motion.Stagger(KpiRow.Children.OfType<FrameworkElement>());};
        foreach(var kpi in KpiRow.Children.OfType<FrameworkElement>())Motion.Lift(kpi);
    }
    void ApplyPhrase()
    {
        var phrase=DailyPhrases.For(DateTime.Today);
        PhraseText.Text=phrase;
        PhraseBox.Visibility=ThemeManager.DailyPhrase&&phrase.Length>0?Visibility.Visible:Visibility.Collapsed;
    }
    void HidePhrase_Click(object s,RoutedEventArgs e)
    {
        ThemeManager.Set(ThemeManager.Theme,ThemeManager.TextScale,ThemeManager.Colors,ThemeManager.MotionOff,false);
        Toast.Show(this,"Frase do dia oculta. Para mostrar de novo: Sistema › Aparência.","Desfazer",()=>ThemeManager.Set(ThemeManager.Theme,ThemeManager.TextScale,ThemeManager.Colors,ThemeManager.MotionOff,true));
    }
    // Data comemorativa chegando (Regras): aviso com resumo do ano passado; carrega uma vez por dia, só para quem vê Relatórios.
    SeasonInsight? season;DateTime seasonDay;
    async void LoadSeason()
    {
        if(seasonDay==DateTime.Today)return;seasonDay=DateTime.Today;
        try{season=await SeasonalSuggestions.LoadAsync(store,DateTime.Today);}
        catch(Exception ex) when(ex is UnauthorizedAccessException or InvalidOperationException or IOException or TimeoutException or System.Net.Http.HttpRequestException){season=null;}
        if(season==null||SeasonalSuggestions.IsHidden(season)){SeasonBox.Visibility=Visibility.Collapsed;return;}
        SeasonTitle.Text=$"{season.Name} {season.When} ({season.Date:dd/MM})";
        var top=season.Products.FirstOrDefault();
        SeasonText.Text=season.Products.Count==0?"Ainda sem vendas para sugerir.":season.Recent?$"Sem histórico do ano passado. Últimos dias: {season.Customers.Count} cliente(s); mais vendido: {top!.Name}.":$"No ano passado: {season.Customers.Count} cliente(s) compraram; mais vendido: {top!.Name}.";
        SeasonBox.Visibility=Visibility.Visible;
    }
    void Season_Click(object s,RoutedEventArgs e){if(season!=null)new SeasonWindow(store,season){Owner=this}.ShowDialog();}
    void HideSeason_Click(object s,RoutedEventArgs e){if(season==null)return;SeasonalSuggestions.Hide(season);SeasonBox.Visibility=Visibility.Collapsed;Toast.Show(this,$"Aviso do {season.Name} oculto.");}
    void CashKpi_Click(object s,RoutedEventArgs e){if(CloseButton.Visibility==Visibility.Visible)Close_Click(this,NoArgs);else Open_Click(this,NoArgs);}
    // Cartões de pedidos abrem a lista já filtrada (mesmos filtros da tela de Pedidos).
    void AttentionKpi_Click(object s,RoutedEventArgs e){if(NavOrders.Visibility==Visibility.Visible)OpenOrders(filter:ScheduledOrdersWindow.PriorityFilter);}
    void ReadyKpi_Click(object s,RoutedEventArgs e){if(NavOrders.Visibility==Visibility.Visible)OpenOrders(filter:OrderStages.Ready);}
    void NewOrderButton_Click(object s,RoutedEventArgs e)=>NewOrder();

    // Barra colorida e legenda com o recebido por forma de pagamento (mesmo agrupamento do texto antigo).
    void UpdatePaymentMix(CashSession? session)
    {
        PaymentMix.ColumnDefinitions.Clear();PaymentMix.Children.Clear();PaymentLegend.Children.Clear();
        if(session==null)return;
        var groups=session.Sales.GroupBy(x=>x.PaymentType).Select(g=>(Type:g.Key,Total:g.Sum(x=>x.Total))).Where(g=>g.Total>0).OrderByDescending(g=>g.Total).ToList();
        var i=0;
        foreach(var (type,total) in groups)
        {
            var key=PaymentColor(type);
            PaymentMix.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength((double)total,GridUnitType.Star)});
            var bar=new Border{Margin=new Thickness(0,0,i<groups.Count-1?2:0,0),ToolTip=$"{type}: {total:C2}"};bar.SetResourceReference(Border.BackgroundProperty,key);Grid.SetColumn(bar,i++);PaymentMix.Children.Add(bar);
            if(PaymentLegend.Children.Count>=4)continue;
            // Etiqueta com a cor da forma de pagamento (fundo suave da mesma cor, funciona no claro e no escuro).
            var pill=new Grid{Margin=new Thickness(0,0,6,4)};
            var tint=new Border{CornerRadius=new CornerRadius(9),Opacity=.16};tint.SetResourceReference(Border.BackgroundProperty,key);pill.Children.Add(tint);
            var item=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(8,2,9,2)};
            var dot=new Ellipse{Width=7,Height=7,Margin=new Thickness(0,0,5,0),VerticalAlignment=VerticalAlignment.Center};dot.SetResourceReference(Shape.FillProperty,key);item.Children.Add(dot);
            var text=new TextBlock{Text=$"{Short(type)} {total:C2}",FontSize=11.5,FontWeight=FontWeights.SemiBold};text.SetResourceReference(TextBlock.ForegroundProperty,"Ink");item.Children.Add(text);
            pill.Children.Add(item);PaymentLegend.Children.Add(pill);
        }
        PaymentLegend.ToolTip=PaymentBreakdownText.Text;
    }
    // Cores de pagamento do padrão: PIX verde, Dinheiro âmbar, Débito azul, Crédito violeta.
    internal static string PaymentColor(string type)=>type.StartsWith("PIX",StringComparison.OrdinalIgnoreCase)?"COk":type.StartsWith("Dinheiro",StringComparison.OrdinalIgnoreCase)?"CWarn":type.Contains("débito",StringComparison.OrdinalIgnoreCase)?"CInfo":type.Contains("crédito",StringComparison.OrdinalIgnoreCase)?"CViolet":"Muted";
    internal static string Short(string type)=>type.Replace("Cartão de ","",StringComparison.OrdinalIgnoreCase);
    void UpdateAttention(int urgent,int overdue,int ready)
    {
        Motion.CountTo(AttentionText,urgent.ToString());
        AttentionHint.Text=overdue>0?$"{overdue} atrasado(s) · {Math.Max(0,urgent-overdue)} em até {GraficaCaixa.BusinessRules.Current.PriorityDays} dias":$"vencem em até {GraficaCaixa.BusinessRules.Current.PriorityDays} dias úteis";AttentionHint.ToolTip=$"Atrasados e com prazo em até {GraficaCaixa.BusinessRules.Current.PriorityDays} dias úteis (sem contar os prontos para retirada).";
        AttentionStripe.SetResourceReference(Border.BackgroundProperty,overdue>0?"CDanger":"CWarn");
        AttentionText.SetResourceReference(TextBlock.ForegroundProperty,overdue>0?"Danger":"Ink");
        Motion.CountTo(ReadyText,ready.ToString());
    }
}
