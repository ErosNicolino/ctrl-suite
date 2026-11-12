using System.Globalization;
namespace GraficaCaixa.Wpf;
public partial class SessionValuesWindow : System.Windows.Window
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");public decimal OpeningAmount{get;private set;}public decimal ClosingAmount{get;private set;}public DateTime OpenedAt{get;private set;}public DateTime ClosedAt{get;private set;}public string OpenedBy{get;private set;}="";public string ClosedBy{get;private set;}="";
    public SessionValuesWindow(GraficaCaixa.CashSession session)
    {
        InitializeComponent();UnsavedChanges.Attach(this,()=>Save_Click(this,new System.Windows.RoutedEventArgs()));KeyboardFlow.Attach(this,()=>Save_Click(this,new System.Windows.RoutedEventArgs()));
        OpeningBox.Text=session.OpeningAmount.ToString("N2",Pt);ClosingBox.Text=session.DeclaredClosingAmount.GetValueOrDefault().ToString("N2",Pt);OpenedDateBox.SelectedDate=session.OpenedAt.Date;OpenedTimeBox.Text=session.OpenedAt.ToString("HH:mm");var closed=session.ClosedAt??DateTime.Now;ClosedDateBox.SelectedDate=closed.Date;ClosedTimeBox.Text=closed.ToString("HH:mm");OpenedByBox.Text=session.OpenedBy;ClosedByBox.Text=session.ClosedBy;
        Loaded+=(_,_)=>{OpeningBox.Focus();OpeningBox.SelectAll();};
    }
    // Erro junto do campo: mensagem embaixo, borda de erro e foco; some ao corrigir.
    static void Error(System.Windows.Controls.TextBox box,System.Windows.Controls.TextBlock label,string message)
    {
        label.Text=message;label.Visibility=System.Windows.Visibility.Visible;Ui.SetHasError(box,true);box.Focus();box.SelectAll();
        void Changed(object? s,System.Windows.Controls.TextChangedEventArgs e){box.TextChanged-=Changed;label.Visibility=System.Windows.Visibility.Collapsed;Ui.SetHasError(box,false);}box.TextChanged+=Changed;
    }
    void Save_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(!decimal.TryParse(OpeningBox.Text,NumberStyles.Currency,Pt,out var opening)||opening<0){Error(OpeningBox,OpeningError,"Informe um valor igual ou maior que zero.");return;}
        if(!decimal.TryParse(ClosingBox.Text,NumberStyles.Currency,Pt,out var closing)||closing<0){Error(ClosingBox,ClosingError,"Informe um valor igual ou maior que zero.");return;}
        if(OpenedDateBox.SelectedDate is not DateTime openedDate||!TimeSpan.TryParse(OpenedTimeBox.Text,out var openedTime)){Error(OpenedTimeBox,OpenedError,"Escolha a data e informe o horário (08:30).");return;}
        if(ClosedDateBox.SelectedDate is not DateTime closedDate||!TimeSpan.TryParse(ClosedTimeBox.Text,out var closedTime)){Error(ClosedTimeBox,ClosedError,"Escolha a data e informe o horário (18:00).");return;}
        var opened=openedDate.Date.Add(openedTime);var closedAt=closedDate.Date.Add(closedTime);if(closedAt<opened){Error(ClosedTimeBox,ClosedError,"O fechamento não pode ser antes da abertura.");return;}
        OpeningAmount=opening;ClosingAmount=closing;OpenedAt=opened;ClosedAt=closedAt;OpenedBy=string.IsNullOrWhiteSpace(OpenedByBox.Text)?"Gráfica":OpenedByBox.Text.Trim();ClosedBy=string.IsNullOrWhiteSpace(ClosedByBox.Text)?"Gráfica":ClosedByBox.Text.Trim();DialogResult=true;
    }
    void Cancel_Click(object s,System.Windows.RoutedEventArgs e)=>DialogResult=false;
}
