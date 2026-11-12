using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
// UX 2.0 (pacote 2): Receber pagamento no diálogo central — saldo devido em destaque, mesmo editor de formas mistas
// (excedente, taxas, data real) e mesma validação (editor.Read). Nada muda na gravação, que continua na tela chamadora.
public partial class PaymentReceivedWindow : Window
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    readonly decimal remaining;readonly PaymentEditor editor=new(false);
    readonly TextBlock informed=new(),rest=new(),fees=new();readonly TextBlock error=new(){Visibility=Visibility.Collapsed,TextWrapping=TextWrapping.Wrap};
    public List<PaymentPart> Payments{get;private set;}=[];
    public DateTime PaidAt{get;private set;}public string PaymentType=>Payments.FirstOrDefault()?.PaymentType??"PIX";public decimal Amount=>Payments.Sum(x=>x.Amount);
    public PaymentReceivedWindow(ScheduledOrder order,bool allowExcess=true)
    {
        InitializeComponent();remaining=order.RemainingAmount;editor.AllowExcess=allowExcess;editor.BalanceProvider=()=>remaining;editor.ShowSummary=false;
        Width=Math.Min(980,SystemParameters.WorkArea.Width-32);Height=Math.Min(640,SystemParameters.WorkArea.Height-32);
        var body=new StackPanel();
        var who=new TextBlock{Text=$"{order.CustomerName} — {order.Description}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),FontWeight=FontWeights.SemiBold};body.Children.Add(who);
        // Pedido já quitado: o recebimento continua possível (valor adicional), mas fica claro antes de confirmar.
        if(remaining<=0&&!order.PricePending){var paid=new TextBlock{Text="Este pedido já está quitado. Confirme só se for um valor adicional do cliente.",TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)};paid.SetResourceReference(TextBlock.ForegroundProperty,"Warning");body.Children.Add(paid);}
        editor.Add(order.FinalPaymentType,remaining,DateTime.Today);
        var payments=new StackPanel();payments.Children.Add(editor);error.Style=(Style)FindResource("FieldError");payments.Children.Add(error);
        body.Children.Add(new SectionCard{Number="1",Header="Formas de pagamento",Hint="pode combinar mais de uma",Content=payments});
        var summary=new StackPanel();summary.Children.Add(DialogKit.Eyebrow("SALDO DEVIDO"));summary.Children.Add(DialogKit.BigValue(remaining.ToString("C2",Pt)));
        summary.Children.Add(DialogKit.Line("Informado agora",informed));summary.Children.Add(DialogKit.Line("Restante",rest));summary.Children.Add(DialogKit.Rule());fees.Style=(Style)FindResource("Hint");summary.Children.Add(fees);
        if(order.Number>0)Title=$"Receber pagamento · Pedido #{order.Number}";
        Content=new DialogFrame{Kind="Pedido",IconName="banknote",Title="Receber pagamento",Subtitle=(order.Number>0?$"Pedido #{order.Number} · ":"")+"Ctrl+Enter confirma · Esc fecha",Content=body,Aside=DialogKit.Summary(summary),AsideWidth=290,Footer=DialogKit.Footer(this,"Confirmar recebimento",Save)};
        editor.Changed+=Refresh;Refresh();
        PreviewKeyDown+=(_,e)=>{if((e.Key==System.Windows.Input.Key.Enter||e.Key==System.Windows.Input.Key.S)&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){e.Handled=true;Save();}};
        UnsavedChanges.Attach(this,Save);
    }
    void Refresh()
    {
        informed.Text=editor.Amount.ToString("C2",Pt);var left=remaining-editor.Amount;
        rest.Text=left>=0?left.ToString("C2",Pt):$"Excedente {(-left).ToString("C2",Pt)}";rest.SetResourceReference(TextBlock.ForegroundProperty,left>0?"Danger":left<0?"Warning":"AccentInk");
        fees.Text=$"Taxas estimadas {editor.Fees.ToString("C2",Pt)} · Líquido {(editor.Amount-editor.Fees).ToString("C2",Pt)}";error.Visibility=Visibility.Collapsed;
    }
    void Save(){try{Payments=editor.Read(remaining);if(Payments.Count==0)throw new InvalidOperationException("Adicione uma forma de pagamento.");PaidAt=Payments[0].PaidAt??DateTime.Now;DialogResult=true;}catch(Exception ex){error.Text=ex.Message;error.Visibility=Visibility.Visible;}}
}
