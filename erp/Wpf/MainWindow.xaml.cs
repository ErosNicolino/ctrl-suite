using System.Collections.ObjectModel;
using System.Windows.Threading;
namespace GraficaCaixa.Wpf;

public partial class MainWindow : System.Windows.Window
{
    bool writeBusy;
    readonly GraficaCaixa.DataStore store;IReadOnlyList<CustomerProfile> dashboardCustomers=[];
    readonly ObservableCollection<SaleRow> rows=[];
    readonly ObservableCollection<DashboardOrderRow> orderRows=[];
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(10)};
    // Evita recarregar o banco (que está na rede) a cada tecla digitada na pesquisa.
    readonly DispatcherTimer searchTimer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    DateTime lastChange,lastDashboardDate; bool displayingOpenCash;
    readonly PageNavigator pages;
    bool refreshBusy;
    DateTime lastRemoteCheck;
    DateTime snapshotLoadedAt;
    AppData? dashboardSnapshot;readonly ModuleLoader remoteLoad;bool refreshAgain;string? dashboardQuery;string? dashboardPermissions;
    CancellationTokenSource dashboardCancellation=new();
    IReadOnlyDictionary<string,bool> permissions=new Dictionary<string,bool>();
    bool Can(string key)=>permissions.GetValueOrDefault(key);
    void ApplyPermissions(IReadOnlyDictionary<string,bool>? effective=null)
    {
        if(!store.IsRemote)return;permissions=effective??store.EffectivePermissions();App.CurrentUser=App.CurrentUser with {Permissions=permissions};
        foreach(var (button,key) in new[]{(NavHome,"view.home"),(NavOrders,"view.orders"),(NavBudget,"view.budget"),(NavPurchases,"view.purchases"),(NavSuppliers,"view.purchases"),(NavProducts,"view.products"),(NavCustomers,"view.customers"),(NavDebtors,"view.debtors"),(NavClosed,"view.closed"),(NavReports,"view.reports"),(NavReminders,"view.reminders"),(NavSearch,"view.cash")})button.Visibility=Can(key)?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
        if(!Can("view.home"))DashboardView.Visibility=LastUpdateBar.Visibility=System.Windows.Visibility.Collapsed;
    }
    public MainWindow(GraficaCaixa.DataStore store){InitializeComponent();SizeChanged+=(_,e)=>ApplyNavDensity(e.NewSize.Height);this.store=store;remoteLoad=new(this,store,showStatus:false);DashboardView.Children.Remove(LastUpdateBar);remoteLoad.AddToFooter(LastUpdateBar);ApplyPermissions(App.CurrentUser.Permissions);if(AppProfile.IsTest)Title="[TESTE] "+Title;SalesGrid.ItemsSource=rows;OrdersList.ItemsSource=orderRows;
        // Pesquisas da tela inicial filtram a lista direto, sem sugestões de clientes (pedido do usuário).
        timer.Tick+=(_,_)=>{ClockText.Text=DateTime.Now.ToString("dddd, dd 'de' MMMM 'de' yyyy  •  HH:mm");if(pages!.Current!=null||!IsVisible||WindowState==System.Windows.WindowState.Minimized)return;if(store.IsRemote){if(DateTime.UtcNow-lastRemoteCheck>TimeSpan.FromSeconds(30)){lastRemoteCheck=DateTime.UtcNow;ProbeRemote();}}else if(store.LastChangeUtc>lastChange||DateTime.Today!=lastDashboardDate)RefreshData(false);};timer.Start();searchTimer.Tick+=(_,_)=>{searchTimer.Stop();RefreshData(false,true);};PreviewKeyDown+=Shortcuts;KeyDown+=NavigateHomeOnEscape;pages=new PageNavigator(this,PageArea,PageHost);RowActions.Attach(SalesGrid,"",ReceiptSaleButton,OrderSaleButton,RefundSaleButton,EditSaleButton,RemoveSaleButton);pages.Closed+=ShowHome;UpdateUserChip();SetupDashboard();Closed+=(_,_)=>{timer.Stop();searchTimer.Stop();dashboardCancellation.Cancel();};Loaded+=(_,_)=>RefreshData();}
    async void ProbeRemote()
    {
        if(refreshBusy)return;refreshBusy=true;bool changed=false;
        try{var revision=await store.QueryRemoteAsync<RemoteRevision>("api/v1/revision",dashboardCancellation.Token);changed=dashboardSnapshot==null||revision.Revision!=dashboardSnapshot.DataVersion.ToString()||ServerAuthorization.Json(permissions)!=ServerAuthorization.Json(revision.Permissions)||DateTime.Today!=lastDashboardDate;}
        catch(OperationCanceledException){}
        catch(Exception){StatusText.Text="Não foi possível verificar a atualização";LastUpdateText.Text="Falha ao verificar atualização";await CheckConnectionAsync();}
        finally{refreshBusy=false;}
        if(changed&&pages.Current==null)RefreshData(false);
    }
    GraficaCaixa.CashSession? Current(GraficaCaixa.AppData d)=>d.Sessions.LastOrDefault(x=>x.IsOpen);
    // Lançamento novo com o caixa aberto em outro dia (não foi fechado): entra na data da abertura, com o horário de agora
    // (nunca antes da abertura). Caixa aberto hoje: data e hora atuais, como antes.
    static DateTime? CashDate(GraficaCaixa.CashSession cash){if(cash.OpenedAt.Date>=DateTime.Today)return null;var at=cash.OpenedAt.Date.Add(DateTime.Now.TimeOfDay);return at<cash.OpenedAt?cash.OpenedAt:at;}
    static void Audit(GraficaCaixa.AppData d,string action,string description,Guid? saleId=null){d.AuditTrail??=[];d.AuditTrail.Add(new GraficaCaixa.AuditEntry{Action=action,Description=description,SaleId=saleId});}
    async void RefreshData(bool alert=true,bool useSnapshot=false)
    {
        if(refreshBusy){refreshAgain=true;return;}refreshBusy=true;
        try
        {
            AppData data;
            if(store.IsRemote){LastUpdateText.Text=dashboardSnapshot==null?"Carregando…":"Atualizando…";var token=dashboardCancellation.Token;
                var revision=await store.QueryRemoteAsync<RemoteRevision>("api/v1/revision",token);var permissionKey=ServerAuthorization.Json(revision.Permissions);ApplyPermissions(revision.Permissions);
                var query=OrdersSearchBox.Text;var changed=dashboardSnapshot==null||revision.Revision!=dashboardSnapshot.DataVersion.ToString()||dashboardPermissions!=permissionKey||DateTime.Today!=lastDashboardDate;
                if(changed||dashboardQuery!=query){var scoped=await remoteLoad.LoadAsync("dashboard","&ordersSearch="+ModuleLoader.Encode(OrdersSearchBox.Text));if(scoped==null){if(remoteLoad.LastError is {} error){LastUpdateText.Text=$"Falha ao atualizar às {DateTime.Now:HH:mm}";LastUpdateText.ToolTip=error;}return;}token.ThrowIfCancellationRequested();dashboardSnapshot=scoped;dashboardQuery=query;dashboardPermissions=permissionKey;snapshotLoadedAt=DateTime.Now;}
                data=dashboardSnapshot!;token.ThrowIfCancellationRequested();}
            else {data=store.Load();snapshotLoadedAt=DateTime.Now;}
            // Só para quem cuida do sistema: o servidor manda o número apenas nos últimos 30 dias do certificado, quando a renovação não concluiu.
            if(store.IsRemote&&remoteLoad.Result?.Counts.TryGetValue("certificateDays",out var certificateDays)==true){CertificateTitle.Text=certificateDays<=0?"O acesso pelo celular e o link de acompanhamento venceram.":$"O acesso pelo celular e o link de acompanhamento vencem em {certificateDays} dia(s).";CertificateBox.Visibility=System.Windows.Visibility.Visible;}else CertificateBox.Visibility=System.Windows.Visibility.Collapsed;
            dashboardCustomers=data.Customers;SetTodayCount(store.IsRemote?(int)(remoteLoad.Result?.Counts.GetValueOrDefault("today")??0):GraficaCaixa.TodayQueue.Build(data,_=>true,store.AuthenticatedUser?.UserId??Guid.Empty,DateTime.Now).Count);if(TodayTab.IsChecked==true)LoadToday();var pendingReminders=store.IsRemote?(int)(remoteLoad.Result?.Counts.GetValueOrDefault("reminders")??0):data.Reminders?.Count(x=>x.DoneAt==null&&!x.ReadByUsers.ContainsKey(store.AuthenticatedUser?.UserId??Guid.Empty))??0;var pendingPurchases=store.IsRemote?(int)(remoteLoad.Result?.Counts.GetValueOrDefault("purchaseNeeds")??0):data.Prompt12.PurchaseNeeds.Count(x=>x.Status==PurchaseNeedStatus.ToBuy);PurchasesCount.Text=pendingPurchases.ToString();PurchasesBadge.Visibility=pendingPurchases>0?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;RemindersCount.Text=pendingReminders.ToString();RemindersBadge.Visibility=pendingReminders>0?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;var effectiveLogo=DocumentPaths.ResolveStoredPath(data.DocumentsRoot,data.LogoPath);ReportWriter.LogoPath=effectiveLogo;try{if(string.IsNullOrWhiteSpace(effectiveLogo))BrandLogo.SetResourceReference(System.Windows.Controls.Image.SourceProperty,"Logo");else BrandLogo.Source=await Task.Run(()=>ProductPhotos.Read(effectiveLogo));}catch(Exception){/* Keep the last available logo when a network image is unavailable. */}dashboardCancellation.Token.ThrowIfCancellationRequested();EnvironmentText.Text=(AppProfile.IsTest?"MODO TESTE • ":"")+$"v{typeof(App).Assembly.GetName().Version} • "+(store.IsRemote?"conectado ao principal":App.PrincipalConnection!=null?"computador principal":"banco local");var current=Current(data);var shown=current;var term=SalesSearchBox.Text.Trim();lastChange=store.LastChangeUtc;lastDashboardDate=DateTime.Today;displayingOpenCash=current!=null;rows.Clear();LoadDashboardOrders(data,OrdersSearchBox.Text.Trim());ClockText.Text=DateTime.Now.ToString("dddd, dd 'de' MMMM 'de' yyyy  •  HH:mm");OpenButton.IsEnabled=current==null;CloseButton.IsEnabled=current!=null;OpenButton.Visibility=current==null?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;CloseButton.Visibility=current!=null?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;UpdateCashChip(current);LastUpdateText.Text=$"Atualizado às {snapshotLoadedAt:HH:mm}";LastUpdateText.ToolTip="Clique ou pressione F5 para atualizar agora";
            SalesSkeleton.Visibility=CashSkeleton.Visibility=System.Windows.Visibility.Collapsed;if(shown==null){OpeningText.Text=SalesText.Text=CashText.Text="R$ 0,00";CountText.Text="0";CountHint.Text="caixa fechado";PaymentBreakdownText.Text="";UpdatePaymentMix(null);StatusText.Text="Caixa fechado";DashboardSubtitle.Text="Abra o caixa para começar • Consulte os caixas anteriores em Fechamentos";StatusText.Foreground=FindResource("Muted") as System.Windows.Media.Brush;StatusPill.Background=GraficaCaixa.Wpf.ThemeManager.Get("SurfaceAlt");SalesEmpty.Title="Nenhum lançamento";SalesEmpty.Hint="Abra o caixa para começar a registrar as vendas.";SalesEmpty.Visibility=System.Windows.Visibility.Visible;UpdateSaleActions();return;}
            OpeningLabel.Text=current==null?"Fundo do último caixa":"Fundo inicial";SalesLabel.Text=current==null?"Recebimentos do último caixa":"Recebimentos do caixa";CashLabel.Text=current==null?"Esperado no fechamento":"Dinheiro esperado";OpeningText.Text=shown.OpeningAmount.ToString("C2");Motion.CountTo(SalesText,shown.SalesTotal.ToString("C2"));Motion.CountTo(CashText,shown.ExpectedCash.ToString("C2"));CountText.Text=shown.Sales.Count.ToString();CountHint.Text=current!=null?"no caixa atual":"no último caixa";PaymentBreakdownText.Text=PaymentBreakdown(shown);UpdatePaymentMix(shown);StatusText.Text=current!=null?(shown.OpenedAt.Date==DateTime.Today?$"Aberto desde {shown.OpenedAt:HH:mm}":$"Aberto desde {shown.OpenedAt:dd/MM HH:mm}"):"Último caixa fechado";DashboardSubtitle.Text=current!=null?$"Caixa de {shown.OpenedAt:dd/MM/yyyy} e pedidos que precisam de atenção":"Resumo do último fechamento e pedidos que precisam de atenção";StatusText.Foreground=FindResource(current!=null?"Accent":"Muted") as System.Windows.Media.Brush;StatusPill.Background=GraficaCaixa.Wpf.ThemeManager.Get(current!=null?"AccentSoft":"SurfaceAlt");var hidden=GraficaCaixa.Cancellations.HiddenSaleIds(data);CountText.Text=shown.Sales.Count(x=>!hidden.Contains(x.Id)).ToString();var sales=shown.Sales.Where(x=>!hidden.Contains(x.Id)).Where(x=>Matches($"{GraficaCaixa.DisplayText.Customer(x)} {x.Product} {x.PaymentType} {x.Total}",term)||PhoneText.Matches(x.Contact,term)).OrderByDescending(x=>x.CreatedAt).ToList();foreach(var x in sales)rows.Add(new SaleRow(x));SalesEmpty.Title=string.IsNullOrWhiteSpace(term)?"Nenhum lançamento neste caixa":"Nenhum lançamento encontrado";SalesEmpty.Hint=string.IsNullOrWhiteSpace(term)?"Use Novo lançamento para registrar a primeira venda.":"Tente outro nome, produto ou pagamento.";SalesEmpty.Visibility=sales.Count==0?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;UpdateSaleActions();
        }
        catch(OperationCanceledException){}
        catch(Exception ex){StatusText.Text="Dados não atualizados";StatusText.Foreground=FindResource("Danger") as System.Windows.Media.Brush;LastUpdateText.Text=$"Falha ao atualizar às {DateTime.Now:HH:mm}";if(store.IsRemote)await CheckConnectionAsync();if(alert)AlertWindow.Info(this,"Não foi possível atualizar",ex.Message);}
        finally{refreshBusy=false;if(refreshAgain&&pages.Current==null){refreshAgain=false;RefreshData(false);}}
    }
    async Task CheckConnectionAsync()
    {
        using var probe=CancellationTokenSource.CreateLinkedTokenSource(dashboardCancellation.Token);probe.CancelAfter(TimeSpan.FromSeconds(3));
        var version=$"v{typeof(App).Assembly.GetName().Version} • ";
        try{await store.PingRemoteAsync(probe.Token);EnvironmentText.Text=version+"conectado ao principal";}
        catch(OperationCanceledException){if(!dashboardCancellation.IsCancellationRequested)EnvironmentText.Text=version+"verificação de conexão pendente";}
        catch(IOException){EnvironmentText.Text=version+"conexão indisponível";}
        catch(Exception){EnvironmentText.Text=version+"verificação de conexão pendente";}
    }
    static System.Windows.Media.Brush Brush(string color)=>new System.Windows.Media.BrushConverter().ConvertFromString(color) as System.Windows.Media.Brush??System.Windows.Media.Brushes.Transparent;
    static bool Matches(string value,string term)=>string.IsNullOrWhiteSpace(term)||value.Contains(term,StringComparison.CurrentCultureIgnoreCase);
    static string PaymentBreakdown(GraficaCaixa.CashSession session)=>string.Join("  •  ",session.Sales.GroupBy(x=>x.PaymentType).OrderByDescending(x=>x.Sum(s=>s.Total)).Take(3).Select(g=>$"{g.Key}: {g.Sum(x=>x.Total):C2}"));
    void LoadDashboardOrders(GraficaCaixa.AppData data,string term){var all=data.ScheduledOrders.Where(x=>!x.IsUncollected).ToList();var active=all.Where(x=>x.Stage is not "Entregue" and not "Pronto para retirada").Where(x=>Matches($"{x.Number} {GraficaCaixa.CustomerDirectory.Label(data,x.CustomerName,x.Contact)} {x.ItemsDescription} {x.Stage}",term)).OrderBy(x=>OrderOperations.OperationalPriority(x)).ThenBy(x=>x.ProductionBusinessDays==0?x.CreatedAt:x.DeadlineStarted?x.DueDate.Date:DateTime.MaxValue.Date).ThenBy(x=>x.Number).ToList();orderRows.Clear();foreach(var x in active.Take(8))orderRows.Add(new DashboardOrderRow(x,data,!store.IsRemote||Can("orders.value")));var urgent=active.Count(x=>x.Stage!="Pronto para retirada"&&x.DeadlineStarted&&GraficaCaixa.BusinessCalendar.RemainingBusinessDays(DateTime.Today,x.DueDate)<=GraficaCaixa.BusinessRules.Current.PriorityDays);var overdue=all.Count(x=>x.Stage!="Pronto para retirada"&&x.Stage!="Entregue"&&x.DeadlineStarted&&GraficaCaixa.BusinessCalendar.RemainingBusinessDays(DateTime.Today,x.DueDate)<0);var ready=all.Count(x=>x.Stage=="Pronto para retirada");if(store.IsRemote&&remoteLoad.Result is {} page){urgent=(int)page.Counts.GetValueOrDefault("urgent");overdue=(int)page.Counts.GetValueOrDefault("overdue");ready=(int)page.Counts.GetValueOrDefault("ready");}var alerts=new List<string>();if(overdue>0)alerts.Add($"{overdue} atrasado{(overdue==1?"":"s")}");if(ready>0)alerts.Add($"{ready} pronto{(ready==1?"":"s")}");OrdersSummaryText.Text=active.Count==0?(string.IsNullOrWhiteSpace(term)?"Nenhum em andamento":"Nenhum encontrado"):$"{(store.IsRemote?remoteLoad.Result?.Counts.GetValueOrDefault("active")??active.Count:active.Count)} em andamento"+(urgent>0?$" • {urgent} prioritário{(urgent==1?"":"s")}":"")+(alerts.Count>0?$" • {string.Join(" • ",alerts)}":"");OrdersEmpty.Visibility=active.Count==0?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;OrdersList.Visibility=active.Count==0?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;UpdateAttention(urgent,overdue,ready);}
    async void Adjustment_Click(object s,System.Windows.RoutedEventArgs e){try{var data=store.IsRemote?await remoteLoad.LoadAsync("dashboard"):store.Load();if(data==null)return;var session=Current(data);if(session==null){AlertWindow.Info(this,"Caixa fechado","Abra o caixa para registrar um ajuste.");return;}new CashAdjustmentWindow(store,session){Owner=this}.ShowDialog();RefreshData();}catch(Exception ex){AlertWindow.Info(this,"Não foi possível abrir o ajuste",ex.Message);}}
    async void Withdrawal_Click(object s,System.Windows.RoutedEventArgs e){try{var data=store.IsRemote?await remoteLoad.LoadAsync("dashboard"):store.Load();if(data==null)return;var session=Current(data);if(session==null){AlertWindow.Info(this,"Caixa fechado","Abra o caixa para registrar uma sangria.");return;}var window=new WithdrawalWindow(store,session){Owner=this};window.ShowDialog();if(window.WantsAdjustment)new CashAdjustmentWindow(store,session){Owner=this}.ShowDialog();RefreshData();}catch(Exception ex){AlertWindow.Info(this,"Sangria",ex.Message);}}
    async void Open_Click(object s,System.Windows.RoutedEventArgs e){
        if(writeBusy)return;writeBusy=true;try{var d=new AmountWindow("Abrir caixa","Valor inicial em dinheiro:"){Owner=this};if(d.ShowDialog()!=true)return;try{await BusinessUi.ExecuteAsync(store,"cash.open",Guid.Empty,null,new CashOpenArgs(d.Amount),()=>store.Update(x=>GraficaCaixa.FinancialOperations.OpenCash(x,d.Amount,App.CurrentUser)));RefreshData();}catch(Exception ex){AlertWindow.Info(this,"Não foi possível abrir",ex.Message);}
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
    async void Add_Click(object s,System.Windows.RoutedEventArgs e){
        if(writeBusy)return;writeBusy=true;try{var cash=Current(await store.BusinessContextAsync("cash"));if(cash==null){AlertWindow.Info(this,"Caixa fechado","Abra o caixa antes de lançar uma venda.");return;}bool again;do{var d=new SaleWindow(defaultDate:CashDate(cash),store:store){Owner=this};if(d.ShowDialog()!=true)return;try{await BusinessUi.ExecuteAsync(store,"sale.add",d.Result!.Id,Current(await store.BusinessContextAsync("cash")),new SaleSaveArgs(d.Result!),()=>store.Update(x=>GraficaCaixa.FinancialOperations.AddSale(x,d.Result!,App.CurrentUser)));RefreshData();again=d.SaveAndNew;}catch(Exception ex){AlertWindow.Info(this,"Não foi possível salvar",ex.Message);return;}}while(again);
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
    async void Refund_Click(object s,System.Windows.RoutedEventArgs e){if(writeBusy||!displayingOpenCash||SalesGrid.SelectedItem is not SaleRow row)return;writeBusy=true;try{if(await RefundWindow.RunAsync(this,store,row.Id))RefreshData();}finally{writeBusy=false;}}
    async void Edit_Click(object s,System.Windows.RoutedEventArgs e){
        if(writeBusy)return;writeBusy=true;try{if(!displayingOpenCash||SalesGrid.SelectedItem is not SaleRow row)return;var original=Current(await store.BusinessContextAsync("cash"))?.Sales.FirstOrDefault(x=>x.Id==row.Id);if(original==null){RefreshData();return;}var dialog=new SaleWindow(original,store:store,prepared:await store.BusinessContextAsync("sale",original.Id)){Owner=this};if(dialog.ShowDialog()!=true)return;var after=dialog.Result!;var effective=(await store.BusinessContextAsync("sale",original.Id)).FinancialCorrections.Where(x=>x.OriginalSaleId==original.Id).OrderBy(x=>x.CreatedAt).LastOrDefault()?.CorrectedAmount??original.Total;var reason="";if(after.PaymentType!=original.PaymentType||after.Total!=effective||after.ItemsTotal!=original.ItemsTotal||!after.Items.Select(x=>(x.Quantity,x.UnitPrice)).SequenceEqual(original.Items.Select(x=>(x.Quantity,x.UnitPrice)))){var ask=new ReasonWindow("Justificativa da correção",$"{original.PaymentType} → {after.PaymentType}\nValor atual: {effective:C2}; corrigido: {after.Total:C2}"){Owner=this};if(ask.ShowDialog()!=true)return;reason=ask.Reason;}try{await BusinessUi.ExecuteAsync(store,"sale.edit",original.Id,original,new SaleSaveArgs(after,Reason:reason),()=>store.Update(x=>{FinancialOperations.EditSale(x,original,after,reason,App.CurrentUser);return 0;}));RefreshData();}catch(Exception ex){AlertWindow.Info(this,"Não foi possível corrigir",ex.Message);}
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
    async void Remove_Click(object s,System.Windows.RoutedEventArgs e){
        if(writeBusy)return;writeBusy=true;try{if(!displayingOpenCash||SalesGrid.SelectedItem is not SaleRow row)return;var reason=new ReasonWindow("Cancelar lançamento","O lançamento original será preservado e receberá um ajuste de cancelamento. Informe o motivo."){Owner=this};if(reason.ShowDialog()!=true)return;try{await BusinessUi.ExecuteAsync(store,"sale.cancel",row.Id,(await store.BusinessContextAsync("sale",row.Id)).Sessions.SelectMany(x=>x.Sales).Single(x=>x.Id==row.Id),new ReasonArgs(reason.Reason),()=>store.Update(x=>{GraficaCaixa.FinancialOperations.CancelSale(x,row.Id,reason.Reason,App.CurrentUser);return 0;}));RefreshData();}catch(Exception ex){AlertWindow.Info(this,"Não foi possível cancelar",ex.Message);}
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
    async void Close_Click(object s,System.Windows.RoutedEventArgs e){
        if(writeBusy)return;writeBusy=true;try{var current=Current(await store.BusinessContextAsync("cash"));if(current==null)return;var d=new CashClosingWindow(current){Owner=this};if(d.ShowDialog()!=true)return;try{var closed=await BusinessUi.ExecuteAsync(store,"cash.close",current.Id,current,new CashCloseArgs(d.Amount),()=>store.Update(x=>GraficaCaixa.FinancialOperations.CloseCash(x,current.Id,current,d.Amount,App.CurrentUser)));RefreshData();var data=await store.BusinessContextAsync("closed",closed.Id);dashboardCustomers=data.Customers;var version=data.CashClosureVersions.Where(x=>x.SessionId==closed.Id).OrderBy(x=>x.Version).LastOrDefault();var path=GraficaCaixa.ReportWriter.Write(closed,version);AlertWindow.Info(this,"Fechamento concluído",$"Caixa fechado com sucesso.\n\nPDF salvo em:\n{path}");RefreshData();}catch(Exception ex){AlertWindow.Info(this,"Não foi possível fechar",ex.Message);}
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
    // Módulos abrem como áreas da janela principal (menu lateral), não como janelas separadas.
    void Navigate(System.Windows.Controls.Button nav,Func<System.Windows.Window> create)
    {
        try{remoteLoad.Cancel();dashboardCancellation.Cancel();dashboardCancellation=new();pages.Close();var page=create();DashboardView.Visibility=LastUpdateBar.Visibility=System.Windows.Visibility.Collapsed;pages.Show(page);SetNav(nav);}
        catch(Exception ex){ShowHome();AlertWindow.Info(this,"Não foi possível abrir",ex.Message);}
    }
    void ShowHome(){var back=pages.Current!=null;remoteLoad.Cancel();dashboardCancellation.Cancel();dashboardCancellation=new();pages.Close();DashboardView.Visibility=LastUpdateBar.Visibility=System.Windows.Visibility.Visible;SetNav(NavHome);
        // Voltando de outra tela: os cards entram e os números contam de novo (como nas demais telas), mesmo sem mudança nos valores.
        if(back&&ThemeManager.MotionEnabled){foreach(var t in new[]{SalesText,CashText,AttentionText,ReadyText})t.Text="";Motion.Stagger(KpiRow.Children.OfType<System.Windows.Controls.Grid>());}
        RefreshData(false);LoadSeason();}
    void SetNav(System.Windows.Controls.Button current){foreach(var b in new[]{NavHome,NavOrders,NavBudget,NavPurchases,NavSuppliers,NavProducts,NavCustomers,NavDebtors,NavClosed,NavSearch,NavReports,NavSystem,NavReminders})b.Style=(System.Windows.Style)FindResource(b==current?"NavCurrentButton":"NavButton");}
    // Janela baixa: o menu aperta as linhas e o logotipo para caber sem rolagem; acima disso, fica como sempre.
    void ApplyNavDensity(double height)
    {
        var low=height<700;if(navLow==low)return;navLow=low;BrandLogo.Height=low?34:58;BrandLogo.Margin=new System.Windows.Thickness(0,0,0,low?2:6);OpenButton.Height=CloseButton.Height=low?32:double.NaN;
        foreach(var button in NavList.Children.OfType<System.Windows.Controls.Button>().Where(x=>x.Style==(System.Windows.Style)FindResource("NavButton")||x.Style==(System.Windows.Style)FindResource("NavCurrentButton")))
        {
            if(low){button.MinHeight=26;button.Margin=new System.Windows.Thickness(0);button.Padding=new System.Windows.Thickness(10,2,10,2);}else{button.ClearValue(MinHeightProperty);button.ClearValue(MarginProperty);button.ClearValue(PaddingProperty);}
        }
    }
    bool navLow;
    void Home_Click(object s,System.Windows.RoutedEventArgs e)=>ShowHome();
    void SetUnreadBadge(int count){RemindersCount.Text=count.ToString();RemindersBadge.Visibility=count>0?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;}
    void Reminders_Click(object s,System.Windows.RoutedEventArgs e)=>Navigate(NavReminders,()=>{var page=new RemindersWindow(store);page.UnreadCountChanged+=SetUnreadBadge;return page;});
    void Receipt_Click(object s,System.Windows.RoutedEventArgs e){new ReceiptDialog(store){Owner=this}.ShowDialog();}
    void HistorySale_Click(object s,System.Windows.RoutedEventArgs e){if(SalesGrid.SelectedItem is SaleRow row)new AuditWindow(store,"Lançamento",row.Id.ToString()){Owner=this}.ShowDialog();}
    // Recibo a partir do lançamento selecionado: já vem com cliente, valor, produto, pagamento e data.
    async void SaleReceipt_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(SalesGrid.SelectedItem is not SaleRow row)return;
        var sale=(await store.BusinessContextAsync("sale",row.Id)).Sessions.SelectMany(x=>x.Sales).FirstOrDefault(x=>x.Id==row.Id);if(sale==null){RefreshData();return;}
        new ReceiptDialog(store,sale.CustomerName=="Não informado"?"":sale.CustomerName,sale.Contact,sale.Total,row.Product,sale.PaymentType,sale.CreatedAt){Owner=this}.ShowDialog();
    }
    // Virar pedido: o lançamento já recebido vira o pagamento do pedido novo, sem lançar de novo (pedido para recibo, produção ou entrega).
    async void SaleToOrder_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(SalesGrid.SelectedItem is not SaleRow row)return;
        try{
            var data=await store.BusinessContextAsync("cash");var sale=ReceivableViews.FreeCashSales(data).FirstOrDefault(x=>x.Id==row.Id);
            if(sale==null){AlertWindow.Info(this,"Lançamento já está em pedido","Este lançamento já pertence a um pedido ou a um devedor (ou é um ajuste), por isso não pode virar outro pedido.");return;}
            var parts=ReceivableViews.FreeGroup(data,sale);var total=parts.Sum(x=>ReceivableViews.EffectiveTotal(data,x));
            var items=sale.Items.Count>0?sale.Items.Select(x=>new OrderItem{ProductId=x.ProductId,Product=x.Product,Quantity=x.Quantity,Unit=x.Unit,UnitPrice=x.UnitPrice}).ToList()
                :[new OrderItem{ProductId=sale.ProductId,Product=sale.Product,Quantity=Math.Max(1,sale.Units),Unit=sale.ProductUnit,UnitPrice=decimal.Round(total/Math.Max(1,sale.Units),2)}];
            var byItems=items.All(x=>x.LineTotal!=null)&&items.Sum(x=>x.LineTotal!.Value)==total;
            var draft=new ScheduledOrder{CustomerName=ReceivableViews.CounterNames.Contains(sale.CustomerName.Trim().ToLowerInvariant())?"":sale.CustomerName,Contact=sale.Contact,Items=items,UseItemPrices=byItems,OrderTotal=total,OriginalTotal=total,Notes=sale.Notes};
            var dialog=new ScheduledOrderDialog(draft,store){Owner=this};
            ((System.Windows.Controls.TextBlock)dialog.FindName("TitleText")).Text=$"Pedido do lançamento de {total:C2} (já recebido)";((System.Windows.Controls.Button)dialog.FindName("SaveButton")).Content="Salvar pedido";
            if(dialog.ShowDialog()!=true)return;
            dialog.Result!.LinkSaleIds=parts.Select(x=>x.Id).ToList();await SaveOrder(dialog);RefreshData();
        }catch(Exception ex){AlertWindow.Info(this,"Não foi possível criar o pedido",ex.Message);}
    }
    void Debtors_Click(object s,System.Windows.RoutedEventArgs e)=>Navigate(NavDebtors,()=>new FollowUpWindow(store,true));
    void Reports_Click(object s,System.Windows.RoutedEventArgs e)=>Navigate(NavReports,()=>new ReportsWindow(store));
    void Products_Click(object s,System.Windows.RoutedEventArgs e)=>Navigate(NavProducts,()=>new ProductsWindow(store));
    void Budget_Click(object s,System.Windows.RoutedEventArgs e)=>Navigate(NavBudget,()=>new BudgetWindow(store));
    // Orçamentos em acompanhamento abre na área de Orçamentos (sem item novo no menu lateral).
    internal void OpenProspects()=>Navigate(NavBudget,()=>new FollowUpWindow(store,false));
    internal void OpenBudget()=>Navigate(NavBudget,()=>new BudgetWindow(store));
    void PurchaseNeeds_Click(object s,System.Windows.RoutedEventArgs e)=>Navigate(NavPurchases,()=>new PurchaseNeedsWindow(store));
    void Suppliers_Click(object s,System.Windows.RoutedEventArgs e)=>Navigate(NavSuppliers,()=>new SuppliersWindow(store));
    void ScheduledOrders_Click(object s,System.Windows.RoutedEventArgs e)=>OpenOrders();
    void Connection_Click(object s,System.Windows.RoutedEventArgs e){try{new ConnectionWindow(store.CreatePairing(),store.DatabasePath){Owner=this}.ShowDialog();}catch(Exception ex){AlertWindow.Info(this,"Pareamento",ex.Message);}}
    void ChangePassword_Click(object s,System.Windows.RoutedEventArgs e){if(new ChangePasswordWindow(store,App.CurrentUser.UserName){Owner=this}.ShowDialog()==true)Toast.Show(this,"Senha alterada. Use a nova senha nos próximos acessos, em todos os computadores.",tone:"ok");}
    void DocumentSettings_Click(object s,System.Windows.RoutedEventArgs e)=>ShowSettings(0);
    void Notes_Click(object s,System.Windows.RoutedEventArgs e)=>new ImprovementNotesWindow(store){Owner=this}.ShowDialog();
    void Customers_Click(object s,System.Windows.RoutedEventArgs e)=>Navigate(NavCustomers,()=>new CustomersWindow(store));
    void ClosedSessions_Click(object s,System.Windows.RoutedEventArgs e)=>Navigate(NavClosed,()=>new ClosedSessionsWindow(store));
    void Search_Click(object s,System.Windows.RoutedEventArgs e)=>Navigate(NavSearch,()=>new SearchWindow(store));
    void LostEntries_Click(object s,System.Windows.RoutedEventArgs e)=>new LostEntriesWindow(store){Owner=this}.ShowDialog();
    void History_Click(object s,System.Windows.RoutedEventArgs e)=>Navigate(NavSystem,()=>new AuditBackupWindow(store));
    void Refresh_Click(object s,System.Windows.RoutedEventArgs e)=>RefreshData();
    void DashboardSearch_TextChanged(object s,System.Windows.Controls.TextChangedEventArgs e){if(!IsLoaded)return;searchTimer.Stop();searchTimer.Start();}
    void LastUpdate_Click(object s,System.Windows.Input.MouseButtonEventArgs e)=>RefreshData();
    // Atalhos: Ctrl+K busca de telas e ações, F2 ou L novo lançamento, N novo pedido (L/N fora de campos de texto), F3 pesquisar lançamentos, F4 pedidos, F5 atualizar, Ctrl+F pesquisar no painel, Delete cancelar o lançamento selecionado.
    void NavigateHomeOnEscape(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(e.Key!=System.Windows.Input.Key.Escape||e.Handled||pages.Current==null||!CanHome()||UnsavedChanges.HasOpenEscapeElement(this))return;
        if(System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().Any(w=>w!=this&&w!=pages.Current&&w.IsVisible&&OwnedByMain(w)))return;
        e.Handled=true;
        if(UnsavedChanges.IsDirty(pages.Current)){UnsavedChanges.Escape(pages.Current);return;}
        Home_Click(this,new System.Windows.RoutedEventArgs());
    }
    bool CanHome()=>!store.IsRemote||Can("view.home");
    bool OwnedByMain(System.Windows.Window window){for(var owner=window.Owner;owner!=null;owner=owner.Owner)if(owner==this)return true;return false;}
    void Shortcuts(object s,System.Windows.Input.KeyEventArgs e)
    {
        var ctrl=(System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Control)!=0;
        switch(e.Key)
        {
            case System.Windows.Input.Key.F2:Add_Click(this,new System.Windows.RoutedEventArgs());break;
            case System.Windows.Input.Key.F3:Search_Click(this,new System.Windows.RoutedEventArgs());break;
            case System.Windows.Input.Key.F4:OpenOrders();break;
            case System.Windows.Input.Key.F5:RefreshData();break;
            case System.Windows.Input.Key.K when ctrl:OpenCommandPalette();break;
            case System.Windows.Input.Key.F when ctrl&&DashboardView.IsVisible:SalesSearchBox.Focus();SalesSearchBox.SelectAll();break;
            // Nas telas do menu, o campo de pesquisa marcado com SearchShortcut (Pedidos, Compras, Clientes, Devedores…).
            case System.Windows.Input.Key.F when ctrl&&pages.Current!=null&&SearchShortcut.Focus(PageHost):break;
            case System.Windows.Input.Key.Delete when DashboardView.IsVisible&&SalesGrid.IsKeyboardFocusWithin&&RemoveSaleButton.IsEnabled:Remove_Click(this,new System.Windows.RoutedEventArgs());break;
            case System.Windows.Input.Key.L when System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.None&&!Typing():Add_Click(this,new System.Windows.RoutedEventArgs());break;
            case System.Windows.Input.Key.N when System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.None&&!Typing():NewOrder();break;
            case System.Windows.Input.Key.F1:ShowShortcuts();break;
            // "?" (Shift + / ; no teclado ABNT2 é a tecla ao lado do Shift direito), fora de campos de texto.
            case System.Windows.Input.Key.OemQuestion or System.Windows.Input.Key.AbntC1 when System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Shift&&!Typing():ShowShortcuts();break;
            default:return;
        }
        e.Handled=true;
    }
    // Painel de atalhos (F1, "?" ou Ctrl+K › Atalhos do teclado): os mesmos que já existiam, agora à mostra.
    void ShowShortcuts()=>AlertWindow.Info(this,"Atalhos do teclado",ShortcutsText);
    public const string ShortcutsText=
        "Em qualquer tela\nF2 ou L — Novo lançamento\nN — Novo pedido\nF4 — Pedidos e produção\nF3 — Pesquisar lançamentos\nCtrl+K — Buscar telas, configurações e ações\nF1 ou ? — Esta lista\nEsc — Fechar a tela e voltar ao Início\n(L, N e ? valem fora dos campos de texto.)\n\n"+
        "No Início\nF5 — Atualizar\nCtrl+F — Pesquisar lançamentos do caixa\nDelete — Cancelar o lançamento selecionado\n\n"+
        "Nas telas do menu\nCtrl+F — Ir para a pesquisa (em Lembretes, para o Telefone)\n\n"+
        "Nas janelas de venda, pedido e cadastro\nEnter — Próximo campo\nCtrl+Enter — Salvar\nEsc — Fechar\nAlt+1 a Alt+6 — Usar um dos mais vendidos (Novo lançamento)";
    void UpdateSaleActions(){var enabled=displayingOpenCash&&SalesGrid.SelectedItem is SaleRow;EditSaleButton.IsEnabled=RemoveSaleButton.IsEnabled=RefundSaleButton.IsEnabled=enabled;ReceiptSaleButton.IsEnabled=SalesGrid.SelectedItem is SaleRow;OrderSaleButton.IsEnabled=enabled;}
    void SalesGrid_SelectionChanged(object s,System.Windows.Controls.SelectionChangedEventArgs e)=>UpdateSaleActions();
    void SalesGrid_DoubleClick(object s,System.Windows.Input.MouseButtonEventArgs e){if(displayingOpenCash)Edit_Click(s,new System.Windows.RoutedEventArgs());}
    void OrdersList_DoubleClick(object s,System.Windows.Input.MouseButtonEventArgs e){if(OrdersList.SelectedItem is DashboardOrderRow row)OpenDashboardOrder(row.Id);}
    // Fila "Hoje": vive na mesma área de Próximos pedidos, para não apertar o Início. Clique abre a tela de origem.
    void QueueTab_Click(object s,System.Windows.RoutedEventArgs e){var today=TodayTab.IsChecked==true;TodayBody.Visibility=TodaySummaryText.Visibility=today?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;OrdersBody.Visibility=OrdersSummaryText.Visibility=OrdersAllButton.Visibility=today?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;if(today)LoadToday();}
    void SetTodayCount(int count)=>TodayTab.Content=count>0?$"Hoje ({count})":"Hoje";
    async void LoadToday()
    {
        try
        {
            var items=store.IsRemote?await store.QueryRemoteAsync<List<GraficaCaixa.TodayItem>>("desktop/today"):GraficaCaixa.TodayQueue.Build(store.Load(),_=>true,store.AuthenticatedUser?.UserId??Guid.Empty,DateTime.Now);
            SetTodayCount(items.Count);TodayList.ItemsSource=items.Take(GraficaCaixa.TodayQueue.MaxItems).Select(x=>new TodayRow(x)).ToList();
            TodayEmpty.Visibility=items.Count==0?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
            TodaySummaryText.ToolTip=null;TodaySummaryText.Text=items.Count==0?"":items.Count>GraficaCaixa.TodayQueue.MaxItems?$"Mostrando os {GraficaCaixa.TodayQueue.MaxItems} primeiros de {items.Count}. Clique para abrir.":"Clique em um item para abrir.";
        }
        catch(Exception ex){TodaySummaryText.Text="Não foi possível carregar a fila.";TodaySummaryText.ToolTip=ex.Message;}
    }
    void TodayList_Click(object s,System.Windows.Input.MouseButtonEventArgs e)=>OpenTodayItem();
    void TodayList_KeyDown(object s,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;OpenTodayItem();}}
    void OpenTodayItem()
    {
        if(TodayList.SelectedItem is not TodayRow row)return;TodayList.SelectedItem=null;
        switch(row.Item.Kind)
        {
            case "order":if(row.Item.Id is Guid id)OpenDashboardOrder(id);break;
            case "purchase":PurchaseNeeds_Click(this,new System.Windows.RoutedEventArgs());break;
            case "debt":Debtors_Click(this,new System.Windows.RoutedEventArgs());break;
            case "prospect":OpenProspects();break;
            case "reminder":Reminders_Click(this,new System.Windows.RoutedEventArgs());break;
        }
    }
    async void OpenDashboardOrder(Guid id){
        try{var order=(store.IsRemote?await store.QueryRemoteAsync<AppData>($"desktop/context/order/{id}"):store.Load()).ScheduledOrders.FirstOrDefault(x=>x.Id==id);if(order==null){RefreshData();return;}
        var dialog=new ScheduledOrderDialog(order,store){Owner=this};
        if(dialog.ShowDialog()==true){await SaveOrder(dialog);await WhatsAppActions.OfferReadyNoticeAsync(this,store,order.Stage,dialog.Result!);}
        RefreshData();}catch(Exception ex){AlertWindow.Info(this,"Não foi possível abrir/salvar o pedido",ex.Message);}}
    // Pedidos abre na última vista escolhida (Lista ou Quadro); ao abrir um pedido específico, sempre na lista.
    internal void OpenOrders(Guid? selectedId=null,string? filter=null)=>Navigate(NavOrders,()=>selectedId==null&&filter==null&&ViewToggle.Stored("pedidos")=="Cards"?new OrderBoardWindow(store):new ScheduledOrdersWindow(store,selectedId,filter:filter));
    async Task SaveOrder(ScheduledOrderDialog dialog)
    {
        var closed=await BusinessUi.SaveOrderAsync(store,dialog.Result!,dialog.CorrectionSaleId,dialog.CorrectedAmount,dialog.CorrectionReason);
        foreach(var id in closed){var data=await store.BusinessContextAsync("closed",id);dashboardCustomers=data.Customers;GraficaCaixa.ReportWriter.Write(data.Sessions.First(x=>x.Id==id),data.CashClosureVersions.Where(x=>x.SessionId==id).OrderBy(x=>x.Version).LastOrDefault());}
    }
}

public sealed class DashboardOrderRow
{
    public Guid Id{get;} public string Customer{get;} public string Description{get;} public string Stage{get;} public string Due{get;} public System.Windows.Media.Brush RowBackground{get;} public System.Windows.Media.Brush StripeBrush{get;} public System.Windows.Media.Brush UrgencyBackground{get;} public System.Windows.Media.Brush UrgencyForeground{get;}
    // Linha compacta do Início: "#48 · Valter" e saldo na 1ª linha; itens e etiqueta curta ("Pronto · desde 25/09") na 2ª.
    public string Title{get;}=""; public string Phone{get;}=""; public string Short{get;}=""; public string Balance{get;}=""; public bool Paid{get;} public string Tip{get;}="";
    public DashboardOrderRow(GraficaCaixa.ScheduledOrder x,GraficaCaixa.AppData? data=null,bool showMoney=true):this(x,data)
    {
        var name=string.IsNullOrWhiteSpace(x.CustomerName)?GraficaCaixa.CustomerDirectory.Label(data,x.CustomerName,x.Contact):x.CustomerName.Trim();Title=$"#{x.Number} · {name}";var phone=PhoneText.Format(x.Contact?.Trim()??"");if(phone.Length>0&&!name.Contains(phone))Phone=$" • {phone}";
        var stage=x.Stage switch{"Pronto para retirada"=>"Pronto","Aguardando aprovação"=>"Aprovação",_=>x.Stage};
        var days=x.DeadlineStarted?GraficaCaixa.BusinessCalendar.RemainingBusinessDays(DateTime.Today,x.DueDate):int.MaxValue;
        var when=x.Stage=="Pronto para retirada"?(x.ReadyAt is DateTime ready?$"desde {ready:dd/MM}":"aguardando"):OrderOperations.IsImmediate(x)?"imediato":!x.DeadlineStarted?"após aprovação":days switch{<0=>$"atrasado {-days}d",0=>"vence hoje",1=>"falta 1 dia",_=>$"faltam {days} dias"};
        Short=$"{stage} · {when}";Paid=showMoney&&!x.PricePending&&x.RemainingAmount<=0;Balance=!showMoney?"":x.PricePending?"A definir":Paid?"Quitado":x.RemainingAmount.ToString("C2");
        Tip=$"{Customer}\n{Description}\n{Stage} • {Due}";
    }
    DashboardOrderRow(GraficaCaixa.ScheduledOrder x,GraficaCaixa.AppData? data){Id=x.Id;var pickup=x.Stage=="Pronto para retirada";Customer=$"#{x.Number} • {GraficaCaixa.CustomerDirectory.Label(data,x.CustomerName,x.Contact)}";Description=x.ItemsDescription;Stage=x.Stage;var days=x.DeadlineStarted?GraficaCaixa.BusinessCalendar.RemainingBusinessDays(DateTime.Today,x.DueDate):int.MaxValue;var overdue=Math.Abs(days);Due=pickup?"Aguardando retirada":OrderOperations.IsImmediate(x)?(x.ArtworkApprovedAt.HasValue?"Produção imediata":"Imediato • após aprovação da arte"):!x.DeadlineStarted?$"{x.ProductionBusinessDays} dias úteis após aprovação":days switch{<0=>$"Atrasado há {overdue} {(overdue==1?"dia útil":"dias úteis")}",0=>$"Vence hoje • {x.DueDate:dd/MM}",1=>$"Falta 1 dia útil • {x.DueDate:dd/MM}",_=>$"Faltam {days} dias úteis • {x.DueDate:dd/MM}"};var urgency=!x.DeadlineStarted?("Aguarda arte","#F2EFE9","#6B655D"):days switch{<0=>("Atrasado","#FDE8E7","#A52A26"),0=>("Vence hoje","#FDE8E7","#A52A26"),1=>("Falta 1 dia","#FDE8E7","#A52A26"),var pr when pr<=GraficaCaixa.BusinessRules.Current.PriorityDays=>("Prioridade","#FFF0D6","#9A5700"),<=7=>("Atenção","#FFF8DB","#756000"),_=>("No prazo","#E4F1ED","#2C6E63")};if(pickup)urgency=("Retirada","#DDF5F8","#147A8A");if(OrderOperations.IsImmediate(x))urgency=("IMEDIATO","#FFF4E1","#98661D");RowBackground=UrgencyColors.Row(urgency.Item1);StripeBrush=UrgencyColors.Stripe(urgency.Item1);var hot=UrgencyColors.IsUrgent(urgency.Item1);UrgencyBackground=hot?UrgencyColors.Stripe(urgency.Item1):Brush(urgency.Item2);UrgencyForeground=hot?System.Windows.Media.Brushes.White:Brush(urgency.Item3);}
    static System.Windows.Media.Brush Brush(string color)=>new System.Windows.Media.BrushConverter().ConvertFromString(color) as System.Windows.Media.Brush??System.Windows.Media.Brushes.Black;
}
public sealed class TodayRow(GraficaCaixa.TodayItem item)
{
    public GraficaCaixa.TodayItem Item{get;}=item;
    public System.Windows.Media.Brush Stripe{get;}=ThemeManager.Get(item.Tone switch{"Danger"=>"CDanger","Warning"=>"CWarn","Ok"=>"COk",_=>"CInfo"});
    public string Tip{get;}=string.IsNullOrWhiteSpace(item.Detail)?$"{item.Label}: {item.Title}":$"{item.Label}: {item.Title}\n{item.Detail}";
}
public sealed class SaleRow
{
    public Guid Id{get;} public string Date{get;} public string Customer{get;} public string Contact{get;} public string Product{get;} public int Units{get;} public string Quantity{get;} public string Payment{get;} public string UnitValue{get;} public string Total{get;}public System.Windows.Media.Brush TotalForeground{get;}
    // Tabela compacta do Início: só o nome na coluna (o telefone vai para o texto de ajuda) e a forma de pagamento como etiqueta colorida.
    public string Name{get;}="";public string Phone{get;}="";public string CustomerTip{get;}="";public string PaymentShort{get;}="";public System.Windows.Media.Brush PaymentBrush{get;}=System.Windows.Media.Brushes.Transparent;public System.Windows.Media.Brush PaymentSoft{get;}=System.Windows.Media.Brushes.Transparent;
    public SaleRow(GraficaCaixa.Sale s):this(s,true){var phone=PhoneText.Format(s.Contact?.Trim()??"");var name=string.IsNullOrWhiteSpace(s.CustomerName)||s.CustomerName.Trim().Equals("Não informado",StringComparison.CurrentCultureIgnoreCase)?"":s.CustomerName.Trim();Name=name.Length>0?name:phone.Length>0?phone:"Não informado";Phone=phone.Length>0&&name.Length>0?$" • {phone}":"";CustomerTip=phone.Length>0&&name.Length>0?$"{name} • {phone}":Name;PaymentBrush=ThemeManager.Get(MainWindow.PaymentColor(s.PaymentType));PaymentShort=MainWindow.Short(s.PaymentType);if(PaymentBrush is System.Windows.Media.SolidColorBrush solid){var soft=new System.Windows.Media.SolidColorBrush(solid.Color){Opacity=.16};soft.Freeze();PaymentSoft=soft;}}
    SaleRow(GraficaCaixa.Sale s,bool _){Id=s.Id;Date=s.CreatedAt.ToString("HH:mm");Customer=GraficaCaixa.DisplayText.Customer(s);Contact=s.Contact;Product=System.Text.RegularExpressions.Regex.Replace(s.Product,@"\s*\b[0-9a-f]{32}\b:?","");Units=s.Units;Quantity=IsOrderPayment(s.Product)?"—":$"{s.Units} un";Payment=s.PaymentType;UnitValue=s.UnitValue>0?s.UnitValue.ToString("C2"):"—";Total=s.Total.ToString("C2");TotalForeground=ThemeManager.Get(s.Total<0?"Danger":"Ink");}
    static bool IsOrderPayment(string product)=>product.StartsWith("Sinal",StringComparison.CurrentCultureIgnoreCase)||product.StartsWith("Parcela",StringComparison.CurrentCultureIgnoreCase)||product.StartsWith("Pagamento final",StringComparison.CurrentCultureIgnoreCase);
}





