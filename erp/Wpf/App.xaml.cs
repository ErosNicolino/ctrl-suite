namespace GraficaCaixa.Wpf;
public partial class App : System.Windows.Application
{
    public static GraficaCaixa.SharedDataServer? Server {get;set;}
    public static GraficaCaixa.ServerConnection? PrincipalConnection {get;set;}
    public static GraficaCaixa.DataStore? AuthenticatedStore {get;set;}
    public static GraficaCaixa.OperationContext CurrentUser {get;set;}=new(""," ");
    // O login abre só quando o programa em execução é o próprio ERP (antes, via StartupUri, também abria dentro de
    // ferramentas e testes que criam o App, e uma tecla nele podia encerrar a sessão deles).
    protected override void OnStartup(System.Windows.StartupEventArgs e){ThemeManager.Initialize();RemoteDataClient.UiWait=BusinessUi.Pump;base.OnStartup(e);if(System.Reflection.Assembly.GetEntryAssembly()==typeof(App).Assembly)new LoginWindow().Show();}
    protected override void OnExit(System.Windows.ExitEventArgs e){AuthenticatedStore=null;if(Server!=null)Task.Run(async()=>await Server.DisposeAsync()).GetAwaiter().GetResult();PrincipalConnection=null;base.OnExit(e);}
    public App()
    {
        AccessibleNames.Register();WindowFit.Register();
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Window),
            System.Windows.Input.Keyboard.KeyDownEvent,
            new System.Windows.Input.KeyEventHandler((sender,e)=>
            {
                if(e.Key==System.Windows.Input.Key.Escape&&!e.Handled&&sender is System.Windows.Window window&&window is not GraficaCaixa.Wpf.MainWindow&&window.IsActive&&!UnsavedChanges.HasOpenEscapeElement(window))
                {e.Handled=true;UnsavedChanges.Escape(window);}
            }));
        // Movimento padrão (150–250 ms, ease-out, só opacidade/escala): clique dos botões e troca de abas.
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Button),System.Windows.UIElement.PreviewMouseLeftButtonDownEvent,new System.Windows.Input.MouseButtonEventHandler((s,_)=>{if(s is System.Windows.Controls.Button{IsEnabled:true} b)Motion.Press(b,true);}),true);
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Button),System.Windows.UIElement.PreviewMouseLeftButtonUpEvent,new System.Windows.Input.MouseButtonEventHandler((s,_)=>{if(s is System.Windows.Controls.Button b)Motion.Press(b,false);}),true);
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Button),System.Windows.UIElement.MouseLeaveEvent,new System.Windows.Input.MouseEventHandler((s,_)=>{if(s is System.Windows.Controls.Button b&&b.RenderTransform is System.Windows.Media.ScaleTransform{ScaleX:<1})Motion.Press(b,false);}),true);
        System.Windows.EventManager.RegisterClassHandler(typeof(System.Windows.Controls.TabControl),System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,new System.Windows.Controls.SelectionChangedEventHandler((s,e)=>
        {
            if(s is not System.Windows.Controls.TabControl tab||!ReferenceEquals(e.OriginalSource,tab)||!tab.IsLoaded)return;
            tab.Dispatcher.BeginInvoke(()=>{if(tab.Template?.FindName("PART_SelectedContentHost",tab) is System.Windows.UIElement host)Motion.Switch(host);},System.Windows.Threading.DispatcherPriority.Loaded);
        }));
        DispatcherUnhandledException += (_, e) =>
        {
            // Acumula os erros (antes cada erro apagava o anterior) e mostra o aviso no visual do sistema.
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "erro_inicializacao.log"), $"===== {DateTime.Now:dd/MM/yyyy HH:mm:ss} • {Environment.MachineName}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}"); } catch { }
            var message = $"Não foi possível concluir a operação.\n\nDetalhe: {e.Exception.Message}\n\nO erro foi registrado em erro_inicializacao.log, na pasta do programa.";
            var owner = Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(x => x.IsActive) ?? Current.MainWindow;
            try { if (owner is { IsLoaded: true, IsVisible: true }) AlertWindow.Info(owner, "Algo deu errado", message); else throw new InvalidOperationException(); }
            catch { System.Windows.MessageBox.Show(message, "Algo deu errado", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); }
            if (Current.MainWindow is MainWindow && e.Exception is IOException or InvalidOperationException) e.Handled = true;
        };
    }
}
