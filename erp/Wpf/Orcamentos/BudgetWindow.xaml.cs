using System.Globalization;
namespace GraficaCaixa.Wpf;

public partial class BudgetWindow : System.Windows.Window
{
    readonly GraficaCaixa.DataStore store; decimal recommended,budgetCost;
    // Custo interno só para quem pode alterar custos (a mesma permissão que o servidor exige para gravar custo no pedido).
    static bool CanSeeCost=>Can("costs.edit");
    static bool Can(string key)=>App.CurrentUser.Permissions?.GetValueOrDefault(key)??App.CurrentUser.Role.Equals("Administrador",StringComparison.OrdinalIgnoreCase);
    static MainWindow? Main=>System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
    void Prospects_Click(object s,System.Windows.RoutedEventArgs e)=>Main?.OpenProspects();
    // Botão "Em acompanhamento" mostra quantos orçamentos estão para cobrar hoje (retorno vencido ou de hoje).
    sealed record ProspectPage(List<ProspectItem> Items);sealed record ProspectItem(string Status,string ReturnStatus);
    async void LoadProspectCount()
    {
        if(!Can("view.prospects"))return;
        try
        {
            static bool Open(string status)=>status is not ("Convertido em pedido" or "Não fechou");
            var due=store.IsRemote?(await store.QueryRemoteAsync<ProspectPage>("api/v1/possiveis-pedidos?page=1&pageSize=100")).Items.Count(x=>Open(x.Status)&&x.ReturnStatus is "Atrasado" or "Hoje")
                :store.Load().Prospects.Count(x=>Open(x.Status)&&x.NextContactAt?.Date<=DateTime.Today);
            ProspectsButton.Content=due>0?$"Em acompanhamento · {due} para cobrar":"Em acompanhamento";
        }
        catch{/* Contagem é só um atalho: sem conexão, o botão fica com o texto normal. */}
    }
    // "Guardar para acompanhar": vira um item de Orçamentos em acompanhamento (situação "Orçamento enviado", retorno pelo prazo das Regras).
    async void SaveProspect_Click(object s,System.Windows.RoutedEventArgs e)
    {
        if(recommended<=0||ProductBox.SelectedItem is not GraficaCaixa.BudgetRule rule){AlertWindow.Info(this,"Orçamento incompleto","Preencha o produto e as medidas antes de guardar.");return;}
        var seed=new GraficaCaixa.Prospect{Description=$"{rule.Product} • {WidthBox.Text} × {HeightBox.Text} {(UnitBox.SelectedIndex==1?"cm":"m")}"+(QuantityBox.Text.Trim() is var q&&q!="1"?$" • {q} un.":""),Amount=recommended,Status="Orçamento enviado",NextContactAt=DateTime.Today.AddDays(GraficaCaixa.BusinessRules.Current.FollowUpDays)};
        var dialog=new FollowUpEditor(false,null,seed){Owner=this};if(dialog.ShowDialog()!=true)return;var result=dialog.Prospect!;
        try{await BusinessUi.ExecuteAsync(store,"prospect.save",result.Id,null,new ProspectSaveArgs(result),()=>store.Update(d=>{d.Prospects.Add(result);d.AuditTrail.Add(new AuditEntry{Action="Possível pedido atualizado",Description=dialog.CustomerText});return 0;}));}
        catch(Exception ex){AlertWindow.Info(this,"Não foi possível guardar",ex.Message);return;}
        Toast.Show(this,"Guardado em Orçamentos em acompanhamento.","Ver",()=>Main?.OpenProspects(),"ok");
    }readonly ModuleLoader remoteLoad;
    public BudgetWindow(GraficaCaixa.DataStore store){InitializeComponent();this.store=store;ProspectsButton.Visibility=Can("view.prospects")?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;SaveProspectButton.Visibility=Can("prospects.edit")?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;remoteLoad=new(this,store);Deadlines.Store=store;Loaded+=(_,_)=>{LoadRules();LoadProspectCount();WidthBox.Focus();WidthBox.SelectAll();};
        // Atalhos: Ctrl+Enter cria o pedido, Ctrl+P abre a prévia em PDF, Ctrl+Shift+C copia o resumo.
        PreviewKeyDown+=(_,e)=>{var m=System.Windows.Input.Keyboard.Modifiers;if(m==System.Windows.Input.ModifierKeys.Control&&e.Key==System.Windows.Input.Key.Enter){e.Handled=true;CreateOrder_Click(this,new System.Windows.RoutedEventArgs());}else if(m==System.Windows.Input.ModifierKeys.Control&&e.Key==System.Windows.Input.Key.P){e.Handled=true;PreviewPdf_Click(this,new System.Windows.RoutedEventArgs());}else if(m==(System.Windows.Input.ModifierKeys.Control|System.Windows.Input.ModifierKeys.Shift)&&e.Key==System.Windows.Input.Key.C){e.Handled=true;Copy_Click(this,new System.Windows.RoutedEventArgs());}};}
    async void LoadRules(Guid? selected=null){var data=store.IsRemote?await remoteLoad.LoadAsync("orcamentos/configuracao"):store.Load();if(data==null)return;var rules=data.BudgetRules;ProductBox.ItemsSource=rules;ProductBox.SelectedItem=rules.FirstOrDefault(x=>x.Id==selected)??rules.FirstOrDefault();Calculate();}
    static bool Number(string value,out decimal result)=>decimal.TryParse(value.Trim().Replace(',','.'),NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out result);
    void Calculate(){if(!IsLoaded||ProductBox.SelectedItem is not GraficaCaixa.BudgetRule rule)return;var unit=UnitBox.SelectedIndex==1?"cm":"m";WidthLabel.Text=$"Largura ({unit})";HeightLabel.Text=$"Altura ({unit})";foreach(var box in new[]{WidthBox,HeightBox,QuantityBox})Ui.SetHasError(box,false);if(!Number(WidthBox.Text,out var width)||!Number(HeightBox.Text,out var height)||width<=0||height<=0||width>100000||height>100000){ValidationText.Text="Informe largura e altura maiores que zero.";Ui.SetHasError(!Number(WidthBox.Text,out var badWidth)||badWidth<=0?WidthBox:HeightBox,true);SetEmpty();return;}if(!Number(QuantityBox.Text,out var quantity)||quantity<=0||quantity>int.MaxValue||quantity!=decimal.Truncate(quantity)){ValidationText.Text="Informe uma quantidade inteira maior que zero.";Ui.SetHasError(QuantityBox,true);SetEmpty();return;}if(UnitBox.SelectedIndex==1){width/=100;height/=100;}ValidationText.Text="";var area=width*height*quantity;var bySize=rule.PriceBySize(width,height,quantity);var without=rule.NoArtworkFee+rule.ArtworkFee;var with=rule.MinimumSale(true);recommended=rule.Recommended(width,height,quantity);AreaText.Text=$"{area:N2} m²";SizePriceText.Text=bySize.ToString("C2");WithoutArtworkText.Text=without.ToString("C2");WithArtworkText.Text=with.ToString("C2");RecommendedText.Text=recommended.ToString("C2");ShowCost(rule,width,height,quantity);FormulaText.Text=$"Área total × {rule.SquareMeterRate:C2} + {rule.NoArtworkFee:C2} de retirada + {rule.ArtworkFee:C2} de arte. Os adicionais entram uma única vez. Mínimo: {rule.MinimumCost:C2} × {rule.MarkupMultiplier:N2} + retirada + arte. Valor final arredondado para o real mais perto. Entrega ao cliente não incluída.";}
    void SetEmpty(){recommended=budgetCost=0;AreaText.Text=SizePriceText.Text=WithoutArtworkText.Text=WithArtworkText.Text="—";RecommendedText.Text="—";FormulaText.Text="";CostPanel.Visibility=System.Windows.Visibility.Collapsed;}
    // "Seu custo": custo pelo tamanho × custo mínimo; o que não vale fica riscado. Ganho = valor para orçar − custo.
    void ShowCost(GraficaCaixa.BudgetRule rule,decimal width,decimal height,decimal quantity)
    {
        budgetCost=rule.Cost(width,height,quantity);if(!CanSeeCost){CostPanel.Visibility=System.Windows.Visibility.Collapsed;return;}var pt=CultureInfo.GetCultureInfo("pt-BR");string M(decimal v)=>v.ToString("C2",pt);
        var bySize=Math.Round(rule.CostBySize(width,height,quantity),2,MidpointRounding.AwayFromZero);var minimumWins=rule.MinimumCost>=bySize;
        CostSizeLabel.Text=$"Custo pelo tamanho ({(width*height*quantity).ToString("N2",pt)} m² × {M(rule.SquareMeterRate)} ÷ {rule.MarkupMultiplier.ToString("0.##",pt)})";
        CostSizeText.Text=M(bySize);CostMinimumText.Text=M(rule.MinimumCost);
        CostSizeText.TextDecorations=minimumWins?System.Windows.TextDecorations.Strikethrough:null;CostMinimumText.TextDecorations=minimumWins?null:System.Windows.TextDecorations.Strikethrough;
        foreach(var (text,muted) in new[]{(CostSizeText,minimumWins),(CostMinimumText,!minimumWins)})if(muted)text.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,"Muted");else text.ClearValue(System.Windows.Controls.TextBlock.ForegroundProperty);
        CostRuleText.Text=minimumWins?"  · vale o mínimo":"  · vale o tamanho";CostText.Text=M(budgetCost);
        var gain=recommended-budgetCost;GainHintText.Text=$"  · {M(recommended)} − {M(budgetCost)}";GainText.Text=recommended>0?$"{M(gain)} · {(gain/recommended).ToString("P0",pt)}":M(gain);
        CostPanel.Visibility=System.Windows.Visibility.Visible;
    } void Input_Changed(object s,System.EventArgs e)=>Calculate();
    async void Settings_Click(object s,System.Windows.RoutedEventArgs e){var id=(ProductBox.SelectedItem as GraficaCaixa.BudgetRule)?.Id;var dialog=new BudgetSettingsWindow(store,await store.BusinessContextAsync("budget")){Owner=this};if(dialog.ShowDialog()==true)LoadRules(id);}
    void Calculator_Click(object s,System.Windows.RoutedEventArgs e)=>ProductionCalculatorLauncher.Open(store,this,measures:ApplyMeasures);
    // Medidas vindas da calculadora (adesivo m²), em cm: arredonda para cima no centímetro e preenche na unidade escolhida (m ou cm).
    void ApplyMeasures(decimal widthCm,decimal heightCm){var w=Math.Ceiling(widthCm);var h=Math.Ceiling(heightCm);var pt=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");if(UnitBox.SelectedIndex==1){WidthBox.Text=w.ToString("0",pt);HeightBox.Text=h.ToString("0",pt);}else{WidthBox.Text=(w/100m).ToString("0.00",pt);HeightBox.Text=(h/100m).ToString("0.00",pt);}Activate();Calculate();}
    void Copy_Click(object s,System.Windows.RoutedEventArgs e){if(recommended<=0||ProductBox.SelectedItem is not GraficaCaixa.BudgetRule rule){AlertWindow.Info(this,"Nada para copiar","Preencha as medidas para calcular o orçamento.");return;}Number(WidthBox.Text,out var width);Number(HeightBox.Text,out var height);Number(QuantityBox.Text,out var qty);if(UnitBox.SelectedIndex==0){width*=100;height*=100;}System.Windows.Clipboard.SetText(BudgetSummary.Format(rule.Product,recommended,qty,$"Tamanho: {width:0.##} x {height:0.##} cm"));Toast.Show(this,"Resumo copiado. Cole no WhatsApp do cliente.",tone:"ok");}
    async void CreateOrder_Click(object s,System.Windows.RoutedEventArgs e){if(recommended<=0||ProductBox.SelectedItem is not GraficaCaixa.BudgetRule rule){AlertWindow.Info(this,"Orçamento incompleto","Preencha o produto e as medidas antes de criar o pedido.");return;}var product=$"{rule.Product} • {WidthBox.Text} × {HeightBox.Text} {(UnitBox.SelectedIndex==1?"cm":"m")} • adicional de arte incluído";var dialog=new ScheduledOrderDialog(store,product,recommended,decimal.Parse(QuantityBox.Text.Replace(',','.'),CultureInfo.InvariantCulture),CanSeeCost&&budgetCost>0?budgetCost:null){Owner=this};if(dialog.ShowDialog()!=true)return;try{await BusinessUi.SaveOrderAsync(store,dialog.Result!,dialog.CorrectionSaleId,dialog.CorrectedAmount,dialog.CorrectionReason);AlertWindow.Info(this,"Pedido criado","O orçamento foi transformado em pedido e já aparece em Pedidos e produção.");}catch(Exception ex){AlertWindow.Info(this,"Não foi possível criar o pedido",ex.Message);}}
    void PreviewPdf_Click(object s,System.Windows.RoutedEventArgs e){OrderItem? seed=null;if(recommended>0&&ProductBox.SelectedItem is BudgetRule rule&&Number(QuantityBox.Text,out var qty)&&qty>0)seed=new(){Product=$"{rule.Product} • {WidthBox.Text} × {HeightBox.Text} {(UnitBox.SelectedIndex==1?"cm":"m")} • arte incluída",Quantity=(int)qty,UnitPrice=recommended/qty};new BudgetPreviewWindow(store,seed){Owner=this}.ShowDialog();}
    void Close_Click(object s,System.Windows.RoutedEventArgs e)=>Close();
}
