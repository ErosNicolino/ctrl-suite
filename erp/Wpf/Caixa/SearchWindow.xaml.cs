using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace GraficaCaixa.Wpf;

public partial class SearchWindow : System.Windows.Window
{
    readonly GraficaCaixa.DataStore store;
    readonly ObservableCollection<SearchSaleRow> rows = [];
    List<SearchSaleRow> allRows = [];string? remoteSort;bool descending;readonly ModuleLoader remoteLoad;bool applying;

    public SearchWindow(GraficaCaixa.DataStore store)
    {
        InitializeComponent();
        this.store = store;remoteLoad=new(this,store,true,true);
        Grid.ItemsSource = rows;remoteLoad.Watch(Grid);Grid.Sorting+=(_,e)=>{if(!store.IsRemote)return;e.Handled=true;var column=e.Column.SortMemberPath;if(string.IsNullOrEmpty(column)&&e.Column is System.Windows.Controls.DataGridBoundColumn b&&b.Binding is System.Windows.Data.Binding binding)column=binding.Path.Path;column=column switch{"Sale.CreatedAt"=>"Date","Sale.Units"=>"Quantity","Sale.Total"=>"Total",_=>column};descending=remoteSort==column&&!descending;remoteSort=column;foreach(var c in Grid.Columns)c.SortDirection=null;e.Column.SortDirection=descending?System.ComponentModel.ListSortDirection.Descending:System.ComponentModel.ListSortDirection.Ascending;remoteLoad.Reset();LoadData();};remoteLoad.Cleared+=()=>{rows.Clear();allRows.Clear();CountText.Text="";};
        PaymentFilter.ItemsSource=PaymentPolicy.Methods.Prepend("Todas as formas").ToArray();PaymentFilter.SelectedIndex=0;
        Loaded += (_, _) => { LoadData(); SearchBox.Focus(); };
    }

    async void LoadData()
    {
        var data=store.IsRemote?await remoteLoad.LoadAsync("lancamentos","&search="+ModuleLoader.Encode(SearchBox.Text)+FilterQuery()+"&sort="+ModuleLoader.Encode(remoteSort)+"&descending="+(descending?"true":"false"),LoadData,true):store.Load();if(data==null)return;
        var hidden=store.IsRemote?new HashSet<Guid>():GraficaCaixa.Cancellations.HiddenSaleIds(data);
        var items=data.Sessions.SelectMany(session=>session.Sales.Where(sale=>!hidden.Contains(sale.Id)).Select(sale=>new SearchSaleRow(session,sale)));
        IComparable? Key(SearchSaleRow x)=>remoteSort switch {"Date"=>x.Sale.CreatedAt,"Customer"=>x.Customer,"Product"=>x.Product,"Quantity"=>x.Sale.Units,"Payment"=>x.Payment,"Total"=>x.Sale.Total,"SessionStatus"=>x.Session.ClosedAt??DateTime.MaxValue,_=>x.Sale.CreatedAt};
        allRows=(store.IsRemote&&remoteSort!=null?(descending?items.OrderByDescending(Key):items.OrderBy(Key)):items.OrderByDescending(x=>x.Sale.CreatedAt)).ThenBy(x=>x.Sale.Id).ToList();
        applying=true;ApplyFilter();applying=false;
    }

    void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();
    void Filter_Changed(object sender,System.Windows.RoutedEventArgs e){if(IsLoaded)ApplyFilter();}
    string? Payment=>PaymentFilter.SelectedIndex>0?PaymentFilter.SelectedItem?.ToString():null;
    // Período e forma de pagamento: no modo conectado vão para o servidor; no local filtram aqui ("Cartão de crédito" inclui as parcelas).
    string FilterQuery()=>(FromDate.SelectedDate is DateTime f?"&from="+f.ToString("yyyy-MM-dd"):"")+(ToDate.SelectedDate is DateTime t?"&to="+t.ToString("yyyy-MM-dd"):"")+(Payment is string p?"&payment="+ModuleLoader.Encode(p):"");
    bool PassesFilters(GraficaCaixa.Sale sale)=>(FromDate.SelectedDate is not DateTime f||sale.CreatedAt>=f.Date)&&(ToDate.SelectedDate is not DateTime t||sale.CreatedAt<t.Date.AddDays(1))&&(Payment is not string p||sale.PaymentType.StartsWith(p,StringComparison.OrdinalIgnoreCase));

    void ApplyFilter()
    {
        if (SearchBox is null) return;if(store.IsRemote&&!applying){remoteLoad.Reset();LoadData();return;}
        var query = SearchSaleRow.Normalize(SearchBox.Text);
        var filtered = store.IsRemote ? allRows : allRows.Where(x => (string.IsNullOrWhiteSpace(query)||x.SearchKey.Contains(query))&&PassesFilters(x.Sale));
        if(!store.IsRemote||!remoteLoad.Appending)rows.Clear();
        var ids=rows.Select(x=>x.Sale.Id).ToHashSet();foreach(var row in filtered)if(ids.Add(row.Sale.Id))rows.Add(row);
        CountText.Text = rows.Count == 1 ? "1 resultado" : $"{rows.Count} resultados";EmptyView.Visibility=rows.Count==0?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;DetailsButton.IsEnabled=Grid.SelectedItem!=null;
    }

    void Details_Click(object sender, System.Windows.RoutedEventArgs e) => ShowDetails();
    void Grid_SelectionChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e)=>DetailsButton.IsEnabled=RefundButton.IsEnabled=Grid.SelectedItem!=null;
    async void Refund_Click(object sender,System.Windows.RoutedEventArgs e){if(Grid.SelectedItem is SearchSaleRow row&&await RefundWindow.RunAsync(this,store,row.Sale.Id)){if(store.IsRemote)remoteLoad.Reset();LoadData();}}
    void Grid_PreviewKeyDown(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Enter&&Grid.SelectedItem!=null){e.Handled=true;ShowDetails();}}
    void Grid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => ShowDetails();

    void ShowDetails()
    {
        if (Grid.SelectedItem is not SearchSaleRow row)
        {
            AlertWindow.Info(this, "Selecione um lançamento", "Escolha uma linha para ver os detalhes.");
            return;
        }
        var sale = row.Sale;
        var dialog=new AlertWindow("Detalhes do lançamento",""){Owner=this};
        var body=(System.Windows.Controls.TextBlock)dialog.FindName("MessageText");
        body.Inlines.Add($"Data: {sale.CreatedAt:dd/MM/yyyy HH:mm}\nCliente / telefone: {GraficaCaixa.DisplayText.Customer(sale)}\nProduto ou serviço: {sale.Product}\nQuantidade: {sale.Units}\nPagamento: {sale.PaymentType}\nValor unitário: ");
        var unit=new System.Windows.Documents.Run(sale.UnitValue!=0?sale.UnitValue.ToString("C2"):"Não informado");if(sale.UnitValue<0)unit.Foreground=ThemeManager.Get("Danger");body.Inlines.Add(unit);body.Inlines.Add("\nTotal: ");var total=new System.Windows.Documents.Run(sale.Total.ToString("C2"));if(sale.Total<0)total.Foreground=ThemeManager.Get("Danger");body.Inlines.Add(total);body.Inlines.Add($"\n\n{row.SessionStatus}");dialog.ShowDialog();
    }
}

public sealed class SearchSaleRow
{
    public GraficaCaixa.CashSession Session { get; }
    public GraficaCaixa.Sale Sale { get; }
    public string Date => Sale.CreatedAt.ToString("dd/MM/yyyy HH:mm");
    public string Customer => GraficaCaixa.DisplayText.Customer(Sale);
    public string Product => Sale.Product;
    public string Quantity => Sale.Product.StartsWith("Sinal")||Sale.Product.StartsWith("Parcela")||Sale.Product.StartsWith("Pagamento final")?"—":$"{Sale.Units} un";
    public string Payment => Sale.PaymentType;
    public string TotalForeground => Sale.Total<0?"#C62828":"#263B42";
    public string Total => Sale.Total.ToString("C2");
    public string SessionStatus => Session.IsOpen ? "Caixa aberto" : $"Fechado em {Session.ClosedAt:dd/MM/yyyy}";
    public string SessionShort => Session.IsOpen ? "Aberto" : $"Fechado {Session.ClosedAt:dd/MM}";
    public string SearchKey { get; }

    public SearchSaleRow(GraficaCaixa.CashSession session, GraficaCaixa.Sale sale)
    {
        Session = session;
        Sale = sale;
        SearchKey = Normalize($"{Date} {Customer} {Product} {Payment} {Total} {sale.Total:0.00} {SessionStatus}");
    }

    public static string Normalize(string value)
    {
        var decomposed = (value ?? "").Normalize(NormalizationForm.FormD);
        return string.Concat(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))).ToLowerInvariant();
    }
}
