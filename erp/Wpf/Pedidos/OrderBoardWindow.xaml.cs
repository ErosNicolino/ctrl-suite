namespace GraficaCaixa.Wpf;
public partial class OrderBoardWindow:System.Windows.Window
{
    readonly System.Collections.ObjectModel.ObservableCollection<BoardCard> entryRows=[],approvalRows=[],productionRows=[],finishRows=[];
    readonly GraficaCaixa.DataStore store;readonly ModuleLoader remoteLoad;public OrderBoardWindow(GraficaCaixa.DataStore store){InitializeComponent();this.store=store;remoteLoad=new(this,store,true,true);EntryList.ItemsSource=entryRows;ApprovalList.ItemsSource=approvalRows;ProductionList.ItemsSource=productionRows;FinishList.ItemsSource=finishRows;foreach(var list in new[]{EntryList,ApprovalList,ProductionList,FinishList})remoteLoad.Watch(list);remoteLoad.Cleared+=()=>{entryRows.Clear();approvalRows.Clear();productionRows.Clear();finishRows.Clear();};Loaded+=(_,_)=>LoadData();
        // UX 2.0 (pacote 4): "Lista" volta para a lista na mesma área (dentro da janela principal); aberto como janela, o seletor e "Novo pedido" não aparecem.
        Loaded+=(_,_)=>{if(Owner is not MainWindow){OrdersView.Visibility=NewOrderButton.Visibility=System.Windows.Visibility.Collapsed;}};
        OrdersView.ModeChanged+=mode=>{if(mode=="Lista"&&Owner is MainWindow main){ViewToggle.Store("pedidos","Lista");main.ShowOrdersList();}};
        // Cores dos cartões vêm do tema: ao trocar claro/escuro, refaz os cartões.
        ThemeManager.Changed+=ThemeChanged;Closed+=(_,_)=>ThemeManager.Changed-=ThemeChanged;}
    void ThemeChanged()=>Dispatcher.BeginInvoke(LoadData);
    void Sync(System.Collections.ObjectModel.ObservableCollection<BoardCard> target,List<BoardCard> source){if(!store.IsRemote||!remoteLoad.Appending)target.Clear();var ids=target.Select(x=>x.Id).ToHashSet();foreach(var row in source)if(ids.Add(row.Id))target.Add(row);}
    async void LoadData(){var data=store.IsRemote?await remoteLoad.LoadAsync("producao",refresh:LoadData):store.Load();if(data==null)return;var items=data.ScheduledOrders.Where(x=>!x.IsUncollected&&x.Stage!="Entregue").OrderBy(x=>OrderOperations.OperationalPriority(x)).ThenBy(x=>OrderOperations.IsImmediate(x)?x.CreatedAt:x.DeadlineStarted?x.DueDate:DateTime.MaxValue).ThenBy(x=>x.Number).Select(x=>new BoardCard(x)).ToList();if(store.IsRemote)items=data.ScheduledOrders.Select(x=>new BoardCard(x)).ToList();var entry=items.Where(x=>x.Group==0).ToList();var approval=items.Where(x=>x.Group==1).ToList();var production=items.Where(x=>x.Group==2).ToList();var finish=items.Where(x=>x.Group==3).ToList();Sync(entryRows,entry);Sync(approvalRows,approval);Sync(productionRows,production);Sync(finishRows,finish);EntryCount.Text=entry.Count.ToString();ApprovalCount.Text=approval.Count.ToString();ProductionCount.Text=production.Count.ToString();FinishCount.Text=finish.Count.ToString();}
    async void PickupReminders_Click(object s,System.Windows.RoutedEventArgs e)=>await WhatsAppActions.PickupQueueAsync(this,store);
    async void Card_DoubleClick(object s,System.Windows.Input.MouseButtonEventArgs e){if(s is not System.Windows.Controls.ListBox{SelectedItem:BoardCard card})return;var order=(store.IsRemote?await store.QueryRemoteAsync<AppData>($"desktop/context/order/{card.Id}"):store.Load()).ScheduledOrders.FirstOrDefault(x=>x.Id==card.Id);if(order==null)return;var dialog=new ScheduledOrderDialog(order,store){Owner=this};if(dialog.ShowDialog()!=true)return;try{await BusinessUi.SaveOrderAsync(store,dialog.Result!,dialog.CorrectionSaleId,dialog.CorrectedAmount,dialog.CorrectionReason);LoadData();await WhatsAppActions.OfferReadyNoticeAsync(this,store,order.Stage,dialog.Result!);}catch(Exception ex){AlertWindow.Info(this,"Não foi possível salvar",ex.Message);}}
    void NewOrder_Click(object s,System.Windows.RoutedEventArgs e){if(Owner is MainWindow main)main.NewOrder();}
}
// Cartão do quadro. UX 2.0 (pacote 4): cores do tema (claro/escuro) em vez de cores fixas; filete à esquerda
// indica a urgência (vermelho atrasado/vence hoje, âmbar imediato, verde-azulado aguardando retirada); sem emoji.
public sealed class BoardCard
{
    public Guid Id{get;}public string Customer{get;}public string Description{get;}public string Stage{get;}public string Due{get;}public string Payment{get;}public int Group{get;}public System.Windows.Media.Brush Background{get;}public string PrintStatus{get;}public System.Windows.Media.Brush DueBrush{get;}
    public System.Windows.Media.Brush Stripe{get;}public System.Windows.Media.Brush PaymentBrush{get;}public string StageLine=>$"{Stage} · {PrintStatus}";
    public BoardCard(GraficaCaixa.ScheduledOrder x){Id=x.Id;var pickup=x.Stage=="Pronto para retirada";var immediate=OrderOperations.IsImmediate(x);PrintStatus=x.PdfGeneratedAt!=null?"PDF gerado":"Ficha pendente";Customer=$"#{x.Number} • {x.CustomerName}";Description=x.ItemsDescription;Stage=x.Stage;var days=x.DeadlineStarted?GraficaCaixa.BusinessCalendar.RemainingBusinessDays(DateTime.Today,x.DueDate):int.MaxValue;Due=pickup?"Aguardando retirada":immediate?"IMEDIATO":!x.DeadlineStarted?$"{x.ProductionBusinessDays}d após aprovação":days switch{<0=>$"{Math.Abs(days)}d atrasado",0=>"Vence hoje",1=>"Falta 1 dia",_=>$"Faltam {days} dias"};Payment=x.PricePending?"Valor a definir":x.RemainingAmount<=0?"Quitado":x.RemainingAmount.ToString("C2");Group=x.Stage switch{OrderStages.Art=>0,OrderStages.Approval=>1,OrderStages.Production=>2,_=>3};
        var late=!pickup&&!immediate&&x.DeadlineStarted&&days<=0;
        var stripe=late?"CDanger":immediate?"CWarn":pickup?"COk":x.DeadlineStarted&&days<=1?"CDanger":x.DeadlineStarted&&days<=GraficaCaixa.BusinessRules.Current.PriorityDays?"CWarn":"Line";
        Stripe=ThemeManager.Get(stripe);Background=ThemeManager.Get(late?"DangerSoft":"Surface");
        DueBrush=ThemeManager.Get(pickup?"AccentInk":immediate?"Warning":x.DeadlineStarted&&days<=1?"Danger":"Muted");
        PaymentBrush=ThemeManager.Get(x.PricePending?"Warning":x.RemainingAmount<=0?"AccentInk":"Danger");}
}
