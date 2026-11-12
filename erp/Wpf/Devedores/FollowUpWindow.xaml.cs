using System.Windows;
using System.Windows.Controls;
using System.Globalization;
namespace GraficaCaixa.Wpf;

public partial class FollowUpWindow : Window
{
    // Faixas de idade: valores anteriores (para a contagem partir do último número) e entrada escalonada só na primeira vez.
    readonly Dictionary<int,string> bandValues=[];bool bandsShown;
    bool writeBusy;bool showLosses;ReceivablePage? debtPage;readonly List<Row> debtRows=[];int debtGeneration;bool debtLoading;CancellationTokenSource? debtRead;
    readonly DataStore store; readonly bool debts;
    readonly System.Windows.Threading.DispatcherTimer timer = new(){Interval=TimeSpan.FromSeconds(15)};
    public sealed record Row(Guid Id,bool Order,string Customer,string Contact,string Description,string Status,string Detail,decimal Balance,DateTime Sort)
    {
        public string CollectionStatus { get; init; } = "";
        // Orçamentos em acompanhamento: valor do orçamento (vazio quando não informado).
        public string AmountText { get; init; } = "";
        public Guid? LossId {get;init;}public string LossNotes{get;init;}="";public string LossDate=>LossId.HasValue?Sort.ToString("dd/MM/yyyy"):"";public bool CanDelete{get;init;}public bool CanEdit{get;init;}=true;public decimal CustomerTotal{get;init;}public int UnknownCount{get;init;}
        public bool OverThirty=>LossId==null&&Balance>0&&(DateTime.Today-Sort.Date).Days>30;
        public string Age => OverThirty?$"Atrasado há {(DateTime.Today-Sort.Date).Days} dias":$"{Sort:dd/MM/yyyy} • {Math.Max(0,(DateTime.Today-Sort.Date).Days)} dias";
        // Idade curta com cor (até 90 dias azul, até 1 ano âmbar, mais de 1 ano vermelho); o texto completo fica no texto de ajuda.
        int Days=>Math.Max(0,(DateTime.Today-Sort.Date).Days);
        bool Open=>LossId==null&&Balance>0;
        public bool OverYear=>Open&&Days>365;
        public string AgeTone=>!Open?"neutral":Days<=90?"info":Days<=365?"warn":"danger";
        public string AgeShort=>LossId==null&&!Open?Sort.ToString("dd/MM/yy"):Days<31?$"{Days} d":Days<365?$"{Days/30} m":$"{Days/365} {(Days/365==1?"ano":"anos")}"+(Days%365/30>0?$" e {Days%365/30} m":"");
    }
    AppData? currentData;readonly ModuleLoader remoteLoad;bool remoteBusy;string? remoteRevision;readonly CancellationTokenSource lifetime=new();
    public FollowUpWindow(DataStore store,bool debts)
    {
        InitializeComponent();Motion.Lift(SummaryTile);this.store=store;this.debts=debts;remoteLoad=new(this,store);
        Heading.Text=Title=debts?"Devedores":"Orçamentos em acompanhamento";
        Subtitle.Text=debts?"Pedidos entregues e serviços avulsos que ainda precisam ser pagos.":"Orçamentos enviados para lembrar de cobrar o retorno até o cliente fechar (ou não) o pedido.";
        AddButton.Content=debts?"＋ Dívida avulsa":"＋ Adicionar orçamento";if(!debts){AmountColumn.Visibility=Visibility.Visible;BalanceColumn.Header="Retorno";BalanceColumn.Width=new DataGridLength(170);FilterBox.Width=230;FilterBox.ToolTip="Em acompanhamento, retorno vencido ou de hoje, convertidos, não fechados ou todos";Grid.SetColumnSpan(SummaryTile,3);if(SummaryTile.Children[0] is Border cap)cap.SetResourceReference(Border.BackgroundProperty,"CInfo");}
        FilterBox.ItemsSource=debts?new[]{"Pendentes","Quitados","Todos","30+ dias"}:new[]{"Em acompanhamento","Retorno vencido / hoje","Convertidos","Não fechados","Todos"};
        FilterBox.SelectedIndex=0;
        if(debts){FinancialStatusColumn.Visibility=Visibility.Collapsed;CollectionFilters.Visibility=OriginFilters.Visibility=CollectionColumn.Visibility=AgeColumn.Visibility=CollectionButton.Visibility=CopyCollectionButton.Visibility=CollectionQueueButton.Visibility=Visibility.Visible;CustomerBalanceText.Visibility=Visibility.Collapsed;CollectionStatusFilter.ItemsSource=new[]{"Qualquer cobrança"}.Concat(CollectionControl.Statuses).Append("Pago");CollectionStatusFilter.SelectedIndex=0;OrderFilter.ItemsSource=new[]{"Mais antigas","Maiores valores","Mais recentes"};OrderFilter.SelectedIndex=0;}
        DeclareLossButton.Visibility=debts?Visibility.Visible:Visibility.Collapsed;
        DeleteDebtButton.Visibility=debts?Visibility.Visible:Visibility.Collapsed;
        ReceiveButton.Visibility=debts?Visibility.Visible:Visibility.Collapsed;
        ContactButton.Visibility=ConvertButton.Visibility=debts?Visibility.Collapsed:Visibility.Visible;
        Loaded+=(_,_)=>Refresh();timer.Tick+=(_,_)=>Poll();Loaded+=(_,_)=>timer.Start();Closed+=(_,_)=>{timer.Stop();lifetime.Cancel();};
        LossHistoryButton.Visibility=RecoverLossButton.Visibility=debts?Visibility.Visible:Visibility.Collapsed;DebtTabs.Visibility=debts?Visibility.Visible:Visibility.Collapsed;
        if(debts)Entries.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((_,e)=>{if(e.VerticalChange>0&&e.ExtentHeightChange==0&&e.OriginalSource is ScrollViewer v&&v.ScrollableHeight-v.VerticalOffset<=Math.Max(1,v.ViewportHeight*.15)&&debtPage!=null&&debtPage.Page*debtPage.PageSize<debtPage.Total)_=RefreshDebts(true);}));
        RowActions.Attach(Entries,"Selecione uma linha para ver as ações (ou clique com o botão direito).",EditButton,WhatsAppButton,ReceiveButton,ContactButton,ConvertButton,CollectionButton,CopyCollectionButton,DeclareLossButton,DeleteDebtButton,LossHistoryButton,RecoverLossButton);
    }
    async void Run(Func<Task> action){
        if(writeBusy)return;writeBusy=true;try{try{await action();}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
    // UX 2.0 (pacote 5): faixas de idade das pendências (só apresentação; as faixas vêm do mesmo cálculo de saldo).
    // Janela baixa: cartões mais finos para mostrar mais linhas. A conta soma a margem para não alternar ao mudar a própria margem.
    bool pageLow;
    void PageRoot_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        var low=debts&&e.NewSize.Height+PageRoot.Margin.Top+PageRoot.Margin.Bottom<700;if(low==pageLow)return;pageLow=low;
        PageRoot.Margin=low?new Thickness(26,12,26,10):new Thickness(26,22,26,18);SummaryHint.Visibility=low?Visibility.Collapsed:Visibility.Visible;Summary.FontSize=low?16:18;
        foreach(var card in BandsPanel.Children.OfType<Grid>().Select(x=>x.Children.OfType<Border>().Last()))
        {
            if(low)card.Padding=new Thickness(14,6,14,6);else card.ClearValue(Border.PaddingProperty);
            if(card.Child is StackPanel stack&&stack.Children[1] is TextBlock value)value.FontSize=low?18:21;
        }
    }
    void UpdateBands(List<AgeBand>? bands)
    {
        BandsPanel.Children.Clear();
        var show=debts&&!showLosses&&bands!=null&&bands.Any(b=>b.Count>0);
        BandsPanel.Visibility=Visibility.Collapsed;if(!show)return;
        BandsPanel.Visibility=Visibility.Visible;if(!bandsShown){bandsShown=true;Dispatcher.BeginInvoke(()=>Motion.Stagger(BandsPanel.Children.OfType<UIElement>()),System.Windows.Threading.DispatcherPriority.Loaded);}
        var stripes=new[]{"CInfo","CWarn","CDanger"};
        for(var i=0;i<bands!.Count;i++)
        {
            var b=bands[i];var tile=new Grid{Margin=new Thickness(i==0?0:10,0,0,0)};
            var cap=new Border{Style=(Style)FindResource("KpiCap")};cap.SetResourceReference(Border.BackgroundProperty,stripes[Math.Min(i,2)]);tile.Children.Add(cap);var card=new Border{Style=(Style)FindResource("KpiCapTile")};if(pageLow)card.Padding=new Thickness(14,6,14,6);var stack=new StackPanel();
            var label=new TextBlock{Text=b.Label,Style=(Style)FindResource("KpiLabel")};stack.Children.Add(label);
            var value=new TextBlock{Text=bandValues.GetValueOrDefault(i,""),FontSize=pageLow?18:21,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,4,0,0)};if(i==2&&b.Total>0)value.SetResourceReference(TextBlock.ForegroundProperty,"Danger");stack.Children.Add(value);
            var hint=new TextBlock{Text=$"{b.Count} pendência(s)",FontSize=12.5,Margin=new Thickness(0,3,0,0),TextTrimming=TextTrimming.CharacterEllipsis};hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted");stack.Children.Add(hint);
            // As antigas: o aviso "cobrar uma última vez ou declarar perda" fica no próprio cartão (antes era uma faixa que tirava linhas da lista).
            if(i==2&&b.Count>0){hint.Text=$"{b.Count} pendência(s) · decidir";hint.ToolTip=$"{b.Count} pendência(s) passam de {(GraficaCaixa.BusinessRules.Current.DebtOldDays==365?"1 ano":GraficaCaixa.BusinessRules.Current.DebtOldDays+" dias")} e somam {b.Total:C2}. Vale decidir: cobrar uma última vez ou declarar perda.";hint.SetResourceReference(TextBlock.ForegroundProperty,"Danger");}
            card.Child=stack;tile.Children.Add(card);BandsPanel.Children.Add(tile);Motion.Lift(tile);var total=b.Total.ToString("C2");bandValues[i]=total;Motion.CountTo(value,total);
        }

    }
    void Run(Action action){try{action();}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}}
    async void Poll(){if(!IsVisible||remoteBusy||remoteLoad.Loading)return;if(!store.IsRemote){Refresh();return;}remoteBusy=true;try{var revision=await store.QueryRemoteAsync<RemoteRevision>("api/v1/revision",lifetime.Token);if(revision.Revision!=remoteRevision){remoteRevision=revision.Revision;Refresh();}}catch{}finally{remoteBusy=false;}}
    async void Refresh(){
        if(debts){await RefreshDebts();return;}
        var scoped=store.IsRemote?await remoteLoad.LoadAsync("possiveis-pedidos",refresh:Refresh,debounce:true):null;if(store.IsRemote&&scoped==null)return;
        Run(()=>{var selected=(Entries.SelectedItem as Row)?.Id;var d=scoped??store.Load();currentData=d;remoteRevision=d.DataVersion.ToString();var active=d.Prospects.Where(x=>x.Status!="Convertido em pedido"&&x.Status!="Não fechou").ToList();var openValue=active.Sum(x=>x.Amount??0);var month=d.Prospects.Where(x=>x.CreatedAt>=new DateTime(DateTime.Today.Year,DateTime.Today.Month,1)).ToList();var won=month.Count(x=>x.Status=="Convertido em pedido");
        var due=active.Count(x=>x.NextContactAt?.Date<=DateTime.Today);
        Summary.Text=$"{active.Count} em acompanhamento"+(openValue>0?$" • {openValue:C2}":"")+(due>0?$" • {due} para cobrar hoje":"");
        SummaryHint.Text=(month.Count>0?$"Este mês, {won} de {month.Count} viraram pedido ({(decimal)won/month.Count:P0}). ":"")+"Use o WhatsApp para cobrar o retorno e \"Registrar contato de hoje\" para marcar a próxima data.";
        var list=d.Prospects.Where(x=>FilterBox.SelectedIndex switch{0=>active.Contains(x),1=>active.Contains(x)&&x.NextContactAt?.Date<=DateTime.Today,2=>x.Status=="Convertido em pedido",3=>x.Status=="Não fechou",_=>true});
        var term=SearchBox.Text.Trim();var rows=list.Select(x=>new Row(x.Id,false,x.CustomerName,PhoneText.Format(x.Contact),x.Description,x.Status,x.NextContactAt is DateTime date?$"{date:dd/MM/yyyy}"+(date.Date<DateTime.Today?" • atrasado":date.Date==DateTime.Today?" • hoje":""):"Sem retorno",0,x.NextContactAt??DateTime.MaxValue){AmountText=x.Amount?.ToString("C2")??"—"}).Where(x=>$"{x.Customer} {x.Contact} {x.Description} {x.Status}".Contains(term,StringComparison.CurrentCultureIgnoreCase)||PhoneText.Matches(x.Contact,term)).OrderBy(x=>x.Sort).ToList();Entries.ItemsSource=rows;Entries.SelectedItem=rows.FirstOrDefault(x=>x.Id==selected);Empty.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;Selection_Changed(this,null!);});
    }
    static Row From(ReceivableRow x)=>new(x.Id,x.Order,x.Customer,x.Contact,x.Description,x.Status,x.Detail,x.Unknown?-1:x.Balance,x.Sort){CollectionStatus=x.CollectionStatus,LossId=x.LossId,LossNotes=x.LossNotes,CanDelete=x.CanDelete,CanEdit=x.CanEdit,CustomerTotal=x.CustomerTotal,UnknownCount=x.UnknownCount};
    async Task RefreshDebts(bool append=false)
    {
        if(append&&debtLoading)return;
        int generation=append?debtGeneration:++debtGeneration;if(!append)debtRead?.Cancel();var read=debtRead=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);debtLoading=true;var selected=(Entries.SelectedItem as Row)?.Id;
        try{
            var filter=FilterBox.SelectedIndex switch{1=>"paid",2=>"all",3=>"old",_=>"pending"};var status=CollectionStatusFilter.SelectedIndex<=0?"":CollectionStatusFilter.SelectedItem?.ToString()??"";var sort=OrderFilter.SelectedIndex switch{1=>"value",2=>"recent",_=>"old"};
            ReceivablePage page;
            if(store.IsRemote){if(!append&&debtPage!=null)await Task.Delay(250,read.Token);var query=$"desktop/receivables?losses={showLosses.ToString().ToLowerInvariant()}&page={(append?(debtPage?.Page??0)+1:1)}&search={ModuleLoader.Encode(SearchBox.Text.Trim())}&filter={filter}&status={ModuleLoader.Encode(showLosses?"":status)}&sort={sort}";if(FromDate.SelectedDate is DateTime from)query+=$"&from={from:yyyy-MM-dd}";if(ToDate.SelectedDate is DateTime to)query+=$"&to={to:yyyy-MM-dd}";page=await store.QueryRemoteAsync<ReceivablePage>(query,read.Token);}
            else {var data=store.Load();currentData=data;var pending=ReceivableViews.Rows(data,false);var losses=ReceivableViews.Rows(data,true);var rows=(showLosses?losses:pending).Where(x=>showLosses||filter=="all"||filter=="paid"&&x.Balance==0&&!x.Unknown||filter=="old"&&x.OverThirty||filter=="pending"&&(x.Unknown||x.Balance>0)).Where(x=>showLosses||status.Length==0||x.CollectionStatus==status).Where(x=>(FromDate.SelectedDate==null||x.Sort.Date>=FromDate.SelectedDate.Value.Date)&&(ToDate.SelectedDate==null||x.Sort.Date<=ToDate.SelectedDate.Value.Date)).Where(x=>$"{x.Customer} {x.Contact} {x.Description} {x.Reason} {x.Notes}".Contains(SearchBox.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)||PhoneText.Matches(x.Contact,SearchBox.Text.Trim()));rows=sort=="value"?rows.OrderByDescending(x=>x.Balance):sort=="recent"?rows.OrderByDescending(x=>x.Sort):rows.OrderBy(x=>x.Sort);var all=rows.ToList();page=new(all,1,all.Count,all.Count,pending.Count(x=>x.Unknown||x.Balance>0),pending.Where(x=>!x.Unknown&&x.Balance>0).Sum(x=>x.Balance),losses.Count,losses.Sum(x=>x.Balance),data.DataVersion,ReceivableViews.Bands(pending));}
            if(generation!=debtGeneration||lifetime.IsCancellationRequested)return;
            if(append&&debtPage?.Revision!=page.Revision){debtLoading=false;await RefreshDebts();return;}
            if(!append)debtRows.Clear();var ids=debtRows.Select(x=>(x.Id,x.LossId)).ToHashSet();debtRows.AddRange(page.Items.Where(x=>ids.Add((x.Id,x.LossId))).Select(From));debtPage=page;remoteRevision=page.Revision.ToString();
            Summary.Text=(showLosses?page.LossTotal:page.PendingTotal).ToString("C2");SummaryHint.Text=showLosses?$"{page.LossCount} perda(s) no total":$"{page.PendingCount} pendência(s) no total";SummaryHint.ToolTip=showLosses?"Somente perdas atuais. Recuperadas e cobranças reabertas ficam fora desta aba.":"O saldo considera os recebimentos registrados. Valores a definir não entram no total.";
            UpdateBands(page.Bands);
            Entries.ItemsSource=debtRows.ToList();Entries.SelectedItem=debtRows.FirstOrDefault(x=>x.Id==selected);Empty.Visibility=debtRows.Count==0?Visibility.Visible:Visibility.Collapsed;
            PendingTab.Background=showLosses?System.Windows.Media.Brushes.Transparent:ThemeManager.Get("AccentSoft");LossTab.Background=showLosses?ThemeManager.Get("AccentSoft"):System.Windows.Media.Brushes.Transparent;
            AddButton.Visibility=showLosses?Visibility.Collapsed:Visibility.Visible;FilterBox.IsEnabled=CollectionStatusFilter.IsEnabled=!showLosses;CollectionColumn.Visibility=showLosses?Visibility.Collapsed:Visibility.Visible;LossDateColumn.Visibility=LossReasonColumn.Visibility=showLosses?Visibility.Visible:Visibility.Collapsed;OriginFromLabel.Text=showLosses?"Perda de":"Origem de";BalanceColumn.Header=showLosses?"Valor perdido":"Saldo / retorno";AgeColumn.Header=showLosses?"Desde a perda":"Idade";Selection_Changed(this,null!);
        }catch(OperationCanceledException){}catch(Exception ex){if(generation==debtGeneration){SummaryHint.Text="Não foi possível atualizar: "+ex.Message;SummaryHint.Visibility=Visibility.Visible;}}finally{if(generation==debtGeneration)debtLoading=false;if(ReferenceEquals(read,debtRead))debtRead=null;read.Dispose();}
    }
    void PendingTab_Click(object s,RoutedEventArgs e){showLosses=false;Entries.SelectedItem=null;Refresh();}
    void LossTab_Click(object s,RoutedEventArgs e){showLosses=true;Entries.SelectedItem=null;Refresh();}
    void Filter_Changed(object s,RoutedEventArgs e){if(IsLoaded)Refresh();}
    bool Can(string key)=>App.CurrentUser.Permissions?.GetValueOrDefault(key)??App.CurrentUser.Role.Equals("Administrador",StringComparison.OrdinalIgnoreCase);
    void Selection_Changed(object s,SelectionChangedEventArgs e){if(EditButton==null)return;var r=Entries.SelectedItem as Row;EditButton.IsEnabled=r?.CanEdit==true;WhatsAppButton.IsEnabled=!showLosses&&r!=null&&ReceivableViews.FullPhone(r.Contact);CollectionButton.IsEnabled=debts&&!showLosses&&r!=null;CopyCollectionButton.IsEnabled=debts&&!showLosses&&r?.Balance>0;
        if(debts)CustomerBalanceText.Text=r==null?"":showLosses?$"{r.Customer} • perda registrada; pedido, valores e pagamentos anteriores preservados.":$"{r.Customer} • total pendente neste controle: {r.CustomerTotal:C2} • {r.UnknownCount} valor(es) a definir. Tempo desde a origem da dívida.";
        CustomerBalanceText.Visibility=string.IsNullOrEmpty(CustomerBalanceText.Text)?Visibility.Collapsed:Visibility.Visible;
        DeleteDebtButton.IsEnabled=debts&&!showLosses&&Can("debtors.edit")&&r?.CanDelete==true;
        DeclareLossButton.IsEnabled=debts&&!showLosses&&Can("system.owner")&&r?.Balance>0;ReceiveButton.IsEnabled=debts&&!showLosses&&r?.Balance>0;ContactButton.IsEnabled=ConvertButton.IsEnabled=!debts&&r!=null&&r.Status!="Convertido em pedido"&&r.Status!="Não fechou";
        LossHistoryButton.IsEnabled=debts&&showLosses&&r?.LossId!=null&&Can("system.audit");RecoverLossButton.IsEnabled=debts&&showLosses&&r?.LossId!=null&&r.CanEdit&&Can("system.owner");}
    void LossHistory_Click(object s,RoutedEventArgs e){if(Entries.SelectedItem is Row {LossId:Guid id})new AuditWindow(store,"Perda",id.ToString()){Owner=this}.ShowDialog();}
    void RecoverLoss_Click(object s,RoutedEventArgs e)=>Run(async()=>{
        if(Entries.SelectedItem is not Row {LossId:Guid id} row)return;if(!OwnerAuthentication.Ensure(this,store,allowSetup:false))return;
        var data=await store.BusinessContextAsync("debt",row.Id,lifetime.Token);var loss=data.Prompt12.Losses.SingleOrDefault(x=>x.Id==id)??throw new InvalidOperationException("Perda alterada. Atualize a lista.");var origin=(object?)data.ScheduledOrders.FirstOrDefault(x=>x.Id==row.Id)??data.ManualDebts.FirstOrDefault(x=>x.Id==row.Id)??throw new InvalidOperationException("Origem não encontrada.");
        if(!AlertWindow.Confirm(this,"Recuperar perda",$"{row.Customer} • {row.Description}\nPerda: {loss.Amount:C2}\n\nReabrir esta cobrança e abrir o recebimento normal? Se cancelar o recebimento, o saldo continuará pendente."))return;
        await BusinessUi.ExecuteAsync(store,"loss.reopen",id,loss,new LossReopenArgs(DataStore.DesktopRevision(origin)),()=>store.Update(d=>{if(DataStore.DesktopRevision(d.Prompt12.Losses.Single(x=>x.Id==id))!=DataStore.DesktopRevision(loss))throw new InvalidOperationException("Perda alterada. Atualize a lista.");if(DataStore.DesktopRevision((object?)d.ScheduledOrders.FirstOrDefault(x=>x.Id==row.Id)??d.ManualDebts.Single(x=>x.Id==row.Id))!=DataStore.DesktopRevision(origin))throw new InvalidOperationException("Saldo alterado. Atualize a lista.");LossOperations.Reopen(d,id,App.CurrentUser);return 0;}),lifetime.Token);
        showLosses=false;FilterBox.SelectedIndex=0;await RefreshDebts();await ReceiveAsync(row with {LossId=null});
    });
    void DeleteDebt_Click(object s,RoutedEventArgs e)=>Run(async ()=>{
        if(!debts||Entries.SelectedItem is not Row {Order:false} row)return;
        try{
            var data=await store.BusinessContextAsync("debt",row.Id,lifetime.Token);
            var debt=data.ManualDebts.FirstOrDefault(x=>x.Id==row.Id)??throw new InvalidOperationException("Dívida avulsa removida. Atualize a lista.");
            if(debt.SaleIds.Count>0||data.Prompt12.Losses.Any(x=>x.OrderId==debt.Id))throw new InvalidOperationException("Não é possível excluir uma dívida com recebimentos ou perda registrados.");
            var message=$"Cliente: {debt.CustomerName}\nServiço: {debt.Description}\nValor: {debt.Amount:C2}\n\nExcluir este lançamento avulso? O registro da exclusão ficará na auditoria.";
            if(!AlertWindow.Confirm(this,"Excluir dívida avulsa",message,true))return;
            await BusinessUi.ExecuteAsync(store,"debt.delete",debt.Id,debt,new EmptyArgs(),()=>store.Update(d=>{DataStore.RemoveManualDebt(d,debt.Id,DataStore.DesktopRevision(debt),App.CurrentUser);return 0;}),lifetime.Token);
            Refresh();
        }catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
    });
    void DeclareLoss_Click(object s,RoutedEventArgs e)=>Run(async ()=>{
        if(!debts||Entries.SelectedItem is not Row r||r.Balance<=0)return;
        if(!OwnerAuthentication.Ensure(this,store))return;
        var data=await store.BusinessContextAsync("debt",r.Id);
        if(!LossOperations.IsNormalReceivable(data,r.Id))throw new InvalidOperationException("Este saldo já está fora da cobrança ativa.");
        var balance=r.Order?data.ScheduledOrders.Single(x=>x.Id==r.Id).RemainingAmount:data.ManualDebts.Single(x=>x.Id==r.Id).Remaining(data);
        var dialog=new LossDeclareDialog(r.Customer,r.Description,balance){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        await BusinessUi.ExecuteAsync(store,"loss.declare",r.Id,r.Order?(object?)data.ScheduledOrders.FirstOrDefault(x=>x.Id==r.Id):data.ManualDebts.FirstOrDefault(x=>x.Id==r.Id),new LossDeclareArgs(balance,dialog.Reason,dialog.Date,dialog.Notes),()=>store.Update(d=>{
            OwnerAuthentication.Require(d);
            var current=r.Order?d.ScheduledOrders.Single(x=>x.Id==r.Id).RemainingAmount:d.ManualDebts.Single(x=>x.Id==r.Id).Remaining(d);
            if(current!=balance)throw new InvalidOperationException("O saldo mudou. Atualize a lista e tente novamente.");
            LossOperations.Declare(d,r.Id,dialog.Reason,dialog.Date,dialog.Notes,App.CurrentUser);
            return 0;
        }));Refresh();
    });
    void Add_Click(object s,RoutedEventArgs e)=>Edit(null);
    void Edit_Click(object s,RoutedEventArgs e){if(Entries.SelectedItem is Row r)Edit(r);}
    void Edit(Row? row)=>Run(async ()=>{
        var data=store.IsRemote&&row?.Order==true?await store.QueryRemoteAsync<AppData>($"desktop/context/order/{row.Id}"):await store.BusinessContextAsync(debts?"debt":"prospect",row?.Id??Guid.Empty);if(debts&&row?.Order==true){var order=data.ScheduledOrders.FirstOrDefault(x=>x.Id==row.Id)??throw new InvalidOperationException("Pedido removido.");var orderDialog=new ScheduledOrderDialog(order,store){Owner=this};if(orderDialog.ShowDialog()==true){var closed=await BusinessUi.SaveOrderAsync(store,orderDialog.Result!,orderDialog.CorrectionSaleId,orderDialog.CorrectedAmount,orderDialog.CorrectionReason);Refresh();foreach(var id in closed){var updated=await store.BusinessContextAsync("closed",id);var session=updated.Sessions.First(x=>x.Id==id);ReportWriter.Write(session,updated.CashClosureVersions.Where(x=>x.SessionId==id).OrderBy(x=>x.Version).LastOrDefault());}}return;}
        var debt=row==null?null:data.ManualDebts.FirstOrDefault(x=>x.Id==row.Id);var prospect=row==null?null:data.Prospects.FirstOrDefault(x=>x.Id==row.Id);
        if(row!=null&&(debts?debt==null:prospect==null))throw new InvalidOperationException("Registro removido. Atualize a lista.");
        var dialog=new FollowUpEditor(debts,debt,prospect){Owner=this};if(dialog.ShowDialog()!=true)return;
        await BusinessUi.ExecuteAsync(store,debts?"debt.save":"prospect.save",debts?dialog.Debt!.Id:dialog.Prospect!.Id,debts?(object?)debt:prospect,debts?(object)new DebtSaveArgs(dialog.Debt!):new ProspectSaveArgs(dialog.Prospect!),()=>store.Update(d=>{if(debts){var result=dialog.Debt!;var old=d.ManualDebts.FirstOrDefault(x=>x.Id==result.Id);if(debt!=null){if(old==null)throw new InvalidOperationException("Registro removido.");DataRelations.RequireUnchanged(old,debt);if(result.Amount<old.Paid(d))throw new InvalidOperationException("O total não pode ser menor que o valor já recebido.");d.ManualDebts.Remove(old);}ReceivableViews.Link(d,result);d.ManualDebts.Add(result);if(result.Remaining(d)>0&&(debt==null||debt.InitialCollectionStatus!=result.InitialCollectionStatus))CollectionControl.Register(d,new CollectionContact{DebtId=result.Id,IsOrder=false,Status=result.InitialCollectionStatus,Notes="Status informado no cadastro da dívida."});}else{var result=dialog.Prospect!;var old=d.Prospects.FirstOrDefault(x=>x.Id==result.Id);if(prospect!=null){if(old==null)throw new InvalidOperationException("Registro removido.");DataRelations.RequireUnchanged(old,prospect);d.Prospects.Remove(old);}d.Prospects.Add(result);}d.AuditTrail.Add(new AuditEntry{Action=debts?"Devedor atualizado":"Possível pedido atualizado",Description=dialog.CustomerText});return 0;}));Refresh();
    });
    // Orçamento: mensagem pronta de retorno (modelo "Retorno do orçamento", editável em Modelos de mensagem). Devedores: conversa em branco, como antes.
    void WhatsApp_Click(object s,RoutedEventArgs e)
    {
        if(Entries.SelectedItem is not Row r)return;
        var prospect=debts?null:currentData?.Prospects.FirstOrDefault(x=>x.Id==r.Id);
        var message=prospect==null||currentData==null?"":WhatsAppActions.Message(currentData,CommunicationOperations.BudgetFollowUpTemplateName,new Dictionary<string,string>{["cliente"]=prospect.CustomerName,["referencia"]=prospect.Description,["total"]=prospect.Amount is decimal v?" no valor de "+v.ToString("C2"):"",["grafica"]=currentData.Company?.Name??""});
        WhatsAppActions.Open(this,r.Contact,message);
    }
    void Receive_Click(object s,RoutedEventArgs e)=>Run(async()=>{if(Entries.SelectedItem is Row r&&r.LossId==null)await ReceiveAsync(r);});
    async Task ReceiveAsync(Row r){
        var source=await store.BusinessContextAsync("debt",r.Id,lifetime.Token);var balance=r.Order?source.ScheduledOrders.Single(x=>x.Id==r.Id).RemainingAmount:source.ManualDebts.Single(x=>x.Id==r.Id).Remaining(source);r=r with {Balance=balance};if(r.Balance<=0){Refresh();return;}
        var dialog=new PaymentReceivedWindow(new ScheduledOrder{CustomerName=r.Customer,Description=r.Description,OrderTotal=r.Balance},allowExcess:r.Order){Owner=this};if(dialog.ShowDialog()!=true){Refresh();return;}var request=Guid.NewGuid();
        if(store.IsRemote&&r.Order){var order=(await store.QueryRemoteAsync<AppData>($"desktop/context/order/{r.Id}")).ScheduledOrders.Single();await store.RemoteDesktopWriteAsync($"desktop/orders/{r.Id}/payments",new DesktopPaymentCommand(request,order.Revision,dialog.Payments,dialog.PaidAt));}
        else await BusinessUi.ExecuteAsync(store,"debt.receive",r.Id,(await store.BusinessContextAsync("debt",r.Id)).ManualDebts.FirstOrDefault(x=>x.Id==r.Id),new DebtReceiveArgs(dialog.Payments,dialog.PaidAt),()=>store.Update(d=>{if(r.Order){var order=d.ScheduledOrders.FirstOrDefault(x=>x.Id==r.Id)??throw new InvalidOperationException("Pedido removido.");OrderOperations.AddPayments(d,order,dialog.Payments,dialog.PaidAt,App.CurrentUser);}else{var debt=d.ManualDebts.FirstOrDefault(x=>x.Id==r.Id)??throw new InvalidOperationException("Dívida removida.");var parts=dialog.Payments.Where(p=>!debt.SaleIds.Contains(p.Id)).ToList();var before=debt.Remaining(d);PaymentPolicy.Validate(parts,before);foreach(var part in parts){var sale=new Sale{Id=part.Id,CreatedAt=part.PaidAt??dialog.PaidAt,CustomerName=debt.CustomerName,Contact=debt.Contact,Product=$"Recebimento avulso: {debt.Description}",Units=1,TotalValue=part.Amount,PaymentType=part.PaymentType,FeePercent=part.FeePercent??PaymentPolicy.Rate(part.PaymentType)};FinancialOperations.AddSale(d,sale,App.CurrentUser);debt.SaleIds.Add(part.Id);}AuditService.Record(d,App.CurrentUser,"Dívida recebida","Conta a receber",debt.Id.ToString(),before.ToString("0.00"),debt.Remaining(d).ToString("0.00"),"",$"{debt.CustomerName} • {dialog.Amount:C2}");}return 0;}));Refresh();
    }
    void Collection_Click(object s,RoutedEventArgs e)=>Run(async ()=>{if(Entries.SelectedItem is Row r){new CollectionHistoryWindow(store,r.Id,r.Order,r.Customer,await store.BusinessContextAsync("collection",r.Id)){Owner=this}.ShowDialog();Refresh();}});
    // Cobrança em sequência: todas as pendências da lista (filtro atual) com saldo e WhatsApp completo, uma por vez.
    // "Enviei, registrar e próximo" grava o contato "Cobrança enviada" no histórico (mesmo comando do Histórico de cobrança).
    void CollectionQueue_Click(object s,RoutedEventArgs e)=>Run(async ()=>{
        if(showLosses){AlertWindow.Info(this,"Cobrar em sequência","Volte para as pendências (fora de Perdas) para cobrar em sequência.");return;}
        for(var guard=0;debtPage!=null&&debtRows.Count<debtPage.Total&&guard<50;guard++){var before=debtRows.Count;await RefreshDebts(true);if(debtRows.Count==before)break;}
        var rows=debtRows.Where(x=>x.LossId==null&&x.Balance>0&&CommunicationOperations.IsWhatsAppNumber(x.Contact)).ToList();
        var noPhone=debtRows.Count(x=>x.LossId==null&&x.Balance>0)-rows.Count;
        if(rows.Count==0){AlertWindow.Info(this,"Cobrar em sequência",noPhone>0?$"Nenhuma pendência da lista tem WhatsApp completo ({noPhone} sem telefone com DDD).":"Nenhuma pendência com saldo na lista atual.");return;}
        var items=rows.Select(r=>{AppData? context=null;return new WhatsAppQueueItem(r.Customer,$"{r.Description} · saldo {r.Balance:C2}",r.Contact,
            async ()=>{context=await store.BusinessContextAsync("collection",r.Id);return CollectionControl.Message(context,r.Id,r.Order);},
            async ()=>{
                var data=context??await store.BusinessContextAsync("collection",r.Id);
                var contact=new CollectionContact{DebtId=r.Id,IsOrder=r.Order,Status="Cobrança enviada",Notes="Mensagem de cobrança enviada pelo WhatsApp (cobrança em sequência)."};
                await BusinessUi.ExecuteAsync(store,"collection.save",r.Id,r.Order?(object?)data.ScheduledOrders.FirstOrDefault(x=>x.Id==r.Id):data.ManualDebts.FirstOrDefault(x=>x.Id==r.Id),new CollectionArgs(contact),()=>store.Update(d=>{CollectionControl.Register(d,contact);return 0;}));
            });}).ToList();
        new WhatsAppQueueWindow("Cobrar em sequência",$"{items.Count} pendência(s)"+(noPhone>0?$" · {noPhone} sem WhatsApp ficaram de fora":"")+" · Esc fecha","Enviei, registrar e próximo",items){Owner=this}.ShowDialog();
        await RefreshDebts();
    });
    void CopyCollection_Click(object s,RoutedEventArgs e)=>Run(async ()=>{if(Entries.SelectedItem is Row r){Clipboard.SetText(CollectionControl.Message(await store.BusinessContextAsync("collection",r.Id),r.Id,r.Order));Toast.Show(this,"Mensagem copiada. Cole e confira antes de enviar — nada foi enviado nem registrado.",tone:"ok");}});
    void Contact_Click(object s,RoutedEventArgs e)=>Run(async ()=>{
        if(Entries.SelectedItem is not Row r)return;await BusinessUi.ExecuteAsync(store,"prospect.contact",r.Id,currentData?.Prospects.FirstOrDefault(x=>x.Id==r.Id),new EmptyArgs(),()=>store.Update(d=>{var p=d.Prospects.First(x=>x.Id==r.Id);if(p.OrderId!=null||p.Status=="Não fechou")throw new InvalidOperationException("Este acompanhamento já foi encerrado.");p.LastContactAt=DateTime.Now;p.Notes+=$"\nContato realizado em {DateTime.Now:dd/MM/yyyy HH:mm}";p.NextContactAt=DateTime.Today.AddDays(GraficaCaixa.BusinessRules.Current.FollowUpDays);d.AuditTrail.Add(new AuditEntry{Action="Contato registrado",Description=p.CustomerName});return 0;}));Refresh();AlertWindow.Info(this,"Contato registrado","Próximo retorno em três dias. Use Ver / editar para alterar a data.");
    });
    void Convert_Click(object s,RoutedEventArgs e)=>Run(async ()=>{
        if(Entries.SelectedItem is not Row r)return;var p=(await store.BusinessContextAsync("prospect",r.Id)).Prospects.First(x=>x.Id==r.Id);if(p.OrderId!=null)return;
        var draft=new ScheduledOrder{UseItemPrices=true,CustomerName=p.CustomerName,Contact=p.Contact,Description=p.Description,Items=[new OrderItem{Product=p.Description,Quantity=1,UnitPrice=p.Amount}],Notes=p.Notes,PricePending=p.Amount==null};var dialog=new ScheduledOrderDialog(draft,store){Owner=this};((TextBlock)dialog.FindName("TitleText")).Text="Transformar em pedido";if(dialog.ShowDialog()!=true)return;
        await BusinessUi.ExecuteAsync(store,"prospect.convert",p.Id,p,new ProspectConvertArgs(dialog.Result!),()=>store.Update(d=>{var current=d.Prospects.First(x=>x.Id==p.Id);DataRelations.RequireUnchanged(current,p);if(current.OrderId!=null)throw new InvalidOperationException("Já convertido.");OrderOperations.Save(d,dialog.Result!,App.CurrentUser);current.OrderId=dialog.Result!.Id;current.Status="Convertido em pedido";current.NextContactAt=null;d.AuditTrail.Add(new AuditEntry{Action="Possível pedido convertido",Description=current.CustomerName,UserName=App.CurrentUser.UserName});return 0;}));Refresh();
    });
}



