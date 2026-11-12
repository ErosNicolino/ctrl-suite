using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace GraficaCaixa.Wpf;

// Campo de senha com "mostrar/ocultar" (os dois controles espelham o mesmo texto) e aviso de Caps Lock.
public sealed class PasswordField : Grid
{
    public readonly PasswordBox Box=new();readonly TextBox plain=new(){Visibility=Visibility.Collapsed};
    bool syncing;
    public string Password=>Box.Password;
    public bool Reveal{get=>plain.Visibility==Visibility.Visible;set{if(value){plain.Text=Box.Password;plain.Visibility=Visibility.Visible;Box.Visibility=Visibility.Collapsed;}else{Box.Password=plain.Text;plain.Clear();plain.Visibility=Visibility.Collapsed;Box.Visibility=Visibility.Visible;}}}
    public event Action? Edited;
    public PasswordField()
    {
        Children.Add(Box);Children.Add(plain);
        Box.PasswordChanged+=(_,_)=>{if(syncing)return;Edited?.Invoke();};
        plain.TextChanged+=(_,_)=>{if(syncing)return;syncing=true;Box.Password=plain.Text;syncing=false;Edited?.Invoke();};
    }
    public new bool Focus(){var target=Reveal?(Control)plain:Box;return target.Focus();}
    public void SelectAll(){if(Reveal)plain.SelectAll();else Box.SelectAll();}
    public Control Input=>Reveal?plain:Box;
    public void MarkError(bool on){Ui.SetHasError(Box,on);Ui.SetHasError(plain,on);}
}

// Troca da senha de acesso.
// UX 2.0 (pacote 7): diálogo simples, mostrar/ocultar, aviso de Caps Lock e erro junto do campo. Mesma regra e mesma gravação (ChangeLoginPassword).
public sealed class ChangePasswordWindow : Window
{
    readonly PasswordField current=new(),next=new(),confirm=new();
    readonly StackPanel caps=new(){Orientation=Orientation.Horizontal,Visibility=Visibility.Collapsed,Margin=new Thickness(0,2,0,6)};
    public ChangePasswordWindow(DataStore store,string user)
    {
        Title="Senha de acesso";Width=Math.Min(520,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var panel=new StackPanel();
        var fields=new List<(PasswordField Box,DialogKit.Field Field)>();
        DialogKit.Field Add(string label,PasswordField box,string? hint=null){var f=DialogKit.LabeledField(label,box,hint);panel.Children.Add(f.Panel);fields.Add((box,f));return f;}
                var currentField=Add("Senha atual",current);
        var nextField=Add($"Nova senha (mínimo {LoginSecurity.MinimumLength} caracteres)",next);var confirmField=Add("Repita a nova senha",confirm);
        var warn=new UiIcon{IconName="triangle-alert",Width=15,Height=15,Margin=new Thickness(0,0,6,0)};warn.SetResourceReference(UiIcon.ForegroundProperty,"Warning");var warnText=new TextBlock{Text="Caps Lock está ligado",FontSize=12.5,FontWeight=FontWeights.SemiBold};warnText.SetResourceReference(TextBlock.ForegroundProperty,"Warning");caps.Children.Add(warn);caps.Children.Add(warnText);panel.Children.Add(caps);
        var show=new CheckBox{Style=(Style)FindResource("Switch"),Content="Mostrar senhas"};show.Click+=(_,_)=>{foreach(var (box,_) in fields)box.Reveal=show.IsChecked==true;};panel.Children.Add(show);
        panel.Children.Add(DialogKit.Hint($"Usuário: {user}. A nova senha vale para todos os computadores conectados.",10));
        void UpdateCaps()=>caps.Visibility=Keyboard.IsKeyToggled(Key.CapsLock)&&fields.Any(f=>f.Box.IsKeyboardFocusWithin)?Visibility.Visible:Visibility.Collapsed;
        foreach(var (box,field) in fields){box.PreviewKeyDown+=(_,_)=>Dispatcher.BeginInvoke(UpdateCaps);box.GotKeyboardFocus+=(_,_)=>Dispatcher.BeginInvoke(UpdateCaps);box.LostKeyboardFocus+=(_,_)=>Dispatcher.BeginInvoke(UpdateCaps);var f=field;box.Edited+=()=>{f.Error.Visibility=Visibility.Collapsed;box.MarkError(false);};}
        Content=new DialogFrame{Kind="Neutro",IconName="key-round",Title="Trocar senha de acesso",Subtitle="Enter avança · Ctrl+Enter salva · Esc fecha",Content=panel,Footer=DialogKit.Footer(this,"Salvar senha",Save),ScrollBody=false};
        KeyboardFlow.Attach(this,Save);
        Loaded+=(_,_)=>current.Focus();
        void Fail(string message,PasswordField target,DialogKit.Field field){field.Error.Text=message;field.Error.Visibility=Visibility.Visible;target.MarkError(true);target.Focus();target.SelectAll();}
        void Save()
        {
            if(next.Password.Length<LoginSecurity.MinimumLength){Fail($"Use pelo menos {LoginSecurity.MinimumLength} caracteres.",next,nextField);return;}
            if(next.Password!=confirm.Password){Fail("As duas senhas novas não são iguais.",confirm,confirmField);return;}
            try{store.ChangeLoginPassword(user,current.Password,next.Password);DialogResult=true;}
            catch(Exception ex) when(ex is UnauthorizedAccessException or InvalidOperationException or IOException){var wrongCurrent=ex is UnauthorizedAccessException;Fail(ex.Message,wrongCurrent?current:next,wrongCurrent?currentField:nextField);}
        }
    }
}
