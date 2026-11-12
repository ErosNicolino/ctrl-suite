using System.Text.Json;
using Microsoft.Win32;
namespace GraficaCaixa.Wpf;
public partial class LoginWindow : System.Windows.Window
{
    bool loginBusy;
    GraficaCaixa.ServerConnection? savedConnection;
    static string ConfigFolder => AppProfile.Folder;
    public LoginWindow()
    {
        InitializeComponent();
        var dataCfg = Path.Combine(ConfigFolder, "pasta.txt");
        var reportsCfg = Path.Combine(ConfigFolder, "pasta_relatorios.txt");
        FolderBox.Text = ReadConfig(dataCfg, Path.Combine(ConfigFolder,"DadosLocais"));
        ReportsFolderBox.Text = ReadConfig(reportsCfg, Path.Combine(AppContext.BaseDirectory, "Relatorios"));
        savedConnection=ClientCredentialStore.LoadConnection();ConnectionBox.Password="";ConnectionBox.ToolTip=savedConnection==null?"Código temporário gerado no PC1":"Este dispositivo já está pareado. Informe outro código somente para refazer o pareamento.";
        ModeBox.ItemsSource=new[]{"Uso individual","Este computador é o principal","Conectar ao principal"};
        ModeBox.SelectedIndex=int.TryParse(ReadConfig(Path.Combine(ConfigFolder,"modo.txt"),"0"),out var mode)?Math.Clamp(mode,0,2):0;
        Loaded+=(_,_)=>{Motion.Pop(CardBox,220);(UserBox.Text.Trim().Length==0?(System.Windows.Controls.Control)UserBox:PasswordBox).Focus();UpdateCaps();};SetupExpander.Expanded+=(_,_)=>Dispatcher.BeginInvoke(()=>SetupExpander.BringIntoView(),System.Windows.Threading.DispatcherPriority.Loaded);
        if(savedConnection!=null&&ClientCredentialStore.LoadSession(savedConnection) is { } remembered){UserBox.Text=remembered.User.Login;Loaded+=(_,_)=>{if(ModeBox.SelectedIndex is 1 or 2)Login_Click(this,new System.Windows.RoutedEventArgs());};}
    }
    // O código temporário não é persistido. A credencial pareada usa ClientCredentialStore/DPAPI.
    static string ConnectionFile=>Path.Combine(ConfigFolder,"conexao.txt");
    static string ReadConfig(string path,string fallback){try{return File.Exists(path)?File.ReadAllText(path):fallback;}catch(IOException){return fallback;}catch(UnauthorizedAccessException){return fallback;}}
    void Choose_Click(object sender, System.Windows.RoutedEventArgs e) { var d = new OpenFolderDialog { Title = "Selecione onde o banco SQLite será salvo", InitialDirectory=Directory.Exists(FolderBox.Text)?FolderBox.Text:null }; if (d.ShowDialog(this) == true) FolderBox.Text = d.FolderName; }
    async void Login_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (loginBusy) return;
        var user=UserBox.Text.Trim();var password=PasswordBox.Password;
        if(user.Length==0){ShowError("Informe o usuário.",UserBox);return;}
        if(password.Length==0&&savedConnection==null){ShowError("Informe a senha.",ShowPassword.IsChecked==true?PlainBox:PasswordBox);return;}
        loginBusy=true;ClearError();LoginButton.IsEnabled=false;LoginButton.Content="Entrando…";
        try
        {
            var reportsFolder = ReportsFolderBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(reportsFolder)) throw new InvalidOperationException("Escolha a pasta onde os relatórios serão salvos.");
            GraficaCaixa.DataStore store;string? activeEndpoint=null;string dataFolder="";
            if(ModeBox.SelectedIndex is 1 or 2)
            {
                // O WPF do PC1 também é cliente do serviço: não resolve caminhos nem carrega a identidade do servidor.
                var code=ConnectionBox.Password.Trim();
                var connection=code.Length>0?GraficaCaixa.ServerConnection.Parse(code):savedConnection??throw new InvalidOperationException("Pareie este dispositivo com um código temporário do PC1. O código antigo da 7B não autoriza a 7C.");
                connection=await GraficaCaixa.RemoteDataClient.SelectEndpointAsync(connection);
                if(connection.Purpose=="pairing"){connection=await GraficaCaixa.RemoteDataClient.PairAsync(connection,Environment.MachineName);ClientCredentialStore.SaveConnection(connection);savedConnection=connection;ConnectionBox.Clear();if(File.Exists(ConnectionFile))File.Delete(ConnectionFile);}
                activeEndpoint=connection.SelectedUrl;
                if(ModeBox.SelectedIndex==1)App.PrincipalConnection=connection;
                store=GraficaCaixa.DataStore.Connect(connection);
                await Task.Run(store.EnsureInitialized);
            }
            else
            {
                var resolution=GraficaCaixa.DataDirectoryResolver.ResolveForMode(0,FolderBox.Text.Trim(),Path.Combine(ConfigFolder,"DadosLocais"))!;
                dataFolder=resolution.DataDirectory;
                store=new GraficaCaixa.DataStore(dataFolder,resolution.IsExplicit);store.EnsureInitialized();
                App.Server=await GraficaCaixa.SharedDataServer.StartAsync(store,Path.Combine(ConfigFolder,"ServidorIndividual"),5075);App.PrincipalConnection=App.Server.Connection;
                var connection=App.PrincipalConnection;var local=connection with {Url=$"https://127.0.0.1:{new Uri(connection.Url).Port}",Urls=null,SelectedUrl=null};
                store=GraficaCaixa.DataStore.Connect(local);activeEndpoint=local.Url;
            }
            var check=password.Length==0?new GraficaCaixa.LoginCheck(await Task.Run(store.ResumeSession),false,"Sessão encerrada. Informe usuário e senha."):await Task.Run(()=>store.VerifyLogin(user,password));
            PasswordBox.Clear();
            if(!check.Ok){await Task.Delay(800);throw new InvalidOperationException(check.Message);}
            var authenticatedIdentity=store.AuthenticatedUser??throw new InvalidOperationException("O servidor não retornou uma identidade autenticada.");
            App.CurrentUser=new GraficaCaixa.OperationContext(authenticatedIdentity.Name,authenticatedIdentity.Role){Permissions=await store.QueryRemoteAsync<Dictionary<string,bool>>("auth/permissions")};App.AuthenticatedStore=store;
            DocumentPaths.BeginSession(activeEndpoint);
            GraficaCaixa.ReportWriter.OutputFolderResolver=()=>DocumentPaths.Folder(store,"Fechamentos");
            Directory.CreateDirectory(ConfigFolder); File.WriteAllText(Path.Combine(ConfigFolder,"modo.txt"),ModeBox.SelectedIndex.ToString()); if(ModeBox.SelectedIndex!=1)File.WriteAllText(Path.Combine(ConfigFolder, "pasta.txt"), dataFolder); File.WriteAllText(Path.Combine(ConfigFolder, "pasta_relatorios.txt"), reportsFolder);
            GraficaCaixa.ReportWriter.OutputFolder = reportsFolder;
            var bootstrap=await store.QueryRemoteAsync<ModulePage>("api/v1/bootstrap");
            DocumentPaths.SetBootstrap(bootstrap.Data.Deserialize<GraficaCaixa.AppData>()!);
            Exception? documentsError=null;
            var main = new MainWindow(store); System.Windows.Application.Current.MainWindow = main; main.Show(); Close();
            if(documentsError!=null)AlertWindow.Info(main,"Documentos indisponíveis",documentsError.Message);
        }
        catch (Exception ex) { if(App.AuthenticatedStore!=null){try{App.AuthenticatedStore.Logout();}catch{}App.AuthenticatedStore=null;}if(App.Server!=null){await App.Server.DisposeAsync();App.Server=null;} App.PrincipalConnection=null;ShowError(ex.Message,ShowPassword.IsChecked==true?PlainBox:PasswordBox); }
        finally{loginBusy=false;if(IsLoaded){LoginButton.IsEnabled=true;LoginButton.Content="Entrar no sistema";}}
    }
    void Mode_Changed(object sender,System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if(FolderBox==null||ConnectionPanel==null)return;
        var principal=ModeBox.SelectedIndex==1;var client=ModeBox.SelectedIndex==2;
        DataPanel.Visibility=client?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;
        ConnectionPanel.Visibility=client?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
        PrepareButton.Visibility=principal?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
        ChooseDataButton.IsEnabled=!principal;FolderBox.IsReadOnly=principal;
        var legacy=Path.Combine(ConfigFolder,"DadosLocais");
        FolderBox.Text=principal?GraficaCaixa.DataDirectoryResolver.GetDisplayPath(legacy):ReadConfig(Path.Combine(ConfigFolder,"pasta.txt"),legacy);
    }
    void Prepare_Click(object sender,System.Windows.RoutedEventArgs e)
    {
        var dialog=new Microsoft.Win32.OpenFileDialog{Title="Selecione o banco recuperado e validado",Filter="Banco de dados (*.db)|*.db"};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var folder=Path.Combine(ConfigFolder,"DadosLocais");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"caixa_dados.db");
            if(File.Exists(path))throw new InvalidOperationException("Já existe um banco local. Ele foi preservado. Não é necessário preparar novamente.");
            using var source=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=dialog.FileName,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly}.ToString());source.Open();
            using(var check=source.CreateCommand()){check.CommandText="PRAGMA integrity_check";using var reader=check.ExecuteReader();while(reader.Read())if(reader.GetString(0)!="ok")throw new InvalidOperationException("O arquivo escolhido ainda apresenta corrupção. Selecione a cópia recuperada.");}
            var staging=Path.Combine(folder,"preparacao-"+Guid.NewGuid().ToString("N")+".db");
            using(var destination=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=staging,Pooling=false}.ToString())){destination.Open();source.BackupDatabase(destination);}
            File.Move(staging,path,false);FolderBox.Text=folder;
            AlertWindow.Info(this,"Dados locais preparados","A cópia foi preparada neste computador. O arquivo de origem permanece intacto. Agora você pode entrar como computador principal.");
        }
        catch(Exception ex){AlertWindow.Info(this,"Não foi possível preparar",ex.Message);}
    }

    // UX 2.0 (pacote 7): estados do formulário. A autenticação em si (VerifyLogin, sessão, pareamento) não mudou.
    void ShowError(string message,System.Windows.Controls.Control? focus)
    {
        ErrorText.Text=message;ErrorBox.Visibility=System.Windows.Visibility.Visible;
        if(focus!=null){Ui.SetHasError(focus,true);focus.Focus();}
    }
    void ClearError(){ErrorBox.Visibility=System.Windows.Visibility.Collapsed;Ui.SetHasError(UserBox,false);Ui.SetHasError(PasswordBox,false);Ui.SetHasError(PlainBox,false);}
    void Field_Changed(object sender,System.Windows.RoutedEventArgs e){if(IsLoaded&&ErrorBox.Visibility==System.Windows.Visibility.Visible)ClearError();if(sender is System.Windows.Controls.PasswordBox pb&&ShowPassword.IsChecked==true&&PlainBox.Text!=pb.Password)PlainBox.Text=pb.Password;}
    void Plain_Changed(object sender,System.Windows.Controls.TextChangedEventArgs e){if(PasswordBox.Password!=PlainBox.Text)PasswordBox.Password=PlainBox.Text;if(IsLoaded&&ErrorBox.Visibility==System.Windows.Visibility.Visible)ClearError();}
    void ShowPassword_Click(object sender,System.Windows.RoutedEventArgs e)
    {
        var show=ShowPassword.IsChecked==true;
        if(show){PlainBox.Text=PasswordBox.Password;PlainBox.Visibility=System.Windows.Visibility.Visible;PasswordBox.Visibility=System.Windows.Visibility.Collapsed;PlainBox.Focus();PlainBox.CaretIndex=PlainBox.Text.Length;}
        else{PasswordBox.Password=PlainBox.Text;PlainBox.Clear();PlainBox.Visibility=System.Windows.Visibility.Collapsed;PasswordBox.Visibility=System.Windows.Visibility.Visible;PasswordBox.Focus();}
        EyeIcon.IconName=show?"eye-off":"eye";
    }
    void Password_KeyDown(object sender,System.Windows.RoutedEventArgs e)=>Dispatcher.BeginInvoke(UpdateCaps);
    void UpdateCaps()=>CapsWarning.Visibility=System.Windows.Input.Keyboard.IsKeyToggled(System.Windows.Input.Key.CapsLock)&&(PasswordBox.IsKeyboardFocusWithin||PlainBox.IsKeyboardFocusWithin)?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
}

