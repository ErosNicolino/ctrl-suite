namespace GraficaCaixa.Wpf;
public partial class ConnectionWindow : System.Windows.Window
{
    public ConnectionWindow(GraficaCaixa.ServerConnection connection,string databasePath)
    {
        InitializeComponent();
        var endpoints=connection.Endpoints;
        AddressText.Text="Computador principal\n\nRede local:\n"+connection.Url;
        var tailscale=endpoints.Where(x=>!x.Equals(connection.Url,StringComparison.OrdinalIgnoreCase)).ToList();
        if(tailscale.Count>0)AddressText.Text+="\n\nTailscale:\n"+string.Join("\n",tailscale);
        // Conectado pelo serviço, o caminho vem como endereço web; mostra uma frase em vez de "https://…ixa_dados.db".
        DataPathText.Text=databasePath.Contains("://",StringComparison.Ordinal)?"Fica no computador principal, na pasta de dados do serviço do ERP.":databasePath;
        CodeBox.Password=connection.PairingCode;
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};void Tick(){if(connection.PairingExpiresAt==null){CountdownText.Text="";return;}var left=connection.PairingExpiresAt.Value-DateTimeOffset.UtcNow;if(left<=TimeSpan.Zero){CodeBox.Clear();CopyButton.IsEnabled=false;CountdownText.Text="Expirado";timer.Stop();}else CountdownText.Text=$"Válido por {(int)left.TotalMinutes:00}:{left.Seconds:00}";}timer.Tick+=(_,_)=>Tick();Closed+=(_,_)=>timer.Stop();Tick();timer.Start();
    }
    void Copy_Click(object sender,System.Windows.RoutedEventArgs e){if(CodeBox.Password.Length==0)return;System.Windows.Clipboard.SetText(CodeBox.Password);Toast.Show(this,"Código copiado. Cole no outro computador em até 10 minutos.",tone:"ok");}
}
