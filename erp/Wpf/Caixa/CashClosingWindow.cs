using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
// UX 2.0 (pacote 2): Fechar caixa como diálogo central (mock-up 4). Mesmos números e regra:
// esperado = fundo + entradas em dinheiro − sangrias ± ajustes; dinheiro contado ≥ 0; diferença ao vivo; PDF gerado pela tela chamadora.
public sealed class CashClosingWindow : Window
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");readonly TextBox counted=new(){FontSize=24,Height=56,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Right};readonly TextBlock difference=new(){FontWeight=FontWeights.SemiBold};readonly Border differenceBox=new(){CornerRadius=new CornerRadius(10),Padding=new Thickness(14,10,14,10),Margin=new Thickness(0,10,0,0)};readonly CashSession session;
    readonly DialogKit.Field countedField;readonly SectionCard countCard;
    public decimal Amount{get;private set;}
    public CashClosingWindow(CashSession session)
    {
        this.session=session;Title="Fechar caixa";Width=Math.Min(880,SystemParameters.WorkArea.Width-32);MinWidth=620;SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height-24;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var body=new StackPanel();
        var cashEntries=session.Sales.Where(x=>x.PaymentType=="Dinheiro").Sum(x=>x.Total);
        var expected=new StackPanel();
        expected.Children.Add(DialogKit.Line("Saldo inicial em dinheiro",session.OpeningAmount.ToString("C2",Pt),top:0));
        expected.Children.Add(DialogKit.Line("Entradas recebidas em dinheiro",cashEntries.ToString("C2",Pt)));
        expected.Children.Add(DialogKit.Line("Sangrias",(-session.WithdrawalsTotal).ToString("C2",Pt),brushKey:session.WithdrawalsTotal>0?"Danger":null));
        if(session.Adjustments.Count>0)expected.Children.Add(DialogKit.Line("Ajustes de caixa",(session.AdjustmentsTotal>0?"+":"")+session.AdjustmentsTotal.ToString("C2",Pt)));
        expected.Children.Add(new Separator{Margin=new Thickness(0,8,0,4)});
        expected.Children.Add(DialogKit.Line("Saldo esperado em dinheiro",session.ExpectedCash.ToString("C2",Pt),strong:true));
        body.Children.Add(new SectionCard{Number="1",Header="Dinheiro esperado na gaveta",IsDone=true,Content=expected});
        var countPanel=new StackPanel();countedField=DialogKit.LabeledField("Valor contado (R$)",counted,"Conte as notas e moedas da gaveta.");countPanel.Children.Add(countedField.Panel);differenceBox.Child=difference;countPanel.Children.Add(differenceBox);
        countCard=new SectionCard{Number="2",Header="Dinheiro contado",Content=countPanel};body.Children.Add(countCard);
        var withdrawalList=new StackPanel();
        if(session.Withdrawals.Count==0)withdrawalList.Children.Add(DialogKit.Hint("Nenhuma sangria registrada.",0));
        else foreach(var w in session.Withdrawals.OrderBy(x=>x.CreatedAt))withdrawalList.Children.Add(DialogKit.Line($"{w.CreatedAt:HH:mm} · {w.Reason}"+(string.IsNullOrWhiteSpace(w.UserName)?"":$" · {w.UserName}"),w.Amount.ToString("C2",Pt)));
        var withdrawals=new StackPanel();withdrawals.Children.Add(new ScrollViewer{Content=withdrawalList,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});withdrawals.Children.Add(DialogKit.Hint("Sangria reduz apenas o dinheiro físico esperado; não é despesa e não reduz o lucro."));
        body.Children.Add(new SectionCard{Header="Sangrias deste caixa",IsOptional=true,Content=withdrawals});
        if(session.Adjustments.Count>0)
        {
            var adjustmentList=new StackPanel();
            foreach(var a in session.Adjustments.OrderBy(x=>x.CreatedAt))adjustmentList.Children.Add(DialogKit.Line($"{a.CreatedAt:HH:mm} · {a.Reason}"+(string.IsNullOrWhiteSpace(a.UserName)?"":$" · {a.UserName}"),(a.Amount>0?"+":"")+a.Amount.ToString("C2",Pt)));
            var adjustments=new StackPanel();adjustments.Children.Add(new ScrollViewer{Content=adjustmentList,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});adjustments.Children.Add(DialogKit.Hint("Ajuste corrige apenas o dinheiro esperado; não é venda e não muda recebimentos nem lucro."));
            body.Children.Add(new SectionCard{Header="Ajustes deste caixa",IsOptional=true,Content=adjustments});
        }
        // Resumo: recebido no caixa por forma de pagamento (mesma soma do Início).
        var summary=new StackPanel();summary.Children.Add(DialogKit.Eyebrow("RECEBIDO NO CAIXA"));summary.Children.Add(DialogKit.BigValue(session.SalesTotal.ToString("C2",Pt)));
        foreach(var g in session.Sales.GroupBy(x=>x.PaymentType).OrderByDescending(x=>x.Sum(s=>s.Total)))summary.Children.Add(DialogKit.Line(g.Key,g.Sum(x=>x.Total).ToString("C2",Pt)));
        summary.Children.Add(DialogKit.Rule());
        var pdf=new StackPanel{Orientation=Orientation.Horizontal};var ic=new UiIcon{IconName="file-text",Width=16,Height=16,Margin=new Thickness(0,0,8,0)};ic.SetResourceReference(UiIcon.ForegroundProperty,"AccentInk");pdf.Children.Add(ic);var pdfText=new TextBlock{Text="O PDF do fechamento é salvo em Fechamentos.",TextWrapping=TextWrapping.Wrap,MaxWidth=220,FontSize=13};pdfText.SetResourceReference(TextBlock.ForegroundProperty,"AccentInk");pdf.Children.Add(pdfText);summary.Children.Add(pdf);
        Content=new DialogFrame{Kind="Neutro",IconName="wallet",Title="Fechar caixa",Subtitle=$"Aberto em {session.OpenedAt:dd/MM 'às' HH:mm}"+(string.IsNullOrWhiteSpace(session.OpenedBy)?"":$" por {session.OpenedBy}")+" · Enter confirma · Esc fecha",Content=body,Aside=DialogKit.Summary(summary),AsideWidth=300,Footer=DialogKit.Footer(this,"Confirmar fechamento",Save)};
        counted.TextChanged+=(_,_)=>RefreshDifference();RefreshDifference();Loaded+=(_,_)=>counted.Focus();
    }
    void RefreshDifference()
    {
        var ok=decimal.TryParse(counted.Text,NumberStyles.Currency,Pt,out var value);countCard.IsDone=ok&&value>=0;
        if(!ok){difference.Text="Informe o dinheiro contado.";differenceBox.SetResourceReference(Border.BackgroundProperty,"SurfaceAlt");difference.SetResourceReference(TextBlock.ForegroundProperty,"Muted");return;}
        var diff=value-session.ExpectedCash;
        difference.Text=diff==0?"Confere com o esperado":diff<0?$"Falta {(-diff).ToString("C2",Pt)}  ·  Diferença: {diff.ToString("C2",Pt)}":$"Sobra {diff.ToString("C2",Pt)}  ·  Diferença: {diff.ToString("C2",Pt)}";
        differenceBox.SetResourceReference(Border.BackgroundProperty,diff<0?"DangerSoft":diff>0?"WarningSoft":"AccentSoft");difference.SetResourceReference(TextBlock.ForegroundProperty,diff<0?"Danger":diff>0?"Warning":"AccentInk");
    }
    void Save(){if(!decimal.TryParse(counted.Text,NumberStyles.Currency,Pt,out var value)||value<0){DialogKit.ShowError(countedField,counted,"Informe o dinheiro contado com valor igual ou maior que zero.");return;}Amount=value;DialogResult=true;}
}
