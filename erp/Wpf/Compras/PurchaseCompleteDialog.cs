using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;

// "Comprado": pergunta quanto foi pago (e, se quiser, quanto chegou, fornecedor e pagamento) para registrar o gasto da compra.
// UX 2.0 (pacote 3): diálogo simples laranja; mesmas validações, erro junto do campo. Não lança no caixa (igual a antes).
public sealed class PurchaseCompleteDialog : Window
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    readonly TextBox amount=new(){Tag="Ex.: 85,90",FontSize=20,Height=50,FontWeight=FontWeights.SemiBold,TextAlignment=TextAlignment.Right},quantity=new();
    readonly SupplierPicker supplier;
    readonly ComboBox payment=new(){ItemsSource=PaymentPolicy.Methods,SelectedItem="PIX"};
    readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,Visibility=Visibility.Collapsed,Margin=new Thickness(0,6,0,0)};
    public Purchase? Result{get;private set;}

    public PurchaseCompleteDialog(PurchaseNeed need,Prompt12State? state=null,DataStore? store=null)
    {
        supplier=new SupplierPicker(state){ToolTip="Escolha um fornecedor cadastrado ou digite outro nome"};if(store!=null)supplier.CreateNew=typed=>SupplierUi.EditAsync(this,store,null,typed);
        Title="Compra concluída";Width=Math.Min(560,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var panel=new StackPanel();
        panel.Children.Add(DialogKit.LabeledField("Quanto pagou no total (R$)",amount).Panel);
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});row.ColumnDefinitions.Add(new ColumnDefinition());
        var q=DialogKit.LabeledField($"Quantidade que chegou ({(string.IsNullOrWhiteSpace(need.Unit)?"un":need.Unit)})",quantity).Panel;var p=DialogKit.LabeledField("Forma de pagamento",payment).Panel;Grid.SetColumn(p,2);row.Children.Add(q);row.Children.Add(p);panel.Children.Add(row);
        panel.Children.Add(DialogKit.LabeledField("Fornecedor / loja",supplier).Panel);
        quantity.Text=need.Quantity.ToString("0.##",Pt);supplier.Value=(state!=null?SupplierBook.Find(state,need.SupplierId,need.Supplier)?.Name:null)??(need.Supplier.Length>0?need.Supplier:GuessSupplier(need.Notes));
        error.SetResourceReference(TextBlock.ForegroundProperty,"Danger");panel.Children.Add(error);
        panel.Children.Add(DialogKit.Hint("O gasto aparece no resumo do mês em Compras. Não entra no caixa.",4));
        Content=new DialogFrame{Kind="Compra",IconName="shopping-cart",Title="Comprado!",Subtitle=$"{need.Material} • {need.Quantity.ToString("0.##",Pt)} {need.Unit}",Content=panel,Footer=DialogKit.Footer(this,"Confirmar compra",Save),ScrollBody=false};
        Loaded+=(_,_)=>amount.Focus();KeyboardFlow.Attach(this,Save);
        void Save()
        {
            if(!decimal.TryParse(amount.Text,NumberStyles.Currency,Pt,out var value)||value<0){error.Text="Informe quanto foi pago (ex.: 85,90).";error.Visibility=Visibility.Visible;Ui.SetHasError(amount,true);amount.Focus();return;}
            if(!decimal.TryParse(quantity.Text,NumberStyles.Number,Pt,out var qty)||qty<=0){error.Text="Informe a quantidade que chegou.";error.Visibility=Visibility.Visible;Ui.SetHasError(quantity,true);quantity.Focus();return;}
            Result=new Purchase{NeedId=need.Id,Amount=decimal.Round(value,2),Quantity=qty,Supplier=supplier.Value,SupplierId=supplier.LinkedId,PaymentMethod=payment.SelectedItem?.ToString()??"",Date=DateTime.Now,Paid=true};
            DialogResult=true;
        }
    }
    // "Comprar na Lavoro…" → "Lavoro".
    static string GuessSupplier(string notes)
    {
        var m=System.Text.RegularExpressions.Regex.Match(notes??"",@"\b(?:na|no|em)\s+([A-ZÁÉÍÓÚ][\wÀ-ú]+(?:\s+[A-ZÁÉÍÓÚ][\wÀ-ú]+)?)");
        return m.Success?m.Groups[1].Value:"";
    }
}
