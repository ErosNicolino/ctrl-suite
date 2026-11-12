using System.Globalization;
using System.Diagnostics;
namespace GraficaCaixa.Wpf;

public partial class ScheduledOrderDialog:System.Windows.Window
{
    bool writeBusy;
    sealed record ItemEditor(System.Windows.Controls.Grid Row,System.Windows.Controls.TextBox Quantity,System.Windows.Controls.TextBox Product,System.Windows.Controls.TextBox UnitPrice,System.Windows.Controls.TextBox Subtotal)
    { public OrderItem Snapshot {get;set;}=new(); public QuantityTierBinding? TierBinding {get;set;} public System.Windows.Controls.TextBox Cost {get;}=new(); public System.Windows.Controls.ComboBox CostState {get;}=new(); public System.Windows.Controls.TextBlock CostLabel {get;}=new(); public System.Windows.Controls.TextBox CostTotal {get;}=new(); public System.Windows.Controls.Grid? CostRow {get;set;}
      // Identidade pela própria linha (não pelo conteúdo): o editor muda depois de entrar em subtotalDriven/costTotalDriven
      // (TierBinding, CostRow) e, com igualdade de record, o HashSet deixava de achá-lo — o "custo pelo total" virava unitário ao salvar.
      public bool Equals(ItemEditor? other)=>ReferenceEquals(this,other); public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");readonly GraficaCaixa.ScheduledOrder? editing;GraficaCaixa.DataStore? store;readonly List<ItemEditor> itemEditors=[];
    ProductSearch? productSearch;IReadOnlyList<CustomerProfile> customerSuggestions=[];
    // Preço por tipo de cliente (mesma regra do lançamento): desconto das Regras para clientes Revenda/Empresa escolhidos na sugestão.
    decimal priceDiscount;string priceLabel="";string? discountCustomer;
    void ApplyCustomerPrice(CustomerProfile? customer){var type=customer?.PriceType??"";priceDiscount=GraficaCaixa.BusinessRules.Current.DiscountFor(type);priceLabel=type;discountCustomer=priceDiscount>0?customer!.Name.Trim():null;foreach(var editor in itemEditors)editor.TierBinding?.SetDiscount(priceDiscount,priceLabel);}
    readonly PaymentEditor mixedPayments=new(false);
    decimal appliedDiscount;decimal? appliedFixed;
    bool updatingItemPrices,updatingCosts;readonly HashSet<ItemEditor> subtotalDriven=[];readonly HashSet<ItemEditor> costTotalDriven=[];
    AppData? editorContext;bool contextReady;
    AppData EditorData()=>store==null?new AppData():store.IsRemote?editorContext??new AppData():store.Load();
    public Guid? CorrectionSaleId{get;private set;}public decimal? CorrectedAmount{get;private set;}public string CorrectionReason{get;private set;}="";public GraficaCaixa.ScheduledOrder? Result{get;private set;}
    // "Novo pedido" em azul-claro (também no modo Sóbria): as cores de destaque da janela apontam para a identidade do pedido (KPed*).
    void UseOrderColors(){var app=System.Windows.Application.Current.Resources;foreach(var (key,from) in new[]{("Accent","KPed"),("AccentInk","KPedInk"),("AccentSoft","KPedSoft"),("Selected","KPedSoft")}){Resources[key]=app[from];Resources[key+"Color"]=app[from+"Color"];}}
    // Opções de prazo das Regras; ao editar, inclui o prazo do próprio pedido mesmo que tenha saído da lista.
    int[] deadlineOptions=[];
    public ScheduledOrderDialog(GraficaCaixa.ScheduledOrder? order=null)
    {
        InitializeComponent();AsidePanel.Children.Add(itemPhoto);itemPhoto.Pick+=index=>{var editor=PhotoEditors().ElementAtOrDefault(index);if(editor==null)return;editor.Product.Focus();editor.Row.BringIntoView();};var depositText=GraficaCaixa.BusinessRules.Current.DepositPercent.ToString("0.##",System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));DepositHalfButton.Content=$"Sinal de {depositText}%";DepositHalfButton.ToolTip=$"Preenche um recebimento de {depositText}% do saldo (você confere a forma em Valores e pagamentos)";BindOrderSummary();SizeChanged+=(_,_)=>AdaptOrderLayout();Width=Math.Min(1280,System.Windows.SystemParameters.WorkArea.Width-16);Height=Math.Min(960,System.Windows.SystemParameters.WorkArea.Height-16);UseOrderColors();GraficaCaixa.Wpf.ThemeManager.Changed+=UseOrderColors;Closed+=(_,_)=>GraficaCaixa.Wpf.ThemeManager.Changed-=UseOrderColors;MinWidth=Math.Min(760,System.Windows.SystemParameters.WorkArea.Width);MinHeight=Math.Min(620,System.Windows.SystemParameters.WorkArea.Height);editing=order;CostReasonPanel.Visibility=order!=null&&GraficaCaixa.OrderOperations.IsFinished(order)?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;DebtOriginBox.SelectedDate=order?.DebtDate??DateTime.Today;IgnoreCostsBox.IsChecked=order?.IgnoreCosts??false;Loaded+=async (_,_)=>{SaveButton.IsEnabled=false;try{if(store!=null){editorContext=store.IsRemote?await store.QueryRemoteAsync<AppData>($"desktop/context/order/{editing?.Id??Guid.Empty}"):store.Load();productSearch=new ProductSearch(editorContext.Products);customerSuggestions=editorContext.Customers;}contextReady=true;RefreshPaymentChoices();SaveButton.IsEnabled=true;}catch(Exception ex){AlertWindow.Info(this,"Pesquisa de produtos",ex.Message);}};appliedDiscount=order?.DiscountPercent??0;appliedFixed=order?.FixedDiscount;DiscountMode.SelectedIndex=appliedFixed.HasValue?1:0;DiscountBox.Text=(appliedFixed??appliedDiscount).ToString("0.##",Pt);UnsavedChanges.Attach(this,()=>Save_Click(this,new System.Windows.RoutedEventArgs()),()=>UnsavedChanges.Snapshot(this)+appliedDiscount+appliedFixed);KeyboardFlow.Attach(this,()=>Save_Click(this,new System.Windows.RoutedEventArgs()));StageBox.ItemsSource=ScheduledOrdersWindow.Stages;deadlineOptions=GraficaCaixa.BusinessCalendar.AllowedDeadlines.Union(order==null?[]:[order.ProductionBusinessDays]).OrderBy(x=>x).ToArray();DeadlineBox.ItemsSource=deadlineOptions.Select(x=>x==0?"Imediato":x==1?"1 dia útil":$"{x} dias úteis");var payments=GraficaCaixa.PaymentPolicy.Methods;DepositPaymentBox.ItemsSource=payments;FinalPaymentBox.ItemsSource=payments;StageBox.SelectedIndex=0;DepositPaymentBox.SelectedIndex=FinalPaymentBox.SelectedIndex=1;DeadlineBox.SelectedIndex=Math.Max(0,Array.IndexOf(deadlineOptions,GraficaCaixa.BusinessRules.Current.DefaultDeadline));FinalPaymentDateBox.SelectedDate=DateTime.Today;FiscalButton.IsEnabled=order!=null&&store!=null;
        if(order==null)AddItemEditor();else{TitleText.Text=$"Editar pedido #{order.Number}";SaveButton.Content="Salvar alterações";NameBox.Text=order.CustomerName;ContactBox.Text=order.Contact;StageBox.SelectedItem=order.Stage;DeadlineBox.SelectedIndex=Math.Max(0,Array.IndexOf(deadlineOptions,order.ProductionBusinessDays));var source=order.Items.Count>0?order.Items:[new GraficaCaixa.OrderItem{Quantity=1,Product=order.Description}];foreach(var item in source)AddItemEditor(item.Quantity,item.Product,item.UnitPrice,item);TotalBox.Text=order.PricePending?"":(order.OriginalTotal??order.OrderTotal).ToString("N2");PricePendingBox.IsChecked=order.PricePending;DepositPaidBox.IsChecked=order.DepositPaid;DepositBox.Text=order.DepositAmount>0?order.DepositAmount.ToString("N2"):"";DepositPaymentBox.SelectedItem=order.DepositPaymentType;FullyPaidBox.IsChecked=order.FullyPaid;FinalPaymentDateBox.SelectedDate=order.PaymentReceivedAt?.Date??DateTime.Today;FinalPaymentBox.SelectedItem=order.FinalPaymentType;PayAfterDeliveryBox.IsChecked=order.PayAfterDelivery;ProductionBox.IsChecked=order.SentToProductionAt!=null;NotesBox.Text=order.Notes;if(order.DepositSaleId!=null){DepositPaidBox.IsEnabled=DepositBox.IsEnabled=DepositPaymentBox.IsEnabled=false;}if(order.FinalSaleId!=null){FullyPaidBox.IsEnabled=FinalPaymentDateBox.IsEnabled=FinalPaymentBox.IsEnabled=PayAfterDeliveryBox.IsEnabled=DepositPaidBox.IsEnabled=DepositBox.IsEnabled=DepositPaymentBox.IsEnabled=false;}}
        PhoneInput.Attach(ContactBox);CustomerAutocomplete.Attach(NameBox,ContactBox,()=>customerSuggestions,()=>store,ApplyCustomerPrice);NameBox.TextChanged+=(_,_)=>{if(discountCustomer!=null&&NameBox.Text.Trim()!=discountCustomer)ApplyCustomerPrice(null);};ExpectedPaymentDateBox.SelectedDate=order?.ExpectedPaymentDate;
        Closed+=(_,_)=>FinalizeResult();
        // UX 2.0: resumo lateral e ✓ das etapas acompanham o que é digitado.
        NameBox.TextChanged+=(_,_)=>UpdateAsideState();StageBox.SelectionChanged+=(_,_)=>UpdateAsideState();DeadlineBox.SelectionChanged+=(_,_)=>UpdateAsideState();OrderTabs.SelectionChanged+=(s,e)=>{if(e.OriginalSource==OrderTabs){if(OrderTabs.SelectedIndex==1)productionVisited=true;UpdateAsideState();}};
        ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)DepositPaidBox.Parent).Parent).Visibility=System.Windows.Visibility.Collapsed;
        ((System.Windows.FrameworkElement)((System.Windows.FrameworkElement)FullyPaidBox.Parent).Parent).Visibility=System.Windows.Visibility.Collapsed;
        MixedPaymentsHost.Children.Add(new System.Windows.Controls.TextBlock{Text="Adicionar pagamentos recebidos",FontWeight=System.Windows.FontWeights.SemiBold});MixedPaymentsHost.Children.Add(QuickPaymentTiles());mixedPayments.ShowSummary=false;mixedPayments.AllowExcess=true;mixedPayments.BalanceProvider=()=>decimal.TryParse(TotalBox.Text,NumberStyles.Currency,Pt,out var raw)?Math.Max(0,Discounted(raw)-(editing?.AppliedAmount??0)):0;MixedPaymentsHost.Children.Add(mixedPayments);mixedPayments.Changed+=UpdateTotals;RenderExistingPayments(order);
        CorrectionTypeBox.ItemsSource=PaymentPolicy.Types;CorrectionTypeBox.SelectionChanged+=(_,_)=>UpdateTotals();
        Loaded+=(_,_)=>RefreshPaymentChoices();
        PricingModeBox.SelectedIndex=order==null||order.UseItemPrices?1:0;
        FreightCostBox.Text=order?.FreightCost?.ToString("N2",Pt)??"";AdditionalCostBox.Text=order?.AdditionalCost?.ToString("N2",Pt)??"";CostNotesBox.Text=order?.CostNotes??"";
        if(order!=null){ExistingPaymentBox.ItemsSource=order.Payments.Where(x=>x.SaleId!=null).Select(x=>new PaymentChoice(x)).ToList();ExistingPaymentBox.SelectedIndex=0;CorrectionPanel.Visibility=order.Payments.Any(x=>x.SaleId!=null)?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;}else CorrectionPanel.Visibility=System.Windows.Visibility.Collapsed;TotalBox.IsEnabled=PricePendingBox.IsChecked!=true;TotalBox.IsReadOnly=PricingModeBox.SelectedIndex==1;UpdateTotals();
    }
    public ScheduledOrderDialog(GraficaCaixa.DataStore store):this(){this.store=store;ProductThumb.Store=store;UpdatePhoto();}
    // ---- UX 2.0 (pacote 2): resumo lateral, ✓ das etapas e atalhos de recebimento ----
    bool productionVisited;
    void UpdateAsideState()
    {
        if(AsideChecks==null||OrderTabs==null)return;
        var filled=itemEditors.Count(x=>!string.IsNullOrWhiteSpace(x.Product.Text));AsideItemsText.Text=filled==0?"—":filled.ToString();
        var days=DeadlineBox.SelectedIndex>=0&&DeadlineBox.SelectedIndex<deadlineOptions.Length?deadlineOptions[DeadlineBox.SelectedIndex]:-1;
        var stage=StageBox.SelectedItem?.ToString()??"";var beforeApproval=OrderStages.BeforeApproval(stage);
        AsideDueText.Text=days<0?"—":days==0?"Imediata (data do pedido)":editing is {DeadlineStarted:true}&&editing.ProductionBusinessDays==days&&!beforeApproval?editing.DueDate.ToString("dd/MM/yyyy"):$"{days} {(days==1?"dia útil":"dias úteis")} após a aprovação";
        var hasClient=!string.IsNullOrWhiteSpace(NameBox.Text);
        var itemsOk=itemEditors.Count>0&&itemEditors.All(x=>!string.IsNullOrWhiteSpace(x.Product.Text)&&int.TryParse(x.Quantity.Text,out var q)&&q>0);
        var pending=PricePendingBox.IsChecked==true;var priced=!pending&&decimal.TryParse(TotalBox.Text,NumberStyles.Currency,Pt,out var total)&&total>0;
        var costsOk=IgnoreCostsBox.IsChecked==true||itemEditors.Count>0&&itemEditors.All(x=>x.CostState.SelectedIndex is 1 or 2);
        SetTabDone(0,hasClient&&itemsOk);SetTabDone(1,(productionVisited||editing!=null)&&StageBox.SelectedItem!=null);SetTabDone(2,priced);SetTabDone(3,costsOk);
        AsideBalanceText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,decimal.TryParse(SummaryBalance.Text,NumberStyles.Currency,Pt,out var due)&&due>0?"Danger":"Ink");
        AsideChecks.Children.Clear();
        AddCheck(hasClient,"Cliente informado","Informe o cliente");
        AddCheck(itemsOk,"Itens completos","Complete quantidade e produto");
        AddCheck(priced,"Valor definido",pending?"Valor a definir":"Informe o valor do pedido");
        AddCheck(costsOk,"Custos conferidos","Custo pendente (opcional)",optional:true);
        DepositHalfButton.IsEnabled=PayAllButton.IsEnabled=priced&&OrderTabs.Items.Count>2&&((System.Windows.UIElement)OrderTabs.Items[2]).IsEnabled;
    }
    void SetTabDone(int index,bool done){if(OrderTabs.Items.Count>index&&OrderTabs.Items[index] is System.Windows.DependencyObject tab)Ui.SetIsDone(tab,done);}
    void AddCheck(bool ok,string okText,string pendingText,bool optional=false)
    {
        var row=new System.Windows.Controls.StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,Margin=new System.Windows.Thickness(0,3,0,0)};
        var key=ok?"AccentInk":optional?"Muted":"Warning";
        var icon=new UiIcon{IconName=ok?"circle-check":"circle-alert",Width=16,Height=16,Margin=new System.Windows.Thickness(0,0,8,0)};icon.SetResourceReference(UiIcon.ForegroundProperty,key);
        var text=new System.Windows.Controls.TextBlock{Text=ok?okText:pendingText,FontSize=13,TextWrapping=System.Windows.TextWrapping.Wrap};text.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,key);
        row.Children.Add(icon);row.Children.Add(text);AsideChecks.Children.Add(row);
    }
    // "Sinal de 50%" e "Quitar" só preenchem uma linha do editor de recebimentos (aba 3) — o usuário confere a forma e salva como sempre.
    void QuickDeposit_Click(object s,System.Windows.RoutedEventArgs e)=>QuickReceive(GraficaCaixa.BusinessRules.Current.DepositPercent/100m);
    void QuickPayAll_Click(object s,System.Windows.RoutedEventArgs e)=>QuickReceive(1m);
    // Botões de forma de pagamento (os mesmos do Novo lançamento): escolhem a forma da 1ª linha e, se ela estiver vazia, preenchem o saldo.
    // Pedido sem valor ou sem saldo: só escolhem a forma. Nada é gravado antes de Salvar.
    System.Windows.Controls.WrapPanel QuickPaymentTiles()
    {
        var tiles=new System.Windows.Controls.WrapPanel{Margin=new System.Windows.Thickness(0,10,0,0)};
        foreach(var (label,kind,color) in PaymentEditor.QuickKinds)
            tiles.Children.Add(PaymentEditor.QuickTile(this,label,kind,color,()=>
            {
                var balance=PricePendingBox.IsChecked==true?0:mixedPayments.BalanceProvider?.Invoke()??0;
                mixedPayments.Quick(kind,balance>0&&(mixedPayments.Count==0||mixedPayments.Amount==0)?balance:null);
            }));
        return tiles;
    }
    void QuickReceive(decimal share)
    {
        var balance=mixedPayments.BalanceProvider?.Invoke()??0;
        if(PricePendingBox.IsChecked==true||balance<=0){AlertWindow.Info(this,"Recebimento","Defina o valor do pedido antes de receber. Pedido sem saldo não precisa de recebimento.");return;}
        OrderTabs.SelectedIndex=2;mixedPayments.Quick(mixedPayments.FirstType==""?"PIX":mixedPayments.FirstType,Math.Round(balance*share,2));
    }
    void AdaptOrderLayout()
    {
        // Resumo lateral só quando há espaço; em telas estreitas volta a faixa de totais embaixo das etapas.
        var windowWidth=ActualWidth>0?ActualWidth:Width;var wide=windowWidth>=1080;
        if(wide&&Frame.Aside==null)Frame.Aside=AsidePanel;else if(!wide&&Frame.Aside!=null)Frame.Aside=null;
        BottomSummary.Visibility=wide?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;
        var narrow=windowWidth-(wide?300:0)<920;
        System.Windows.Controls.Grid.SetColumnSpan(ClientName,narrow?5:1);
        System.Windows.Controls.Grid.SetRow(ClientPhone,narrow?1:0);System.Windows.Controls.Grid.SetColumn(ClientPhone,narrow?0:2);
        System.Windows.Controls.Grid.SetRow(ClientDate,narrow?1:0);System.Windows.Controls.Grid.SetColumn(ClientDate,narrow?2:4);System.Windows.Controls.Grid.SetColumnSpan(ClientDate,narrow?3:1);
        ClientPhone.Margin=ClientDate.Margin=new System.Windows.Thickness(0,narrow?8:0,0,0);
        ClientGrid.ColumnDefinitions[0].Width=new System.Windows.GridLength(narrow?1:2.1,System.Windows.GridUnitType.Star);
        ClientGrid.ColumnDefinitions[2].Width=new System.Windows.GridLength(narrow?1:1.4,System.Windows.GridUnitType.Star);
        ClientGrid.ColumnDefinitions[4].Width=narrow?new System.Windows.GridLength(0):new System.Windows.GridLength(1.25,System.Windows.GridUnitType.Star);
        System.Windows.Controls.Grid.SetRow(ItemActions,narrow?1:0);ItemActions.Margin=new System.Windows.Thickness(0,narrow?8:0,0,0);
        System.Windows.Controls.Grid.SetRow(ItemsTotalText,narrow?1:0);System.Windows.Controls.Grid.SetColumn(ItemsTotalText,narrow?0:2);System.Windows.Controls.Grid.SetColumnSpan(ItemsTotalText,narrow?3:1);
        ItemsTotalText.Margin=new System.Windows.Thickness(0,narrow?12:0,0,0);
        CalculationGrid.ColumnDefinitions[1].Width=narrow?new System.Windows.GridLength(1,System.Windows.GridUnitType.Star):new System.Windows.GridLength(300);
        CalculationGrid.ColumnDefinitions[2].Width=narrow?new System.Windows.GridLength(0):new System.Windows.GridLength(1,System.Windows.GridUnitType.Star);
        var widths=new[]{60d,12d,0d,12d,narrow?100d:115d,narrow?105d:125d,54d};
        for(var i=0;i<widths.Length;i++){var width=i==2?new System.Windows.GridLength(1,System.Windows.GridUnitType.Star):new System.Windows.GridLength(widths[i]);ItemColumns.ColumnDefinitions[i].Width=width;foreach(var item in itemEditors)item.Row.ColumnDefinitions[i].Width=width;}
    }
    void BindOrderSummary()
    {
        // Reuse the shared summary's formatted values and calculations.
        var panel=(System.Windows.Controls.StackPanel)OrderSummary.Child;
        var values=(System.Windows.Controls.Primitives.UniformGrid)panel.Children[0];
        var targets=new[]{SummaryTotal,SummaryPaid,SummaryBalance};
        for(var i=0;i<targets.Length;i++)targets[i].SetBinding(System.Windows.Controls.TextBlock.TextProperty,new System.Windows.Data.Binding("Text"){Source=((System.Windows.Controls.StackPanel)values.Children[i]).Children[1]});
        PaymentFeesText.SetBinding(System.Windows.Controls.TextBlock.TextProperty,new System.Windows.Data.Binding("Text"){Source=panel.Children[1]});
    }
    void Notes_Click(object s,System.Windows.RoutedEventArgs e){var text=NotesDialog.Edit(this,"Observações para a ficha","Saem impressas na ficha de produção",NotesBox.Text,"Pedido");if(text!=null)NotesBox.Text=text;}
    void Calculator_Click(object s,System.Windows.RoutedEventArgs e){if(store!=null)ProductionCalculatorLauncher.Open(store,this,(quantity,product)=>{AddItemEditor(quantity,product);UpdateTotals();},"Usar no pedido");}
    // PDF do pedido salvo (abrir, salvar ou enviar pelo WhatsApp); alterações ainda não salvas não entram na ficha.
    void Pdf_Click(object s,System.Windows.RoutedEventArgs e){if(editing is {Number:>0}&&store!=null)OrderSheetPdf.Offer(this,PdfButton,store,editing);}
    void History_Click(object s,System.Windows.RoutedEventArgs e){if(editing is {Number:>0}&&store!=null)new AuditWindow(store,"Pedido",editing.Id.ToString()){Owner=this}.ShowDialog();}
    public ScheduledOrderDialog(GraficaCaixa.ScheduledOrder order,GraficaCaixa.DataStore store):this(order){this.store=store;ProductThumb.Store=store;UpdatePhoto();FiscalButton.IsEnabled=ExtrasButton.IsEnabled=order.Number>0;HistoryButton.Visibility=order.Number>0?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;PdfButton.Visibility=HistoryButton.Visibility;}
    public ScheduledOrderDialog(GraficaCaixa.DataStore store,Product product):this(store)
    {
        var editor=itemEditors[0];editor.Product.Text=product.Description;editor.Snapshot.ProductId=product.Id;editor.Snapshot.Unit=product.Unit;editor.TierBinding?.Choose(product);ApplyRegisteredCost(editor,product);UpdateItemTotals();
    }
    // Do Orçamento: o custo calculado lá entra como custo total do item, origem "Orçamento", ainda "Pendente" até você conferir (igual ao custo do cadastro).
    public ScheduledOrderDialog(GraficaCaixa.DataStore store,string product,decimal total,decimal quantity=1,decimal? cost=null):this(store)
    {
        var editor=itemEditors[0];editor.Quantity.Text=quantity.ToString("0.##");editor.Product.Text=product;editor.Subtotal.Text=total.ToString("N2",Pt);UpdateItemTotals();
        if(cost is not decimal value||value<=0||quantity<=0)return;updatingCosts=true;
        try{editor.Snapshot.CostSource="Orçamento";editor.Cost.Text=(value/quantity).ToString("0.00##",Pt);editor.CostTotal.Text=value.ToString("N2",Pt);costTotalDriven.Add(editor);}finally{updatingCosts=false;}
        UpdateCostSummary();
    }
    void RenderExistingPayments(ScheduledOrder? order)
    {
        if(order==null||order.Payments.Count==0){ExistingPaymentsHost.Visibility=System.Windows.Visibility.Collapsed;return;}
        ExistingPaymentsHost.Children.Add(new System.Windows.Controls.TextBlock{Text="Pagamentos existentes",FontWeight=System.Windows.FontWeights.SemiBold,Margin=new System.Windows.Thickness(0,0,0,6)});
        foreach(var payment in order.Payments.Where(x=>x.SaleId!=null).OrderBy(x=>x.PaidAt))ExistingPaymentsHost.Children.Add(new System.Windows.Controls.TextBlock{Text=$"{payment.PaymentType}  •  {payment.Amount:C2}  •  {payment.PaidAt:dd/MM/yyyy}  •  taxa {payment.EstimatedFee:C2}",Margin=new System.Windows.Thickness(0,3,0,0)});
        ExistingPaymentsHost.Children.Add(new System.Windows.Controls.TextBlock{Text=$"Recebido: {order.PaidAmount:C2}  •  Saldo: {order.RemainingAmount:C2}",FontWeight=System.Windows.FontWeights.SemiBold,Margin=new System.Windows.Thickness(0,8,0,0)});
    }
    void FinalizeResult()
    {
        if(Result==null)return;Result.PayAfterDelivery=PayAfterDeliveryBox.IsChecked==true&&Result.RemainingAmount>0;Result.ExpectedPaymentDate=Result.PayAfterDelivery?ExpectedPaymentDateBox.SelectedDate?.Date:null;
        foreach(var editor in itemEditors){var item=Result.Items.First(x=>x.Id==editor.Snapshot.Id);item.CostIgnored=editor.CostState.SelectedIndex==2;item.CostReviewed=editor.CostState.SelectedIndex==1;if(costTotalDriven.Contains(editor)&&decimal.TryParse(editor.CostTotal.Text,NumberStyles.Currency,Pt,out var total))item.EnteredTotalCost=total;else item.EnteredTotalCost=null;}
    }
    void RefreshPaymentChoices()
    {
        if(store?.IsRemote==true&&!contextReady)return;
        if(editing==null||store==null)return;
        var data=EditorData();var adjustments=data.FinancialCorrections.Select(x=>x.AdjustmentSaleId).ToHashSet();
        ExistingPaymentBox.ItemsSource=editing.Payments.Where(x=>x.SaleId is Guid id&&!adjustments.Contains(id)).Select(x=>new PaymentChoice(x,data.FinancialCorrections.Where(c=>c.OriginalSaleId==x.SaleId).OrderBy(c=>c.CreatedAt).LastOrDefault()?.CorrectedAmount)).ToList();ExistingPaymentBox.SelectedIndex=0;
    }
    async void NewProduct_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(store==null)return;
        try{var p=await ProductCreation.CreateAsync(this,store);if(p==null)return;productSearch=new ProductSearch(EditorData().Products);var target=itemEditors.LastOrDefault(x=>string.IsNullOrWhiteSpace(x.Product.Text));if(target==null){AddItemEditor();target=itemEditors[^1];}target.Product.Text=p.Description;target.Snapshot.ProductId=p.Id;target.Snapshot.Unit=p.Unit;target.TierBinding?.Choose(p);ApplyRegisteredCost(target,p);target.Product.Focus();}
        catch(Exception ex){AlertWindow.Info(this,"Novo produto",ex.Message);}
    }
    void AddItem_Click(object s,System.Windows.RoutedEventArgs e){if(itemEditors.Count>=10){AlertWindow.Info(this,"Limite de itens","Cada pedido pode ter até dez itens.");return;}AddItemEditor();itemEditors[^1].Product.Focus();}
    void AddItemEditor(int quantity=1,string product="",decimal? unitPrice=null,OrderItem? source=null)
    {
        var row=new System.Windows.Controls.Grid{Margin=new System.Windows.Thickness(0,itemEditors.Count==0?0:8,0,0)};row.RowDefinitions.Add(new System.Windows.Controls.RowDefinition{Height=System.Windows.GridLength.Auto});row.RowDefinitions.Add(new System.Windows.Controls.RowDefinition{Height=System.Windows.GridLength.Auto});row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition{Width=new System.Windows.GridLength(90)});row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition{Width=new System.Windows.GridLength(18)});row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition{Width=new System.Windows.GridLength(18)});row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition{Width=new System.Windows.GridLength(160)});row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition{Width=new System.Windows.GridLength(190)});row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition{Width=new System.Windows.GridLength(56)});var qty=new System.Windows.Controls.TextBox{Text=quantity.ToString(),ToolTip="Quantidade"};if(editing==null)QuantityInput.ClearOnEntry(qty);var name=new System.Windows.Controls.TextBox{Text=product,ToolTip="Produto ou serviço"};System.Windows.Controls.Grid.SetColumn(name,2);var remove=new System.Windows.Controls.Button{Content=new UiIcon{IconName="trash",Width=22,Height=22},Style=FindResource("OrderDelete") as System.Windows.Style,Margin=new System.Windows.Thickness(6,0,0,0),ToolTip="Remover item",Tag=row};remove.Click+=RemoveItem_Click;System.Windows.Controls.Grid.SetColumn(remove,6);row.Children.Add(qty);row.Children.Add(name);row.Children.Add(remove);ItemsStack.Children.Add(row);var price=new System.Windows.Controls.TextBox{Text=unitPrice?.ToString("0.00##########################",Pt)??"",ToolTip="Valor de uma unidade (R$)",TextAlignment=System.Windows.TextAlignment.Right};System.Windows.Controls.Grid.SetColumn(price,4);row.Children.Add(price);
        var subtotal=new System.Windows.Controls.TextBox{ToolTip="Valor total deste item (R$). Ao preencher, o unitário é calculado.",FontWeight=System.Windows.FontWeights.Bold,Margin=new System.Windows.Thickness(16,0,12,0),TextAlignment=System.Windows.TextAlignment.Right};subtotal.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty,"AccentSoft");subtotal.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty,"Accent");System.Windows.Controls.Grid.SetColumn(subtotal,5);row.Children.Add(subtotal);
        var editor=new ItemEditor(row,qty,name,price,subtotal){Snapshot=source==null?new OrderItem():System.Text.Json.JsonSerializer.Deserialize<OrderItem>(System.Text.Json.JsonSerializer.Serialize(source))!};
        row.RowDefinitions.Add(new System.Windows.Controls.RowDefinition{Height=System.Windows.GridLength.Auto});itemEditors.Add(editor);AddCostEditor(editor);var hint=new System.Windows.Controls.TextBlock{FontSize=11,Margin=new System.Windows.Thickness(0,4,0,0),Foreground=System.Windows.Media.Brushes.DimGray};System.Windows.Controls.Grid.SetRow(hint,2);System.Windows.Controls.Grid.SetColumnSpan(hint,7);row.Children.Add(hint);editor.TierBinding=new QuantityTierBinding(qty,price,subtotal,hint,()=>subtotalDriven.Contains(editor),()=>subtotalDriven.Remove(editor));editor.TierBinding.SetDiscount(priceDiscount,priceLabel);var choices=editor.TierBinding.ExactChoices;choices.Margin=new System.Windows.Thickness(0,5,0,0);System.Windows.Controls.Grid.SetRow(choices,1);System.Windows.Controls.Grid.SetColumnSpan(choices,3);row.Children.Add(choices);
        name.TextChanged+=(_,_)=>{editor.Snapshot.ProductId=null;editor.TierBinding?.Clear();editor.Snapshot.CostSource="Manual";editor.CostState.SelectedIndex=0;UpdateCostSummary();UpdatePhoto();};
        row.IsKeyboardFocusWithinChanged+=(_,e)=>{if(e.NewValue is true&&focusedEditor!=editor){focusedEditor=editor;UpdatePhoto();}};
        ProductAutocomplete.Attach(name,()=>productSearch,p=>{editor.Snapshot.ProductId=p.Id;editor.Snapshot.Unit=p.Unit;editor.TierBinding?.Choose(p);if(editorContext!=null){editorContext.Products.RemoveAll(x=>x.Id==p.Id);editorContext.Products.Add(p);}ApplyRegisteredCost(editor,p);editor.CostState.SelectedIndex=0;UpdateCostSummary();editorProducts[editor]=p;UpdatePhoto();},()=>store);qty.TextChanged+=ItemPrice_Changed;qty.TextChanged+=(_,_)=>{if(editor.Snapshot.ProductId is Guid id&&(store==null?null:EditorData())?.Products.FirstOrDefault(x=>x.Id==id) is Product registered&&editor.Snapshot.CostSource.StartsWith("Cadastro",StringComparison.Ordinal))ApplyRegisteredCost(editor,registered);};price.TextChanged+=ItemPrice_Changed;subtotal.TextChanged+=ItemPrice_Changed;UpdateItemTotals();UpdateRemoveButtons();if(IsLoaded)AdaptOrderLayout();
    }
    void RemoveItem_Click(object s,System.Windows.RoutedEventArgs e){if(itemEditors.Count<=1)return;if(s is System.Windows.Controls.Button{Tag:System.Windows.Controls.Grid row}){var editor=itemEditors.First(x=>x.Row==row);itemEditors.Remove(editor);editorProducts.Remove(editor);if(focusedEditor==editor)focusedEditor=null;ItemsStack.Children.Remove(row);if(editor.CostRow!=null)CostItemsStack.Children.Remove(editor.CostRow);UpdateRemoveButtons();UpdateItemTotals();}}
    void UpdateRemoveButtons(){foreach(var x in itemEditors)if(x.Row.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault() is { } b)b.IsEnabled=itemEditors.Count>1;}
    void NameBox_LostFocus(object s,System.Windows.RoutedEventArgs e){if(store==null||string.IsNullOrWhiteSpace(NameBox.Text))return;var customer=EditorData().Customers.FirstOrDefault(x=>x.Name.Equals(NameBox.Text.Trim(),StringComparison.CurrentCultureIgnoreCase));if(customer!=null&&(string.IsNullOrWhiteSpace(ContactBox.Text)||ContactBox.Text=="Não informado"))ContactBox.Text=customer.Contact;}
    void ContactBox_TextChanged(object s,System.Windows.Controls.TextChangedEventArgs e) { }
    void WhatsApp_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(!CommunicationOperations.IsWhatsAppNumber(ContactBox.Text)){AlertWindow.Info(this,"WhatsApp","Informe o telefone completo com DDD.");return;}
        var order=editing??Result??new ScheduledOrder{CustomerName=NameBox.Text,Contact=ContactBox.Text,OrderTotal=decimal.TryParse(TotalBox.Text,NumberStyles.Number,Pt,out var total)?total:0};
        var values=new Dictionary<string,string>{{"cliente",NameBox.Text.Trim()},{"pedido",order.Number>0?order.Number.ToString():"novo"},{"total",order.OrderTotal.ToString("C2")},{"recebido",order.PaidAmount.ToString("C2")},{"saldo",order.RemainingAmount.ToString("C2")},{"prazo",DeadlineBox.Text},{"referencia",order.Number>0?$" referente ao pedido #{order.Number}":""},{"grafica",EditorData().Company?.Name??""},{"pronto",WhatsAppActions.ReadySince(order).ToString("dd/MM")},{"pix",PixBrCode.MessageBlock(EditorData().Company,order.PricePending?0:order.RemainingAmount,order.Number>0?$"PED{order.Number}":null)}};
        var templates=(store==null?CommunicationOperations.EffectiveTemplates(new Prompt12State()):CommunicationOperations.EffectiveTemplates(EditorData().Prompt12)).Concat(new[]{
            new CommunicationTemplate{Name="Solicitar aprovação da arte",Body="Olá {cliente}, podemos aprovar a arte do pedido {pedido} para iniciar a produção?"},
            new CommunicationTemplate{Name="Confirmar pedido",Body="Olá {cliente}, confirmamos o pedido {pedido}, total {total}. Prazo: {prazo}."}});
        var menu=new System.Windows.Controls.ContextMenu();
        foreach(var template in templates){var item=new System.Windows.Controls.MenuItem{Header=template.Name,Tag=template};item.Click+=(_,_)=>{var message=MessageTemplates.Render(((CommunicationTemplate)item.Tag).Body,values);WhatsAppActions.Open(this,ContactBox.Text,message);};menu.Items.Add(item);}menu.IsOpen=true;
    }
    void Extras_Click(object s,System.Windows.RoutedEventArgs e){if(store!=null&&editing!=null)new OrderExtrasWindow(store,editing){Owner=this}.ShowDialog();}
    void Fiscal_Click(object s,System.Windows.RoutedEventArgs e){if(store!=null&&editing!=null)new OrderFiscalWindow(store,editing){Owner=this}.ShowDialog();}
    void CopyContact_Click(object s,System.Windows.RoutedEventArgs e){if(!string.IsNullOrWhiteSpace(ContactBox.Text))System.Windows.Clipboard.SetText(ContactBox.Text);}
    async void LinkCustomer_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(writeBusy)return;writeBusy=true;try{
        if(store==null)return;var dialog=new CustomerLinkDialog(store){Owner=this};if(dialog.ShowDialog()!=true||dialog.Selected==null)return;var customer=dialog.Selected;
        if(editing==null){NameBox.Text=customer.Name;ContactBox.Text=CustomerDirectory.Clean(customer.Contact);return;}
        var linkedRevision=await BusinessUi.ExecuteAsync<long>(store,"order.link",editing.Id,null,new CustomerLinkArgs(editing.Revision,customer.Id,DataStore.DesktopRevision(customer)),()=>store.Update(d=>{var order=d.ScheduledOrders.Single(x=>x.Id==editing.Id);var before=order.CustomerName;order.CustomerName=customer.Name;order.Contact=customer.Contact;order.Revision++;var ids=order.Payments.Where(x=>x.SaleId.HasValue).Select(x=>x.SaleId!.Value).Concat(new[]{order.DepositSaleId??Guid.Empty,order.FinalSaleId??Guid.Empty}).Where(x=>x!=Guid.Empty).ToHashSet();foreach(var sale in d.Sessions.SelectMany(x=>x.Sales).Where(x=>ids.Contains(x.Id))){sale.CustomerName=customer.Name;sale.Contact=customer.Contact;}AuditService.Record(d,App.CurrentUser,"Cliente vinculado ao pedido","Pedido",order.Id.ToString(),before,customer.Name,"",$"Pedido #{order.Number}");return order.Revision;}));editing.CustomerId=customer.Id;editing.Revision=linkedRevision;editing.CustomerName=customer.Name;editing.Contact=customer.Contact;NameBox.Text=customer.Name;ContactBox.Text=CustomerDirectory.Clean(customer.Contact);AlertWindow.Info(this,"Cliente vinculado","Pedido, lançamentos vinculados e histórico foram preservados.");
    
        }catch(OperationCanceledException){}catch(Exception ex){AlertWindow.Info(this,"Não foi possível concluir",ex.Message);}finally{writeBusy=false;}
    }
    void PaymentCheck_Click(object s,System.Windows.RoutedEventArgs e){if(DepositPaidBox.IsChecked==true&&string.IsNullOrWhiteSpace(DepositBox.Text)&&decimal.TryParse(TotalBox.Text,NumberStyles.Currency,Pt,out var total))DepositBox.Text=(Discounted(total)/2).ToString("N2");if(s==FullyPaidBox&&FullyPaidBox.IsChecked==true)PayAfterDeliveryBox.IsChecked=false;if(s==PayAfterDeliveryBox&&PayAfterDeliveryBox.IsChecked==true)FullyPaidBox.IsChecked=false;UpdateTotals();}
    void Totals_TextChanged(object s,System.Windows.Controls.TextChangedEventArgs e)=>UpdateTotals();
    void UpdateTotals(){if(OrderSummary==null)return;mixedPayments.RefreshBalance();UpdateCostSummary();decimal.TryParse(TotalBox.Text,NumberStyles.Currency,Pt,out var total);total=Discounted(total);UpdateDiscountPreview();decimal.TryParse(DepositBox.Text,NumberStyles.Currency,Pt,out var deposit);var received=editing?.PaidAmount??0;if(editing?.DepositSaleId==null&&DepositPaidBox.IsChecked==true)received=Math.Max(received,deposit);if(FullyPaidBox.IsChecked==true&&editing?.FullyPaid!=true)received=total;if(AdjustRecordedPaymentBox?.IsChecked==true&&ExistingPaymentBox?.SelectedItem is PaymentChoice selected&&decimal.TryParse(ExistingPaymentAmountBox?.Text,NumberStyles.Currency,Pt,out var correction)&&correction>=0)received=received-selected.Amount+correction;var unknown=PricePendingBox?.IsChecked==true||string.IsNullOrWhiteSpace(TotalBox.Text);received+=mixedPayments.Amount;var fees=(editing?.EstimatedFees??0)+mixedPayments.Fees;if(AdjustRecordedPaymentBox?.IsChecked==true && ExistingPaymentBox?.SelectedItem is PaymentChoice chosen && decimal.TryParse(ExistingPaymentAmountBox?.Text,NumberStyles.Currency,Pt,out var correctedFeeAmount))fees+=PaymentPolicy.Fee(correctedFeeAmount,(CorrectionTypeBox.SelectedItem?.ToString()??chosen.PaymentType)==chosen.PaymentType?chosen.FeePercent:PaymentPolicy.Rate(CorrectionTypeBox.SelectedItem?.ToString()??chosen.PaymentType))-PaymentPolicy.Fee(chosen.Amount,chosen.FeePercent);OrderSummary.Update(total,received,fees,unknown);UpdateAsideState();}
    void Pending_Click(object s,System.Windows.RoutedEventArgs e){TotalBox.IsEnabled=PricePendingBox.IsChecked!=true;if(PricePendingBox.IsChecked==true)FullyPaidBox.IsChecked=false;UpdateTotals();}
    void PaymentChoice_Changed(object s,System.Windows.Controls.SelectionChangedEventArgs e){if(ExistingPaymentBox.SelectedItem is PaymentChoice p&&ExistingPaymentAmountBox!=null){ExistingPaymentAmountBox.Text=p.Amount.ToString("N2",Pt);if(CorrectionTypeBox!=null)CorrectionTypeBox.SelectedItem=p.PaymentType=="Cartão de crédito"?"Cartão de crédito 1x":p.PaymentType;}}
    bool resolvingItems;
    async void Save_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(resolvingItems)return;
        if(store?.IsRemote==true&&!contextReady){AlertWindow.Info(this,"Carregando pedido","Aguarde o contexto do pedido antes de salvar.");return;}
        // Itens digitados que não estão no cadastro: oferece cadastrar, escolher/editar um existente ou manter como texto.
        // Itens livres que já estavam no pedido salvo não são perguntados de novo.
        var loose=itemEditors.Where(x=>x.Snapshot.ProductId==null&&!string.IsNullOrWhiteSpace(x.Product.Text)&&editing?.Items.Any(i=>i.ProductId==null&&i.Product==x.Product.Text.Trim())!=true).ToList();
        if(store!=null&&loose.Count>0)
        {
            resolvingItems=true;
            try
            {
                var pending=loose.Select(x=>new UnregisteredItems.Pending(x.Product.Text.Trim(),int.TryParse(x.Quantity.Text,out var q)?q:1,decimal.TryParse(x.UnitPrice.Text,NumberStyles.Number,Pt,out var u)?u:null)).ToList();
                var chosen=await UnregisteredItems.ResolveAsync(this,store,pending);if(chosen==null)return;
                for(var i=0;i<loose.Count;i++)if(chosen[i] is Product p)
                {
                    var target=loose[i];target.Product.Text=p.Description;target.Snapshot.ProductId=p.Id;target.Snapshot.Unit=p.Unit;
                    productSearch=new ProductSearch(EditorData().Products.Where(x=>x.Id!=p.Id).Append(p));editorContext?.Products.RemoveAll(x=>x.Id==p.Id);editorContext?.Products.Add(p);
                    target.TierBinding?.Choose(p);ApplyRegisteredCost(target,p);
                }
            }
            finally{resolvingItems=false;}
        }
        await SaveCore();
    }
    async Task SaveCore()
    {

        List<PaymentPart> pending;
        try{decimal.TryParse(TotalBox.Text,NumberStyles.Currency,Pt,out var raw);pending=mixedPayments.Read(Math.Max(0,Discounted(raw)-(editing?.AppliedAmount??0)));if(PricePendingBox.IsChecked==true&&pending.Count>0)throw new InvalidOperationException("Defina o valor antes de receber.");}
        catch(Exception ex){AlertWindow.Info(this,"Confira o pagamento",ex.Message);return;}
        if(DebtOriginBox.SelectedDate is not DateTime debtDate || debtDate.Date>DateTime.Today){AlertWindow.Info(this,"Data inválida","Informe a data de origem, sem data futura.");return;}
        var items=new List<GraficaCaixa.OrderItem>();foreach(var editor in itemEditors){if(!int.TryParse(editor.Quantity.Text,out var quantity)||quantity<=0||string.IsNullOrWhiteSpace(editor.Product.Text)){AlertWindow.Info(this,"Itens incompletos","Informe a quantidade e o nome de cada item do pedido.");return;}decimal? unitPrice=null;if(!string.IsNullOrWhiteSpace(editor.UnitPrice.Text)){if(!decimal.TryParse(editor.UnitPrice.Text,NumberStyles.Currency,Pt,out var parsedPrice)||parsedPrice<0){AlertWindow.Info(this,"Valor do item inválido","Informe valores unitários válidos, sem valores negativos.");return;}unitPrice=parsedPrice;}
        if(PricingModeBox.SelectedIndex==1&&PricePendingBox.IsChecked!=true&&unitPrice==null){AlertWindow.Info(this,"Valor do item obrigatório","Preencha o valor unitário de todos os itens ou escolha Total manual.");return;}
        if(!ReadCost(editor.Cost.Text,out var itemCost)||(editor.CostState.SelectedIndex==1&&!itemCost.HasValue)){AlertWindow.Info(this,"Custo inválido","Informe o custo de cada item conferido; zero é permitido.");return;}
        items.Add(new GraficaCaixa.OrderItem{Id=editor.Snapshot.Id,ProductId=editor.Snapshot.ProductId,Unit=editor.Snapshot.Unit,CostSource=editor.Snapshot.CostSource,UnitCost=itemCost,EnteredTotalCost=costTotalDriven.Contains(editor)&&decimal.TryParse(editor.CostTotal.Text,NumberStyles.Currency,Pt,out var enteredTotal)?enteredTotal:null,CostIgnored=editor.CostState.SelectedIndex==2,CostReviewed=editor.CostState.SelectedIndex==1,Quantity=quantity,Product=editor.Product.Text.Trim(),UnitPrice=unitPrice});}if(string.IsNullOrWhiteSpace(NameBox.Text)){AlertWindow.Info(this,"Cliente obrigatório","Informe o cliente do pedido.");return;}if(!PhoneText.IsCompleteOrEmpty(ContactBox.Text)&&PhoneText.Digits(ContactBox.Text)!=PhoneText.Digits(editing?.Contact)){AlertWindow.Info(this,"Telefone do cliente",PhoneText.IncompleteMessage);ContactBox.Focus();return;}var unknown=PricePendingBox.IsChecked==true||string.IsNullOrWhiteSpace(TotalBox.Text);decimal total=0;if(!unknown&&(!decimal.TryParse(TotalBox.Text,NumberStyles.Currency,Pt,out total)||total<0)){AlertWindow.Info(this,"Valor inválido","Informe um valor válido ou marque Valor a definir.");return;}decimal.TryParse(DepositBox.Text,NumberStyles.Currency,Pt,out var deposit);if(DepositPaidBox.IsChecked==true&&(deposit<=0||(!unknown&&editing?.DepositSaleId==null&&deposit>Discounted(total)))){AlertWindow.Info(this,"Sinal inválido","Confira o valor realmente recebido como sinal.");return;}if(AdjustRecordedPaymentBox.IsChecked==true){if(ExistingPaymentBox.SelectedItem is not PaymentChoice choice||!decimal.TryParse(ExistingPaymentAmountBox.Text,NumberStyles.Currency,Pt,out var corrected)||corrected<0){AlertWindow.Info(this,"Confira a correção","Escolha o pagamento e informe o valor correto.");return;}if(string.IsNullOrWhiteSpace(CorrectionReasonBox.Text)||CorrectionReasonBox.Text.Trim().Length<5){AlertWindow.Info(this,"Justificativa obrigatória","Informe o motivo da correção com pelo menos 5 caracteres.");return;}CorrectionSaleId=choice.SaleId;CorrectedAmount=corrected;CorrectionReason=CorrectionReasonBox.Text.Trim();}
        if(!ReadCost(FreightCostBox.Text,out var freight)||!ReadCost(AdditionalCostBox.Text,out var additional)){AlertWindow.Info(this,"Custo inválido","Informe valores positivos ou zero. Deixe vazio o que ainda não foi informado.");return;}
        if((appliedDiscount>0||appliedFixed>0)&&(unknown||Discounted(total)<(editing?.AppliedAmount??0))){AlertWindow.Info(this,"Confira o desconto","Defina o valor e mantenha o total com desconto igual ou maior que o já recebido.");return;}
        DateTime? sent=ProductionBox.IsChecked==true?(editing?.SentToProductionAt??DateTime.Now):null;var paidDay=(FinalPaymentDateBox.SelectedDate??DateTime.Today).Date;var paid=FullyPaidBox.IsChecked==true?(editing?.PaymentReceivedAt is DateTime received&&received.Date==paidDay?received:paidDay.Add(DateTime.Now.TimeOfDay)):editing?.PaymentReceivedAt; /* mesma data: mantém o horário gravado (antes era reescrito a cada salvar) */ var days=deadlineOptions[Math.Max(0,DeadlineBox.SelectedIndex)];var description=string.Join(" + ",items.Select(x=>$"{x.Quantity} {x.Product}"));Result=new GraficaCaixa.ScheduledOrder{CustomerId=editing?.CustomerName==NameBox.Text.Trim()&&PhoneText.Normalize(editing.Contact)==PhoneText.Normalize(ContactBox.Text)?editing.CustomerId:null,PendingPayments=pending,PaymentCorrectionType=AdjustRecordedPaymentBox.IsChecked==true?CorrectionTypeBox.SelectedItem?.ToString()??"":"",Id=editing?.Id??Guid.NewGuid(),Number=editing?.Number??0,CreatedAt=editing?.CreatedAt??DateTime.Now,DueDate=editing?.DueDate??DateTime.MaxValue.Date,ArtworkApprovedAt=editing?.ArtworkApprovedAt,ProductionBusinessDays=days,CustomerName=NameBox.Text.Trim(),Contact=string.IsNullOrWhiteSpace(ContactBox.Text)?"Não informado":PhoneText.Normalize(ContactBox.Text),Description=description,Items=items,Stage=StageBox.SelectedItem?.ToString()??"Agendado",OrderTotal=Discounted(total),OriginalTotal=total,DiscountPercent=appliedDiscount,FixedDiscount=appliedFixed,UseItemPrices=PricingModeBox.SelectedIndex==1,ItemCostsEnabled=true,IgnoreCosts=IgnoreCostsBox.IsChecked==true,DebtOriginDate=DebtOriginBox.SelectedDate,CostChangeReason=CostReasonBox.Text.Trim(),FreightCost=freight,AdditionalCost=additional,CostNotes=CostNotesBox.Text.Trim(),PricePending=unknown,Revision=editing?.Revision??0,PdfGeneratedAt=editing?.PdfGeneratedAt,DepositPaid=DepositPaidBox.IsChecked==true,DepositAmount=DepositPaidBox.IsChecked==true?deposit:0,DepositPaymentType=DepositPaymentBox.SelectedItem?.ToString()??"PIX",DepositSaleId=editing?.DepositSaleId,FullyPaid=FullyPaidBox.IsChecked==true,FinalPaymentType=FinalPaymentBox.SelectedItem?.ToString()??"PIX",FinalSaleId=editing?.FinalSaleId,PayAfterDelivery=PayAfterDeliveryBox.IsChecked==true||((editing?.PaidAmount??0)+pending.Sum(x=>x.Amount)<Discounted(total)),PaymentReceivedAt=paid,SentToProductionAt=sent,IsUncollected=editing?.IsUncollected??false,ArchivedAsUncollectedAt=editing?.ArchivedAsUncollectedAt,Notes=NotesBox.Text.Trim()};if(Array.IndexOf(ScheduledOrdersWindow.Stages,Result.Stage)>=Array.IndexOf(ScheduledOrdersWindow.Stages,"Aprovado")){if(Result.ArtworkApprovedAt is DateTime approved)Result.StartProductionDeadline(approved);else if(!Result.DeadlineStarted)Result.StartProductionDeadline(DateTime.Now);}else Result.ResetProductionDeadline();if(editing!=null && OrderOperations.RequiresPaidCorrection(editing,Result) && string.IsNullOrWhiteSpace(CorrectionReason)){var reason=new ReasonWindow("Justificativa da alteração","O pedido já possui recebimentos. Informe o motivo da alteração."){Owner=this};if(reason.ShowDialog()!=true)return;CorrectionReason=reason.Reason;}if(editing!=null&&editing.Stage!=OrderStages.Delivered&&Result.Stage==OrderStages.Delivered&&!Result.PricePending&&Result.OrderTotal-editing.PaidAmount-pending.Sum(x=>x.Amount) is var due&&due>0&&!AlertWindow.Confirm(this,"Entregar pedido com saldo",$"Saldo pendente: {due:C2}.\nO pedido fica em Devedores até ser pago. Para receber agora, use a aba Valores e pagamentos.\n\nMarcar como entregue?"))return;if(pending.Count>0&&store!=null&&!Result.PricePending){var link=await CashSaleLink.AskAsync(this,store,Result.CustomerName,Result.Contact,Result.OrderTotal-(editing?.AppliedAmount??0),pending.Sum(x=>x.Amount));if(link.Count>0){Result.LinkSaleIds=link;Result.PendingPayments=[];}}DialogResult=true;
    }
    public void OpenCostTab(){OrderTabs.SelectedIndex=3;foreach(var tab in OrderTabs.Items.OfType<System.Windows.Controls.TabItem>().Take(3))tab.IsEnabled=false;}
    void PricingMode_Changed(object s,System.Windows.Controls.SelectionChangedEventArgs e)=>UpdateItemTotals();
    void ItemPrice_Changed(object s,System.Windows.Controls.TextChangedEventArgs e)
    {
        if(updatingItemPrices)return;
        var editor=itemEditors.FirstOrDefault(x=>x.Subtotal==s||x.UnitPrice==s||x.Quantity==s);
        if(editor!=null){if(s==editor.Subtotal)subtotalDriven.Add(editor);else if(s==editor.UnitPrice)subtotalDriven.Remove(editor);}
        UpdateItemTotals();
    }
    // Item em foco (lateral): a linha em que a pessoa está; sem foco numa linha, o último item preenchido.
    readonly ItemPhotoPanel itemPhoto=new();ItemEditor? focusedEditor;readonly Dictionary<ItemEditor,Product> editorProducts=[];Dictionary<Guid,Product>? localProducts;
    IEnumerable<ItemEditor> PhotoEditors()=>itemEditors.Where(x=>!string.IsNullOrWhiteSpace(x.Product.Text));
    void UpdatePhoto()
    {
        if(AsidePanel==null)return;var rows=PhotoEditors().ToList();
        var entries=rows.Select(editor=>{
            var qty=int.TryParse(editor.Quantity.Text,out var q)?q:0;decimal.TryParse(editor.UnitPrice.Text,NumberStyles.Currency,Pt,out var unit);decimal.TryParse(editor.Subtotal.Text,NumberStyles.Currency,Pt,out var line);
            var detail=qty>0&&unit>0?$"{qty} × {unit.ToString("C2",Pt)} = {(line>0?line:qty*unit).ToString("C2",Pt)}":qty>0?$"Quantidade {qty}":"";
            if(editor.Snapshot.ProductId is not Guid id)return new ItemPhotoPanel.Entry(null,null,editor.Product.Text.Trim(),detail);
            var product=editorProducts.TryGetValue(editor,out var p)&&p.Id==id?p:null;
            if(product==null&&store!=null){if(store.IsRemote)product=editorContext?.Products.FirstOrDefault(x=>x.Id==id);else{localProducts??=store.Load().Products.ToDictionary(x=>x.Id);localProducts.TryGetValue(id,out product);}}
            return new ItemPhotoPanel.Entry(id,product==null?null:product.PhotoPath??"",editor.Product.Text.Trim(),detail);
        }).ToList();
        var focus=focusedEditor==null?-1:rows.IndexOf(focusedEditor);
        itemPhoto.Show(entries,focus<0?entries.Count-1:focus);
    }
    void UpdateItemTotals()
    {
        UpdatePhoto();
        if(TotalBox==null||PricingModeBox==null||updatingItemPrices)return;
        updatingItemPrices=true;
        try{
        decimal sum=0;var complete=itemEditors.Count>0;
        foreach(var editor in itemEditors)
        {
            var line=ItemPricing.Update(editor.Quantity,editor.UnitPrice,editor.Subtotal,subtotalDriven.Contains(editor),out var valid);
            if(valid)sum+=line;else complete=false;
        }
        ItemsTotalText.Text=complete?$"Total dos itens: {sum.ToString("C2",Pt)}":"Total dos itens: —";
        TotalBox.IsReadOnly=PricingModeBox.SelectedIndex==1;
        if(TotalBox.IsReadOnly)TotalBox.Text=complete?sum.ToString("N2",Pt):"";
        UpdateTotals();
        }finally{updatingItemPrices=false;}
    }
    void ApplyRegisteredCost(ItemEditor editor,Product product)
    {
        if(!int.TryParse(editor.Quantity.Text,out var quantity)||quantity<=0)return;var reference=ProductCosting.Resolve(product,quantity);updatingCosts=true;
        try{editor.Snapshot.CostSource=$"Cadastro ({reference.Kind})";editor.Cost.Text=reference.UnitCost?.ToString("0.00##",Pt)??"";editor.CostTotal.Text=reference.TotalCost?.ToString("N2",Pt)??"";if(reference.Kind==ProductCostKind.ExactQuantity)costTotalDriven.Add(editor);else costTotalDriven.Remove(editor);}finally{updatingCosts=false;}
    }    static bool ReadCost(string text,out decimal? cost)
    {
        cost=null;if(string.IsNullOrWhiteSpace(text))return true;
        if(!decimal.TryParse(text,NumberStyles.Currency,Pt,out var amount)||amount<0)return false;
        cost=amount;return true;
    }
    void AddCostEditor(ItemEditor editor)
    {
        var row=new System.Windows.Controls.Grid{Margin=new System.Windows.Thickness(0,6,0,6)};
        foreach(var width in new[]{new System.Windows.GridLength(1,System.Windows.GridUnitType.Star),new System.Windows.GridLength(150),new System.Windows.GridLength(170),new System.Windows.GridLength(160)})row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition{Width=width});
        editor.CostRow=row;editor.CostLabel.TextWrapping=System.Windows.TextWrapping.Wrap;editor.CostLabel.FontSize=15;editor.CostLabel.FontWeight=System.Windows.FontWeights.SemiBold;editor.CostLabel.Foreground=GraficaCaixa.Wpf.ThemeManager.Get("Ink");editor.CostLabel.VerticalAlignment=System.Windows.VerticalAlignment.Center;editor.CostLabel.Margin=new System.Windows.Thickness(0,0,10,0);row.Children.Add(editor.CostLabel);
        editor.Cost.Text=editor.Snapshot.UnitCost?.ToString("0.00##########",Pt)??"";editor.Cost.ToolTip="Custo unitário real (mostra todas as casas gravadas, para salvar sem arredondar)";System.Windows.Controls.Grid.SetColumn(editor.Cost,1);row.Children.Add(editor.Cost);
        editor.CostTotal.Text=editor.Snapshot.TotalCost?.ToString("N2",Pt)??"";editor.CostTotal.Margin=new System.Windows.Thickness(10,0,0,0);editor.CostTotal.ToolTip="Custo total exato; ao editar, o unitário equivalente é calculado";System.Windows.Controls.Grid.SetColumn(editor.CostTotal,2);row.Children.Add(editor.CostTotal);if(editor.Snapshot.EnteredTotalCost.HasValue)costTotalDriven.Add(editor);
        editor.CostState.ItemsSource=new[]{"Pendente","Conferido","Ignorado"};editor.CostState.SelectedIndex=editor.Snapshot.CostIgnored?2:editor.Snapshot.CostReviewed?1:0;System.Windows.Controls.Grid.SetColumn(editor.CostState,3);row.Children.Add(editor.CostState);
        CostItemsStack.Children.Add(row);editor.Cost.TextChanged+=(_,_)=>{if(updatingCosts)return;costTotalDriven.Remove(editor);editor.Snapshot.CostSource="Manual";UpdateCostSummary();};editor.CostTotal.TextChanged+=(_,_)=>{if(updatingCosts)return;costTotalDriven.Add(editor);editor.Snapshot.CostSource="Manual";UpdateCostSummary();};editor.CostState.SelectionChanged+=(_,_)=>UpdateCostSummary();
    }
    void IgnoreCosts_Click(object s,System.Windows.RoutedEventArgs e)=>UpdateCostSummary();
    void UpdateCostSummary()
    {
        if(CostTotalText==null||CostItemsStack==null)return;
        decimal sum=0;bool complete=itemEditors.Count>0,valid=true;updatingCosts=true;
        try{foreach(var editor in itemEditors){int.TryParse(editor.Quantity.Text,out var q);editor.CostLabel.Text=$"{q} × {editor.Product.Text}";decimal? line=null;if(costTotalDriven.Contains(editor)){if(!ReadCost(editor.CostTotal.Text,out line)||q<=0)valid=false;if(line.HasValue&&q>0)editor.Cost.Text=(line.Value/q).ToString("0.00##",Pt);}else{var parsed=ReadCost(editor.Cost.Text,out var c);if(!parsed||q<=0)valid=false;line=c.HasValue?decimal.Round(q*c.Value,2,MidpointRounding.AwayFromZero):null;editor.CostTotal.Text=line?.ToString("N2",Pt)??"";}var itemIgnored=editor.CostState.SelectedIndex==2;if(!itemIgnored&&line.HasValue)sum+=line.Value;complete&=itemIgnored||line.HasValue&&editor.CostState.SelectedIndex==1;}}finally{updatingCosts=false;}
        ItemsCostText.Text=$"Custo dos itens: {sum:C2}";
        if(!ReadCost(FreightCostBox.Text,out var freight)||!ReadCost(AdditionalCostBox.Text,out var additional)||!valid){CostTotalText.Text="Confira valores";CostPartialText.Visibility=System.Windows.Visibility.Collapsed;ProfitText.Text=MarginText.Text="—";UpdateAsideCost("Confira valores","—");return;}
        var ignored=IgnoreCostsBox.IsChecked==true;var cost=sum+(freight??0)+(additional??0);var status=ignored?"Ignorado":complete?"Conferido":"Pendente";
        CostTotalText.Text=cost.ToString("C2",Pt);CostPartialText.Visibility=complete&&!ignored?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;
        var priced=PricePendingBox?.IsChecked!=true&&decimal.TryParse(TotalBox.Text,NumberStyles.Currency,Pt,out _);
        decimal.TryParse(TotalBox.Text,NumberStyles.Currency,Pt,out var total);total=Discounted(total);
        CostSaleText.Text=$"Venda final: {total:C2} • Custo: {status}";
        ProfitText.Text=complete&&!ignored&&priced?(total-cost).ToString("C2",Pt):status;MarginText.Text=complete&&!ignored&&priced&&total>0?((total-cost)/total*100).ToString("N2",Pt)+"%":"—";
        // Sem nenhum custo informado: "—" no custo e na margem (R$ 0,00 parecia custo zero). Com custo parcial mostra o valor e "Pendente".
        var noCost=!complete&&!ignored&&cost==0;
        UpdateAsideCost(ignored?"Ignorado":noCost?"—":cost.ToString("C2",Pt),complete&&!ignored&&priced&&total>0?$"{(total-cost).ToString("C2",Pt)} · {Math.Round((total-cost)/total*100)}%":noCost?"—":ignored||!complete?status:"—",complete&&!ignored&&priced&&total-cost<0);
    }
    // Resumo lateral: Custo e Margem com os mesmos valores da aba Custo.
    void UpdateAsideCost(string cost,string margin,bool negative=false)
    {
        if(AsideCostText==null)return;AsideCostText.Text=cost;AsideMarginText.Text=margin;
        AsideMarginText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,negative?"Danger":margin.Contains('%')?"KindPedido":"Muted");
    }
    void AdjustCost_Click(object s,System.Windows.RoutedEventArgs e)=>OrderTabs.SelectedIndex=3;
    decimal Discounted(decimal original)=>original-(appliedFixed??decimal.Round(original*appliedDiscount/100,2,MidpointRounding.AwayFromZero));
    void DiscountMode_Changed(object s,System.Windows.Controls.SelectionChangedEventArgs e)=>UpdateDiscountPreview();
    void Discount_Changed(object s,System.Windows.Controls.TextChangedEventArgs e)=>UpdateDiscountPreview();
    void UpdateDiscountPreview()
    {
        if(DiscountPreviewText==null||DiscountStateText==null||TotalBox==null)return;
        if(!decimal.TryParse(TotalBox.Text,NumberStyles.Currency,Pt,out var original)||PricePendingBox?.IsChecked==true){DiscountPreviewText.Text="Defina o valor original para simular.";return;}
        if(!decimal.TryParse(DiscountBox.Text,NumberStyles.Number,Pt,out var percent)||percent<0||(DiscountMode.SelectedIndex==0?percent>100:percent>original||decimal.Round(percent,2)!=percent)){DiscountPreviewText.Text="Informe 0% a 100% ou um valor em reais até o total original.";return;}
        var discount=DiscountMode.SelectedIndex==1?percent:decimal.Round(original*percent/100,2,MidpointRounding.AwayFromZero);
        DiscountPreviewText.Text=$"Original: {original:C2}  •  Desconto: {discount:C2}  •  Final: {original-discount:C2}";
        DiscountStateText.Text=appliedFixed.HasValue?$"Aplicado ao pedido: {appliedFixed:C2} — será gravado ao salvar.":appliedDiscount>0?$"Aplicado ao pedido: {appliedDiscount:0.##}% — será gravado ao salvar.":"Simulação: nenhum desconto aplicado.";
    }
    void ApplyDiscount_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(PricePendingBox.IsChecked==true||!decimal.TryParse(TotalBox.Text,NumberStyles.Currency,Pt,out var original)||!decimal.TryParse(DiscountBox.Text,NumberStyles.Number,Pt,out var percent)||percent<0||(DiscountMode.SelectedIndex==0?percent>100:percent>original||decimal.Round(percent,2)!=percent)){AlertWindow.Info(this,"Confira o desconto","Informe o valor original e um desconto válido em % ou R$.");return;}
        var final=original-(DiscountMode.SelectedIndex==1?percent:decimal.Round(original*percent/100,2,MidpointRounding.AwayFromZero));
        if(final<(editing?.AppliedAmount??0)){AlertWindow.Info(this,"Desconto não aplicado","O valor final não pode ser menor que o já recebido.");return;}
        appliedFixed=DiscountMode.SelectedIndex==1?percent:null;appliedDiscount=appliedFixed.HasValue?0:percent;UpdateTotals();
    }
    void RemoveDiscount_Click(object s,System.Windows.RoutedEventArgs e){appliedFixed=null;appliedDiscount=0;DiscountBox.Text="0";UpdateTotals();}
    void Cancel_Click(object s,System.Windows.RoutedEventArgs e)=>DialogResult=false;
}
public sealed class PaymentChoice{public override string ToString()=>Label;public Guid? SaleId{get;}public decimal Amount{get;}public string Label{get;}public string PaymentType{get;}public decimal FeePercent{get;}public PaymentChoice(GraficaCaixa.OrderPayment p,decimal? effective=null){FeePercent=p.FeePercent??PaymentPolicy.HistoricalRate(p.PaymentType);PaymentType=p.PaymentType;SaleId=p.SaleId;Amount=effective??p.Amount;Label=$"{p.PaidAt:dd/MM/yyyy} • {p.Note} • {p.PaymentType} • {Amount:C2}";}}


