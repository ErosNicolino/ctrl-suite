using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;

public partial class ScheduledOrdersWindow : System.Windows.Window
{
    // Janela baixa: cartões e linhas mais finos para mostrar mais pedidos. A conta soma a margem para não alternar ao mudar a própria margem.
    void PageRoot_SizeChanged(object sender,System.Windows.SizeChangedEventArgs e)
    {
        var low=e.NewSize.Height+PageRoot.Margin.Top+PageRoot.Margin.Bottom<700;
        PageRoot.Margin=low?new System.Windows.Thickness(28,14,28,14):new System.Windows.Thickness(28);PageHeader.Margin=new System.Windows.Thickness(0,0,0,low?10:18);KpiRow.Margin=new System.Windows.Thickness(0,0,0,low?10:16);Grid.RowHeight=low?48:56;
        foreach(var card in KpiRow.Children.OfType<KpiCard>())card.IsCompact=low;
        // Em janela menor, o filtro encolhe para o Cancelar pedido continuar na mesma linha das abas.
        FilterColumn.Width=new System.Windows.GridLength(e.NewSize.Width<1100?160:190);
    }
    public static readonly string[] Stages = OrderStages.All;
    bool showUnpaid;bool showCompleted;bool showUnprinted;bool showReady; string? sortColumn; ListSortDirection sortDirection=ListSortDirection.Ascending; Guid? initialSelection; readonly string? initialSearch;
    readonly GraficaCaixa.DataStore store; readonly ObservableCollection<ScheduledOrderRow> rows = [];
    AppData? loadedData; readonly ModuleLoader remoteLoad;
    bool loading,receiptBusy;
    readonly CancellationTokenSource lifetime=new();
    void History_Click(object s,System.Windows.RoutedEventArgs e){var order=SelectedOrder();if(order!=null)new AuditWindow(store,"Pedido",order.Id.ToString()){Owner=this}.ShowDialog();}
    public const string PriorityFilter="Prioridades";
    // filter (opcional): um item da lista de fases/filtros já escolhido ao abrir (cartões do Início).
    public ScheduledOrdersWindow(GraficaCaixa.DataStore store, Guid? initiallySelected=null, string? search=null, string? filter=null)
    {
        InitializeComponent(); this.store = store;Loaded+=(_,_)=>LoadTrackingStatus();remoteLoad=new(this,store,true,true); remoteLoad.Watch(Grid);remoteLoad.Cleared+=()=>{rows.Clear();loadedData=null;UpdateActionButtons();}; initialSelection=initiallySelected; initialSearch=search; OrdersView.ModeChanged+=mode=>{if(mode=="Cards")Board_Click(this,new System.Windows.RoutedEventArgs());};
        var filters = new[] { "Todas as fases" }.Concat(Stages.Where(x=>x!="Entregue"&&x!=OrderStages.Ready)).Concat(new[]{PriorityFilter, "Arquivados"}).ToArray(); FilterBox.ItemsSource = filters; FilterBox.SelectedIndex = Math.Max(0, Array.IndexOf(filters, filter)); showReady=filter==OrderStages.Ready;
        Loaded += (_, _) => { if(!string.IsNullOrWhiteSpace(initialSearch)) SearchBox.Text=initialSearch; Motion.Stagger(KpiRow.Children.OfType<System.Windows.UIElement>()); RefreshData(); };
        RowActions.Attach(Grid,"Selecione um pedido para ver as ações (ou clique com o botão direito).",PaymentButton,ReceiptButton,CostButton,RepeatButton,EditButton,ArchiveButton,RemoveButton);
        Closed+=(_,_)=>lifetime.Cancel();
    }
    async void RefreshData(bool cached=false)
    {
        if(loading&&!store.IsRemote)return;loading=true;
        try{
        
        var filterText=FilterBox.SelectedItem?.ToString();var scope=showUnprinted?"unprinted":showReady?"ready":showUnpaid?"unpaid":showCompleted?"completed":filterText=="Arquivados"?"archived":filterText=="Prioridades"?"priority":"active";
        var data=store.IsRemote?await remoteLoad.LoadAsync("pedidos","&search="+ModuleLoader.Encode(SearchBox.Text)+"&scope="+scope+"&sort="+ModuleLoader.Encode(sortColumn)+"&descending="+(sortDirection==ListSortDirection.Descending?"true":"false")+(!showCompleted&&!showUnpaid&&Stages.Contains(filterText)?"&stage="+ModuleLoader.Encode(filterText):""),()=>RefreshData(false),cached):store.Load();if(data==null)return;lifetime.Token.ThrowIfCancellationRequested();loadedData=data;var all = data.ScheduledOrders; var today = DateTime.Today;
        ActiveKpi.Value = all.Count(x => !x.IsUncollected && ReceivableViews.OrderBucket(data,x)=="active").ToString();
        var urgent=all.Count(x => !x.IsUncollected && !GraficaCaixa.OrderOperations.IsFinished(x) && x.Stage != "Pronto para retirada" && x.DeadlineStarted && GraficaCaixa.BusinessCalendar.RemainingBusinessDays(today, x.DueDate) <= GraficaCaixa.BusinessRules.Current.PriorityDays);UrgentKpi.Value=urgent.ToString();UrgentKpi.Hint=$"vence em até {GraficaCaixa.BusinessRules.Current.PriorityDays} dias úteis ou atrasado";UrgentKpi.Tone=urgent>0?"danger":"ok";
        ProductionKpi.Value = all.Count(x => !x.IsUncollected && !GraficaCaixa.OrderOperations.IsFinished(x) && x.Stage == "Produção").ToString();
        PendingKpi.Value = all.Count(x => !x.IsUncollected && x.RemainingAmount > 0&&ReceivableViews.ActiveLoss(data,x.Id)==null).ToString();
        var filter=FilterBox.SelectedItem?.ToString();
        IEnumerable<GraficaCaixa.ScheduledOrder> query=all.Where(x=>ReceivableViews.OrderBucket(data,x) is var bucket&&(showUnprinted?bucket is "active" or "ready":bucket==(showUnpaid?"unpaid":showCompleted?"completed":showReady?"ready":"active")));
        query=filter=="Arquivados" && !showCompleted&&!showUnprinted?all.Where(x=>x.IsArchived):query.Where(x=>!x.IsUncollected);if(showUnprinted)query=query.Where(x=>x.PdfGeneratedAt==null);
        if(!showCompleted&&!showUnpaid&&!showUnprinted&&!showReady) {
            if(Stages.Contains(filter)) query=query.Where(x=>x.Stage==filter);
            else if(filter=="Prioridades") query=query.Where(x=>x.Stage!="Pronto para retirada" && x.DeadlineStarted && GraficaCaixa.BusinessCalendar.RemainingBusinessDays(today,x.DueDate)<=GraficaCaixa.BusinessRules.Current.PriorityDays);
        }
        var term = SearchBox.Text.Trim(); var orders = query.Where(x => string.IsNullOrWhiteSpace(term) || store.IsRemote||($"{x.Number} {GraficaCaixa.CustomerDirectory.Label(data,x.CustomerName,x.Contact)} {x.ItemsDescription} {x.Stage}").Contains(term, StringComparison.CurrentCultureIgnoreCase)||PhoneText.Matches(x.Contact,term)).OrderBy(x => x.IsUncollected).ThenBy(x=>showReady?x.ReadyAt??x.CreatedAt:DateTime.MinValue).ThenBy(x => x.Stage == "Entregue").ThenBy(x=>OrderOperations.OperationalPriority(x)).ThenBy(x=>OrderOperations.IsImmediate(x)?x.CreatedAt:x.DeadlineStarted?x.DueDate.Date:DateTime.MaxValue.Date).ThenBy(x => x.Number).ToList();
        if(store.IsRemote)orders=query.ToList();if(!store.IsRemote||!remoteLoad.Appending)rows.Clear();var ids=rows.Select(x=>x.Id).ToHashSet();foreach(var x in orders)if(ids.Add(x.Id))rows.Add(new ScheduledOrderRow(x,data));
        ApplySort();
        Grid.ItemsSource=rows; if(initialSelection is Guid selected){var row=rows.FirstOrDefault(x=>x.Id==selected);if(row!=null){Grid.SelectedItem=row;Grid.ScrollIntoView(row);}initialSelection=null;}else if(rows.Count>0&&(!store.IsRemote||!remoteLoad.Appending))Grid.ScrollIntoView(rows[0]);
        FilterBox.IsEnabled=!showCompleted&&!showUnpaid&&!showUnprinted&&!showReady;
        ReadyButton.Background=showReady?ThemeManager.Get("AccentSoft"):System.Windows.Media.Brushes.Transparent;ActiveButton.Background=showCompleted||showUnpaid||showUnprinted||showReady||FilterBox.SelectedItem?.ToString()=="Arquivados"?System.Windows.Media.Brushes.Transparent:GraficaCaixa.Wpf.ThemeManager.Get("AccentSoft");
        CompletedButton.Background=showCompleted?GraficaCaixa.Wpf.ThemeManager.Get("AccentSoft"):System.Windows.Media.Brushes.Transparent;
        ActiveButton.Content=$"Em andamento ({all.Count(x=>!x.IsUncollected&&ReceivableViews.OrderBucket(data,x)=="active")})";ReadyButton.Content=$"Aguardando retirada ({all.Count(x=>!x.IsUncollected&&ReceivableViews.OrderBucket(data,x)=="ready")})";UnpaidButton.Content=$"Falta pagamento ({all.Count(x=>!x.IsUncollected&&ReceivableViews.OrderBucket(data,x)=="unpaid")})";UnpaidButton.Background=showUnpaid?ThemeManager.Get("AccentSoft"):System.Windows.Media.Brushes.Transparent;CompletedButton.Content=$"Concluídos ({all.Count(x=>!x.IsUncollected&&ReceivableViews.OrderBucket(data,x)=="completed")})";UnprintedButton.Content=$"Sem ficha ({all.Count(x=>!x.IsUncollected&&ReceivableViews.OrderBucket(data,x) is "active" or "ready"&&x.PdfGeneratedAt==null)})";UnprintedButton.Background=showUnprinted?ThemeManager.Get("AccentSoft"):System.Windows.Media.Brushes.Transparent;var archivedOn=!showCompleted&&!showUnpaid&&!showUnprinted&&!showReady&&FilterBox.SelectedItem?.ToString()=="Arquivados";ArchivedButton.Content=$"Arquivados ({all.Count(x=>x.IsArchived)})";ArchivedButton.Background=archivedOn?ThemeManager.Get("AccentSoft"):System.Windows.Media.Brushes.Transparent;ArchivedButton.SetResourceReference(ForegroundProperty,archivedOn?"AccentInk":"Ink"); if(store.IsRemote&&remoteLoad.Result is {} page){ActiveButton.Content=$"Em andamento ({page.Counts.GetValueOrDefault("active")})";UnpaidButton.Content=$"Falta pagamento ({page.Counts.GetValueOrDefault("unpaid")})";ReadyButton.Content=$"Aguardando retirada ({page.Counts.GetValueOrDefault("ready")})";ActiveKpi.Value=page.Counts.GetValueOrDefault("active").ToString();UrgentKpi.Value=page.Counts.GetValueOrDefault("urgent").ToString();UrgentKpi.Tone=page.Counts.GetValueOrDefault("urgent")>0?"danger":"ok";ProductionKpi.Value=page.Counts.GetValueOrDefault("production").ToString();PendingKpi.Value=page.Counts.GetValueOrDefault("pending").ToString();CompletedButton.Content=$"Concluídos ({page.Counts.GetValueOrDefault("completed")})";UnprintedButton.Content=$"Sem ficha ({page.Counts.GetValueOrDefault("unprinted")})";ArchivedButton.Content=$"Arquivados ({page.Counts.GetValueOrDefault("archived")})";}
        var priority=GraficaCaixa.BusinessRules.Current.PriorityDays;UrgentKpi.Hint=$"vence em até {priority} {(priority==1?"dia útil":"dias úteis")} ou atrasado";
        ActiveKpi.IsLoading=UrgentKpi.IsLoading=ProductionKpi.IsLoading=PendingKpi.IsLoading=false;foreach(var (button,on) in new[]{(ReadyButton,showReady),(ActiveButton,!showCompleted&&!showUnpaid&&!showUnprinted&&!showReady&&FilterBox.SelectedItem?.ToString()!="Arquivados"),(UnpaidButton,showUnpaid),(CompletedButton,showCompleted),(UnprintedButton,showUnprinted)})button.SetResourceReference(ForegroundProperty,on?"AccentInk":"Ink");EmptyView.Visibility = orders.Count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; UpdateActionButtons();
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível atualizar pedidos",ex.Message);}finally{loading=false;}
    }
    void ApplySort()
    {
        var view=System.Windows.Data.CollectionViewSource.GetDefaultView(rows);
        view.SortDescriptions.Clear();
        if(!store.IsRemote&&sortColumn!=null)view.SortDescriptions.Add(new SortDescription(sortColumn,sortDirection));
    }
    void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        var column=e.Column.SortMemberPath;
        if(string.IsNullOrWhiteSpace(column))return;
        e.Handled=true;
        var next=sortColumn==column&&sortDirection==ListSortDirection.Ascending?ListSortDirection.Descending:ListSortDirection.Ascending;
        foreach(var item in Grid.Columns)item.SortDirection=null;
        sortColumn=column;sortDirection=next;e.Column.SortDirection=next;
        if(store.IsRemote){remoteLoad.Reset();RefreshData(true);}else ApplySort();
    }
    IEnumerable<ScheduledOrderRow> SelectedRows => Grid.SelectedItems.OfType<ScheduledOrderRow>();
    Guid? SelectedId => (Grid.SelectedItem as ScheduledOrderRow)?.Id;
    GraficaCaixa.ScheduledOrder? SelectedOrder() => SelectedId is Guid id ? (store.IsRemote?loadedData:store.Load())?.ScheduledOrders.FirstOrDefault(x => x.Id == id) : null;
    // "Mais": ações menos usadas num menu, para a barra caber em uma linha. Os botões continuam existindo (ocultos) e o menu só os aciona.
    void More_Click(object s,System.Windows.RoutedEventArgs e)
    {
        var menu=new System.Windows.Controls.ContextMenu{PlacementTarget=MoreButton,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
        foreach(System.Windows.Controls.Button b in MoreActions.Children)
        {
            var item=new System.Windows.Controls.MenuItem{Header=b.Content as string??b.ToolTip?.ToString(),IsEnabled=b.IsEnabled,ToolTip=b.ToolTip};
            item.Click+=(_,_)=>b.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));menu.Items.Add(item);
        }
        menu.IsOpen=true;
    }
    void Add_Click(object s, System.Windows.RoutedEventArgs e) { var dialog = new ScheduledOrderDialog(store) { Owner = this }; if (dialog.ShowDialog() == true) Save(dialog.Result!,dialog.CorrectionSaleId,dialog.CorrectedAmount,dialog.CorrectionReason); }
    // Quadro: dentro da janela principal troca a vista na mesma área (e grava a escolha); fora dela, abre como antes.
    void Board_Click(object s, System.Windows.RoutedEventArgs e) { if (Owner is MainWindow main) { ViewToggle.Store("pedidos", "Cards"); main.ShowOrdersBoard(); } else { new OrderBoardWindow(store) { Owner = this }.ShowDialog(); OrdersView.Mode = "Lista"; } }
    async void OrderPdf_Click(object s, System.Windows.RoutedEventArgs e)
    {
        var selectedRows = SelectedRows.ToList(); if (selectedRows.Count == 0) { AlertWindow.Info(this, "Selecione os pedidos", "Escolha um ou dois pedidos para montar a folha A4."); return; }
        if (selectedRows.Count > 2) { AlertWindow.Info(this, "Limite de dois pedidos", "Cada folha A4 comporta duas fichas A5. Retire alguns pedidos da seleção e tente novamente."); return; }
        string? savedPath = null;
        try
        {
            var orders=selectedRows.Select(x=>loadedData!.ScheduledOrders.Single(o=>o.Id==x.Id)).ToList();
            savedPath=await OrderSheetPdf.GenerateAsync(store,orders,lifetime.Token);RefreshData();PdfDocuments.Open(this,savedPath);
        }
        catch(Exception ex){AlertWindow.Info(this,savedPath==null?"Não foi possível gerar":"PDF salvo; indicador pendente",savedPath==null?ex.Message:$"O PDF foi salvo em:\n{savedPath}\n\nNão foi possível confirmar o indicador no pedido.\n{ex.Message}");}
    }
    async void PickupReminders_Click(object s,System.Windows.RoutedEventArgs e)=>await WhatsAppActions.PickupQueueAsync(this,store);
    async void Repeat_Click(object s, System.Windows.RoutedEventArgs e) { var selected = SelectedOrder(); if (selected == null) { AlertWindow.Info(this, "Selecione um pedido", "Escolha o pedido que deseja repetir."); return; } try { var dialog = await OrderRepeatUi.OpenAsync(this, store, selected.Id); if (dialog != null) Save(dialog.Result!); } catch (Exception ex) { AlertWindow.Info(this, "Não foi possível repetir o pedido", ex.Message); } }
    void Edit_Click(object s, System.Windows.RoutedEventArgs e) { var original = SelectedOrder(); if (original == null) { AlertWindow.Info(this, "Selecione um pedido", "Escolha a linha que deseja editar."); return; } var dialog = new ScheduledOrderDialog(original, store) { Owner = this }; if (dialog.ShowDialog() == true) Save(dialog.Result!,dialog.CorrectionSaleId,dialog.CorrectedAmount,dialog.CorrectionReason,original.Stage); }
    async void Save(GraficaCaixa.ScheduledOrder result, Guid? correctedSale=null, decimal? amount=null, string correctionReason="", string? previousStage=null)
    {
        try { var changed=await BusinessUi.SaveOrderAsync(store,result,correctedSale,amount,correctionReason); if(changed.Count>0) { foreach(var id in changed){var data=await store.BusinessContextAsync("closed",id);GraficaCaixa.ReportWriter.Write(data.Sessions.First(x=>x.Id==id),data.CashClosureVersions.Where(x=>x.SessionId==id).OrderBy(x=>x.Version).LastOrDefault());} } RefreshData(); await WhatsAppActions.OfferReadyNoticeAsync(this,store,previousStage,result); }
        catch(Exception ex) { AlertWindow.Info(this,"Não foi possível salvar",ex.Message); }
    }
    async void MarkPaid_Click(object s, System.Windows.RoutedEventArgs e)
    {
        var selected = SelectedOrder(); if (selected == null) { AlertWindow.Info(this, "Selecione um pedido", "Escolha o pedido que recebeu o pagamento."); return; }
        if(loadedData!=null&&ReceivableViews.ActiveLoss(loadedData,selected.Id)!=null){AlertWindow.Info(this,"Pedido em perda","Reabra a cobrança em Devedores → Perdas → Recuperar perda antes de receber.");return;}
        if (!selected.PricePending && selected.RemainingAmount <= 0) { AlertWindow.Info(this, "Pagamento já registrado", "Este pedido já está quitado."); return; }
        if(selected.PricePending) { AlertWindow.Info(this,"Valor a definir","Defina o total do pedido antes de receber o saldo. Sinais podem ser registrados ao editar o pedido.");return; }
        var requestId=Guid.NewGuid();
        // Pagamento já lançado no caixa (venda feita antes): liga ao pedido em vez de abrir o recebimento.
        var link=await CashSaleLink.AskAsync(this,store,selected.CustomerName,selected.Contact,selected.RemainingAmount);List<PaymentPart> payments=[];var paidAt=DateTime.Now;
        if(link.Count==0){var dialog = new PaymentReceivedWindow(selected) { Owner = this }; if (dialog.ShowDialog() != true) return;payments=dialog.Payments;paidAt=dialog.PaidAt;}
        try { if(store.IsRemote)await store.RemoteDesktopWriteAsync($"desktop/orders/{selected.Id}/payments",new DesktopPaymentCommand(requestId,selected.Revision,payments,paidAt,link));else store.Update(d => { var order = d.ScheduledOrders.FirstOrDefault(x => x.Id == selected.Id) ?? throw new InvalidOperationException("O pedido não existe mais."); if(order.Revision!=selected.Revision)throw new InvalidOperationException("Pedido alterado. Atualize a lista.");if(link.Count>0)OrderOperations.LinkSales(d,order,link,App.CurrentUser);if(payments.Count>0)OrderOperations.AddPayments(d,order,payments,paidAt,App.CurrentUser); return 0; }); RefreshData(); } catch (Exception ex) { AlertWindow.Info(this, "Não foi possível receber", ex.Message); }
    }
    async void Archive_Click(object s, System.Windows.RoutedEventArgs e)
    {
        var selected = SelectedOrder(); if (selected == null) { AlertWindow.Info(this, "Selecione um pedido", "Escolha o pedido que deseja ocultar ou restaurar."); return; }
        var restore = selected.IsUncollected; if (!restore && !AlertWindow.Confirm(this, "Arquivar pedido", "Este pedido sai da lista principal e fica no botão Arquivados. Quando o cliente voltar, selecione-o lá e clique em Desarquivar pedido.")) return;
        await BusinessUi.ExecuteAsync(store,"order.archive",selected.Id,null,new OrderArchiveArgs(selected.Revision,restore),()=>store.Update(d => { var order = d.ScheduledOrders.First(x => x.Id == selected.Id); if(order.Revision != selected.Revision) throw new InvalidOperationException("O pedido mudou. Atualize a lista antes de continuar."); order.Revision++; order.IsUncollected = !restore; order.ArchivedAsUncollectedAt = restore ? null : DateTime.Now; Audit(d, restore ? "Pedido restaurado" : "Pedido arquivado", $"{order.CustomerName} — {order.Description}"); return 0; })); RefreshData();
    }
    // Link "acompanhe seu pedido": o botão só aparece com o acompanhamento ligado no servidor e um pedido não entregue selecionado.
    bool trackingOn;
    async void LoadTrackingStatus(){if(!store.IsRemote)return;try{trackingOn=(await store.QueryRemoteAsync<TrackingStatus>("desktop/acompanhamento")).Ativo;}catch{trackingOn=false;}UpdateActionButtons();}
    sealed record TrackingStatus(bool Ativo);sealed record TrackingLink(string Url);
    async void Tracking_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(SelectedOrder() is not { } order)return;
        try{var link=await store.QueryRemoteAsync<TrackingLink>($"desktop/orders/{order.Id}/acompanhamento");await WhatsAppActions.SendTrackingAsync(this,store,order.Id,link.Url);}
        catch(Exception ex){AlertWindow.Info(this,"Link de acompanhamento",ex.Message);}
    }
    void UpdateActionButtons(){var selected=SelectedOrder();var available=selected!=null;TrackingButton.Visibility=trackingOn&&available&&selected!.Stage!="Entregue"?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;ReceiptButton.IsEnabled=available&&!receiptBusy;CostButton.IsEnabled=RepeatButton.IsEnabled=EditButton.IsEnabled=ArchiveButton.IsEnabled=RemoveButton.IsEnabled=available;PaymentButton.IsEnabled=available&&selected!.RemainingAmount>0&&!selected.PricePending&&(loadedData==null||ReceivableViews.ActiveLoss(loadedData,selected.Id)==null);ArchiveButton.Content=selected?.IsUncollected==true?"Desarquivar pedido":"Arquivar pedido";}
    async void Cost_Click(object s,System.Windows.RoutedEventArgs e)
    {
        var order=SelectedOrder();if(order==null)return;
        var dialog=new ScheduledOrderDialog(order,store){Owner=this};dialog.OpenCostTab();
        if(dialog.ShowDialog()!=true)return;
        try {await BusinessUi.ExecuteAsync(store,"order.cost",dialog.Result!.Id,null,OrderCostArgs.FromOrder(dialog.Result!),()=>store.Update(d=>{GraficaCaixa.OrderOperations.SaveCosts(d,dialog.Result!,App.CurrentUser);return 0;}));RefreshData();}
        catch(Exception ex){AlertWindow.Info(this,"Não foi possível salvar",ex.Message);}
    }
    async void Receipt_Click(object s,System.Windows.RoutedEventArgs e)
    {
        var id=SelectedId;if(id==null||receiptBusy)return;
        receiptBusy=true;UpdateActionButtons();
        try
        {
            // The list may be stale. Emit from a fresh persisted snapshot, with all items.
            var data=store.IsRemote?await store.QueryRemoteAsync<AppData>($"desktop/context/order/{id.Value}",lifetime.Token):await store.LoadAsync(lifetime.Token);lifetime.Token.ThrowIfCancellationRequested();
            var order=data.ScheduledOrders.FirstOrDefault(x=>x.Id==id.Value)
                ??throw new InvalidOperationException("O pedido não existe mais. Atualize a lista.");
            var path=Path.Combine(GraficaCaixa.ReportWriter.OutputFolder,$"Recibo_Pedido_{order.Number}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.pdf");
            GraficaCaixa.ReportWriter.WriteReceipt(order,path);
            OrderSheetPdf.ShowActions(this,ReceiptButton,path,order.Contact??"",()=>WhatsAppActions.Message(data,CommunicationOperations.ReceiptTemplateName,WhatsAppActions.OrderValues(data,order)));
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception ex){AlertWindow.Info(this,"Não foi possível gerar o recibo",$"Confira o acesso à pasta de relatórios configurada.\n{ex.Message}");}
        finally{receiptBusy=false;if(!lifetime.IsCancellationRequested)UpdateActionButtons();}
    }

    async void Remove_Click(object s,System.Windows.RoutedEventArgs e)
    {
        var order=SelectedOrder();if(order==null)return;
        if(order.AppliedAmount>0){AlertWindow.Info(this,"Devolva o valor antes de cancelar",$"O pedido #{order.Number} tem {order.AppliedAmount:C2} recebidos. Faça a devolução ao cliente (Dinheiro ou Pix) e depois cancele.");return;}
        var reason=new ReasonWindow("Cancelar pedido",$"O pedido #{order.Number} será cancelado: sai de todas as listas (inclusive Arquivados) e não conta no faturamento nem em a receber. Fica só em Relatórios › Cancelados. Para guardar e talvez retomar depois, use Arquivar. Informe o motivo."){Owner=this};if(reason.ShowDialog()!=true)return;
        try{await BusinessUi.ExecuteAsync(store,"order.cancel",order.Id,null,new OrderArchiveArgs(order.Revision,false,reason.Reason),()=>store.Update(d=>{var target=d.ScheduledOrders.Single(x=>x.Id==order.Id);if(target.Revision!=order.Revision)throw new InvalidOperationException("Pedido alterado. Atualize a lista.");OrderOperations.Cancel(target,reason.Reason,App.CurrentUser.UserName);AuditService.Record(d,App.CurrentUser,"Pedido cancelado","Pedido",target.Id.ToString(),"Ativo","Cancelado",reason.Reason,$"Pedido #{target.Number}");return 0;}));Toast.Show(this,$"Pedido #{order.Number} cancelado.",tone:"ok");RefreshData();}catch(Exception ex){AlertWindow.Info(this,"Não foi possível cancelar",ex.Message);}
    }
    static void Audit(GraficaCaixa.AppData d, string action, string description, Guid? saleId = null) => d.AuditTrail.Add(new GraficaCaixa.AuditEntry { Action = action, Description = description, SaleId = saleId });
    void SearchBox_TextChanged(object s, System.Windows.Controls.TextChangedEventArgs e) { if (IsLoaded){remoteLoad.Reset();RefreshData(true);} }
    void FilterBox_SelectionChanged(object s, System.Windows.Controls.SelectionChangedEventArgs e) { if (IsLoaded){remoteLoad.Reset();RefreshData(true);} }
    void Grid_SelectionChanged(object s, System.Windows.Controls.SelectionChangedEventArgs e) { if(IsLoaded) UpdateActionButtons(); }
    void ClearFilters_Click(object s,System.Windows.RoutedEventArgs e){showReady=false;showCompleted=false;showUnpaid=false;showUnprinted=false;SearchBox.Text="";FilterBox.SelectedIndex=0;remoteLoad.Reset();RefreshData();}
    void Active_Click(object s,System.Windows.RoutedEventArgs e) { showReady=false;showCompleted=false;showUnpaid=false;showUnprinted=false;remoteLoad.Reset();FilterBox.SelectedIndex=0;RefreshData(); }
    void Ready_Click(object s,System.Windows.RoutedEventArgs e){showCompleted=false;showUnpaid=false;showUnprinted=false;showReady=true;remoteLoad.Reset();FilterBox.SelectedIndex=0;RefreshData();}
    void Unpaid_Click(object s,System.Windows.RoutedEventArgs e){showReady=false;showCompleted=false;showUnpaid=true;showUnprinted=false;remoteLoad.Reset();FilterBox.SelectedIndex=0;RefreshData();}
    void Unprinted_Click(object s,System.Windows.RoutedEventArgs e){showReady=false;showCompleted=false;showUnpaid=false;showUnprinted=true;remoteLoad.Reset();FilterBox.SelectedIndex=0;RefreshData();}
    void Archived_Click(object s,System.Windows.RoutedEventArgs e){showReady=false;showCompleted=false;showUnpaid=false;showUnprinted=false;remoteLoad.Reset();FilterBox.SelectedItem="Arquivados";RefreshData();}
    void Completed_Click(object s,System.Windows.RoutedEventArgs e) { showReady=false;showCompleted=true;showUnpaid=false;showUnprinted=false;remoteLoad.Reset();FilterBox.SelectedIndex=0;RefreshData(); } void Grid_DoubleClick(object s, System.Windows.Input.MouseButtonEventArgs e) => Edit_Click(s, new System.Windows.RoutedEventArgs());
}
public sealed class ScheduledOrderRow
{
    public bool IsLoss {get;}
    public string Profit {get;}
    public decimal ProfitValue {get;}
    // Cor do lucro pela margem (faixas em Configurações › Regras); sem custo conferido, cor normal.
    public System.Windows.Media.Brush ProfitBrush {get;}
    public long NumberValue {get;}
    public DateTime CreatedAt {get;}
    public string Created => CreatedAt==default?"—":CreatedAt.ToString("dd/MM/yyyy HH:mm");
    public DateTime DueDate {get;}
    public decimal RemainingAmount {get;}

    public Guid Id { get; }
    public string Number { get; }
    public string Due { get; }
    public string Customer { get; }
    public string Description { get; }
    public string Stage { get; }
    public string Production { get; }
    public int BoardGroup {get;}
    public string PrintStatus {get;}
    public string PrintHint {get;}
    public string Payment { get; }
    public string PaymentSummary { get; }
    // Saldo como no protótipo: falta pagar em destaque e "de R$ total" embaixo, ou etiqueta Quitado. Barra da etapa: Arte 20% … Entregue 100%.
    public string BalanceMain { get; }
    public string BalanceSub { get; }
    public bool Settled { get; }
    public double StageBarWidth { get; }
    public string Total { get; }
    public string Received { get; }
    public string Remaining { get; }
    public string Urgency { get; }
    public System.Windows.Media.Brush UrgencyBackground { get; }
    public System.Windows.Media.Brush UrgencyForeground { get; }
    public System.Windows.Media.Brush RowBackground { get; }
    // Faixa colorida à esquerda indica a etapa; a linha fica com o fundo normal.
    public System.Windows.Media.Brush StripeBrush { get; }
    public ScheduledOrderRow(GraficaCaixa.ScheduledOrder x,GraficaCaixa.AppData? data=null)
    {
        IsLoss=data!=null&&ReceivableViews.ActiveLoss(data,x.Id)!=null;Profit=x.ProfitText;ProfitValue=x.Profit??decimal.MinValue;ProfitBrush=ThemeManager.Get(x.ProfitMargin is decimal margin?GraficaCaixa.BusinessRules.Current.MarginTone(margin):"Ink");NumberValue=x.Number;CreatedAt=x.CreatedAt;DueDate=x.DueDate;RemainingAmount=x.RemainingAmount;var pickup=x.Stage=="Pronto para retirada"; BoardGroup=x.Stage switch { "Agendado" or "Arte / criação"=>0,"Aguardando aprovação" or "Aprovado"=>1,_=>2 }; PrintStatus=x.PdfGeneratedAt!=null?"PDF gerado":"Pendente"; PrintHint=x.PdfGeneratedAt!=null?$"PDF gerado em {x.PdfGeneratedAt:dd/MM/yyyy HH:mm}. A impressão física não é monitorada.":"Nenhum PDF gerado por esta versão."; Id = x.Id; Number = $"#{x.Number}"; var days = x.DeadlineStarted ? GraficaCaixa.BusinessCalendar.RemainingBusinessDays(DateTime.Today, x.DueDate) : int.MaxValue; Due = pickup ? (x.ReadyAt is DateTime readyAt ? ((DateTime.Today-readyAt.Date).Days is var age && age<=0 ? "Pronto hoje" : age==1 ? "Pronto ontem" : $"Pronto há {age} dias") : "Aguardando retirada") : OrderOperations.IsImmediate(x)?"Imediato": x.Stage=="Entregue" ? (x.DeadlineStarted ? x.DueDate.ToString("dd/MM/yyyy") : "—") : x.DeadlineStarted ? $"{x.DueDate:dd/MM/yyyy} ({(days < 0 ? $"{Math.Abs(days)}d atrasado" : days == 0 ? "hoje" : days == 1 ? "falta 1 dia" : $"faltam {days} dias")})" : $"Após aprovação ({x.ProductionBusinessDays}d)"; Customer = GraficaCaixa.CustomerDirectory.Label(data,x.CustomerName,x.Contact); Description = x.ItemsDescription; Stage = x.Stage; Production = x.SentToProductionAt is null ? "Não enviado" : $"Enviado {x.SentToProductionAt:dd/MM}"; Payment = x.PricePending ? "Valor a definir" : x.RemainingAmount <= 0 ? "Quitado" : x.PaidAmount > 0 ? "Parcial" : "Pendente"; Total = x.PricePending?"A definir":x.OrderTotal.ToString("C2"); Received = x.PaidAmount.ToString("C2"); Remaining = x.PricePending?"A definir":x.RemainingAmount.ToString("C2"); if(IsLoss)Payment="Perda";PaymentSummary=IsLoss?$"Perda • saldo histórico {x.RemainingAmount:C2}":x.PricePending?"Valor a definir":x.RemainingAmount<=0?$"Quitado • {x.OrderTotal:C2}":$"Saldo {x.RemainingAmount:C2}\nRecebido {x.PaidAmount:C2}";var stageIndex=Array.IndexOf(OrderStages.All,OrderStages.Normalize(x.Stage));StageBarWidth=Math.Max(0,stageIndex+1)*100.0/OrderStages.All.Length;Settled=!IsLoss&&!x.PricePending&&x.RemainingAmount<=0;BalanceMain=IsLoss?"Perda":x.PricePending?"A definir":Settled?"Quitado":x.RemainingAmount.ToString("C2");BalanceSub=IsLoss?$"saldo {x.RemainingAmount:C2}":x.PricePending?"valor do pedido":Settled?$"{x.OrderTotal:C2}":$"de {x.OrderTotal:C2}"; var urgency = x.IsUncollected ? ("Arquivado", "#ECEAE6", "#6B655D") : x.Stage == "Entregue" ? ("Entregue", "#DDF4E5", "#217A46") : !x.DeadlineStarted ? ("Aguarda arte", "#F2EFE9", "#6B655D") : days switch { < 0 => ("Atrasado", "#FDE8E7", "#A52A26"), 0 => ("Para hoje", "#FDE8E7", "#A52A26"), 1 => ("Falta 1 dia", "#FDE8E7", "#A52A26"), var pr when pr <= GraficaCaixa.BusinessRules.Current.PriorityDays => ("Prioridade", "#FFF0D6", "#9A5700"), <= 7 => ("Atenção", "#FFF8DB", "#756000"), _ => ("No prazo", "#E4F1ED", "#2C6E63") }; if(pickup&&!x.IsUncollected)urgency=("Retirada","#DDF5F8","#147A8A");if(OrderOperations.IsImmediate(x))urgency=("IMEDIATO","#FFF4E1","#98661D"); Urgency = UrgencyColors.Label(urgency.Item1); var hot = UrgencyColors.IsUrgent(urgency.Item1); UrgencyBackground = hot ? UrgencyColors.Stripe(urgency.Item1) : Brush(urgency.Item2); UrgencyForeground = hot ? System.Windows.Media.Brushes.White : Brush(urgency.Item3); RowBackground = UrgencyColors.Row(urgency.Item1); StripeBrush = UrgencyColors.Stripe(urgency.Item1);
    }
    static System.Windows.Media.Brush Brush(string color) => new System.Windows.Media.BrushConverter().ConvertFromString(color) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Transparent;
}


