using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GraficaCaixa.Wpf;

// Devolução ao cliente: valor (até o que foi pago), forma em que o dinheiro sai (Dinheiro da gaveta ou Pix) e motivo.
// O pagamento original fica preservado; a devolução entra no caixa aberto de hoje (FinancialOperations.RefundSale).
public sealed class RefundWindow : Window
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    readonly TextBox amount=new(){TextAlignment=TextAlignment.Right},reason=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=70,VerticalContentAlignment=VerticalAlignment.Top,Padding=new Thickness(0,6,0,6)};
    readonly ComboBox method=new(){ItemsSource=FinancialOperations.RefundMethods};
    public decimal Amount{get;private set;}
    public string Method=>method.SelectedItem?.ToString()??"Dinheiro";
    public string Reason=>reason.Text.Trim();

    RefundWindow(Sale original,decimal paid)
    {
        Title="Devolver valor";Width=Math.Min(560,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
        method.SelectedItem=original.PaymentType=="PIX"?"PIX":"Dinheiro";
        var panel=new StackPanel();
        var summary=new Border{Style=(Style)FindResource("NoteCalm"),Margin=new Thickness(0,0,0,12),Child=new TextBlock{TextWrapping=TextWrapping.Wrap,Text=$"{DisplayText.Customer(original)} · {original.Product}\nPago em {original.CreatedAt:dd/MM/yyyy HH:mm} · {original.PaymentType} · valor atual {paid.ToString("C2",Pt)}"}};panel.Children.Add(summary);
        var amountField=DialogKit.LabeledField($"Valor a devolver (até {paid.ToString("C2",Pt)})",amount);var methodField=DialogKit.LabeledField("Devolver em",method);var reasonField=DialogKit.LabeledField("Motivo *",reason);
        foreach(var f in new[]{amountField,methodField,reasonField})panel.Children.Add(f.Panel);
        var card=original.PaymentType.StartsWith("Cartão",StringComparison.OrdinalIgnoreCase);
        var hint=DialogKit.Hint("",4);panel.Children.Add(hint);
        void Explain()=>hint.Text=(card?"Pago no cartão: a devolução sai em Dinheiro ou Pix, e a taxa do cartão sobre o valor original continua. ":"")+"O pagamento original fica como foi; a devolução entra no caixa de hoje"+(Method=="Dinheiro"?" e sai da gaveta (o esperado no fechamento diminui).":" como Pix enviado (a Conciliação Pix confere com a saída do extrato).");
        method.SelectionChanged+=(_,_)=>Explain();Explain();
        void Confirm()
        {
            if(!decimal.TryParse(amount.Text,NumberStyles.Currency,Pt,out var value)||value<=0||decimal.Round(value,2)!=value){DialogKit.ShowError(amountField,amount,"Informe o valor a devolver.");return;}
            if(value>paid){DialogKit.ShowError(amountField,amount,$"A devolução não pode passar de {paid.ToString("C2",Pt)}.");return;}
            if(Reason.Length<5){DialogKit.ShowError(reasonField,reason,"Explique o motivo (mínimo 5 caracteres).");return;}
            if(!AlertWindow.Confirm(this,"Confirmar devolução",$"Devolver {value.ToString("C2",Pt)} em {Method} para {DisplayText.Customer(original)}?"))return;
            Amount=value;DialogResult=true;
        }
        Content=new DialogFrame{Kind="Lancamento",IconName="rotate-ccw",Title="Devolver valor",Subtitle="Enter avança · Ctrl+Enter confirma · Esc fecha",Content=panel,Footer=DialogKit.Footer(this,"Registrar devolução",Confirm),ScrollBody=false};
        KeyboardFlow.Attach(this,Confirm);
        Loaded+=(_,_)=>amount.Focus();
    }

    // Abre a devolução para um lançamento (caixa do Início ou Pesquisar lançamentos) e grava pelo servidor.
    public static async Task<bool> RunAsync(Window owner,DataStore store,Guid saleId)
    {
        try
        {
            var data=store.IsRemote?await store.BusinessContextAsync("sale",saleId):store.Load();
            var original=data.Sessions.SelectMany(x=>x.Sales).FirstOrDefault(x=>x.Id==saleId)??throw new InvalidOperationException("O lançamento não existe mais. Atualize a lista.");
            if(FinancialOperations.IsAdjustmentLine(original))throw new InvalidOperationException("Selecione o lançamento original, não uma linha de ajuste ou de devolução.");
            var paid=data.FinancialCorrections.Where(x=>x.OriginalSaleId==saleId).OrderBy(x=>x.CreatedAt).LastOrDefault()?.CorrectedAmount??original.Total;
            if(paid<=0)throw new InvalidOperationException("Este lançamento não tem valor a devolver.");
            var dialog=new RefundWindow(original,paid){Owner=owner};if(dialog.ShowDialog()!=true)return false;
            await BusinessUi.ExecuteAsync(store,"sale.refund",saleId,original,new RefundArgs(dialog.Amount,dialog.Method,dialog.Reason),()=>store.Update(d=>FinancialOperations.RefundSale(d,saleId,dialog.Amount,dialog.Method,dialog.Reason,App.CurrentUser)));
            Toast.Show(owner,$"Devolução de {dialog.Amount.ToString("C2",Pt)} em {dialog.Method} registrada.",tone:"ok");return true;
        }
        catch(Exception ex) when(ex is InvalidOperationException or IOException or TimeoutException or UnauthorizedAccessException){AlertWindow.Info(owner,"Não foi possível registrar a devolução",ex.Message);return false;}
    }
}
