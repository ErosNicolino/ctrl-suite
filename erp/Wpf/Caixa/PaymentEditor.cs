using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
public sealed class PaymentEditor : StackPanel
{
    sealed record Row(Guid Id,Grid Grid,ComboBox Type,DatePicker PaidAt,ComboBox Installments,TextBox Amount,TextBlock Fee,TextBlock Suggestion,Button UseSuggested);
    readonly bool allowInstallments;readonly List<Row> rows=[];readonly StackPanel lines=new();readonly TextBlock summary=new(){Margin=new Thickness(0,10,0,10),TextWrapping=TextWrapping.Wrap};readonly Button addButton;
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    public bool AllowExcess {get;set;}
    public string ExcessOrigin {get;set;}="pedido";
    public Func<decimal>? BalanceProvider {get;set;}
    public bool ShowSummary { set => summary.Visibility=value?Visibility.Visible:Visibility.Collapsed; }
    public event Action? Changed;
    public decimal Amount => rows.Sum(x=>decimal.TryParse(x.Amount.Text,NumberStyles.Currency,Pt,out var n)?n:0);
    public decimal Fees => rows.Sum(x=>PaymentPolicy.Fee(decimal.TryParse(x.Amount.Text,NumberStyles.Currency,Pt,out var n)?n:0,PaymentPolicy.Rate(TypeOf(x))));
    public PaymentEditor(bool add=true,bool allowInstallments=true)
    {
        this.allowInstallments=allowInstallments;
        var header=new Grid{Margin=new Thickness(0,8,0,6)};AddColumns(header);var labels=new[]{"Forma","Valor (R$)","Data do recebimento","Parcelas","Taxa","Ação"};for(var i=0;i<labels.Length;i++){var label=new TextBlock{Text=labels[i],FontSize=11,Visibility=!allowInstallments&&i==3?Visibility.Collapsed:Visibility.Visible};Grid.SetColumn(label,i);header.Children.Add(label);}Children.Add(header);Children.Add(lines);
        addButton=new Button{Style=TryFindResource("SoftButton") as Style,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};addButton.Click+=(_,_)=>Add();Children.Add(addButton);Children.Add(summary);UpdateButton();if(add)Add();
    }
    void AddColumns(Grid grid){foreach(var width in new[]{new GridLength(1,GridUnitType.Star),new GridLength(78),new GridLength(176),new GridLength(allowInstallments?72:0),new GridLength(60),new GridLength(38)})grid.ColumnDefinitions.Add(new ColumnDefinition{Width=width});}
    public void Add(string type="PIX",decimal? amount=null,DateTime? paidAt=null)
    {
        var grid=new Grid{Margin=new Thickness(0,5,0,0)};AddColumns(grid);
        var method=new ComboBox{ItemsSource=PaymentPolicy.Methods,Margin=new Thickness(0,0,8,0)};
        var value=new TextBox{Text=amount?.ToString("N2",Pt)??"",ToolTip="Valor recebido nesta modalidade",Margin=new Thickness(0,0,8,0)};
        var date=new DatePicker{SelectedDateFormat=DatePickerFormat.Short,FontSize=12,SelectedDate=(paidAt??DateTime.Today).Date,Margin=new Thickness(0,0,8,0)};
        var installments=new ComboBox{ItemsSource=Enumerable.Range(1,12).Select(x=>$"{x}x"),Margin=new Thickness(0,0,8,0)};
        var fee=new TextBlock{VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Right,Margin=new Thickness(0,0,8,0)};
        var remove=new Button{Content="×",Style=TryFindResource("SoftButton") as Style,Padding=new Thickness(5),ToolTip="Remover antes de confirmar"};
        var suggestion=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11,VerticalAlignment=VerticalAlignment.Center};var useSuggested=new Button{Content="Usar valor sugerido",Style=TryFindResource("SoftButton") as Style,Margin=new Thickness(10,0,0,0),HorizontalAlignment=HorizontalAlignment.Right};var helper=new DockPanel{Margin=new Thickness(0,6,0,4)};DockPanel.SetDock(useSuggested,Dock.Right);helper.Children.Add(useSuggested);helper.Children.Add(suggestion);grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.SetRow(helper,1);Grid.SetColumnSpan(helper,6);grid.Children.Add(helper);var row=new Row(Guid.NewGuid(),grid,method,date,installments,value,fee,suggestion,useSuggested);useSuggested.Click+=(_,_)=>{try{var target=Math.Max(0,(BalanceProvider?.Invoke()??0)-rows.Where(x=>x!=row).Sum(x=>decimal.TryParse(x.Amount.Text,NumberStyles.Currency,Pt,out var n)?n:0));value.Text=PaymentPolicy.SuggestedGross(target,TypeOf(row)).ToString("N2",Pt);}catch(Exception ex){AlertWindow.Info(Window.GetWindow(this),"Repasse da taxa",ex.Message);}};rows.Add(row);UIElement[] controls=[method,value,date,installments,fee,remove];for(var i=0;i<controls.Length;i++){Grid.SetColumn(controls[i],i);grid.Children.Add(controls[i]);}lines.Children.Add(grid);
        method.SelectedItem=type.StartsWith("Cartão de crédito")?"Cartão de crédito":type;installments.SelectedIndex=Math.Max(0,Array.IndexOf(PaymentPolicy.Types.Where(x=>x.StartsWith("Cartão de crédito")).ToArray(),type));
        method.SelectionChanged+=(_,_)=>Refresh();installments.SelectionChanged+=(_,_)=>Refresh();value.TextChanged+=(_,_)=>Refresh();date.SelectedDateChanged+=(_,_)=>Refresh();remove.Click+=(_,_)=>{rows.Remove(row);lines.Children.Remove(grid);Refresh();};Refresh();
    }
    // Botões coloridos de forma de pagamento: os mesmos, na mesma ordem, no lançamento e no pedido.
    public static readonly (string Label,string Kind,string Color)[] QuickKinds=[("PIX","PIX","COk"),("Dinheiro","Dinheiro","CWarn"),("Débito","Cartão de débito","CInfo"),("Crédito 1x","Cartão de crédito 1x","CViolet"),("Crédito 2x","Cartão de crédito 2x","CViolet"),("Crédito 3x","Cartão de crédito 3x","CViolet")];
    public static Button QuickTile(FrameworkElement owner,string label,string kind,string color,Action click)
    {
        var dot=new Border{Width=12,Height=12,CornerRadius=new CornerRadius(6),Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center};dot.SetResourceReference(Border.BackgroundProperty,color);
        var content=new StackPanel{Orientation=Orientation.Horizontal};content.Children.Add(dot);content.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center});
        var tile=new Button{Content=content,Style=(Style)owner.FindResource("SoftButton"),Margin=new Thickness(0,0,8,8),MinWidth=104,ToolTip=$"Recebido em {kind}"};
        tile.Click+=(_,_)=>click();
        return tile;
    }
    public void Clear(){rows.Clear();lines.Children.Clear();Refresh();}
    public int Count=>rows.Count;
    // Atalho dos botões coloridos (UX 2.0): troca a forma da primeira linha (ou cria uma) e, se pedido, preenche o valor.
    // Não muda nenhuma regra: o usuário continua podendo editar, somar outras formas ou remover a linha.
    public void Quick(string type,decimal? amount)
    {
        if(rows.Count==0)Add(type,amount);
        else{var row=rows[0];row.Type.SelectedItem=type.StartsWith("Cartão de crédito")?"Cartão de crédito":type;if(type.StartsWith("Cartão de crédito"))row.Installments.SelectedIndex=Math.Max(0,Array.IndexOf(PaymentPolicy.Types.Where(x=>x.StartsWith("Cartão de crédito")).ToArray(),type));if(amount.HasValue)row.Amount.Text=amount.Value.ToString("N2",Pt);}
        Refresh();
    }
    public string FirstType=>rows.Count==0?"":TypeOf(rows[0]);
    public decimal CashAmount=>rows.Where(x=>x.Type.SelectedItem?.ToString()=="Dinheiro").Sum(x=>decimal.TryParse(x.Amount.Text,NumberStyles.Currency,Pt,out var n)?n:0);
    string TypeOf(Row row)=>row.Type.SelectedItem?.ToString()=="Cartão de crédito"?$"Cartão de crédito {(allowInstallments?row.Installments.SelectedItem:"1x")}":row.Type.SelectedItem?.ToString()??"";
    void UpdateButton()=>addButton.Content=rows.Count==0?"+ Adicionar pagamento":"+ Adicionar outra forma de pagamento";
    public void RefreshBalance()=>Refresh(false);
    void Refresh(bool notify=true){foreach(var row in rows){row.Installments.Visibility=allowInstallments&&row.Type.SelectedItem?.ToString()=="Cartão de crédito"?Visibility.Visible:Visibility.Collapsed;decimal.TryParse(row.Amount.Text,NumberStyles.Currency,Pt,out var amount);var rate=PaymentPolicy.Rate(TypeOf(row));row.Fee.Text=$"{PaymentPolicy.Fee(amount,rate):C2}\n{rate:0.##}%";var show=AllowExcess&&BalanceProvider!=null&&rate>0;row.Suggestion.Visibility=row.UseSuggested.Visibility=show?Visibility.Visible:Visibility.Collapsed;if(show){var target=Math.Max(0,BalanceProvider!()-rows.Where(x=>x!=row).Sum(x=>decimal.TryParse(x.Amount.Text,NumberStyles.Currency,Pt,out var n)?n:0));if(rate>=100){row.Suggestion.Text="Taxa de 100%: repasse integral indisponível.";row.UseSuggested.IsEnabled=false;}else{var suggested=PaymentPolicy.SuggestedGross(target,TypeOf(row));var difference=amount-PaymentPolicy.Fee(amount,rate)-target;row.UseSuggested.IsEnabled=target>0;row.Suggestion.Text=$"Saldo nesta forma: {target:C2} • Sugerido: {suggested:C2} • Líquido: {amount-PaymentPolicy.Fee(amount,rate):C2} • Diferença: {difference:C2}"+(difference<0?$" • Você absorverá {-difference:C2} da taxa.":difference>0?$" • Após a taxa, haverá {difference:C2} acima do pedido.":"");}}}UpdateButton();summary.Text=$"Informado: {Amount:C2} • Taxas estimadas: {Fees:C2} • Líquido estimado: {Amount-Fees:C2}";if(notify)Changed?.Invoke();}
    public List<PaymentPart> Read(decimal balance)
    {
        var result=new List<PaymentPart>();foreach(var row in rows){if(!decimal.TryParse(row.Amount.Text,NumberStyles.Currency,Pt,out var amount))throw new InvalidOperationException("Preencha o valor de cada forma ou remova a linha vazia.");if(row.PaidAt.SelectedDate is not DateTime date)throw new InvalidOperationException("Informe a data real de cada recebimento.");var type=TypeOf(row);result.Add(new PaymentPart{Id=row.Id,Amount=amount,PaymentType=type,PaidAt=date.Date.Add(DateTime.Now.TimeOfDay),FeePercent=PaymentPolicy.Rate(type)});}PaymentPolicy.Validate(result,balance,AllowExcess);if(AllowExcess&&result.Sum(x=>x.Amount)>balance){var owner=Window.GetWindow(this);if(owner==null||!ExcessPaymentConfirmation.Confirm(owner,result,balance,ExcessOrigin))throw new InvalidOperationException("Recebimento não confirmado. Confira os valores antes de salvar.");}return result;
    }
}
public static class PhoneInput
{
    public static void Attach(TextBox box)
    {
        box.Text=PhoneText.Format(box.Text);bool busy=false;
        box.TextChanged+=(_,_)=>{if(busy)return;var formatted=PhoneText.Format(box.Text);if(formatted==box.Text)return;var before=PhoneText.Digits(box.Text[..Math.Min(box.CaretIndex,box.Text.Length)]).Length;busy=true;box.Text=formatted;var pos=0;var count=0;while(pos<formatted.Length&&count<before){if(char.IsAsciiDigit(formatted[pos]))count++;pos++;}box.CaretIndex=pos;busy=false;};
    }
}
