namespace GraficaCaixa.Wpf;
public partial class AlertWindow : System.Windows.Window
{
    public AlertWindow(string title, string message, bool confirm = false, bool danger = false)
    {
        InitializeComponent(); TitleText.Text = title; MessageText.Text = message;
        CancelBtn.Visibility = confirm ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; OkBtn.Content = confirm ? "Confirmar" : "Entendi";
        // Informação: "i" na cor da marca; confirmação: alerta âmbar; ação irreversível: vermelho.
        if (danger) { OkBtn.Style = (System.Windows.Style)FindResource("DangerButton"); Badge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "DangerSoft"); BadgeIcon.SetResourceReference(UiIcon.ForegroundProperty, "Danger"); BadgeIcon.IconName = "triangle-alert"; }
        else if (confirm) { Badge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "WarningSoft"); BadgeIcon.SetResourceReference(UiIcon.ForegroundProperty, "Warning"); BadgeIcon.IconName = "circle-alert"; }
        // Ação irreversível: Enter NÃO confirma e o foco começa em Cancelar (evita confirmação acidental).
        if (danger) OkBtn.IsDefault = false;
        Loaded += (_, _) => { Motion.Pop(CardBox); if (danger) CancelBtn.Focus(); else OkBtn.Focus(); };
    }
    void Ok_Click(object sender, System.Windows.RoutedEventArgs e) { DialogResult = true; }
    void Cancel_Click(object sender, System.Windows.RoutedEventArgs e) { DialogResult = false; }
    public static void Info(System.Windows.Window owner, string title, string message) { var alert = new AlertWindow(title, message); SetOwner(alert, owner); alert.ShowDialog(); }
    public static bool Confirm(System.Windows.Window owner, string title, string message, bool danger = false, string? yes = null, string? no = null) { var alert = new AlertWindow(title, message, true, danger); if (yes != null) alert.OkBtn.Content = yes; if (no != null) alert.CancelBtn.Content = no; SetOwner(alert, owner); return alert.ShowDialog() == true; }
    // Robustez (pacote 11): se a tela dona já foi fechada (ex.: a carga terminou depois do Esc), o aviso fica na
    // janela principal em vez de derrubar o programa com "Owner para uma Janela fechada".
    static void SetOwner(AlertWindow alert, System.Windows.Window owner)
    {
        try { alert.Owner = owner; }
        catch (System.InvalidOperationException) { var main = System.Windows.Application.Current?.MainWindow; if (main != null && !ReferenceEquals(main, owner) && main.IsVisible) alert.Owner = main; }
    }
}
