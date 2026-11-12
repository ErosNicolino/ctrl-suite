using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
namespace GraficaCaixa.Wpf;

public static class DocumentPaths
{

    static DocumentRootResolver resolver=new(null);static AppData? bootstrap;
    public static AppData? Bootstrap=>bootstrap;
    public static void SetBootstrap(AppData value){bootstrap=value;PaymentPolicy.Configure(value.PaymentRates);if(value.Rules!=null)BusinessRules.Current=value.Rules;}
    public static async Task EnsureBootstrapAsync(DataStore store,long revision,CancellationToken cancellation)
    {
        if(bootstrap?.DataVersion==revision)return;
        var page=await store.QueryRemoteAsync<ModulePage>("api/v1/bootstrap",cancellation);
        var value=await Task.Run(()=>page.Data.Deserialize<AppData>()!,cancellation);
        cancellation.ThrowIfCancellationRequested();SetBootstrap(value);
    }
    public static void BeginSession(string? activeEndpoint){bootstrap=null;resolver=new DocumentRootResolver(activeEndpoint,ConfiguredDocumentsRoot());}
    static string? ConfiguredDocumentsRoot()
    {
        var program=Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var erpRoot=Directory.GetParent(program)?.FullName;
        if(erpRoot==null)return null;
        var path=Path.Combine(erpRoot,"Configuracao","servidor.json");
        if(!File.Exists(path))return null;
        using var document=System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        if(!document.RootElement.TryGetProperty("DocumentsRoot",out var property))return null;
        if(property.ValueKind!=System.Text.Json.JsonValueKind.String)throw new InvalidOperationException("DocumentsRoot deve ser texto em servidor.json.");
        return property.GetString();
    }
    public static string InitializeSession(DataStore store)=>Root(store);
    public static string ResolveServerDocument(DataStore store,string path){var canonical=(store.IsRemote?bootstrap??throw new InvalidOperationException("Atualize a tela para carregar a pasta de documentos."):store.Load()).DocumentsRoot;Root(canonical);return ResolveStoredPath(canonical,path);}
    public static string Root(DataStore store)=>Root((store.IsRemote?bootstrap??throw new InvalidOperationException("Configuração de documentos ainda não carregada. Atualize a tela."):store.Load()).DocumentsRoot);
    // Telas que só usam a pasta para foto (produto): abrem mesmo sem o compartilhamento (ex.: notebook pelo Tailscale); a foto fica indisponível.
    public static string TryRoot(DataStore store){try{return Root(store);}catch(Exception ex) when(ex is InvalidOperationException or IOException or UnauthorizedAccessException){return "";}}
    public static string Root(string canonicalRoot)=>resolver.Resolve(canonicalRoot,Probe);
    public static string ResolveStoredPath(string canonicalRoot,string path)
    {
        if(string.IsNullOrWhiteSpace(path))return path;
        var effective=resolver.EffectiveOrCanonical(canonicalRoot);
        return DocumentRootResolver.MapToEffective(path,canonicalRoot,effective);
    }
    public static bool IsAuthorizedPath(string path,string canonicalRoot)
    {
        var effective=resolver.EffectiveOrCanonical(canonicalRoot);
        return DocumentRootResolver.IsWithinEquivalentRoot(path,canonicalRoot,effective);
    }
    public static string ToCanonicalPath(string path,string canonicalRoot)
    {
        var effective=resolver.EffectiveOrCanonical(canonicalRoot);
        return DocumentRootResolver.MapToCanonical(path,canonicalRoot,effective);
    }
    public static string Folder(DataStore store,string category)
    {
        var data=store.IsRemote?bootstrap??throw new InvalidOperationException("Configuração de documentos ainda não carregada. Atualize a tela."):store.Load();var root=Root(data.DocumentsRoot);ReportWriter.LogoPath=ResolveStoredPath(data.DocumentsRoot,data.LogoPath);ReportWriter.Company=data.Company??new();ReportWriter.StampPath=string.IsNullOrWhiteSpace(data.Company?.StampPath)?"":ResolveStoredPath(data.DocumentsRoot,data.Company.StampPath);
        var folder=Path.Combine(root,category);
        try{Directory.CreateDirectory(folder);return folder;}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {resolver.Invalidate();throw new InvalidOperationException("Não foi possível acessar a pasta de "+category+". Verifique a rede, o compartilhamento e a permissão do Windows.",ex);}
    }
    public static string Validate(string folder,bool create=false)
    {
        try
        {
            return ValidatePhysical(folder,create);
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {throw new InvalidOperationException("Não foi possível acessar a pasta de documentos: "+folder+"\nVerifique se o PC principal está ligado, a rede e a permissão de leitura/gravação. Confira Sistema > Configurações de arquivos. Nenhum documento foi movido.",ex);}
    }
    static Exception? Probe(string folder){try{ValidatePhysical(folder,false);return null;}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){return ex;}}
    static string ValidatePhysical(string folder,bool create)
    {
        if(!Path.IsPathFullyQualified(folder))throw new IOException("Informe um caminho completo de pasta.");
        var full=Path.GetFullPath(folder);
        if(create)Directory.CreateDirectory(full);
        if(!Directory.Exists(full))throw new DirectoryNotFoundException("A pasta não está disponível.");
        // A raiz compartilhada possui RX; somente Impressões recebe o teste de escrita.
        using(var entries=Directory.EnumerateFileSystemEntries(full).GetEnumerator())_ = entries.MoveNext();
        var writable=Path.GetFileName(Path.TrimEndingDirectorySeparator(full)).Equals("Impressões",StringComparison.OrdinalIgnoreCase)?full:Path.Combine(full,"Impressões");
        if(!Directory.Exists(writable))throw new DirectoryNotFoundException("A pasta Impressões não está disponível.");
        var probe=Path.Combine(writable,".erp-acesso-"+Guid.NewGuid().ToString("N")+".tmp");
        using(var file=new FileStream(probe,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,1,FileOptions.DeleteOnClose)){file.WriteByte(1);file.Flush();file.Position=0;if(file.ReadByte()!=1)throw new IOException("Falha na leitura de teste.");}
        return full;
    }
}

public sealed class DocumentSettingsWindow:Window
{
    bool writeBusy;
    readonly DataStore store;readonly TextBox path=new();readonly string original;
    public DocumentSettingsWindow(DataStore store,AppData? prepared=null)
    {
        this.store=store;original=(prepared??(store.IsRemote?throw new InvalidOperationException("Carregue as configurações antes de abrir."):store.Load())).DocumentsRoot;
        Title="Configurações de arquivos";Width=660;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(26)};Content=panel;
        panel.Children.Add(new TextBlock{Text="Documentos compartilhados",Style=(Style)FindResource("SectionTitle")});
        panel.Children.Add(new TextBlock{Text="Pasta base do ERP acessível pelos dois computadores. A configuração é única e fica no banco de dados.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)});
        path.Text=original;panel.Children.Add(path);
        panel.Children.Add(new TextBlock{Text="Fichas: Impressões • Recibos e relatórios: Fechamentos\nAlterar a pasta não move nem apaga arquivos antigos. O banco permanece no computador principal.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,16)});
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(buttons);
        var test=new Button{Content="Testar acesso",Style=(Style)FindResource("SoftButton")};test.Click+=async (_,_)=>{try{test.IsEnabled=false;var documentsRoot=path.Text.Trim();await Task.Run(()=>{DocumentPaths.Validate(documentsRoot);DocumentPaths.Validate(Path.Combine(documentsRoot,"Impressões"));});AlertWindow.Info(this,"Pasta disponível","Leitura e gravação confirmadas neste computador. Confira também no outro PC.");}catch(Exception ex){AlertWindow.Info(this,"Pasta indisponível",ex.Message);}finally{test.IsEnabled=true;}};buttons.Children.Add(test);
        var save=new Button{Content="Salvar",Style=(Style)FindResource("PrimaryButton"),Margin=new Thickness(8,0,0,0)};save.Click+=(_,_)=>Save();buttons.Children.Add(save);
        UnsavedChanges.Attach(this,Save);
    }
    async void Save()
    {
        if(writeBusy)return;writeBusy=true;try{
        try
        {
            if((store.IsRemote||App.PrincipalConnection!=null)&&!path.Text.Trim().StartsWith(@"\\"))throw new InvalidOperationException(@"Para os dois PCs, use uma pasta de rede no formato \\computador\compartilhamento\ERP.");
            var value=DocumentPaths.Validate(path.Text.Trim());
            await BusinessUi.ExecuteAsync(store,"settings.documents",Guid.Empty,original,new DocumentRootArgs(value),()=>store.Update(d=>{if(d.DocumentsRoot!=original)throw new InvalidOperationException("A configuração foi alterada em outro computador. Reabra esta janela antes de salvar.");d.DocumentsRoot=value;return 0;}));
            ReportWriter.OutputFolderResolver=()=>DocumentPaths.Folder(store,"Fechamentos");DialogResult=true;
        }
        catch(Exception ex){AlertWindow.Info(this,"Confira a pasta",ex.Message);}
    
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
}
