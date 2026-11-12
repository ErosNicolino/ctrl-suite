namespace GraficaCaixa.Wpf;
public partial class ImprovementNotesWindow : System.Windows.Window
{
    readonly GraficaCaixa.DataStore store;
    readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval=TimeSpan.FromSeconds(1.5) };
    string baseline="";
    bool ready,discarding;
    public ImprovementNotesWindow(GraficaCaixa.DataStore store)
    {
        InitializeComponent();this.store=store;
        // Com servidor, o texto chega depois de a janela abrir (antes a janela travava esperando a rede).
        if(store.IsRemote){Editor.IsEnabled=false;Loaded+=async(_,_)=>{try{baseline=(await store.QueryRemoteAsync<DesktopNotesState>("desktop/notes")).Text;Editor.Text=baseline;Editor.IsEnabled=true;ready=true;}catch(Exception ex){Status.Text=ex.Message;}};}
        else{baseline=store.Load().ImprovementNotes;Editor.Text=baseline;ready=true;}
        timer.Tick+=(_,_)=>{timer.Stop();Save(false);};
        UnsavedChanges.Attach(this,()=>{timer.Stop();if(Save(true))Close();},()=>Editor.Text==baseline?"":Editor.Text,beforePrompt:()=>timer.Stop(),discard:()=>{discarding=true;timer.Stop();},resume:()=>timer.Start());
        Closing+=(_,e)=>{timer.Stop();if(!discarding&&!Save(true))e.Cancel=true;};
    }
    void Editor_Changed(object sender,System.Windows.Controls.TextChangedEventArgs e)
    {if(!ready)return;Status.Text="Alterações ainda não salvas…";timer.Stop();timer.Start();}
    bool Save(bool alert)
    {
        if(Editor.Text==baseline)return true;
        var text=Editor.Text;
        try
        {
            if(store.IsRemote)store.RemoteDesktopWrite("desktop/notes",new DesktopNotesCommand(Guid.NewGuid(),DataStore.DesktopRevision(baseline),text));else store.Update(d=>{if(d.ImprovementNotes!=baseline)throw new InvalidOperationException("As anotações mudaram em outro computador. Seu texto continua nesta janela; copie-o antes de reabrir o caderno para combinar as alterações.");d.ImprovementNotes=text;return 0;});
            baseline=text;Status.Text=$"Salvo às {DateTime.Now:HH:mm:ss}";return true;
        }
        catch(Exception ex){Status.Text="Não foi possível salvar. Seu texto está preservado nesta janela.";if(alert)AlertWindow.Info(this,"Confira as anotações",ex.Message);return false;}
    }
    void Save_Click(object sender,System.Windows.RoutedEventArgs e){timer.Stop();Save(true);}
}
