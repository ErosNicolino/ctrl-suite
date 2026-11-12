using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
// Confirmação de valor acima do saldo (mesma regra: classificação do excedente por forma, reforço quando muito acima).
// UX 2.0: diálogo central com os números em linhas alinhadas e o aviso âmbar em destaque; Enter não confirma.
public static class ExcessPaymentConfirmation
{
    public static bool Confirm(Window owner,List<PaymentPart> parts,decimal balance,string origin="pedido")
    {
        var pt=CultureInfo.GetCultureInfo("pt-BR");
        var gross=parts.Sum(x=>x.Amount);var excess=gross-balance;var fees=parts.Sum(x=>PaymentPolicy.Fee(x.Amount,PaymentPolicy.Rate(x.PaymentType)));var reinforced=PaymentPolicy.ReinforcedConfirmation(balance,excess);var running=balance;var causing=new List<PaymentPart>();foreach(var p in parts){if(p.Amount>running)causing.Add(p);running=Math.Max(0,running-p.Amount);}
        var window=new Window{Owner=owner,Title=reinforced?$"Atenção: valor muito acima do {origin}":$"Confirmar valor acima do {origin}?",Width=Math.Min(660,SystemParameters.WorkArea.Width-32),SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
        var body=new StackPanel();
        var numbers=new StackPanel();
        numbers.Children.Add(DialogKit.Line($"Saldo do {origin}",balance.ToString("C2",pt),top:0));numbers.Children.Add(DialogKit.Line("Valor informado",gross.ToString("C2",pt)));
        numbers.Children.Add(DialogKit.Line("Excedente",excess.ToString("C2",pt),strong:true,brushKey:"Warning"));numbers.Children.Add(new Separator{Margin=new Thickness(0,8,0,4)});
        numbers.Children.Add(DialogKit.Line("Taxa estimada",fees.ToString("C2",pt)));numbers.Children.Add(DialogKit.Line("Líquido estimado",(gross-fees).ToString("C2",pt)));numbers.Children.Add(DialogKit.Line("Diferença líquida",(gross-fees-balance).ToString("C2",pt)));
        body.Children.Add(new SectionCard{Header="Valores",Content=numbers});
        var selectors=new List<(PaymentPart Part,ComboBox Type)>();
        if(causing.Count>0)
        {
            var kinds=new StackPanel();
            foreach(var p in causing){var select=new ComboBox{ItemsSource=new[]{PaymentPolicy.CollectionSurcharge,PaymentPolicy.CustomerExtra},SelectedItem=PaymentPolicy.DefaultExcessKind(p.PaymentType)};kinds.Children.Add(DialogKit.LabeledField($"{p.PaymentType} • {PaymentPolicy.Rate(p.PaymentType):0.##}% • bruto {p.Amount.ToString("C2",pt)}",select).Panel);selectors.Add((p,select));}
            body.Children.Add(new SectionCard{Header="Como classificar o excedente",Content=kinds});
        }
        if(reinforced)body.Children.Add(new Border{Style=(Style)owner.FindResource("Note"),Child=new TextBlock{Text="Confira se houve erro de digitação antes de confirmar.",FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap}});
        var footer=new StackPanel{Orientation=Orientation.Horizontal};
        var back=new Button{Content="Voltar e corrigir",IsCancel=true,Style=(Style)owner.FindResource("SoftButton"),Margin=new Thickness(0,0,8,0)};footer.Children.Add(back);
        var confirm=new Button{Content="Confirmar recebimento",Style=(Style)owner.FindResource("DialogPrimaryButton")};
        confirm.Click+=(_,_)=>{foreach(var (part,type) in selectors){part.ExcessConfirmed=true;part.ExcessKind=type.SelectedItem?.ToString()??PaymentPolicy.DefaultExcessKind(part.PaymentType);}window.DialogResult=true;};footer.Children.Add(confirm);
        window.Content=new DialogFrame{Kind="Neutro",IconName="triangle-alert",Title=window.Title,Subtitle="Esc volta para corrigir",Content=body,Footer=footer,ScrollBody=false};
        window.Loaded+=(_,_)=>back.Focus();
        return window.ShowDialog()==true;
    }
}
