using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GraficaCaixa.Wpf;

// Replaceable first read and single incremental flight, tied to the real page lifecycle.
public sealed class ModuleLoader
{
    readonly Window window;readonly DataStore store;readonly bool incremental;
    CancellationTokenSource? current;bool closed,requested,loadedOnce;
    AppData? accumulated;Action? reload;
    public event Action? Cleared;
    public int Page {get;private set;}=1;
    public ModulePage? Result {get;private set;}
    public bool Loading {get;private set;}
    public bool Appending {get;private set;}
    public bool HasMore=>Result!=null&&(long)Page*Result.PageSize<Result.Total;
    readonly TextBlock status=new(){Margin=new Thickness(12,5,12,5),VerticalAlignment=VerticalAlignment.Center};
    readonly StackPanel tools=new(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
    // Coloca um elemento na faixa de rodapé (à esquerda do status), sem criar outra linha na janela.
    public void AddToFooter(UIElement element)=>tools.Children.Insert(0,element);
    readonly Button previous=new(){Content="Anterior",Margin=new Thickness(4)},next=new(){Content="Próxima",Margin=new Thickness(4)};
    // Último erro da carga (vazio quando deu certo ou foi cancelada): para quem não mostra o rodapé do carregador.
    public string? LastError {get;private set;}
    // showStatus=false: o rodapé continua ocupando o mesmo espaço (as telas embutidas não mudam de altura), mas sem texto;
    // quem usa mostra o próprio status (Início: LastUpdateText).
    public ModuleLoader(Window window,DataStore store,bool paged=false,bool incremental=false,bool showStatus=true)
    {
        this.window=window;this.store=store;this.incremental=incremental;
        window.Closed+=(_,_)=>{closed=true;Cancel();};
        if(!showStatus)status.Visibility=Visibility.Hidden;
        if(window.Content is Grid root)Attach(root,paged);
    }
    public void Attach(Grid root,bool paged)
    {
        if(status.Parent!=null)return;
        if(root.RowDefinitions.Count==0)root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        tools.Children.Add(status);
        if(paged&&!incremental){tools.Children.Add(previous);tools.Children.Add(next);previous.Click+=(_,_)=>{if(Page>1){Page--;reload?.Invoke();}};next.Click+=(_,_)=>{Page++;reload?.Invoke();};}
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Grid.SetRow(tools,root.RowDefinitions.Count-1);Grid.SetColumnSpan(tools,Math.Max(1,root.ColumnDefinitions.Count));root.Children.Add(tools);
    }
    public void Watch(UIElement list)
    {
        if(!incremental||!store.IsRemote)return;
        list.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((_,e)=>{
            if(e.VerticalChange>0&&e.ExtentHeightChange==0&&e.ViewportHeightChange==0&&e.OriginalSource is ScrollViewer s&&NearEnd(s))LoadMore();
        }));
        list.AddHandler(UIElement.PreviewMouseWheelEvent,new MouseWheelEventHandler((_,e)=>{
            if(e.Delta<0&&FindScroll(list) is {} s&&NearEnd(s))LoadMore();
        }),true);
    }
    static bool NearEnd(ScrollViewer s)=>s.ViewportHeight>0&&s.ScrollableHeight-s.VerticalOffset<=Math.Max(1,s.ViewportHeight*.15);
    static ScrollViewer? FindScroll(DependencyObject root){if(root is ScrollViewer s)return s;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var result=FindScroll(VisualTreeHelper.GetChild(root,i));if(result!=null)return result;}return null;}
    public void LoadMore(){if(closed||!incremental||!store.IsRemote||Loading||requested||!HasMore||reload==null)return;requested=true;reload();}
    public void Reset(){Cancel();Page=1;Result=null;accumulated=null;Appending=false;requested=false;if(incremental)Cleared?.Invoke();}
    public void Cancel(){current?.Cancel();requested=false;}
    public async Task<AppData?> LoadAsync(string module,string query="",Action? refresh=null,bool debounce=false)
    {
        if(closed)return null;
        var append=incremental&&requested&&accumulated!=null;requested=false;
        if(incremental&&!append)Reset();
        current?.Cancel();var source=current=new();Loading=true;reload=refresh;Appending=false;LastError=null;status.Text=append?"Carregando mais…":"Carregando…";
        try
        {
            // A espera só serve para a digitação (pesquisa/filtros): a primeira carga da tela sai na hora.
            if(debounce&&!append&&loadedOnce)await Task.Delay(350,source.Token);
            var target=append?Page+1:Page;
            var page=await store.QueryRemoteAsync<ModulePage>($"api/v1/{module}?projection=desktop&page={target}&pageSize=30"+query,source.Token);
            var data=await Task.Run(()=>page.Data.Deserialize<AppData>()!,source.Token);
            await DocumentPaths.EnsureBootstrapAsync(store,data.DataVersion,source.Token);
            source.Token.ThrowIfCancellationRequested();if(closed||!ReferenceEquals(source,current))return null;
            // Offsets belong to one revision. Discard a changed batch and restart instead of skipping rows.
            if(append&&accumulated!.DataVersion!=data.DataVersion){Reset();status.Text="Atualizando…";_=window.Dispatcher.BeginInvoke(()=>reload?.Invoke());return null;}
            if(append){Merge(accumulated!,data,module);data=accumulated!;}
            else if(incremental)accumulated=data;
            Page=target;Result=page;Appending=append;loadedOnce=true;previous.IsEnabled=Page>1;next.IsEnabled=HasMore;
            // "Atualizado" fica só no rodapé do Início (LastUpdateText); aqui só a paginação.
            status.Text=incremental?"":page.Total>0?$"Página {Page} • {page.Total} itens":"";
            return data;
        }
        catch(OperationCanceledException)when(source.IsCancellationRequested){return null;}
        catch(Exception ex){if(!closed&&ReferenceEquals(source,current)){status.Text=ex.Message;LastError=ex.Message;}return null;}
        finally{if(ReferenceEquals(source,current)){Loading=false;current=null;}source.Dispose();}
    }
    static void Add<T>(List<T> target,IEnumerable<T> source,Func<T,Guid> id){var ids=target.Select(id).ToHashSet();foreach(var row in source)if(ids.Add(id(row)))target.Add(row);}
    static void Merge(AppData target,AppData source,string module)
    {
        if(module=="produtos")Add(target.Products,source.Products,x=>x.Id);
        else if(module=="clientes")Add(target.Customers,source.Customers,x=>x.Id);
        else if(module=="pedidos"||module=="producao")Add(target.ScheduledOrders,source.ScheduledOrders,x=>x.Id);
        else if(module=="fechamentos"){Add(target.Sessions,source.Sessions,x=>x.Id);Add(target.CashClosureVersions,source.CashClosureVersions,x=>x.Id);}
        else if(module=="lancamentos")foreach(var session in source.Sessions){var existing=target.Sessions.FirstOrDefault(x=>x.Id==session.Id);if(existing==null)target.Sessions.Add(session);else Add(existing.Sales,session.Sales,x=>x.Id);}
    }
    public static string Encode(string? text)=>Uri.EscapeDataString(text??"");
}
