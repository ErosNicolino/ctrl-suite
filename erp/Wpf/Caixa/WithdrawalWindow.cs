using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
// UX 2.0 (pacote 2): sangria em diálogo simples, com Cancelar (antes só havia o X) e prévia do dinheiro esperado.
// Mesma gravação (cash.withdraw), mesmo requestId contra duplicidade, mesmo ajuste de horário.
public sealed class WithdrawalWindow:Window
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    readonly DataStore store;readonly Guid sessionId;readonly decimal expected;
    readonly TextBox amount=new(){FontSize=22,Height=52,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Right},reason=new(){Height=74,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalContentAlignment=VerticalAlignment.Top,Padding=new Thickness(0,8,0,8),Tag="Ex.: troco levado ao banco"},time=new();
    readonly DatePicker date=new();
    readonly Guid requestId=Guid.NewGuid();
    readonly TextBlock after=new();readonly DialogKit.Field amountField,dateField;
    public bool WantsAdjustment{get;private set;}
    public WithdrawalWindow(DataStore store,CashSession session)
    {
        this.store=store;sessionId=session.Id;expected=session.ExpectedCash;
        Title="Registrar sangria";Width=Math.Min(820,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var body=new StackPanel();
        amountField=DialogKit.LabeledField("Valor (R$)",amount);body.Children.Add(amountField.Panel);
        body.Children.Add(DialogKit.LabeledField("Motivo / descrição",reason).Panel);
        var when=new Grid();when.ColumnDefinitions.Add(new ColumnDefinition());when.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(14)});when.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(140)});
        dateField=DialogKit.LabeledField("Data",date);when.Children.Add(dateField.Panel);var timeField=DialogKit.LabeledField("Horário (HH:mm)",time);Grid.SetColumn(timeField.Panel,2);when.Children.Add(timeField.Panel);body.Children.Add(when);
        date.SelectedDate=DateTime.Today;time.Text=DateTime.Now.ToString("HH:mm");
        var summary=new StackPanel();summary.Children.Add(DialogKit.Eyebrow("DINHEIRO ESPERADO"));summary.Children.Add(DialogKit.BigValue(expected.ToString("C2",Pt)));summary.Children.Add(DialogKit.Line("Após a sangria",after));
        summary.Children.Add(DialogKit.Hint("Retirada de dinheiro físico. Não é venda, recebimento nem despesa."));
        summary.Children.Add(DialogKit.Rule());summary.Children.Add(DialogKit.Hint("Só corrigir o valor, sem tirar dinheiro?",0));
        var adjust=new Button{Content="Usar Ajuste de caixa",Style=(Style)FindResource("SoftButton"),Margin=new Thickness(0,8,0,0),ToolTip="Corrige o dinheiro esperado sem contar como venda nem sangria"};adjust.Click+=(_,_)=>{WantsAdjustment=true;DialogResult=false;};summary.Children.Add(adjust);
        Content=new DialogFrame{Kind="Neutro",IconName="banknote",Title="Sangria do caixa",Subtitle="Enter confirma · Esc fecha",Content=body,Aside=DialogKit.Summary(summary),AsideWidth=290,Footer=DialogKit.Footer(this,"Registrar sangria",Save),ScrollBody=false};
        amount.TextChanged+=(_,_)=>Preview();Preview();Loaded+=(_,_)=>amount.Focus();
        UnsavedChanges.Attach(this,Save);
    }
    void Preview(){var ok=decimal.TryParse(amount.Text,NumberStyles.Currency,Pt,out var value);after.Text=ok?(expected-value).ToString("C2",Pt):"—";after.SetResourceReference(TextBlock.ForegroundProperty,ok&&expected-value<0?"Danger":"Ink");}
    async void Save()
    {
        try
        {
            if(!decimal.TryParse(amount.Text,NumberStyles.Currency,Pt,out var value)){DialogKit.ShowError(amountField,amount,"Informe o valor da sangria.");return;}
            if(date.SelectedDate is not DateTime day||!TimeSpan.TryParseExact(time.Text,@"hh\:mm",CultureInfo.InvariantCulture,out var hour)){DialogKit.ShowError(dateField,time,"Confira a data e o horário (HH:mm).");return;}
            var at=day.Date.Add(hour);
            // A caixa recém-aberto pode ter segundos posteriores ao horário digitado.
            var session=(await store.BusinessContextAsync("cash",sessionId)).Sessions.First(x=>x.Id==sessionId);
            if(at<session.OpenedAt&&at.AddMinutes(1)>session.OpenedAt)at=session.OpenedAt;
            await BusinessUi.ExecuteAsync(store,"cash.withdraw",sessionId,session,new WithdrawalArgs(new CashWithdrawal{Id=requestId,Amount=value,Reason=reason.Text.Trim(),CreatedAt=at}),()=>store.Update(data=>{CashOperations.Withdraw(data,sessionId,new CashWithdrawal{Id=requestId,Amount=value,Reason=reason.Text.Trim(),CreatedAt=at},App.CurrentUser);return 0;}));
            DialogResult=true;
        }
        catch(Exception ex){AlertWindow.Info(this,"Não foi possível registrar",ex.Message);}
    }
}
