using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
// Ajuste de caixa: corrige o dinheiro esperado na gaveta sem virar venda, recebimento, despesa ou sangria.
// Motivo obrigatório, auditoria na gravação (cash.adjust) e requestId contra duplicidade. O horário é o do servidor.
public sealed class CashAdjustmentWindow:Window
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    readonly DataStore store;readonly Guid sessionId;readonly decimal expected;
    readonly TextBox amount=new(){FontSize=22,Height=52,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Right},reason=new(){Height=74,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalContentAlignment=VerticalAlignment.Top,Padding=new Thickness(0,8,0,8),Tag="Ex.: sangria de ontem não registrada"};
    readonly RadioButton increase=new(){Content="Aumentar",GroupName="adjust",IsChecked=true},decrease=new(){Content="Diminuir",GroupName="adjust"};
    readonly Guid requestId=Guid.NewGuid();
    readonly TextBlock after=new();readonly DialogKit.Field amountField,reasonField;
    public CashAdjustmentWindow(DataStore store,CashSession session)
    {
        this.store=store;sessionId=session.Id;expected=session.ExpectedCash;
        Title="Ajuste de caixa";Width=Math.Min(820,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        increase.Style=decrease.Style=(Style)FindResource("Segment");
        var body=new StackPanel();
        var options=new StackPanel{Orientation=Orientation.Horizontal};options.Children.Add(increase);options.Children.Add(decrease);
        body.Children.Add(DialogKit.LabeledField("Dinheiro esperado",new Border{Style=(Style)FindResource("SegmentBar"),HorizontalAlignment=HorizontalAlignment.Left,Child=options}).Panel);
        amountField=DialogKit.LabeledField("Valor (R$)",amount);body.Children.Add(amountField.Panel);
        reasonField=DialogKit.LabeledField("Motivo",reason);body.Children.Add(reasonField.Panel);
        var summary=new StackPanel();summary.Children.Add(DialogKit.Eyebrow("DINHEIRO ESPERADO"));summary.Children.Add(DialogKit.BigValue(expected.ToString("C2",Pt)));summary.Children.Add(DialogKit.Line("Após o ajuste",after));
        summary.Children.Add(DialogKit.Hint("Corrige só o valor esperado na gaveta. Não é venda, recebimento nem despesa, e fica registrado na Auditoria."));
        Content=new DialogFrame{Kind="Neutro",IconName="wallet",Title="Ajuste de caixa",Subtitle="Enter confirma · Esc fecha",Content=body,Aside=DialogKit.Summary(summary),AsideWidth=290,Footer=DialogKit.Footer(this,"Registrar ajuste",Save),ScrollBody=false};
        amount.TextChanged+=(_,_)=>Preview();increase.Checked+=(_,_)=>Preview();decrease.Checked+=(_,_)=>Preview();Preview();Loaded+=(_,_)=>amount.Focus();
        UnsavedChanges.Attach(this,Save);
    }
    decimal Signed(decimal value)=>decrease.IsChecked==true?-value:value;
    void Preview(){var ok=decimal.TryParse(amount.Text,NumberStyles.Currency,Pt,out var value)&&value>0;var result=expected+Signed(value);after.Text=ok?result.ToString("C2",Pt):"—";after.SetResourceReference(TextBlock.ForegroundProperty,ok&&result<0?"Danger":"Ink");}
    async void Save()
    {
        try
        {
            if(!decimal.TryParse(amount.Text,NumberStyles.Currency,Pt,out var value)||value<=0){DialogKit.ShowError(amountField,amount,"Informe o valor do ajuste.");return;}
            if(reason.Text.Trim().Length<5){DialogKit.ShowError(reasonField,reason,"Informe o motivo do ajuste.");return;}
            var session=(await store.BusinessContextAsync("cash",sessionId)).Sessions.First(x=>x.Id==sessionId);
            CashAdjustment Draft()=>new(){Id=requestId,Amount=Signed(value),Reason=reason.Text.Trim()};
            await BusinessUi.ExecuteAsync(store,"cash.adjust",sessionId,session,new CashAdjustmentArgs(Draft()),()=>store.Update(data=>{CashOperations.Adjust(data,sessionId,Draft(),App.CurrentUser);return 0;}));
            DialogResult=true;
        }
        catch(Exception ex){AlertWindow.Info(this,"Não foi possível registrar",ex.Message);}
    }
}
