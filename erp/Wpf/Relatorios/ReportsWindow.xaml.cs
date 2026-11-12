using System.Collections.ObjectModel;
using System.Windows.Media;
namespace GraficaCaixa.Wpf;
public partial class ReportsWindow : System.Windows.Window
{
    readonly GraficaCaixa.DataStore store; readonly ObservableCollection<ReportRow> closings=[]; GraficaCaixa.AppData? currentData; DateTime start; DateTime end;readonly ModuleLoader remoteLoad;
    public ReportsWindow(GraficaCaixa.DataStore store){InitializeComponent();this.store=store;DayView.Store=store;remoteLoad=new(this,store);ClosingsGrid.ItemsSource=closings;Loaded+=(_,_)=>{foreach(var (grid,title) in new[]{(WithdrawalsGrid,"Nenhuma sangria ou ajuste no período"),(SalesGrid,"Nenhum recebimento no período"),(OrdersGrid,"Nenhum pedido no período"),(ClosingsGrid,"Nenhum fechamento no período"),(CancelledGrid,"Nenhum cancelamento no período"),(AbcProductsGrid,"Nenhuma venda de produto no período"),(AbcCustomersGrid,"Nenhum cliente com compra no período")})AttachEmpty(grid,title);Initialize();};ClosingsGrid.MouseDoubleClick+=(_,_)=>Details();}
    // Estado vazio padrão (EmptyState) sobre cada tabela; aparece quando o período não tem linhas.
    readonly Dictionary<System.Windows.Controls.DataGrid,EmptyState> empties=[];
    void AttachEmpty(System.Windows.Controls.DataGrid grid,string title)
    {
        var empty=new EmptyState{Title=title,Hint="Escolha outro período ou use Tudo.",IsHitTestVisible=false,Visibility=System.Windows.Visibility.Collapsed};
        if(grid.Parent is System.Windows.Controls.Border border){border.Child=null;border.Child=new System.Windows.Controls.Grid{Children={grid,empty}};}
        else if(grid.Parent is System.Windows.Controls.Panel panel){System.Windows.Controls.Grid.SetRow(empty,System.Windows.Controls.Grid.GetRow(grid));System.Windows.Controls.Grid.SetColumn(empty,System.Windows.Controls.Grid.GetColumn(grid));System.Windows.Controls.Grid.SetRowSpan(empty,System.Windows.Controls.Grid.GetRowSpan(grid));panel.Children.Add(empty);}
        else return;
        empties[grid]=empty;
    }
    void UpdateEmpty(){foreach(var (grid,empty) in empties)empty.Visibility=grid.Items.Count==0&&grid.Visibility==System.Windows.Visibility.Visible?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;}
    void Initialize(){var now=DateTime.Today;StartDateBox.SelectedDate=new DateTime(now.Year,now.Month,1);EndDateBox.SelectedDate=now;LoadData();}
    bool ReadPeriod(){if(StartDateBox.SelectedDate is not DateTime s||EndDateBox.SelectedDate is not DateTime e)return false;start=s.Date;end=e.Date.AddDays(1);if(start>=end){PeriodText.Text="Período inválido";return false;}return true;}
    async void LoadData()
    {
        if(!IsLoaded||!ReadPeriod())return;var periodLabel=PeriodText.Text;PeriodText.Text="Carregando…";currentData=store.IsRemote?await remoteLoad.LoadAsync("relatorios/resumo",$"&from={start:yyyy-MM-dd}&to={end.AddDays(-1):yyyy-MM-dd}&totais=1",debounce:true):store.Load();if(currentData==null){PeriodText.Text=periodLabel;return;}var data=currentData;var totals=store.IsRemote?Totals():null;var sales=GraficaCaixa.FinancialOverview.SalesByCashDay(data).Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).OrderByDescending(x=>x.CreatedAt).ToList();var orders=data.ScheduledOrders.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end&&!x.IsCancelled).OrderByDescending(x=>x.CreatedAt).ToList();var sessions=data.Sessions.Where(x=>!x.IsOpen&&x.ClosedAt>=start&&x.ClosedAt<end).OrderByDescending(x=>x.ClosedAt).ToList();var revenue=sales.Sum(x=>x.Total);var costs=orders.Where(x=>x.HasCosts).Sum(x=>x.TotalCost);var estimatedProfit=orders.Where(x=>x.Profit.HasValue).Sum(x=>x.Profit!.Value);var costedOrders=orders.Count(x=>x.HasCosts);var receivableOrders=totals?.ReceivableOrders??data.ScheduledOrders.Where(x=>!x.PricePending).Sum(x=>x.RemainingAmount);var receivableDebts=totals?.ReceivableDebts??data.ManualDebts.Sum(x=>x.Remaining(data));var receivable=receivableOrders+receivableDebts;
        RevenueText.Text=revenue.ToString("C2");RevenueHint.Text=$"{sales.Count} lançamento(s) no período";CostsText.Text=costs.ToString("C2");ProfitText.Text=estimatedProfit.ToString("C2");ProfitHint.Text=costedOrders==0?"Nenhum pedido com custo informado":$"Baseado em {costedOrders} pedido(s) com custos";ReceivableText.Text=receivable.ToString("C2");ReceivableHint.Text=$"Pedidos: {receivableOrders:C2} • Dívidas: {receivableDebts:C2}";AverageTicketText.Text=(sales.Count==0?0:revenue/sales.Count).ToString("C2");OrdersTotalText.Text=$"{orders.Count} • {orders.Sum(x=>x.OrderTotal):C2}";var cashDiff=sessions.Sum(x=>(x.DeclaredClosingAmount??0)-x.ExpectedCash);CashDifferenceText.Text=cashDiff.ToString("C2");CashDifferenceText.Foreground=(cashDiff<0?GraficaCaixa.Wpf.ThemeManager.Get("Danger"):GraficaCaixa.Wpf.ThemeManager.Get("Accent"));PeriodText.Text=start.Year==end.AddDays(-1).Year?$"{start:dd/MM} a {end.AddDays(-1):dd/MM/yyyy}":$"{start:dd/MM/yyyy} a {end.AddDays(-1):dd/MM/yyyy}";PeriodText.ToolTip=$"{start:dd/MM/yyyy} a {end.AddDays(-1):dd/MM/yyyy}";
        var overview=totals?.Overview??new GraficaCaixa.FinancialOverview(data,start,end);
        CostsText.Text=overview.ConfirmedCosts.ToString("C2");ProfitText.Text=overview.GrossProfit.ToString("C2");
        ProfitHint.Text=overview.CostsIncomplete?"PARCIAL — custos ainda não apurados":"Faturamento menos custos reais conferidos";
        // UX 2.0 (pacote 5): só apresentação — mesmo valor (GrossProfit), nome honesto quando há custos pendentes, e cobertura em barra.
        ProfitLabel.Text=overview.CostsIncomplete?"Resultado bruto provisório":"Lucro bruto";ProfitText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,overview.CostsIncomplete?"Warning":"Accent");
        if(overview.CostsIncomplete)ProfitHint.Text="Provisório: há custos pendentes, o resultado real pode ser menor";
        var cov=(double)Math.Clamp(overview.Coverage??0m,0m,100m);CoverageFill.Width=new System.Windows.GridLength(cov,System.Windows.GridUnitType.Star);CoverageRest.Width=new System.Windows.GridLength(Math.Max(0,100-cov),System.Windows.GridUnitType.Star);CoveragePercent.Text=overview.Coverage.HasValue?$"{overview.Coverage.Value:N1}%":"—";CoverageBar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty,cov>=90?"COk":cov>=50?"CWarn":"CDanger");
        CoverageText.Text=overview.CoverageText+(overview.CostsIncomplete?"\nApuração PARCIAL: custos pendentes/ignorados não são custos zero.":"");
        DreText.Text=$"Faturamento: {overview.Billed:C2}\n(-) Custos reais conferidos (itens + frete + adicionais): {overview.ConfirmedCosts:C2}\n= Lucro bruto{(overview.CostsIncomplete?" parcial":"")}: {overview.GrossProfit:C2}\nMargem bruta{(overview.CostsIncomplete?" parcial":"")}: {overview.GrossMargin:N2}%\nDespesas fixas / variáveis / total: não apurados\nLucro líquido gerencial e margem líquida: não apurados — sem fonte de despesas operacionais.";FillDre(overview.Billed.ToString("C2"),overview.ConfirmedCosts.ToString("C2"),overview.GrossProfit.ToString("C2"),$"{overview.GrossMargin:N2}%",overview.CostsIncomplete);
        CostChart.ItemsSource=Series(overview.CostSeries);
        BilledText.Text=overview.Billed.ToString("C2");WithdrawalsText.Text=overview.Withdrawals.ToString("C2");
        ReceivableHint.Text+="\n"+overview.ReceiptComposition;
        ComparisonText.Text=$"Recebimentos: {GraficaCaixa.FinancialOverview.Compare(overview.Receipts,overview.PreviousReceipts)}\nVendas/pedidos: {GraficaCaixa.FinancialOverview.Compare(overview.Billed,overview.PreviousBilled)}";
        ComparisonHint.Text=$"Anterior: {start-(end-start):dd/MM/yyyy} a {start.AddDays(-1):dd/MM/yyyy}\nRecebido: {overview.PreviousReceipts:C2} • Vendas: {overview.PreviousBilled:C2}";
        ReceivableHint.Text+=$"\nSaldo atual de todos os períodos; {overview.UnknownOrders} pedido(s) a definir não somados.";
        PaymentChart.ItemsSource=Bars(sales.GroupBy(x=>x.PaymentType).Select(x=>(x.Key,x.Sum(y=>y.Total))).OrderByDescending(x=>x.Item2));
        var priced=orders.Where(x=>!x.PricePending).ToList();
        BalanceChart.ItemsSource=Bars(new[]{("Recebido",priced.Sum(x=>x.PaidAmount)),("Pendente",priced.Sum(x=>x.RemainingAmount))});
        ReceiptsChart.ItemsSource=Series(overview.ReceiptSeries);
        BilledChart.ItemsSource=Series(overview.BilledSeries);
        WithdrawalsGrid.ItemsSource=data.Sessions.SelectMany(session=>session.Withdrawals.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).Select(x=>new{Date=x.CreatedAt.ToString("dd/MM/yyyy HH:mm"),CreatedAt=x.CreatedAt,Session=session.OpenedAt.ToString("dd/MM/yyyy"),Type="Sangria",User=string.IsNullOrWhiteSpace(x.UserName)?"Não registrado":x.UserName,x.Reason,Total=x.Amount.ToString("C2")}).Concat(session.Adjustments.Where(x=>x.CreatedAt>=start&&x.CreatedAt<end).Select(x=>new{Date=x.CreatedAt.ToString("dd/MM/yyyy HH:mm"),CreatedAt=x.CreatedAt,Session=session.OpenedAt.ToString("dd/MM/yyyy"),Type="Ajuste",User=string.IsNullOrWhiteSpace(x.UserName)?"Não registrado":x.UserName,x.Reason,Total=(x.Amount>0?"+":"")+x.Amount.ToString("C2")}))).OrderBy(x=>x.CreatedAt).ToList();
        PaymentGrid.ItemsSource=sales.GroupBy(x=>string.IsNullOrWhiteSpace(x.PaymentType)?"Não informado":x.PaymentType).Select(g=>new SummaryRow(g.Key,g.Count(),g.Sum(x=>x.Total),revenue)).OrderByDescending(x=>x.Value).ToList();ProductGrid.ItemsSource=sales.GroupBy(x=>string.IsNullOrWhiteSpace(x.Product)?"Não informado":x.Product).Select(g=>new SummaryRow(g.Key,g.Count(),g.Sum(x=>x.Total),revenue)).OrderByDescending(x=>x.Value).Take(12).ToList();SalesGrid.ItemsSource=sales.Select(x=>new FinancialSaleRow(x)).ToList();OrdersGrid.ItemsSource=orders.Select(x=>new FinancialOrderRow(x)).ToList();closings.Clear();foreach(var session in sessions)closings.Add(new ReportRow(session,data.CashClosureVersions.Where(x=>x.SessionId==session.Id).OrderBy(x=>x.Version).LastOrDefault()));
        FillAbc(data,sales);
        await LoadCancelled();
        UpdateEmpty();
        await LoadYearAgo();
    }
    // Mesmo período do ano passado: vem à parte para não atrasar a tela. Sem acesso às fontes financeiras, fica sem valor.
    async Task LoadYearAgo()
    {
        var (from,to)=(start,end);
        try
        {
            var y=store.IsRemote?await store.QueryRemoteAsync<GraficaCaixa.YearAgoComparison>($"desktop/reports/year-ago?from={from:yyyy-MM-dd}&to={to.AddDays(-1):yyyy-MM-dd}"):GraficaCaixa.ReportIndicators.YearAgo(store.Load(),from,to);
            if(from!=start||to!=end)return;
            YearAgoText.Text=$"Recebimentos: {GraficaCaixa.FinancialOverview.Compare(y.CurrentReceived,y.Received)}\nVendas/pedidos: {GraficaCaixa.FinancialOverview.Compare(y.CurrentBilled,y.Billed)}";
            YearAgoHint.Text=$"{y.From:dd/MM/yyyy} a {y.To:dd/MM/yyyy}\nRecebido: {y.Received:C2} • Vendas: {y.Billed:C2}";
        }
        catch(Exception){if(from==start&&to==end){YearAgoText.Text="—";YearAgoHint.Text="";}}
    }
    // Curva ABC (produtos e clientes) com a mesma base do faturamento; resumo "N de M fazem 80%".
    void FillAbc(GraficaCaixa.AppData data,List<GraficaCaixa.Sale> sales)
    {
        var (products,customers)=GraficaCaixa.AbcCurve.Of(data,start,end,sales);
        AbcProductsGrid.ItemsSource=products.Select((x,i)=>new AbcRow(x,i)).ToList();AbcCustomersGrid.ItemsSource=customers.Select((x,i)=>new AbcRow(x,i)).ToList();
        static string Summary(List<GraficaCaixa.AbcLine> lines,string noun)=>lines.Count==0?"":$"{lines.Count(x=>x.Class=="A")} de {lines.Count} {noun} fazem 80% de {lines.Sum(x=>x.Revenue):C2}";
        abcSummaries=(Summary(products,"itens"),Summary(customers,"clientes"));AbcView_Changed(this,new System.Windows.RoutedEventArgs());
    }    (string Products,string Customers) abcSummaries=("","");
    void AbcView_Changed(object s,System.Windows.RoutedEventArgs e){if(AbcProductsGrid==null||AbcCustomersGrid==null)return;var products=AbcProductsOption.IsChecked==true;AbcProductsGrid.Visibility=products?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;AbcCustomersGrid.Visibility=products?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;AbcSummary.Text=products?abcSummaries.Products:abcSummaries.Customers;UpdateEmpty();}
    async Task LoadCancelled()
    {
        try
        {
            var all=store.IsRemote?await store.QueryRemoteAsync<List<GraficaCaixa.CancelledRecord>>("desktop/cancelled"):GraficaCaixa.Cancellations.List(store.Load());
            var rows=all.Where(x=>x.CancelledAt>=start&&x.CancelledAt<end).Select(x=>new CancelledRow(x)).ToList();
            CancelledGrid.ItemsSource=rows;CancelledHint.Text=rows.Count==0?"Nenhum cancelamento no período.":$"{rows.Count} cancelamento(s) no período. Eles não aparecem mais nas telas do dia a dia.";var spend=GraficaCaixa.SupplierBook.SpendBySupplier(currentData?.Prompt12??new GraficaCaixa.Prompt12State(),start,end);var spendTotal=spend.Sum(x=>x.Amount);SupplierSpendGrid.ItemsSource=spend.Select(x=>new{x.Supplier,x.Count,x.Amount,Total=x.Amount.ToString("C2"),Share=spendTotal==0?"—":(x.Amount/spendTotal).ToString("P1")}).ToList();SupplierSpendHint.Text=spend.Count==0?"Nenhuma compra registrada no período (ou sem permissão para ver Compras).":$"{spend.Sum(x=>x.Count)} compra(s) · {spendTotal:C2} gastos no período. Compras não entram no caixa.";
        }
        catch(Exception ex){CancelledHint.Text="Não foi possível carregar os cancelados: "+ex.Message;}
    }
    static List<ChartBar> Bars(IEnumerable<(string Name,decimal Value)> values)
    {
        var list=values.ToList();if(list.Count==0)list.Add(("Sem movimento",0));
        var max=list.Max(x=>x.Value);var sum=list.Sum(x=>Math.Max(0,x.Value));return list.Select(x=>new ChartBar(x.Name,x.Value,max,sum)).ToList();
    }
    List<ChartBar> Series(List<(DateTime Date,decimal Value)> values)
    {
        bool daily=(end-start).TotalDays<=62;
        var from=daily?start:new DateTime(start.Year,start.Month,1);
        var rows=new List<(string,decimal)>();
        for(var at=from;at<end;at=daily?at.AddDays(1):at.AddMonths(1))
        {
            var next=daily?at.AddDays(1):at.AddMonths(1);
            rows.Add((at.ToString(daily?"dd/MM":"MM/yyyy"),values.Where(x=>x.Date>=at&&x.Date<next).Sum(x=>x.Value)));
        }
        return Bars(rows);
    }
    void Period_Changed(object s,System.Windows.Controls.SelectionChangedEventArgs e){if(IsLoaded)LoadData();}
    void Today_Click(object s,System.Windows.RoutedEventArgs e){StartDateBox.SelectedDate=EndDateBox.SelectedDate=DateTime.Today;LoadData();}
    void ThisMonth_Click(object s,System.Windows.RoutedEventArgs e){var now=DateTime.Today;StartDateBox.SelectedDate=new DateTime(now.Year,now.Month,1);EndDateBox.SelectedDate=now;LoadData();}
    void LastMonth_Click(object s,System.Windows.RoutedEventArgs e){var first=new DateTime(DateTime.Today.Year,DateTime.Today.Month,1).AddMonths(-1);StartDateBox.SelectedDate=first;EndDateBox.SelectedDate=first.AddMonths(1).AddDays(-1);LoadData();}
    void Last30_Click(object s,System.Windows.RoutedEventArgs e){EndDateBox.SelectedDate=DateTime.Today;StartDateBox.SelectedDate=DateTime.Today.AddDays(-29);LoadData();}
    // Totais prontos do servidor (resumo, a receber e primeira data); null quando o servidor é anterior e mandou tudo.
    GraficaCaixa.ReportTotals? Totals()=>remoteLoad.Result?.Data.TryGetProperty("Report",out var report)==true?System.Text.Json.JsonSerializer.Deserialize<GraficaCaixa.ReportTotals>(report,GraficaCaixa.FinancialOverview.Wire):null;
    async void AllTime_Click(object s,System.Windows.RoutedEventArgs e)
    {
        DateTime? first;
        if(store.IsRemote){var today=DateTime.Today;var d=await remoteLoad.LoadAsync("relatorios/resumo",$"&from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&totais=1");if(d==null)return;var totals=Totals();first=totals!=null?totals.FirstDate:Dates(d);}
        else first=Dates(store.Load());
        StartDateBox.SelectedDate=first?.Date??DateTime.Today;EndDateBox.SelectedDate=DateTime.Today;LoadData();
        static DateTime? Dates(GraficaCaixa.AppData d){var dates=d.Sessions.SelectMany(x=>x.Sales).Select(x=>x.CreatedAt).Concat(d.ScheduledOrders.Select(x=>x.CreatedAt)).ToList();return dates.Count==0?null:dates.Min();}
    }
    ReportRow? Selected=>ClosingsGrid.SelectedItem as ReportRow; void Details_Click(object s,System.Windows.RoutedEventArgs e)=>Details();
    void Details(){var r=Selected;if(r==null){AlertWindow.Info(this,"Selecione um fechamento","Escolha uma linha para visualizar.");return;}var s=r.Session;var pay=string.Join("\n",s.Sales.GroupBy(x=>x.PaymentType).Select(g=>$"{g.Key}: {g.Sum(x=>x.Total):C2}"));var version=r.Version;AlertWindow.Info(this,"Detalhes do fechamento",$"Versão: {r.VersionText}\n"+(version?.Version>1?$"Retificado por {version.UserName} em {version.CreatedAt:dd/MM/yyyy HH:mm}\nMotivo: {version.Reason}\n":"")+$"Abertura: {s.OpenedAt:dd/MM/yyyy HH:mm}\nFechamento: {s.ClosedAt:dd/MM/yyyy HH:mm}\n\nFundo inicial: {s.OpeningAmount:C2}\nVendas: {s.SalesTotal:C2}\nLançamentos: {s.Sales.Count}\n\nPor pagamento\n{pay}\nSangrias: {s.WithdrawalsTotal:C2}{(s.Adjustments.Count>0?$"\nAjustes de caixa: {(s.AdjustmentsTotal>0?"+":"")}{s.AdjustmentsTotal:C2}":"")}\n\nDinheiro esperado: {s.ExpectedCash:C2}\nDinheiro contado: {s.DeclaredClosingAmount:C2}\nDiferença: {(s.DeclaredClosingAmount-s.ExpectedCash):C2}");}
    void Pdf_Click(object s,System.Windows.RoutedEventArgs e){var r=Selected;if(r==null){AlertWindow.Info(this,"Selecione um fechamento","Escolha uma linha para gerar o PDF.");return;}try{var path=GraficaCaixa.ReportWriter.Write(r.Session,r.Version);AlertWindow.Info(this,"Relatório gerado",$"PDF salvo em:\n{path}");}catch(Exception ex){AlertWindow.Info(this,"Não foi possível gerar",ex.Message);}}
    void Reconciliation_Click(object s,System.Windows.RoutedEventArgs e)=>ReconciliationWindow.Open(this,store);
    void CopySummary_Click(object s,System.Windows.RoutedEventArgs e){if(!ReadPeriod())return;System.Windows.Clipboard.SetText($"Relatório financeiro — {start:dd/MM/yyyy} a {end.AddDays(-1):dd/MM/yyyy}\nRecebimentos: {RevenueText.Text}\nVendas/pedidos registrados: {BilledText.Text}\nSangrias: {WithdrawalsText.Text}\nCustos conferidos: {CostsText.Text}\n{ProfitLabel.Text}: {ProfitText.Text} ({ProfitHint.Text})\n{CoverageText.Text}\n{DreText.Text}\nA receber agora (todos os períodos): {ReceivableText.Text}\nMédia por recebimento: {AverageTicketText.Text}\nPedidos: {OrdersTotalText.Text}\nDiferença de caixa: {CashDifferenceText.Text}");}
    void Close_Click(object s,System.Windows.RoutedEventArgs e)=>Close();
    // Demonstrativo em linhas: descrição à esquerda e valor alinhado à direita.
    void FillDre(string billed,string costs,string profit,string margin,bool partial)
    {
        DrePanel.Children.Clear();
        void Row(string label,string value,string valueBrush="Ink",bool strong=false,bool separator=false,string? tip=null)
        {
            var row=new System.Windows.Controls.Grid{Margin=new System.Windows.Thickness(0,separator?6:0,0,0),ToolTip=tip};
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition{Width=System.Windows.GridLength.Auto});
            var name=new System.Windows.Controls.TextBlock{Text=label,TextWrapping=System.Windows.TextWrapping.Wrap,FontSize=strong?16:14,FontWeight=strong?System.Windows.FontWeights.SemiBold:System.Windows.FontWeights.Normal,Margin=new System.Windows.Thickness(0,7,12,7)};
            var amount=new System.Windows.Controls.TextBlock{Text=value,FontSize=strong?18:14,FontWeight=System.Windows.FontWeights.SemiBold,Foreground=GraficaCaixa.Wpf.ThemeManager.Get(valueBrush),VerticalAlignment=System.Windows.VerticalAlignment.Center};
            System.Windows.Controls.Grid.SetColumn(amount,1);row.Children.Add(name);row.Children.Add(amount);
            DrePanel.Children.Add(separator?new System.Windows.Controls.Border{BorderBrush=GraficaCaixa.Wpf.ThemeManager.Get("Line"),BorderThickness=new System.Windows.Thickness(0,1,0,0),Child=row}:row);
        }
        Row("Faturamento (vendas e pedidos)",billed);
        Row("(−) Custos conferidos (itens, frete e adicionais)",costs,"Danger");
        Row(partial?"= Resultado bruto provisório":"= Lucro bruto",profit,partial?"Warning":"Accent",strong:true,separator:true);
        Row("Margem bruta",margin);
        Row("Despesas e lucro líquido","não apurados","Muted",tip:"Ainda não há cadastro de despesas operacionais (aluguel, luz, salários…).");
        if(partial)DrePanel.Children.Add(new System.Windows.Controls.TextBlock{Text="⚠ Há pedidos com custo pendente: o lucro real pode ser menor.",Foreground=GraficaCaixa.Wpf.ThemeManager.Get("Warning"),FontWeight=System.Windows.FontWeights.SemiBold,TextWrapping=System.Windows.TextWrapping.Wrap,Margin=new System.Windows.Thickness(0,8,0,0)});
    }
}
public sealed class AbcRow(GraficaCaixa.AbcLine x,int rank)
{
    static readonly System.Globalization.CultureInfo Pt=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
    public int Rank=>rank;public string Class=>x.Class;public string ClassTip=>x.Class switch{"A"=>"Classe A: entre os que somam 80% do faturamento","B"=>"Classe B: dos 80% aos 95% do faturamento",_=>"Classe C: os últimos 5% do faturamento"};public string Name=>x.Name;public int Count=>x.Count;
    public decimal QuantityValue=>x.Quantity;public string Quantity=>x.Quantity.ToString("N0",Pt);public decimal RevenueValue=>x.Revenue;public string Revenue=>x.Revenue.ToString("C2",Pt);
    public string Share=>$"{x.Share.ToString("N1",Pt)}%";public string Cumulative=>$"{x.Cumulative.ToString("N1",Pt)}%";
    public decimal MarginValue=>x.Margin??-1000;public string Margin=>x.Margin.HasValue?$"{x.Margin.Value.ToString("N1",Pt)}%":"—";
    public string MarginTip=>x.Margin.HasValue?$"Custo {x.Cost!.Value.ToString("C2",Pt)} · lucro {(x.Revenue-x.Cost.Value).ToString("C2",Pt)}":"Falta custo em alguma venda: margem não apurada.";
    public System.Windows.Media.Brush MarginBrush=>ThemeManager.Get(x.Margin is decimal m?GraficaCaixa.BusinessRules.Current.MarginTone(m):"Muted");
}public sealed class SummaryRow{public string Name{get;}public int Count{get;}public decimal Value{get;}public string Total=>Value.ToString("C2");public string Share{get;}public SummaryRow(string name,int count,decimal value,decimal whole){Name=name;Count=count;Value=value;Share=whole<=0?"0%":$"{value/whole:P1}";}}
public sealed class FinancialSaleRow{public string Date{get;}public string Customer{get;}public string Product{get;}public string Payment{get;}public string Total{get;}public FinancialSaleRow(GraficaCaixa.Sale x){Date=x.CreatedAt.ToString("dd/MM/yyyy HH:mm");Customer=GraficaCaixa.DisplayText.Customer(x);Product=x.Product;Payment=x.PaymentType;Total=x.Total.ToString("C2");}}
public sealed class FinancialOrderRow{public string Number{get;}public string Customer{get;}public string Stage{get;}public string Total{get;}public string Paid{get;}public string Remaining{get;}public string Costs{get;}public string Profit{get;}public string Margin{get;}public System.Windows.Media.Brush MarginBrush{get;}public FinancialOrderRow(GraficaCaixa.ScheduledOrder x){Number=$"#{x.Number}";Customer=x.CustomerName;Stage=x.Stage;Total=x.OrderTotal.ToString("C2");Paid=x.PaidAmount.ToString("C2");Remaining=x.RemainingAmount.ToString("C2");Costs=x.HasCosts?x.TotalCost.ToString("C2"):x.CostStatus;Profit=x.ProfitText;Margin=x.ProfitMargin.HasValue?$"{x.ProfitMargin:N1}%":"—";MarginBrush=ThemeManager.Get(x.ProfitMargin is decimal margin?GraficaCaixa.BusinessRules.Current.MarginTone(margin):"Ink");}}
public sealed class CancelledRow(GraficaCaixa.CancelledRecord x)
{
    public DateTime CancelledAt=>x.CancelledAt;public string Date=>x.CancelledAt.ToString("dd/MM/yyyy HH:mm");public string Kind=>x.Kind;public string Customer=>x.Customer;
    public string Description=>x.Description;public string Total=>x.Amount.ToString("C2");public string Reason=>string.IsNullOrWhiteSpace(x.Reason)?"Não informado":x.Reason;public string User=>x.User;
}
public sealed class ReportRow
{
    public GraficaCaixa.CashSession Session{get;}public GraficaCaixa.CashClosureVersion? Version{get;}public string VersionText=>Version?.Version>1?$"Retificado (v{Version.Version})":"Original (v1)"; public string Opened=>Session.OpenedAt.ToString("dd/MM/yyyy HH:mm");public string Closed=>Session.ClosedAt?.ToString("dd/MM/yyyy HH:mm")??"–";public int Count=>Session.Sales.Count;public string Sales=>Session.SalesTotal.ToString("C2");public string Expected=>Session.ExpectedCash.ToString("C2");public string Declared=>Session.DeclaredClosingAmount?.ToString("C2")??"–";public decimal Diff=>(Session.DeclaredClosingAmount??0)-Session.ExpectedCash;public string Difference=>Diff.ToString("C2");public Brush DifferenceBrush=>(Diff<0?GraficaCaixa.Wpf.ThemeManager.Get("Danger"):GraficaCaixa.Wpf.ThemeManager.Get("Accent"));public ReportRow(GraficaCaixa.CashSession s,GraficaCaixa.CashClosureVersion? version){Session=s;Version=version;}
}
// Barra dos gráficos: valor em R$ pt-BR, participação no total e cor consistente com o Início (forma de pagamento) ou com o sentido (recebido/pendente).
public sealed record ChartBar(string Name,decimal Value,decimal Maximum,decimal Sum=0)
{
    public string Total=>Value.ToString("C2");
    public string Share=>Sum>0?(Value/Sum).ToString("P1",System.Globalization.CultureInfo.GetCultureInfo("pt-BR")):"";
    public string Tip=>Sum>0?$"{Name}: {Total} ({Share} do total)":$"{Name}: {Total}";
    public System.Windows.Media.Brush Color=>GraficaCaixa.Wpf.ThemeManager.Get(Name switch{"Pendente"=>"CWarn","Recebido"=>"COk","PIX"=>"COk","Dinheiro"=>"CWarn","Cartão de débito"=>"CInfo",_ when Name.StartsWith("Cartão de crédito")=>"CViolet","Sem movimento"=>"Line",_=>"Accent"});
    public System.Windows.GridLength Fill=>new((double)Math.Max(0,Value),System.Windows.GridUnitType.Star);
    public System.Windows.GridLength Remainder=>new(Maximum<=0?1:(double)Math.Max(0,Maximum-Value),System.Windows.GridUnitType.Star);
}
