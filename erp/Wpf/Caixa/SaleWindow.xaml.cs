using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
namespace GraficaCaixa.Wpf;
public partial class SaleWindow : Window
{
    sealed record ItemRow(OrderItem Source,TextBox Quantity,TextBox Description,TextBox Price,TextBox Subtotal,TextBox Cost,Grid Grid);
    // Preço por tipo de cliente: cliente Revenda/Empresa escolhido na sugestão aplica o desconto das Regras aos itens do catálogo.
    decimal priceDiscount;string priceLabel="";string? discountCustomer;
    void ApplyCustomerPrice(CustomerProfile? customer){var type=customer?.PriceType??"";priceDiscount=BusinessRules.Current.DiscountFor(type);priceLabel=type;discountCustomer=priceDiscount>0?customer!.Name.Trim():null;foreach(var binding in tierBindings.Values)binding.SetDiscount(priceDiscount,priceLabel);}
    readonly DataStore? store;readonly AppData? editingData;bool updating;readonly HashSet<ItemRow> subtotalDriven=[];readonly Dictionary<ItemRow,QuantityTierBinding> tierBindings=[];readonly Sale? editing;readonly List<ItemRow> items=[];readonly StackPanel itemPanel=new();
    readonly TextBox name=new(),contact=new(),notes=new(){AcceptsReturn=true,MinHeight=40,TextWrapping=TextWrapping.Wrap},description=new(),received=new();
    readonly DatePicker date=new(){SelectedDateFormat=DatePickerFormat.Short};readonly TextBox time=new();readonly ComboBox type=new();
    readonly FinancialSummary financialSummary=new();readonly PaymentEditor payments=new(false,false);ProductSearch? search;
    ProductsWindow? productsWindow;
    Border? errorBanner;readonly TextBlock errorText=new(){TextWrapping=TextWrapping.Wrap};Control? markedField;object? markedBorder,markedBackground;
    sealed class FieldException(string message,Control? field):InvalidOperationException(message){public Control? Field{get;}=field;}
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    public Sale? Result{get;private set;}public bool SaveAndNew{get;private set;}
    readonly DateTime? openCashDay;
    public SaleWindow(Sale? sale=null,DateTime? defaultDate=null,DataStore? store=null,AppData? prepared=null)
    {
        openCashDay=sale==null&&defaultDate is DateTime openDay&&openDay.Date!=DateTime.Today?openDay:null;
        InitializeComponent();editing=sale;this.store=store;if(store!=null)ProductThumb.Store=store;editingData=sale==null?null:prepared??(store?.IsRemote==true?throw new InvalidOperationException("Carregue o lançamento antes de abrir o editor."):store?.Load());payments.ShowSummary=false;Loaded+=(_,_)=>name.Focus();Width=Math.Min(1260,SystemParameters.WorkArea.Width-16);Height=Math.Min(960,SystemParameters.WorkArea.Height-16);MinWidth=900;MinHeight=620;MaxWidth=SystemParameters.WorkArea.Width;MaxHeight=SystemParameters.WorkArea.Height;
        foreach(var controlType in new[]{typeof(TextBox),typeof(ComboBox),typeof(DatePicker)}){var compact=new Style(controlType,TryFindResource(controlType) as Style);compact.Setters.Add(new Setter(Control.MinHeightProperty,32d));compact.Setters.Add(new Setter(Control.HeightProperty,34d));compact.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8,4,8,4)));Resources[controlType]=compact;}
        if(store!=null){var data=prepared??(store.IsRemote?new AppData():store.Load());search=new ProductSearch(data.Products);CustomerAutocomplete.Attach(name,contact,()=>data.Customers,()=>store,ApplyCustomerPrice);name.TextChanged+=(_,_)=>{if(discountCustomer!=null&&name.Text.Trim()!=discountCustomer)ApplyCustomerPrice(null);};}
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0)};DockPanel.SetDock(footer,Dock.Bottom);Host.Children.Add(footer);
        void Button(string label,Action action,string style){var b=new Button{Content=label,Style=(Style)FindResource(style),Margin=new Thickness(6,0,0,0)};b.Click+=(_,_)=>action();footer.Children.Add(b);}
        Button("Cancelar",()=>DialogResult=false,"QuietButton");if(sale==null)Button("Salvar e lançar outro",()=>Complete(true),"SoftButton");Button("Salvar lançamento",()=>Complete(false),"PrimaryButton");
        DockPanel.SetDock(financialSummary,Dock.Bottom);Host.Children.Add(financialSummary);
        var panel=new StackPanel();Host.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        panel.Children.Add(new TextBlock{Text=sale==null?"Novo lançamento":"Editar lançamento",Style=(Style)FindResource("Title")});
        void Field(string label,UIElement control){panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,8,0,4),FontWeight=FontWeights.SemiBold});panel.Children.Add(control);}
        name.Text=sale?.CustomerName??"";contact.Text=sale?.Contact??"";notes.Text=sale?.Notes??"";PhoneInput.Attach(contact);
        void Pair(string first,UIElement a,string second,UIElement b){var grid=new Grid{Margin=new Thickness(0,8,0,0)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});var controls=new[]{a,b};var labels=new[]{first,second};for(int i=0;i<2;i++){var cell=new StackPanel{Margin=new Thickness(0,0,i==0?12:0,0)};cell.Children.Add(new TextBlock{Text=labels[i],FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,4)});cell.Children.Add(controls[i]);Grid.SetColumn(cell,i);grid.Children.Add(cell);}panel.Children.Add(grid);}
        Pair("Cliente",name,"Telefone",contact);
        var at=sale?.CreatedAt??defaultDate??DateTime.Now;date.SelectedDate=at.Date;time.Text=at.ToString("HH:mm:ss");if(sale==null)Pair("Data",date,"Horário",time);
        if(sale==null || sale.Items.Count>0)
        {
            var header=ItemGrid();var labels=new[]{"Quantidade","Produto/Descrição","Valor Unitário","Subtotal","Ação"};for(int i=0;i<labels.Length;i++){var label=new TextBlock{Text=labels[i],FontSize=11,FontWeight=FontWeights.SemiBold};Grid.SetColumn(label,i);header.Children.Add(label);}panel.Children.Add(header);panel.Children.Add(itemPanel);
            if(sale==null)AddItem();else foreach(var item in sale.Items)AddItem(item);
            if(sale==null){var add=new Button{Content="+ Adicionar Item",Style=(Style)FindResource("SoftButton"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};add.Click+=(_,_)=>AddItem();var actions=new StackPanel{Orientation=Orientation.Horizontal};actions.Children.Add(add);var productButton=new Button{Content="+ Novo Produto",Style=(Style)FindResource("SoftButton"),Margin=new Thickness(8,8,0,0),IsEnabled=store!=null};productButton.Click+=(_,_)=>NewProduct();actions.Children.Add(productButton);panel.Children.Add(actions);}
        }
        description.Text=sale?.Product??"";if(sale!=null)Field("Descrição complementar / referência (opcional)",description);
        Field("Observação",notes);
        // Cliente que deixa o troco: valor acima do total passa pela mesma confirmação dos pedidos.
        if(sale==null){payments.AllowExcess=true;payments.ExcessOrigin="lançamento";payments.Add();Field("Pagamentos recebidos — remova todas as linhas para deixar em aberto",payments);payments.Changed+=Refresh;}
        else
        {
            var effective=editingData?.FinancialCorrections.Where(x=>x.OriginalSaleId==sale.Id).OrderBy(x=>x.CreatedAt).LastOrDefault()?.CorrectedAmount??sale.Total;
            received.Text=effective.ToString("N2",Pt);type.ItemsSource=PaymentPolicy.Types.Concat(new[]{sale.PaymentType}).Distinct();type.SelectedItem=sale.PaymentType;
            Field("Forma de pagamento desta linha (crédito inclui parcelas)",type);Field("Valor recebido corrigido (gera ajuste vinculado)",received);
            type.SelectionChanged+=(_,_)=>Refresh();received.TextChanged+=(_,_)=>Refresh();
            panel.Children.Add(new TextBlock{Text="Nome, telefone, descrição e observação podem ser corrigidos. Alterações financeiras exigem justificativa. Quantidades e preços de itens vinculados podem ser corrigidos com justificativa, mantendo o histórico financeiro.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)});
        }
        Refresh();Redesign();UnsavedChanges.Attach(this,()=>Complete(false));KeyboardFlow.Attach(this,()=>Complete(false));
    }
    // UX 2.0 (pacote 2): diálogo central azul "Novo lançamento" — seções numeradas com ✓, recibo ao vivo à direita,
    // formas de pagamento em botões coloridos (atalho para o mesmo editor de pagamentos), troco informativo e rodapé fixo.
    // Os campos, validações, nomes internos e a gravação continuam os mesmos.
    SectionCard? customerCard,itemsCard,paymentCard;
    readonly TextBlock asideTotal=new(),asideItems=new(),asidePaid=new(),asideBalance=new(),asideFees=new(),asideChange=new(),asideCost=new(),asideMargin=new(),checkCustomer=new(),checkItems=new(),checkPayment=new();
    readonly UiIcon checkCustomerIcon=new(),checkItemsIcon=new(),checkPaymentIcon=new();
    readonly TextBox cashGiven=new(){Tag="Cliente entregou (R$)",ToolTip="Opcional: só para calcular o troco. Não altera o lançamento."};
    WhatsAppButton? whatsappButton;
    void Redesign()
    {
        foreach(var element in new UIElement[]{name,contact,notes,date,time,itemPanel,payments,financialSummary,description,received,type})Detach(element);
        Host.Children.Clear();Resources.Remove("Accent");Resources.Remove("AccentSoft");
        foreach(var key in new[]{typeof(TextBox),typeof(ComboBox),typeof(DatePicker)})Resources.Remove(key);

        // Rodapé: erro (à esquerda, junto do botão) + Cancelar · Salvar e lançar outro · Salvar.
        var footer=new StackPanel{Orientation=Orientation.Horizontal};
        void FooterButton(string label,Action action,string style,string? tip=null){var button=new Button{Content=label,Style=(Style)FindResource(style),Margin=new Thickness(8,0,0,0),ToolTip=tip};button.Click+=(_,_)=>action();footer.Children.Add(button);}
        FooterButton("Cancelar",()=>DialogResult=false,"GhostButton","Esc");if(editing==null)FooterButton("Salvar e lançar outro",()=>Complete(true),"SoftButton");FooterButton(editing==null?"Salvar lançamento":"Salvar alterações",()=>Complete(false),"DialogPrimaryButton","Ctrl+Enter ou Ctrl+S");
        errorText.SetResourceReference(TextBlock.ForegroundProperty,"Danger");errorText.FontWeight=FontWeights.SemiBold;errorText.MaxWidth=420;
        errorBanner=new Border{CornerRadius=new CornerRadius(8),Padding=new Thickness(12,7,12,7),Visibility=Visibility.Collapsed,Child=errorText};errorBanner.SetResourceReference(Border.BackgroundProperty,"DangerSoft");

        // Cabeçalho: Observações e Calculadora (mesmas ações de antes).
        var tools=new StackPanel{Orientation=Orientation.Horizontal};
        var observations=new Button{Content="Observações",Style=(Style)FindResource("SmallButton"),ToolTip="Observação salva no lançamento"};observations.Click+=(_,_)=>EditNotes();tools.Children.Add(observations);
        var calculator=new Button{Content="Calculadora",Style=(Style)FindResource("SmallButton"),Margin=new Thickness(8,0,0,0),IsEnabled=store!=null};calculator.Click+=(_,_)=>OpenCalculator();tools.Children.Add(calculator);

        var body=new StackPanel();
        // 1 · Cliente
        var customerGrid=new Grid();foreach(var w in editing==null?new[]{new GridLength(1.35,GridUnitType.Star),new GridLength(14),new GridLength(1,GridUnitType.Star),new GridLength(14),new GridLength(172)}:new[]{new GridLength(1.35,GridUnitType.Star),new GridLength(14),new GridLength(1,GridUnitType.Star)})customerGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=w});
        var customer=new Grid();customer.ColumnDefinitions.Add(new ColumnDefinition());customer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});customer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});customer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});customer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});customer.Children.Add(name);name.Tag="Vazio = Não informado";
        var customerSearch=new Button{Style=(Style)FindResource("IconButton"),Tag="search",ToolTip="Pesquisar cliente cadastrado",Margin=new Thickness(6,0,0,0),IsEnabled=store!=null};customerSearch.Click+=(_,_)=>ChooseCustomer();Grid.SetColumn(customerSearch,1);customer.Children.Add(customerSearch);
        // Atendimento de balcão, sem cadastro: preenche o nome de sempre e segue para o item.
        var counter=new Button{Content="Balcão",Style=(Style)FindResource("SoftButton"),ToolTip="Atendimento de balcão: preenche Cliente Balcão, sem telefone",Margin=new Thickness(6,0,0,0)};counter.Click+=(_,_)=>{name.Text="Cliente Balcão";contact.Text="";items.FirstOrDefault()?.Quantity.Focus();};Grid.SetColumn(counter,2);customer.Children.Add(counter);
        var nameHint=new TextBlock{Text="Parece um número. Digite o nome do cliente; o telefone vai no campo ao lado.",FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0),Visibility=Visibility.Collapsed};nameHint.SetResourceReference(TextBlock.ForegroundProperty,"Warning");Grid.SetRow(nameHint,1);Grid.SetColumnSpan(nameHint,3);customer.Children.Add(nameHint);
        void CheckName(){var typed=name.Text.Trim();nameHint.Visibility=typed.Any(char.IsDigit)&&!typed.Any(char.IsLetter)?Visibility.Visible:Visibility.Collapsed;}name.TextChanged+=(_,_)=>CheckName();CheckName();
        Cell(customerGrid,0,"Cliente",customer);
        var phone=new Grid();phone.ColumnDefinitions.Add(new ColumnDefinition());phone.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});phone.Children.Add(contact);contact.Tag="(11) 90000-0000";
        whatsappButton=new WhatsAppButton{Compact=true,OpenOnClick=false,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center,Phone=contact.Text};whatsappButton.Click+=(_,_)=>OpenWhatsApp();Grid.SetColumn(whatsappButton,1);phone.Children.Add(whatsappButton);
        contact.TextChanged+=(_,_)=>{if(whatsappButton!=null)whatsappButton.Phone=contact.Text;};name.TextChanged+=(_,_)=>UpdateChecks();
        Cell(customerGrid,2,"Telefone / WhatsApp",phone);
        // Caixa aberto em outro dia e ainda não fechado: a data vem da abertura (MainWindow.CashDate) e o rótulo avisa.
        var cashDay=openCashDay;if(cashDay!=null)date.ToolTip=$"O caixa aberto em {cashDay:dd/MM} ainda não foi fechado: o lançamento entra na data da abertura.";
        if(editing==null)Cell(customerGrid,4,cashDay==null?"Data":$"Data do caixa ({cashDay:dd/MM})",date);
        customerCard=new SectionCard{Number="1",Header="Cliente",Hint="vazio = Não informado",Content=customerGrid};body.Children.Add(customerCard);

        // 2 · Itens
        if(editing==null||items.Count>0)
        {
            var itemContent=new StackPanel();
            var columns=ItemGrid(false);columns.Margin=new Thickness(0,0,0,2);var labels=new[]{"Qtd.","Produto ou serviço","Unitário R$","Subtotal R$",""};
            for(var i=0;i<labels.Length;i++){var label=new TextBlock{Text=labels[i],FontSize=12,FontWeight=FontWeights.SemiBold};label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetColumn(label,i);columns.Children.Add(label);}
            if(editing==null)itemContent.Children.Add(topSellers);itemContent.Children.Add(columns);itemContent.Children.Add(itemPanel);
            StackPanel? itemTools=null;
            if(editing==null)
            {
                itemTools=new StackPanel{Orientation=Orientation.Horizontal};
                var add=new Button{Content="Adicionar item",Style=(Style)FindResource("SmallButton")};add.Click+=(_,_)=>AddItem();itemTools.Children.Add(add);
                var products=new Button{Content="Novo produto",Style=(Style)FindResource("SmallButton"),Margin=new Thickness(8,0,0,0),IsEnabled=store!=null};products.Click+=(_,_)=>OpenProducts();itemTools.Children.Add(products);
            }
            if(editing==null)Loaded+=(_,_)=>LoadTopSellers();
            itemsCard=new SectionCard{Number="2",Header="Itens",Hint=editing==null?"o que foi vendido":"itens do lançamento",Content=itemContent,HeaderTools=itemTools};body.Children.Add(itemsCard);
        }
        if(editing!=null){var details=new StackPanel();AddField(details,"Descrição complementar / referência (opcional)",description);body.Children.Add(new SectionCard{Number=items.Count>0?"3":"2",Header="Detalhes",Content=details});}

        // 3 · Pagamento
        var paymentContent=new StackPanel();
        if(editing==null)
        {
            var tiles=new WrapPanel{Margin=new Thickness(0,0,0,4)};
            foreach(var (label,kind,color) in PaymentEditor.QuickKinds)
                tiles.Children.Add(PaymentEditor.QuickTile(this,label,kind,color,()=>{var total=Total();payments.Quick(kind,payments.Count==0||payments.Amount==0?total:null);}));
            var exact=new Button{Content="Valor exato",Style=(Style)FindResource("SmallButton"),ToolTip="Recebido = total do lançamento",Margin=new Thickness(0,0,8,8)};exact.Click+=(_,_)=>{payments.Quick(payments.FirstType==""?"PIX":payments.FirstType,Total());};tiles.Children.Add(exact);
            paymentContent.Children.Add(tiles);
            paymentContent.Children.Add(payments);
            var change=new Grid{Margin=new Thickness(0,4,0,0)};change.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(200)});change.ColumnDefinitions.Add(new ColumnDefinition());
            var changeCell=new StackPanel();changeCell.Children.Add(new TextBlock{Text="Troco (dinheiro)",Style=(Style)FindResource("FieldLabel")});changeCell.Children.Add(cashGiven);change.Children.Add(changeCell);
            var changeHint=new TextBlock{Text="Opcional: informe quanto o cliente entregou para ver o troco. Não altera o lançamento.",Style=(Style)FindResource("Hint"),VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(12,0,0,10)};Grid.SetColumn(changeHint,1);change.Children.Add(changeHint);
            paymentContent.Children.Add(change);cashGiven.TextChanged+=(_,_)=>Refresh();
            paymentContent.Children.Add(new TextBlock{Text="Remova todas as linhas para deixar o valor em aberto.",Style=(Style)FindResource("Hint"),Margin=new Thickness(0,6,0,0)});
        }
        else{AddField(paymentContent,"Forma de pagamento desta linha (crédito inclui parcelas)",type);AddField(paymentContent,"Valor recebido corrigido (gera ajuste vinculado)",received);paymentContent.Children.Add(new TextBlock{Text="Nome, telefone, descrição e observação podem ser corrigidos. Alterações financeiras exigem justificativa. Quantidades e preços de itens vinculados podem ser corrigidos com justificativa, mantendo o histórico financeiro.",Style=(Style)FindResource("Hint"),Margin=new Thickness(0,12,0,0)});}
        paymentCard=new SectionCard{Number=editing==null?"3":items.Count>0?"4":"3",Header=editing==null?"Pagamento":"Pagamento recebido",Hint=editing==null?"forma e valor recebidos agora":"",Content=paymentContent};body.Children.Add(paymentCard);

        // Recibo ao vivo (à direita).
        var aside=new StackPanel();
        var receipt=new StackPanel();
        receipt.Children.Add(new TextBlock{Text=editing==null?"TOTAL DO LANÇAMENTO":"TOTAL",Style=(Style)FindResource("Eyebrow"),Margin=new Thickness(0)});
        asideTotal.Style=(Style)FindResource("SummaryValue");receipt.Children.Add(asideTotal);
        receipt.Children.Add(DialogKit.Line("Itens",asideItems));receipt.Children.Add(DialogKit.Line("Recebido",asidePaid));receipt.Children.Add(DialogKit.Line("Saldo",asideBalance));receipt.Children.Add(DialogKit.Line("Troco",asideChange));
        receipt.Children.Add(new Separator{Margin=new Thickness(0,8,0,8)});
        asideFees.Style=(Style)FindResource("Hint");receipt.Children.Add(asideFees);
        // Custo e margem (informativo, não muda o que o cliente paga): soma o custo dos itens; "Ajustar custo" abre o custo por item.
        if(items.Count>0)
        {
            var costBox=new StackPanel();
            costBox.Children.Add(DialogKit.Line("Custo",asideCost,top:0));costBox.Children.Add(DialogKit.Line("Margem",asideMargin));
            var adjust=new Button{Content="Ajustar custo",Style=(Style)FindResource("GhostButton"),FontSize=12.5,MinHeight=0,Padding=new Thickness(6,3,6,3),Margin=new Thickness(-6,4,0,0),HorizontalAlignment=HorizontalAlignment.Left,IsEnabled=items.Any(x=>!x.Cost.IsReadOnly),ToolTip="Custo interno de cada item (não aparece para o cliente). Vem do cadastro do produto."};
            adjust.SetResourceReference(Control.ForegroundProperty,"AccentInk");adjust.Click+=(_,_)=>AdjustCosts();costBox.Children.Add(adjust);
            var tint=new Border{CornerRadius=new CornerRadius(8),Padding=new Thickness(10,8,10,4),Margin=new Thickness(-10,10,-10,0),Child=costBox};tint.SetResourceReference(Border.BackgroundProperty,"KindLancamentoBand");receipt.Children.Add(tint);
        }
        receipt.Children.Add(new Separator{Margin=new Thickness(0,8,0,8)});
        receipt.Children.Add(Check(checkCustomerIcon,checkCustomer));receipt.Children.Add(Check(checkItemsIcon,checkItems));receipt.Children.Add(Check(checkPaymentIcon,checkPayment));
        aside.Children.Add(new Border{Style=(Style)FindResource("SummaryCard"),Child=receipt});
        financialSummary.Visibility=Visibility.Collapsed;aside.Children.Add(financialSummary);
        itemPhoto.Pick+=index=>{var row=PhotoRows().ElementAtOrDefault(index);if(row==null)return;row.Description.Focus();row.Grid.BringIntoView();};aside.Children.Add(itemPhoto);

        var frame=new DialogFrame{Kind="Lancamento",IconName="wallet",Title=editing==null?"Novo lançamento":"Editar lançamento",
            Subtitle=editing==null?"Venda paga na hora, entra no caixa · Enter avança · Ctrl+Enter salva · Esc fecha":"Corrija preservando o histórico · Ctrl+Enter salva · Esc fecha",
            HeaderExtra=tools,Content=body,Aside=aside,Footer=footer,FooterLeft=errorBanner};
        Content=frame;
        Refresh();
    }
    static StackPanel Check(UiIcon icon,TextBlock text){icon.Width=icon.Height=16;icon.Margin=new Thickness(0,0,8,0);text.FontSize=13;text.TextWrapping=TextWrapping.Wrap;var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,3,0,0)};row.Children.Add(icon);row.Children.Add(text);return row;}
    static void Cell(Grid grid,int column,string label,UIElement control){var cell=new StackPanel();cell.Children.Add(new TextBlock{Text=label,Style=(Style)Application.Current.FindResource("FieldLabel")});cell.Children.Add(control);Grid.SetColumn(cell,column);grid.Children.Add(cell);}
    void SetCheck(UiIcon icon,TextBlock text,bool ok,string okText,string pendingText)
    {
        icon.IconName=ok?"circle-check":"circle-alert";icon.SetResourceReference(UiIcon.ForegroundProperty,ok?"AccentInk":"Warning");
        text.Text=ok?okText:pendingText;text.SetResourceReference(TextBlock.ForegroundProperty,ok?"AccentInk":"Warning");
    }
    // Atualiza o recibo ao vivo e o ✓ das seções (só apresentação; os valores vêm dos mesmos cálculos de antes).
    void UpdateAside(decimal total,decimal paid,decimal fees)
    {
        if(customerCard==null)return;var pt=Pt;asideTotal.Text=total.ToString("C2",pt);
        var count=items.Count(x=>!string.IsNullOrWhiteSpace(x.Description.Text));asideItems.Text=count==0?"—":count.ToString();
        asidePaid.Text=paid.ToString("C2",pt);
        var balance=total-paid;asideBalance.Text=balance<0?$"{(-balance).ToString("C2",pt)} a mais":balance.ToString("C2",pt);asideBalance.SetResourceReference(TextBlock.ForegroundProperty,balance>0?"Danger":balance<0?"Warning":"Ink");
        decimal.TryParse(cashGiven.Text,NumberStyles.Currency,pt,out var given);var cash=payments.CashAmount;
        var changeRow=(Grid)asideChange.Parent;changeRow.Visibility=editing==null&&given>0?Visibility.Visible:Visibility.Collapsed;
        if(given>0){var change=given-cash;asideChange.Text=change>=0?change.ToString("C2",pt):$"Faltam {(-change).ToString("C2",pt)}";asideChange.SetResourceReference(TextBlock.ForegroundProperty,change>=0?"AccentInk":"Danger");}
        asideFees.Text=$"Taxas estimadas {fees.ToString("C2",pt)} · Líquido {(paid-fees).ToString("C2",pt)}";
        UpdateCost();UpdateChecks(total,paid);
    }
    // Margem só sobre os itens com custo informado; os itens sem custo aparecem contados ao lado.
    void UpdateCost()
    {
        var pt=Pt;decimal cost=0,revenue=0;var missing=0;
        foreach(var row in items)
        {
            if(string.IsNullOrWhiteSpace(row.Description.Text))continue;
            if(!decimal.TryParse(row.Cost.Text,NumberStyles.Currency,pt,out var c)){missing++;continue;}
            cost+=c;revenue+=decimal.TryParse(row.Subtotal.Text,NumberStyles.Currency,pt,out var s)?s:0;
        }
        var known=items.Count(x=>!string.IsNullOrWhiteSpace(x.Description.Text))-missing;
        asideCost.Text=known==0?"—":cost.ToString("C2",pt)+(missing>0?$" ({missing} sem custo)":"");
        if(known==0||revenue<=0){asideMargin.Text="—";asideMargin.SetResourceReference(TextBlock.ForegroundProperty,"Ink");return;}
        var margin=revenue-cost;asideMargin.Text=$"{margin.ToString("C2",pt)} · {Math.Round(margin/revenue*100)}%";
        asideMargin.SetResourceReference(TextBlock.ForegroundProperty,margin<0?"Danger":"AccentInk");
    }
    // Ajustar custo: um campo por item com o custo total da linha; só grava de volta se todos forem válidos.
    void AdjustCosts()
    {
        var rows=items.Where(x=>!string.IsNullOrWhiteSpace(x.Description.Text)).ToList();
        if(rows.Count==0){ShowError("Informe os itens antes de ajustar o custo.",items.FirstOrDefault()?.Description);return;}
        var dialog=new Window{Title="Ajustar custo",Owner=this,Width=Math.Min(560,SystemParameters.WorkArea.Width-32),SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false};
        var body=new StackPanel();var boxes=new List<(ItemRow Row,TextBox Box,DialogKit.Field Field)>();
        foreach(var row in rows)
        {
            var box=new TextBox{Text=row.Cost.Text,TextAlignment=TextAlignment.Right,Tag="vazio = sem custo",IsReadOnly=row.Cost.IsReadOnly};
            var field=DialogKit.LabeledField($"{row.Quantity.Text} × {row.Description.Text.Trim()} · custo total R$",box);body.Children.Add(field.Panel);boxes.Add((row,box,field));
        }
        body.Children.Add(DialogKit.Hint("Custo interno, não aparece para o cliente. Vem do cadastro do produto; deixe vazio se não souber.",0));
        void Save()
        {
            foreach(var (_,box,field) in boxes)if(!string.IsNullOrWhiteSpace(box.Text)&&!(decimal.TryParse(box.Text,NumberStyles.Currency,Pt,out var c)&&c>=0&&decimal.Round(c,2)==c)){DialogKit.ShowError(field,box,"Valor em reais com até duas casas, ou deixe vazio.");return;}
            dialog.DialogResult=true;
        }
        dialog.Content=new DialogFrame{Kind="Lancamento",IconName="wallet",Title="Ajustar custo",Subtitle="Custo de cada item deste lançamento · Esc cancela",Content=body,Footer=DialogKit.Footer(dialog,"Usar estes custos",Save),ScrollBody=false};
        dialog.Loaded+=(_,_)=>{var first=boxes.FirstOrDefault(x=>!x.Box.IsReadOnly).Box;first?.Focus();first?.SelectAll();};
        if(dialog.ShowDialog()!=true)return;
        foreach(var (row,box,_) in boxes)if(!row.Cost.IsReadOnly&&row.Cost.Text!=box.Text.Trim())row.Cost.Text=box.Text.Trim();
    }
    void UpdateChecks()=>UpdateChecks(null,null);
    void UpdateChecks(decimal? total,decimal? paid)
    {
        if(customerCard==null)return;
        var hasName=!string.IsNullOrWhiteSpace(name.Text);customerCard.IsDone=hasName;
        SetCheck(checkCustomerIcon,checkCustomer,true,hasName?"Cliente informado":"Cliente: Não informado","");
        var itemsOk=items.Count>0&&items.All(x=>int.TryParse(x.Quantity.Text,out var q)&&q>0&&!string.IsNullOrWhiteSpace(x.Description.Text)&&decimal.TryParse(x.Price.Text,NumberStyles.Currency,Pt,out var p)&&p>=0);
        if(itemsCard!=null)itemsCard.IsDone=itemsOk;SetCheck(checkItemsIcon,checkItems,itemsOk||editing!=null,"Itens completos","Complete quantidade, produto e valor");
        if(total is decimal t&&paid is decimal p2)
        {
            var covered=editing!=null||t>0&&p2>=t;if(paymentCard!=null)paymentCard.IsDone=editing==null&&covered;
            SetCheck(checkPaymentIcon,checkPayment,covered,editing!=null?"Pagamento conferido":"Pagamento cobre o total",t<=0?"Informe os itens e o valor":p2<=0?"Sem pagamento: fica em aberto":$"Falta receber {(t-p2).ToString("C2",Pt)}");
        }
    }
    static Grid TwoColumns(GridLength first,GridLength second){var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition{Width=first});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(22)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=second});return grid;}
    static void AddLabeled(Grid grid,int index,string label,UIElement control){var cell=new StackPanel();cell.Children.Add(new TextBlock{Text=label,FontSize=13,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,6)});cell.Children.Add(control);Grid.SetColumn(cell,index==0?0:2);grid.Children.Add(cell);}
    static void AddField(Panel panel,string label,UIElement control){panel.Children.Add(new TextBlock{Text=label,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,panel.Children.Count==0?12:14,0,6)});panel.Children.Add(control);}
    Button IconButton(string icon,string tooltip)=>new(){Content=new UiIcon{IconName=icon,Width=20,Height=20,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center},ToolTip=tooltip,Style=(Style)FindResource("SoftButton"),Padding=new Thickness(0),Margin=new Thickness(8,0,0,0),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center};
    static void Detach(UIElement element){switch(LogicalTreeHelper.GetParent(element)){case Panel panel:panel.Children.Remove(element);break;case ContentControl content when ReferenceEquals(content.Content,element):content.Content=null;break;case Decorator decorator when ReferenceEquals(decorator.Child,element):decorator.Child=null;break;}}
    void ChooseCustomer(){if(store==null)return;var dialog=new CustomerLinkDialog(store){Owner=this};if(dialog.ShowDialog()!=true||dialog.Selected==null)return;name.Text=dialog.Selected.Name;contact.Text=dialog.Selected.Contact;name.Focus();}
    void CopyPhone(){if(!string.IsNullOrWhiteSpace(contact.Text))Clipboard.SetText(contact.Text);}
    void OpenWhatsApp()=>WhatsAppActions.Open(this,contact.Text,"");
    void EditNotes(){var text=NotesDialog.Edit(this,"Observações do lançamento","A mesma observação salva no lançamento",notes.Text,"Lancamento");if(text!=null)notes.Text=text;}
    void OpenProducts(){if(store==null)return;productsWindow=Application.Current.Windows.OfType<ProductsWindow>().FirstOrDefault(x=>x.IsVisible&&x.ShowInTaskbar)??productsWindow;if(productsWindow is { IsVisible:true }){if(productsWindow.WindowState==WindowState.Minimized)productsWindow.WindowState=WindowState.Normal;productsWindow.Activate();return;}productsWindow=new ProductsWindow(store){Owner=this};productsWindow.Closed+=(_,_)=>productsWindow=null;productsWindow.Show();}
    void OpenCalculator(){if(store!=null)ProductionCalculatorLauncher.Open(store,this,(quantity,product)=>AddItem(new OrderItem{Quantity=quantity,Product=product}),"Usar no lançamento");}
    void AddItem(OrderItem? source=null)
    {
        var snapshot=source==null?new OrderItem():JsonSerializer.Deserialize<OrderItem>(JsonSerializer.Serialize(source))!;
        var grid=ItemGrid();
        var canCorrectItems=editing==null||editingData?.ManualDebts.Any(x=>x.SaleIds.Contains(editing.Id))==true;
        var quantity=new TextBox{Text=snapshot.Quantity.ToString(),IsReadOnly=!canCorrectItems,Margin=new Thickness(0,0,10,0)};var product=new TextBox{Text=snapshot.Product,Margin=new Thickness(0,0,10,0)};var price=new TextBox{Text=snapshot.UnitPrice?.ToString("0.00##########################",Pt)??"",IsReadOnly=!canCorrectItems,Margin=new Thickness(0,0,10,0)};var subtotal=new TextBox{IsReadOnly=!canCorrectItems,TextAlignment=TextAlignment.Right,Margin=new Thickness(0,0,10,0),Background=(Brush)FindResource("AccentSoft"),ToolTip="Informe um total fechado; o unitário será calculado."};var cost=new TextBox{Text=snapshot.TotalCost?.ToString("N2",Pt)??"",IsReadOnly=!canCorrectItems,TextAlignment=TextAlignment.Right,Margin=new Thickness(0,0,10,0),Tag="opcional",ToolTip="Custo interno do item (não aparece para o cliente). Vem do cadastro do produto; pode alterar ou deixar vazio."};var remove=new Button{Content=new UiIcon{IconName="trash",Width=18,Height=18},Style=(Style)FindResource("DangerCompactButton"),Width=44,Height=38,Padding=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,IsEnabled=editing==null,ToolTip="Excluir item"};
        if(editing==null)QuantityInput.ClearOnEntry(quantity);var row=new ItemRow(snapshot,quantity,product,price,subtotal,cost,grid);items.Add(row);grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});UIElement[] fields=[quantity,product,price,subtotal,remove];for(var i=0;i<fields.Length;i++){Grid.SetColumn(fields[i],i);grid.Children.Add(fields[i]);}var hint=new TextBlock{FontSize=11,Foreground=(Brush)FindResource("Muted"),Margin=new Thickness(0,4,0,0)};Grid.SetRow(hint,2);Grid.SetColumnSpan(hint,5);grid.Children.Add(hint);tierBindings[row]=new QuantityTierBinding(quantity,price,subtotal,hint,()=>subtotalDriven.Contains(row),()=>subtotalDriven.Remove(row));tierBindings[row].SetDiscount(priceDiscount,priceLabel);var choices=tierBindings[row].ExactChoices;choices.Margin=new Thickness(0,5,6,0);Grid.SetRow(choices,1);Grid.SetColumnSpan(choices,2);grid.Children.Add(choices);itemPanel.Children.Add(grid);
        if(editing==null){product.TextChanged+=(_,_)=>{snapshot.ProductId=null;tierBindings[row].Clear();};ProductAutocomplete.Attach(product,()=>search,p=>{snapshot.ProductId=p.Id;snapshot.Unit=p.Unit;tierBindings[row].Choose(p);rowProducts[row]=p;manualCost.Remove(row);FillCost(row);UpdatePhoto();},()=>store);}
        grid.IsKeyboardFocusWithinChanged+=(_,e)=>{if(e.NewValue is true&&focusedRow!=row){focusedRow=row;UpdatePhoto();}};
        // Custo do item: preenchido pelo cadastro (quantidade exata, faixa ou custo padrão) até a pessoa digitar outro valor.
        // Não fica na linha: aparece no resumo à direita (Custo · Margem) e é alterado em "Ajustar custo".
        quantity.TextChanged+=(_,_)=>FillCost(row);cost.TextChanged+=(_,_)=>{if(!fillingCost)manualCost.Add(row);Refresh();};
        quantity.TextChanged+=(_,_)=>Refresh();price.TextChanged+=(_,_)=>{if(updating)return;subtotalDriven.Remove(row);Refresh();};subtotal.TextChanged+=(_,_)=>{if(updating)return;subtotalDriven.Add(row);Refresh();};remove.Click+=(_,_)=>{if(items.Count<=1)return;items.Remove(row);itemPanel.Children.Remove(grid);if(focusedRow==row)focusedRow=null;Refresh();};Refresh();
    }
    // Mais vendidos (modelo enxuto aprovado): uma linha com os 6 produtos mais lançados nos últimos 90 dias. Clique ou Alt+1…6:
    // lança o produto (preço por faixa/quantidade e custo do cadastro) e leva o cursor para a Quantidade; se já está no lançamento,
    // vai para a quantidade daquela linha (não duplica). Some quando ainda não há histórico.
    readonly WrapPanel topSellers=CreateTopSellers();
    // Os atalhos chegam depois de abrir: não contam como alteração (usar um deles preenche o item, e isso conta).
    static WrapPanel CreateTopSellers(){var panel=new WrapPanel{Margin=new Thickness(0,0,0,10),Visibility=Visibility.Collapsed};UnsavedChanges.SetIgnore(panel,true);return panel;}
    readonly List<(System.Windows.Controls.Primitives.ToggleButton Chip,Product Product)> topChips=[];
    async void LoadTopSellers()
    {
        if(store==null||topChips.Count>0)return;
        List<RankedProduct> ranked;
        try{ranked=store.IsRemote?await store.QueryRemoteAsync<List<RankedProduct>>("desktop/top-products"):ProductRanking.Top(store.Load());}catch(Exception){return;}
        if(ranked.Count==0)return;
        var label=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,4),ToolTip="Mais vendidos (últimos 90 dias) · clique ou Alt+1 a Alt+6",Background=Brushes.Transparent};
        var bolt=new UiIcon{IconName="zap",Width=14,Height=14,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,5,0)};bolt.SetResourceReference(UiIcon.ForegroundProperty,"Warning");label.Children.Add(bolt);
        var title=new TextBlock{Text="Mais vendidos",FontSize=12.5,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};title.SetResourceReference(TextBlock.ForegroundProperty,"Muted");label.Children.Add(title);
        title.Visibility=Visibility.Collapsed;topSellers.Children.Add(label);
        var cardText=new ProductCardText();
        for(var i=0;i<ranked.Count;i++)
        {
            var product=ranked[i].Product;var price=cardText.Convert(product,typeof(string),"price",Pt) as string;var basis=cardText.Convert(product,typeof(string),"basis",Pt) as string;
            var chip=new System.Windows.Controls.Primitives.ToggleButton{Content=ProductRanking.ShortName(product.Description),Style=(Style)FindResource("Chip"),Margin=new Thickness(0,0,4,4),Padding=new Thickness(7,2,7,2),FontSize=12.5,MinHeight=0,Height=28,Focusable=false,
                ToolTip=$"{product.Description}\n{price} {basis}\nVendido {ranked[i].Count}× nos últimos 90 dias · Alt+{i+1}"};
            chip.Click+=(_,_)=>UseTop(product);topChips.Add((chip,product));topSellers.Children.Add(chip);
        }
        PreviewKeyDown+=(_,e)=>{if(Keyboard.Modifiers!=ModifierKeys.Alt||e.Key!=Key.System)return;var index=e.SystemKey switch{Key.D1 or Key.NumPad1=>0,Key.D2 or Key.NumPad2=>1,Key.D3 or Key.NumPad3=>2,Key.D4 or Key.NumPad4=>3,Key.D5 or Key.NumPad5=>4,Key.D6 or Key.NumPad6=>5,_=>-1};if(index<0||index>=topChips.Count)return;e.Handled=true;UseTop(topChips[index].Product);};
        topSellers.Visibility=Visibility.Visible;RefreshTop();
    }
    void RefreshTop(){foreach(var (chip,product) in topChips){var used=items.Any(r=>r.Source.ProductId==product.Id);chip.IsChecked=used;chip.Content=(used?"✓ ":"")+ProductRanking.ShortName(product.Description);}}
    void UseTop(Product product)
    {
        var row=items.FirstOrDefault(r=>r.Source.ProductId==product.Id);
        if(row==null)
        {
            row=items.FirstOrDefault(r=>string.IsNullOrWhiteSpace(r.Description.Text)&&string.IsNullOrWhiteSpace(r.Price.Text));
            if(row==null){AddItem();row=items.Last();}
            row.Description.Text=product.Description;row.Source.ProductId=product.Id;row.Source.Unit=product.Unit;tierBindings[row].Choose(product);rowProducts[row]=product;manualCost.Remove(row);FillCost(row);
        }
        Refresh();RefreshTop();
        var target=row;Dispatcher.BeginInvoke(()=>{target.Quantity.Focus();target.Quantity.SelectAll();},System.Windows.Threading.DispatcherPriority.Input);
    }
    // Item em foco (lateral): a linha em que a pessoa está; sem foco numa linha, o último item preenchido.
    readonly ItemPhotoPanel itemPhoto=new();ItemRow? focusedRow;Dictionary<Guid,Product>? localProducts;
    IEnumerable<ItemRow> PhotoRows()=>items.Where(x=>!string.IsNullOrWhiteSpace(x.Description.Text));
    void UpdatePhoto()
    {
        var rows=PhotoRows().ToList();
        var entries=rows.Select(row=>{
            var qty=int.TryParse(row.Quantity.Text,out var q)?q:0;decimal.TryParse(row.Price.Text,NumberStyles.Currency,Pt,out var unit);decimal.TryParse(row.Subtotal.Text,NumberStyles.Currency,Pt,out var line);
            var detail=qty>0&&unit>0?$"{qty} × {unit.ToString("C2",Pt)} = {(line>0?line:qty*unit).ToString("C2",Pt)}":qty>0?$"Quantidade {qty}":"";
            if(row.Source.ProductId is not Guid id)return new ItemPhotoPanel.Entry(null,null,row.Description.Text.Trim(),detail);
            var product=rowProducts.TryGetValue(row,out var p)&&p.Id==id?p:search?.ById(id);
            if(product==null&&store?.IsRemote==false){localProducts??=store.Load().Products.ToDictionary(x=>x.Id);localProducts.TryGetValue(id,out product);}
            return new ItemPhotoPanel.Entry(id,product==null?null:product.PhotoPath??"",row.Description.Text.Trim(),detail);
        }).ToList();
        var focus=focusedRow==null?-1:rows.IndexOf(focusedRow);
        itemPhoto.Show(entries,focus<0?entries.Count-1:focus);
    }
    readonly Dictionary<ItemRow,Product> rowProducts=[];readonly HashSet<ItemRow> manualCost=[];bool fillingCost;
    void FillCost(ItemRow row){if(manualCost.Contains(row)||!rowProducts.TryGetValue(row,out var p))return;fillingCost=true;try{row.Cost.Text=int.TryParse(row.Quantity.Text,out var q)&&ProductCostLookup.CostFor(p,q) is decimal c?c.ToString("N2",Pt):"";}finally{fillingCost=false;}}
    static Grid ItemGrid(bool margin=true){var grid=new Grid{Margin=margin?new Thickness(0,8,0,0):new Thickness(0)};foreach(var w in new[]{new GridLength(76),new GridLength(1,GridUnitType.Star),new GridLength(118),new GridLength(118),new GridLength(46)})grid.ColumnDefinitions.Add(new ColumnDefinition{Width=w});return grid;}
    async void NewProduct(){if(store==null)return;try{var p=await ProductCreation.CreateAsync(this,store);if(p==null)return;search=new ProductSearch([p]);var target=items.LastOrDefault(x=>string.IsNullOrWhiteSpace(x.Description.Text));if(target==null){AddItem();target=items[^1];}target.Description.Text=p.Description;target.Source.ProductId=p.Id;target.Source.Unit=p.Unit;tierBindings[target].Choose(p);target.Description.Focus();}catch(Exception ex){AlertWindow.Info(this,"Novo produto",ex.Message);}}
    decimal Total(){decimal total=0;updating=true;try{foreach(var row in items)total+=ItemPricing.Update(row.Quantity,row.Price,row.Subtotal,subtotalDriven.Contains(row),out _);}finally{updating=false;}return total;}
    void Refresh(){if(updating)return;RefreshTop();UpdatePhoto();var total=Total();if(editing==null){financialSummary.Update(total,payments.Amount,payments.Fees);UpdateAside(total,payments.Amount,payments.Fees);}else{decimal.TryParse(received.Text,NumberStyles.Currency,Pt,out var amount);var fee=PaymentPolicy.Fee(amount,PaymentPolicy.Rate(type.SelectedItem?.ToString()??""));var data=editingData;var debt=data?.ManualDebts.FirstOrDefault(x=>x.SaleIds.Contains(editing!.Id));var paid=amount;var fees=fee;if(debt!=null&&data!=null){var all=data.Sessions.SelectMany(x=>x.Sales).Where(x=>debt.SaleIds.Contains(x.Id)).ToList();var chain=data.FinancialCorrections.Where(x=>x.OriginalSaleId==editing.Id).Select(x=>x.AdjustmentSaleId).Append(editing.Id).ToHashSet();var effective=data.FinancialCorrections.Where(x=>x.OriginalSaleId==editing.Id).OrderBy(x=>x.CreatedAt).LastOrDefault()?.CorrectedAmount??editing.Total;paid=debt.Paid(data)-effective+amount;fees=all.Sum(x=>x.EstimatedFee)-all.Where(x=>chain.Contains(x.Id)).Sum(x=>x.EstimatedFee)+fee;}var displayTotal=debt!=null?total:editing!.ItemsTotal??amount;financialSummary.Update(displayTotal,paid,fees);UpdateAside(displayTotal,paid,fees);}}
    bool resolvingItems;
    // Novo lançamento com item fora do cadastro: oferece cadastrar, escolher/editar um existente ou manter como texto.
    async void Complete(bool again)
    {
        if(resolvingItems)return;
        var loose=editing==null&&store!=null?items.Where(x=>x.Source.ProductId==null&&!string.IsNullOrWhiteSpace(x.Description.Text)).ToList():[];
        if(loose.Count>0)
        {
            resolvingItems=true;
            try
            {
                var pending=loose.Select(x=>new UnregisteredItems.Pending(x.Description.Text.Trim(),int.TryParse(x.Quantity.Text,out var q)?q:1,decimal.TryParse(x.Price.Text,NumberStyles.Number,Pt,out var u)?u:null)).ToList();
                var chosen=await UnregisteredItems.ResolveAsync(this,store!,pending);if(chosen==null)return;
                for(var i=0;i<loose.Count;i++)if(chosen[i] is Product p)
                {
                    var row=loose[i];row.Description.Text=p.Description;row.Source.ProductId=p.Id;row.Source.Unit=p.Unit;row.Source.UnitCost=p.Cost;row.Source.CostSource="Cadastro (sugerido)";
                    tierBindings[row].Choose(p);
                }
                Refresh();
            }
            finally{resolvingItems=false;}
        }
        CompleteCore(again);
    }
    void CompleteCore(bool again)
    {
        try
        {
            var result=editing==null?new Sale():JsonSerializer.Deserialize<Sale>(JsonSerializer.Serialize(editing))!;
            if(!PhoneText.IsCompleteOrEmpty(contact.Text)&&PhoneText.Digits(contact.Text)!=PhoneText.Digits(editing?.Contact))throw new FieldException(PhoneText.IncompleteMessage,contact);
            result.CustomerName=string.IsNullOrWhiteSpace(name.Text)?"Não informado":name.Text.Trim();result.Contact=PhoneText.Normalize(contact.Text);result.Notes=notes.Text.Trim();
            result.Items=[];
            foreach(var row in items){var position=items.Count>1?$" do item {items.IndexOf(row)+1}":"";if(!int.TryParse(row.Quantity.Text,out var q)||q<=0)throw new FieldException($"Informe a quantidade{position} (número inteiro maior que zero).",row.Quantity);if(string.IsNullOrWhiteSpace(row.Description.Text))throw new FieldException($"Informe o produto ou a descrição{position}.",row.Description);if(!decimal.TryParse(row.Price.Text,NumberStyles.Currency,Pt,out var price)||price<0)throw new FieldException($"Informe o valor unitário{position}.",row.Price);var item=JsonSerializer.Deserialize<OrderItem>(JsonSerializer.Serialize(row.Source))!;item.Quantity=q;item.Product=row.Description.Text.Trim();item.UnitPrice=price;if(string.IsNullOrWhiteSpace(row.Cost.Text)){item.EnteredTotalCost=null;item.UnitCost=null;item.CostReviewed=false;}else if(decimal.TryParse(row.Cost.Text,NumberStyles.Currency,Pt,out var itemCost)&&itemCost>=0&&decimal.Round(itemCost,2)==itemCost){item.EnteredTotalCost=itemCost;item.UnitCost=null;item.CostReviewed=true;item.CostSource="Lançamento";}else throw new FieldException($"Confira o custo{position} em Ajustar custo, no resumo (valor em reais, ou deixe vazio).",null);result.Items.Add(item);}
            if(editing==null){if(date.SelectedDate is not DateTime day||!TimeSpan.TryParse(time.Text,out var hour)||hour<TimeSpan.Zero||hour>=TimeSpan.FromDays(1))throw new FieldException("Confira data e horário.",date);result.CreatedAt=day.Date.Add(hour);result.Product=string.Join(" + ",result.Items.Select(x=>$"{x.Quantity} {x.Product}"));result.Units=result.Items.Sum(x=>x.Quantity);result.TotalValue=Total();if(result.Total<=0)throw new FieldException("O total deve ser maior que zero.",items.FirstOrDefault()?.Price);result.PendingPayments=payments.Read(result.Total);}
            else{result.Product=string.IsNullOrWhiteSpace(description.Text)?editing.Product:description.Text.Trim();if(!decimal.TryParse(received.Text,NumberStyles.Currency,Pt,out var amount)||(amount<0&&amount!=editing.Total)||decimal.Round(amount,2)!=amount)throw new FieldException("Informe o valor corrigido com até duas casas decimais.",received);result.TotalValue=amount;result.PaymentType=type.SelectedItem?.ToString()??editing.PaymentType;if(editingData?.ManualDebts.Any(x=>x.SaleIds.Contains(editing.Id))==true){result.Units=result.Items.Sum(x=>x.Quantity);result.ItemsTotal=Total();}}
            if(string.IsNullOrWhiteSpace(result.Product))throw new FieldException("Informe a descrição.",editing==null?items.FirstOrDefault()?.Description:description);Result=result;SaveAndNew=again;DialogResult=true;
        }catch(Exception ex){ShowError(ex.Message,(ex as FieldException)?.Field);}
    }
    // Mostra o erro na faixa acima dos botões e destaca o campo, em vez de abrir uma janela de aviso.
    void ShowError(string message,Control? field)
    {
        if(errorBanner==null){AlertWindow.Info(this,"Confira o lançamento",message);return;}
        ClearFieldMark();errorText.Text=message;errorBanner.Visibility=Visibility.Visible;
        if(field==null)return;
        markedField=field;markedBorder=field.ReadLocalValue(Control.BorderBrushProperty);markedBackground=field.ReadLocalValue(Control.BackgroundProperty);
        field.BorderBrush=(Brush)FindResource("Danger");field.Background=(Brush)FindResource("DangerSoft");
        field.BringIntoView();field.Focus();if(field is TextBox box)box.SelectAll();
        if(field is TextBox text){void Changed(object? s,TextChangedEventArgs e){text.TextChanged-=Changed;HideError();}text.TextChanged+=Changed;}
        else if(field is DatePicker picker){void Picked(object? s,SelectionChangedEventArgs e){picker.SelectedDateChanged-=Picked;HideError();}picker.SelectedDateChanged+=Picked;}
    }
    void HideError(){ClearFieldMark();if(errorBanner!=null)errorBanner.Visibility=Visibility.Collapsed;}
    void ClearFieldMark()
    {
        if(markedField==null)return;
        if(markedBorder==DependencyProperty.UnsetValue)markedField.ClearValue(Control.BorderBrushProperty);else markedField.SetValue(Control.BorderBrushProperty,markedBorder);
        if(markedBackground==DependencyProperty.UnsetValue)markedField.ClearValue(Control.BackgroundProperty);else markedField.SetValue(Control.BackgroundProperty,markedBackground);
        markedField=null;
    }
}

