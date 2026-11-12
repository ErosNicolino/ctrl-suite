namespace GraficaCaixa.Wpf;
public partial class OrderHistoryWindow:System.Windows.Window
{
    CancellationTokenSource? selection;readonly CancellationTokenSource lifetime=new();GraficaCaixa.AppData data=new();readonly DataStore store;readonly ModuleLoader remoteLoad;
    public OrderHistoryWindow(GraficaCaixa.DataStore store,string search="")
    {
        InitializeComponent();this.store=store;remoteLoad=new(this,store,true,true);remoteLoad.Watch(OrdersGrid);SearchBox.Text=search;Loaded+=(_,_)=>Refresh();Closed+=(_,_)=>{lifetime.Cancel();selection?.Cancel();};
    }
    void Search_Changed(object s,System.Windows.Controls.TextChangedEventArgs e){if(data!=null)Refresh();}
    async void Refresh()
    {
        if(store.IsRemote){var page=await remoteLoad.LoadAsync("pedidos","&scope=all&sort=CreatedAt&descending=true&search="+ModuleLoader.Encode(SearchBox.Text),Refresh,true);if(page==null)return;data=page;}else data=store.Load();
        var term=SearchBox.Text.Trim().TrimStart('#');
        OrdersGrid.ItemsSource=data.ScheduledOrders.Where(x=>string.IsNullOrEmpty(term)||$"{x.Number} {x.CustomerName} {x.Contact} {x.ItemsDescription} {x.Stage}".Contains(term,StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(x=>x.CreatedAt).ToList();
        if(OrdersGrid.Items.Count>0)OrdersGrid.SelectedIndex=0;else DetailText.Text="Nenhum pedido encontrado para esta pesquisa.";
    }
    async void Selected_Changed(object s,System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if(OrdersGrid.SelectedItem is not GraficaCaixa.ScheduledOrder x)return;
        selection?.Cancel();selection=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=selection.Token;
        if(store.IsRemote)try{var detail=await store.QueryRemoteAsync<AppData>($"desktop/context/order/{x.Id}",token);token.ThrowIfCancellationRequested();data.Sessions=detail.Sessions;}catch(OperationCanceledException){return;}catch(Exception ex){DetailText.Text=ex.Message;return;}
        var lines=new List<string>{$"Pedido #{x.Number} • {x.CustomerName} • {PhoneText.Format(x.Contact)}",$"Criado em {x.CreatedAt:dd/MM/yyyy} • {x.Stage}"+(x.IsCancelled?$" • Cancelado em {x.CancelledAt:dd/MM/yyyy}":x.IsUncollected?" • Arquivado":""),"",x.ItemsDescription,"",
            x.PricePending?"Valor a definir":$"Venda: {x.OrderTotal:C2} • Recebido: {x.PaidAmount:C2} • Saldo: {x.RemainingAmount:C2}",
            x.HasCosts?$"Custo: {x.TotalCost:C2} • Lucro: {(x.Profit.HasValue?x.Profit.Value.ToString("C2"):"A definir")}":"Custos ainda não informados","", "PAGAMENTOS E FECHAMENTOS"};
        var payments=x.Payments.Where(p=>p.SaleId!=null).ToList();
        if(payments.Count==0)lines.Add("Nenhum pagamento registrado.");
        foreach(var p in payments)
        {
            var cash=data.Sessions.FirstOrDefault(c=>c.Sales.Any(v=>v.Id==p.SaleId));
            lines.Add($"{p.PaidAt:dd/MM/yyyy HH:mm} • {p.Note} • {p.PaymentType} • {p.Amount:C2} • "+(cash==null?"Caixa não localizado":cash.IsOpen?$"Caixa aberto em {cash.OpenedAt:dd/MM/yyyy}":$"Caixa de {cash.OpenedAt:dd/MM/yyyy}, fechado em {cash.ClosedAt:dd/MM/yyyy HH:mm}"));
        }
        DetailText.Text=string.Join("\n",lines);
    }
}
