using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
public partial class UsersWindow : Window
{
    readonly DataStore store;
    List<UserListRow> all=[];
    UserListRow? Selected=>UsersGrid.SelectedItem as UserListRow;
    public UsersWindow(DataStore store){InitializeComponent();this.store=store;Loaded+=(_,_)=>Reload();}
    void Reload(){try{all=store.ListUsers().Select(x=>new UserListRow(x)).ToList();Filter();}catch(Exception ex){Feedback.Text=ex.Message;}}
    void Filter(){if(UsersGrid==null)return;var search=SearchBox.Text.Trim();var list=all.Where(x=>(StatusBox.SelectedIndex==2||x.User.Active==(StatusBox.SelectedIndex==0))&&(x.Name.Contains(search,StringComparison.CurrentCultureIgnoreCase)||x.Login.Contains(search,StringComparison.CurrentCultureIgnoreCase))).ToList();UsersGrid.ItemsSource=list;EmptyState.Visibility=list.Count==0?Visibility.Visible:Visibility.Collapsed;Actions.IsEnabled=Selected!=null;}
    void FilterChanged(object s,RoutedEventArgs e)=>Filter();
    void SelectionChanged(object s,SelectionChangedEventArgs e){Actions.IsEnabled=Selected!=null;ToggleButton.Content=Selected?.User.Active==true?"Desativar":"Ativar";}
    void Execute(Action action,string message){try{action();Feedback.Text=message;Reload();}catch(Exception ex){AlertWindow.Info(this,"Não foi possível salvar",ex.Message);}}
    void New_Click(object s,RoutedEventArgs e){var editor=new UserEditorWindow(store,null){Owner=this};if(editor.ShowDialog()==true){Feedback.Text="Usuário criado com sucesso.";Reload();}}
    void Edit_Click(object s,RoutedEventArgs e){if(Selected==null)return;var editor=new UserEditorWindow(store,Selected.User){Owner=this};if(editor.ShowDialog()==true){Feedback.Text="Usuário atualizado. Sessões anteriores encerradas.";Reload();}}
    void Permissions_Click(object s,RoutedEventArgs e){if(Selected==null)return;new PermissionsWindow(store,Selected.User.Id,Selected.Role,Selected.Name){Owner=this}.ShowDialog();}
    void Profiles_Click(object s,RoutedEventArgs e){new ProfilesWindow(store){Owner=this}.ShowDialog();Reload();}
    void Toggle_Click(object s,RoutedEventArgs e){if(Selected is not { } row)return;if(!AlertWindow.Confirm(this,row.User.Active?"Desativar usuário":"Ativar usuário",$"{row.Name}: {(row.User.Active?"o acesso será encerrado; o histórico será preservado.":"o acesso será habilitado.")}"))return;Execute(()=>store.SaveUser(new(row.User.Id,row.Name,row.Login,row.Role,!row.User.Active,null)),"Status atualizado. Histórico preservado.");}
    void Reset_Click(object s,RoutedEventArgs e){if(Selected!=null){var editor=new UserEditorWindow(store,Selected.User,true){Owner=this};if(editor.ShowDialog()==true)Feedback.Text="Senha redefinida. Sessões anteriores encerradas.";}}
    void Revoke_Click(object s,RoutedEventArgs e){if(Selected is not {} row)return;if(AlertWindow.Confirm(this,"Encerrar sessões",$"Encerrar todos os acessos de {row.Name}?"))Execute(()=>store.RevokeSessions(row.User.Id),"Sessões encerradas.");}
}
public sealed class UserListRow(UserSummary user)
{
    public UserSummary User=>user;public string Name=>user.Name;public string Login=>user.Login;public string Role=>user.Role;public string Status=>user.Active?"Ativo":"Inativo";public string LastAccess=>user.LastAccess?.ToLocalTime().ToString("dd/MM/yyyy HH:mm")??"Sem registro";
}
public sealed class UserEditorWindow : Window
{
    // Perfis do servidor (fixos + criados em Perfis de acesso); sem conexão, só os fixos.
    static IReadOnlyList<string> RoleChoices(GraficaCaixa.DataStore store){try{return store.ListRoles().Select(x=>x.Name).ToList();}catch(Exception){return IdentitySecurity.Roles;}}
    public UserEditorWindow(DataStore store,UserSummary? user,bool reset=false)
    {
        Title=reset?"Redefinir senha":user==null?"Novo usuário":"Editar usuário";Width=Math.Min(560,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
        // UX 2.0 (pacote 3): diálogo central; mesmos campos e regras (senhas iguais, mínimo de 8, perfil base, não editar o próprio usuário).
        var panel=new StackPanel();
        var name=new TextBox{Text=user?.Name??""};var login=new TextBox{Text=user?.Login??""};var role=new ComboBox{ItemsSource=RoleChoices(store),SelectedItem=user?.Role??"Financeiro"};var active=new CheckBox{Content="Usuário ativo",IsChecked=user?.Active??true,Margin=new Thickness(0,0,0,12),Style=(Style)FindResource("Switch")};
        var password=new PasswordBox();var confirmation=new PasswordBox();
        void Field(string label,Control control){panel.Children.Add(DialogKit.LabeledField(label,control).Panel);}
        if(!reset){Field("Nome",name);Field("Login individual",login);Field("Perfil base",role);panel.Children.Add(active);}
        Field(user==null?"Senha inicial":reset?"Nova senha":"Nova senha (opcional)",password);Field("Confirmar senha",confirmation);
        var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,MaxWidth=300};feedback.SetResourceReference(TextBlock.ForegroundProperty,"Danger");
        var buttons=new StackPanel{Orientation=Orientation.Horizontal};var cancel=new Button{Content="Cancelar",IsCancel=true,Style=(Style)FindResource("GhostButton")};var save=new Button{Content=reset?"Redefinir senha":"Salvar usuário",Style=(Style)FindResource("DialogPrimaryButton"),Margin=new Thickness(8,0,0,0)};buttons.Children.Add(cancel);buttons.Children.Add(save);
        Content=new DialogFrame{Kind="Neutro",IconName=reset?"key-round":"user-round",Title=Title,Subtitle=reset?"A senha nunca aparece na tela · Esc cancela":"Acesso individual ao ERP · Esc cancela",Content=panel,Footer=buttons,FooterLeft=feedback,ScrollBody=false};
        save.Click+=(_,_)=>{try{if(password.Password!=confirmation.Password)throw new InvalidOperationException("As senhas não coincidem.");if((reset||user==null)&&password.Password.Length<8)throw new InvalidOperationException("Use uma senha de pelo menos 8 caracteres.");if(user?.Id==store.AuthenticatedUser?.UserId)throw new InvalidOperationException("Use Trocar senha de acesso para sua própria senha. Peça a outro administrador para alterar seu cadastro.");store.SaveUser(new(user?.Id,name.Text,login.Text,role.SelectedItem?.ToString()??"",active.IsChecked==true,string.IsNullOrEmpty(password.Password)?null:password.Password));password.Clear();confirmation.Clear();DialogResult=true;}catch(Exception ex){feedback.Text=ex.Message;}};
    }
}
// Perfis de acesso: ver o que cada perfil libera, editar (exceto Administrador, sempre total), criar copiando outro e excluir os criados sem usuários.
public sealed class ProfilesWindow : Window
{
    readonly GraficaCaixa.DataStore store;readonly StackPanel list=new();
    public ProfilesWindow(GraficaCaixa.DataStore store)
    {
        this.store=store;Title="Perfis de acesso";Width=Math.Min(720,SystemParameters.WorkArea.Width-32);SizeToContent=SizeToContent.Height;MaxHeight=SystemParameters.WorkArea.Height-40;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        var body=new StackPanel();body.Children.Add(DialogKit.Hint("Cada usuário tem um perfil base. Ajuste o perfil para todos que o usam, ou use Permissões no usuário para uma exceção individual.",0));
        list.Margin=new Thickness(0,12,0,0);body.Children.Add(list);
        var add=new Button{Content="+ Novo perfil",Style=(Style)FindResource("SoftButton"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0),ToolTip="Ex.: Balcão, Produção — começa com as permissões de um perfil existente"};add.Click+=(_,_)=>Create();body.Children.Add(add);
        var close=new Button{Content="Fechar",Style=(Style)FindResource("GhostButton"),IsCancel=true};close.Click+=(_,_)=>Close();
        Content=new DialogFrame{Kind="Neutro",IconName="user-cog",Title="Perfis de acesso",Subtitle="O que cada perfil libera · Esc fecha",Content=body,Footer=close};
        Loaded+=(_,_)=>Reload();
    }
    void Reload()
    {
        list.Children.Clear();IReadOnlyList<RoleSummary> roles;try{roles=store.ListRoles();}catch(Exception ex){AlertWindow.Info(this,"Perfis de acesso",ex.Message);return;}
        foreach(var r in roles)
        {
            var row=new Border{Style=(Style)FindResource("Card"),Margin=new Thickness(0,0,0,8),Padding=new Thickness(14,10,14,10)};var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.Child=grid;
            var info=new StackPanel();info.Children.Add(new TextBlock{Text=r.Name+(r.BuiltIn?" (fixo)":""),FontWeight=FontWeights.SemiBold,FontSize=15});
            var detail=new TextBlock{Text=r.Name=="Administrador"?$"Acesso total · {r.Users} usuário(s) ativo(s)":$"{r.Allowed} de {r.Total} permissões liberadas · {r.Users} usuário(s) ativo(s)",FontSize=12.5,Margin=new Thickness(0,2,0,0)};detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");info.Children.Add(detail);
            var bar=new Border{Height=5,CornerRadius=new CornerRadius(2.5),Margin=new Thickness(0,6,0,0),Width=220,HorizontalAlignment=HorizontalAlignment.Left};bar.SetResourceReference(Border.BackgroundProperty,"Line");var fill=new Border{Width=220.0*r.Allowed/Math.Max(1,r.Total),HorizontalAlignment=HorizontalAlignment.Left,CornerRadius=new CornerRadius(2.5)};fill.SetResourceReference(Border.BackgroundProperty,"Accent");bar.Child=fill;info.Children.Add(bar);
            grid.Children.Add(info);
            var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(actions,1);grid.Children.Add(actions);
            var role=r.Name;var open=new Button{Content=r.Name=="Administrador"?"Ver permissões":"Ver / editar permissões",Style=(Style)FindResource("SoftButton")};open.Click+=(_,_)=>{new PermissionsWindow(store,null,role,"Perfil "+role){Owner=this}.ShowDialog();Reload();};actions.Children.Add(open);
            if(!r.BuiltIn){var del=new Button{Content="Excluir",Style=(Style)FindResource("DangerCompactButton"),Margin=new Thickness(8,0,0,0),IsEnabled=r.Users==0,ToolTip=r.Users==0?"Excluir este perfil":"Troque o perfil dos usuários antes de excluir"};del.Click+=(_,_)=>{if(!AlertWindow.Confirm(this,"Excluir perfil",$"Excluir o perfil \"{role}\"?",true))return;try{store.DeleteRole(role);Reload();}catch(Exception ex){AlertWindow.Info(this,"Perfil não excluído",ex.Message);}};actions.Children.Add(del);}
            list.Children.Add(row);
        }
    }
    void Create()
    {
        IReadOnlyList<RoleSummary> roles;try{roles=store.ListRoles();}catch(Exception ex){AlertWindow.Info(this,"Perfis de acesso",ex.Message);return;}
        var name=new TextBox{Tag="Ex.: Balcão, Produção"};var copy=new ComboBox{ItemsSource=roles.Select(x=>x.Name).ToList(),SelectedItem="Financeiro"};
        var dialog=new Window{Title="Novo perfil",Width=Math.Min(480,SystemParameters.WorkArea.Width-32),SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,Owner=this};
        var panel=new StackPanel();var nameField=DialogKit.LabeledField("Nome do perfil *",name);panel.Children.Add(nameField.Panel);panel.Children.Add(DialogKit.LabeledField("Começar com as permissões de",copy,"Depois ajuste em Ver / editar permissões.").Panel);
        void Save(){if(name.Text.Trim().Length<3){DialogKit.ShowError(nameField,name,"Informe o nome (mínimo 3 letras).");return;}try{store.CreateRole(new RoleCreate(name.Text.Trim(),copy.SelectedItem?.ToString()??"Financeiro"));dialog.DialogResult=true;}catch(Exception ex){DialogKit.ShowError(nameField,name,ex.Message);}}
        dialog.Content=new DialogFrame{Kind="Neutro",IconName="user-cog",Title="Novo perfil",Subtitle="Ctrl+Enter salva · Esc fecha",Content=panel,Footer=DialogKit.Footer(dialog,"Criar perfil",Save),ScrollBody=false};
        KeyboardFlow.Attach(dialog,Save);dialog.Loaded+=(_,_)=>name.Focus();
        if(dialog.ShowDialog()==true){Toast.Show(this,"Perfil criado. Ajuste as permissões em Ver / editar.",tone:"ok");Reload();}
    }
}
