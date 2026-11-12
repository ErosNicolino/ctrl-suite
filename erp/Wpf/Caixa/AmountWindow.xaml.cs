using System.Globalization;
namespace GraficaCaixa.Wpf;
public partial class AmountWindow : System.Windows.Window
{
    public decimal Amount { get; private set; }
    public AmountWindow(string title,string prompt)
    {
        InitializeComponent();UnsavedChanges.Attach(this,()=>Ok_Click(this,new System.Windows.RoutedEventArgs()));TitleText.Text=title;PromptText.Text=prompt;Title=title;Frame.Title=title;Frame.Subtitle="Enter confirma · Esc fecha";
        AmountBox.TextChanged+=(_,_)=>{ErrorText.Visibility=System.Windows.Visibility.Collapsed;Ui.SetHasError(AmountBox,false);};
        Loaded+=(_,_)=>{AmountBox.Focus();AmountBox.SelectAll();};
    }
    void Ok_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(!decimal.TryParse(AmountBox.Text,NumberStyles.Currency,CultureInfo.GetCultureInfo("pt-BR"),out var v)||v<0){ErrorText.Text="Digite um valor válido (zero ou maior).";ErrorText.Visibility=System.Windows.Visibility.Visible;Ui.SetHasError(AmountBox,true);AmountBox.Focus();AmountBox.SelectAll();return;}
        Amount=v;DialogResult=true;
    }
    void Cancel_Click(object s,System.Windows.RoutedEventArgs e)=>DialogResult=false;
}
