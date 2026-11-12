namespace GraficaCaixa.Wpf;

// Justificativa obrigatória (mínimo 5 caracteres). Padrão P3: diálogo central simples, erro junto do campo.
public sealed class ReasonWindow : System.Windows.Window
{
    readonly System.Windows.Controls.TextBox box=new(){MinHeight=88,Height=double.NaN,TextWrapping=System.Windows.TextWrapping.Wrap,AcceptsReturn=true,VerticalContentAlignment=System.Windows.VerticalAlignment.Top,Padding=new System.Windows.Thickness(0,8,0,8)};
    readonly System.Windows.Controls.TextBlock error=new(){Visibility=System.Windows.Visibility.Collapsed};
    public string Reason {get;private set;}="";
    public ReasonWindow(string title,string message)
    {
        Title=title;Width=560;SizeToContent=System.Windows.SizeToContent.Height;WindowStartupLocation=System.Windows.WindowStartupLocation.CenterOwner;ResizeMode=System.Windows.ResizeMode.NoResize;ShowInTaskbar=false;
        var panel=new System.Windows.Controls.StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBlock{Text=message,TextWrapping=System.Windows.TextWrapping.Wrap,Margin=new System.Windows.Thickness(0,0,0,12)});
        panel.Children.Add(new System.Windows.Controls.TextBlock{Text="Motivo",Style=(System.Windows.Style)FindResource("FieldLabel")});
        panel.Children.Add(box);error.Style=(System.Windows.Style)FindResource("FieldError");panel.Children.Add(error);
        box.TextChanged+=(_,_)=>{if(error.Visibility==System.Windows.Visibility.Visible&&box.Text.Trim().Length>=5){error.Visibility=System.Windows.Visibility.Collapsed;Ui.SetHasError(box,false);}};
        var buttons=new System.Windows.Controls.StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal};
        var cancel=new System.Windows.Controls.Button{Content="Cancelar",Style=(System.Windows.Style)FindResource("GhostButton"),Margin=new System.Windows.Thickness(0,0,8,0)};cancel.Click+=(_,_)=>DialogResult=false;
        var save=new System.Windows.Controls.Button{Content="Confirmar",MinWidth=120,Style=(System.Windows.Style)FindResource("DialogPrimaryButton")};
        save.Click+=(_,_)=>{if(string.IsNullOrWhiteSpace(box.Text)||box.Text.Trim().Length<5){error.Text="Informe o motivo com pelo menos 5 caracteres.";error.Visibility=System.Windows.Visibility.Visible;Ui.SetHasError(box,true);box.Focus();return;}Reason=box.Text.Trim();DialogResult=true;};
        buttons.Children.Add(cancel);buttons.Children.Add(save);
        // Ctrl+Enter confirma (Enter fica para quebra de linha dentro do motivo).
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control){e.Handled=true;save.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}};
        var frame=new DialogFrame{Kind="Neutro",IconName="pencil",Title=title,Subtitle="Ctrl+Enter confirma · Esc cancela",Content=panel,Footer=buttons,ScrollBody=false};Content=frame;
        // Acompanha a cor da tela de onde foi aberta (pedido azul, lançamento verde…); sem tela de origem conhecida, fica neutra.
        SourceInitialized+=(_,_)=>frame.Kind=KindOf(Owner);
        Loaded+=(_,_)=>box.Focus();
    }

    // Cor do tipo de registro da janela dona: o DialogFrame dela ou, nas páginas de lista, o tipo da página.
    static string KindOf(System.Windows.Window? owner)
    {
        if(owner==null)return "Neutro";
        if(Find(owner) is DialogFrame frame)return frame.Kind;
        return owner switch{ScheduledOrdersWindow or OrderBoardWindow=>"Pedido",MainWindow or ClosedSessionsWindow=>"Lancamento",_=>"Neutro"};
        static DialogFrame? Find(System.Windows.DependencyObject root)
        {
            if(root is DialogFrame found)return found;
            if(root is System.Windows.Window{Content:System.Windows.DependencyObject content})return Find(content);
            for(var i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)if(Find(System.Windows.Media.VisualTreeHelper.GetChild(root,i)) is DialogFrame child)return child;
            return null;
        }
    }
}
