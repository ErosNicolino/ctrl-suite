using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GraficaCaixa.Wpf;

public static class ProductionCalculatorLauncher
{
    static ProductionCalculatorWindow? current;
    // insert: quando aberta de um pedido/lançamento, "Usar no pedido" envia quantidade e descrição para a tela de origem.
    public static async void Open(DataStore store, Window owner, Action<int,string>? insert=null, string insertLabel="Usar no pedido", Action<decimal,decimal>? measures=null)
    {
        if(Reuse(insert,insertLabel,measures))return;
        // Configurações compartilhadas (banco); sem permissão ou sem conexão, segue com o arquivo antigo do PC ou os padrões.
        var shared="";try{shared=(await store.BusinessContextAsync("budget")).CalculatorSettingsJson;}catch(Exception){}
        if(Reuse(insert,insertLabel,measures))return;
        current=new ProductionCalculatorWindow(store,shared){Owner=owner};current.SetInsert(insert,insertLabel);current.SetMeasures(measures);current.Closed+=(_,_)=>current=null;current.Show();
    }
    static bool Reuse(Action<int,string>? insert,string insertLabel,Action<decimal,decimal>? measures)
    {
        if(current is not { IsLoaded:true })return false;
        current.SetInsert(insert,insertLabel);current.SetMeasures(measures);if(current.WindowState==WindowState.Minimized)current.WindowState=WindowState.Normal;current.Activate();current.Topmost=true;current.Topmost=false;return true;
    }
}

public sealed class ProductionCalculatorWindow : Window
{
    static readonly CultureInfo Br=CultureInfo.GetCultureInfo("pt-BR");
    readonly DataStore store; CalculatorSettings settings; string baseline; bool legacyFile; TextBlock? legacyNotice;
    readonly TabControl pages=new(), modes=new();
    readonly Dictionary<string,TextBox> settingFields=[];
    DataGrid? yieldsGrid; ComboBox? yieldShape; TextBox? yieldMeasure,yieldCount;
    readonly Brush accent=GraficaCaixa.Wpf.ThemeManager.Get("Accent");

    public ProductionCalculatorWindow(DataStore store,string shared="")
    {
        this.store=store;baseline=shared;(settings,legacyFile)=CalculatorSettingsStore.Resolve(shared);Title="Calculadora de produção";Width=980;Height=700;MinWidth=760;MinHeight=580;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=TryFindResource("Paper") as Brush??GraficaCaixa.Wpf.ThemeManager.Get("SurfaceAlt");
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};DockPanel.SetDock(footer,Dock.Bottom);
        copyButton=ActionButton("Copiar resultado",CopyResult);copyButton.IsEnabled=false;copyButton.ToolTip="Copia o último resultado (Ctrl+C fora dos campos)";footer.Children.Add(copyButton);
        useButton=ActionButton("Usar no pedido",UseResult);useButton.IsEnabled=false;useButton.Visibility=Visibility.Collapsed;useButton.ToolTip="Adiciona um item com a quantidade e a descrição do cálculo na tela de origem";footer.Children.Add(useButton);
        // Adesivo m²: largura utilizada e comprimento vão para Largura/Altura do Orçamento (arredondados para cima no centímetro).
        measuresButton=ActionButton("Usar medidas no orçamento",UseMeasures);measuresButton.IsEnabled=false;measuresButton.Visibility=Visibility.Collapsed;measuresButton.ToolTip="Leva a largura utilizada e o comprimento do adesivo para os campos do Orçamento";footer.Children.Add(measuresButton);
        footer.Children.Add(ActionButton("Criar pedido",CreateOrder,true));root.Children.Add(footer);
        historyList.SetResourceReference(Control.ForegroundProperty,"Ink");historyList.MouseDoubleClick+=(_,_)=>{if(historyList.SelectedItem is string line){Clipboard.SetText(line);Toast.Show(this,"Cálculo copiado.",tone:"ok");}};
        var historyBox=new Expander{Header="Últimos cálculos",Content=historyList,Margin=new Thickness(0,10,0,0),ToolTip="Duplo clique copia a linha"};DockPanel.SetDock(historyBox,Dock.Bottom);root.Children.Add(historyBox);historyHeader=historyBox;RefreshHistory();
        Loaded+=(_,_)=>Motion.Fade(root,180);
        // Fechamento suave (150 ms, só opacidade); sem movimento fecha na hora.
        Closing+=(_,e)=>{if(closingAnimated||!ThemeManager.MotionEnabled)return;e.Cancel=true;closingAnimated=true;var fade=new System.Windows.Media.Animation.DoubleAnimation(1,0,TimeSpan.FromMilliseconds(150)){EasingFunction=new System.Windows.Media.Animation.CubicEase{EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut}};fade.Completed+=(_,_)=>Close();root.BeginAnimation(OpacityProperty,fade);};
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.C&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control&&e.OriginalSource is not TextBox&&copyButton.IsEnabled){e.Handled=true;CopyResult();}};
        pages.SelectionChanged+=(s,e)=>{if(e.Source==pages)footer.Visibility=pages.SelectedIndex==1?Visibility.Collapsed:Visibility.Visible;};
        modes.SelectionChanged+=(s,e)=>{if(e.Source==modes)Dispatcher.BeginInvoke(FocusFirstField,System.Windows.Threading.DispatcherPriority.Input);};Loaded+=(_,_)=>FocusFirstField();
        // Enter calcula no modo aberto (sem modificadores, a partir de um campo da aba Calcular).
        PreviewKeyDown+=(_,e)=>{if(e.Key!=System.Windows.Input.Key.Enter||System.Windows.Input.Keyboard.Modifiers!=System.Windows.Input.ModifierKeys.None||pages.SelectedIndex!=0||e.OriginalSource is not (TextBox or ComboBox))return;var calc=FindAll<Button>((modes.SelectedItem as TabItem)?.Content as DependencyObject).FirstOrDefault(x=>Equals(x.Content,"Calcular"));if(calc==null)return;e.Handled=true;calc.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));};
        var header=new Grid{Margin=new Thickness(0,0,0,14)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var heading=new StackPanel();heading.Children.Add(new TextBlock{Text="Calculadora de produção",Style=TryFindResource("Title") as Style});heading.Children.Add(new TextBlock{Text="Informe as medidas e veja o aproveitamento do material.",Foreground=Muted(),Margin=new Thickness(0,5,0,0)});header.Children.Add(heading);root.Children.Add(header);DockPanel.SetDock(header,Dock.Top);
        pages.Items.Add(new TabItem{Header="Calcular",Content=BuildCalculator()});pages.Items.Add(new TabItem{Header="Configurações",Content=BuildSettings()});root.Children.Add(pages);
    }

    UIElement BuildCalculator()
    {
        modes.Items.Add(new TabItem{Header="Folha A4",Content=BuildA4()});
        modes.Items.Add(new TabItem{Header="Adesivo m²",Content=BuildAdhesive()});
        modes.Items.Add(new TabItem{Header="Área fixa",Content=BuildFixed()});modes.SelectedIndex=0;return modes;
    }

    UIElement BuildA4()
    {
        var shape=ShapeBox();TextBox width=Input("5"),height=Input("5"),quantity=Input("100");var heightHost=new StackPanel();var results=ResultPanel();
        var input=InputCard("Folha A4",shape,width,height,heightHost,quantity,"Quantidade desejada");
        ShapeBehavior(shape,width,heightHost,"Lado (cm)","Diâmetro (cm)");
        input.Children.Add(ActionButton("Calcular",()=>Run(results,()=>
        {
            var kind=Shape(shape);var w=CalculatorEngine.Measure(width.Text);var h=kind==ProductionShape.Rectangle?CalculatorEngine.Measure(height.Text):w;var q=CalculatorEngine.Quantity(quantity.Text);
            var r=CalculatorEngine.A4(settings,kind,w,h,q);
            lastUse=(Convert.ToInt32(q),$"{ShapeName(kind)} {Size(w,h,kind)} – {r.Sheets:N0} folha(s) A4");Show(results,r.Validated?"Validado em produção":"Calculado pela regra A4",$"{ShapeName(kind)} · {Size(w,h,kind)} · {q:N0} un.",new[]{("Folhas necessárias",$"{r.Sheets:N0}"),("Capacidade por A4",$"{r.PerSheet:N0} un."),("Capacidade total",$"{r.Capacity:N0} un."),("Sobra técnica",$"{r.Waste:N0} un.")},
                r.Validated?"Aproveitamento validado tem prioridade absoluta sobre o cálculo genérico.":$"Orientação: {r.Orientation}\nMontagem: {r.Columns} colunas × {r.Rows} linhas\nÁrea ocupada: {r.UsedWidth:N2} × {r.UsedHeight:N2} cm\nÁrea útil: {settings.A4.Width-settings.A4.Left-settings.A4.Right:N2} × {settings.A4.Height-settings.A4.Top-settings.A4.Bottom:N2} cm");
        }),true));return TwoColumns(input,results);
    }

    UIElement BuildAdhesive()
    {
        var operation=new ComboBox{ItemsSource=new[]{"Material necessário","Quantos cabem na área?"},SelectedIndex=0};
        var shape=ShapeBox();TextBox width=Input("14,5"),height=Input("15,5"),quantity=Input("200"),maxWidth=Input(F(settings.Adhesive.Width));
        var heightHost=new StackPanel();var inverse=new StackPanel{Visibility=Visibility.Collapsed};var preset=new ComboBox{ItemsSource=new[]{"100 × 100 cm","Usar largura padrão da bobina","Personalizado"},SelectedIndex=0};
        TextBox area=Input("1"),areaWidth=Input("100"),areaHeight=Input("100");inverse.Children.Add(Field("Formato da área disponível",preset));inverse.Children.Add(Field("Área disponível (m²)",area));inverse.Children.Add(Field("Largura personalizada (cm)",areaWidth));inverse.Children.Add(Field("Altura personalizada (cm)",areaHeight));
        var results=ResultPanel();var input=InputCard("Adesivo m²",shape,width,height,heightHost,quantity,"Quantidade desejada");input.Children.Insert(1,Field("Cálculo",operation));input.Children.Add(Field("Largura máxima neste cálculo (cm)",maxWidth));input.Children.Add(inverse);
        ShapeBehavior(shape,width,heightHost,"Lado (cm)","Diâmetro (cm)");
        void UpdateOperation(){var inv=operation.SelectedIndex==1;inverse.Visibility=inv?Visibility.Visible:Visibility.Collapsed;if(quantity.Parent is UIElement qField)qField.Visibility=inv?Visibility.Collapsed:Visibility.Visible;if(maxWidth.Parent is UIElement widthField)widthField.Visibility=inv?Visibility.Collapsed:Visibility.Visible;}
        void UpdatePreset(){areaWidth.IsEnabled=areaHeight.IsEnabled=preset.SelectedIndex==2;area.IsEnabled=preset.SelectedIndex!=2;}
        operation.SelectionChanged+=(_,_)=>UpdateOperation();preset.SelectionChanged+=(_,_)=>UpdatePreset();UpdateOperation();UpdatePreset();
        input.Children.Add(ActionButton("Calcular",()=>Run(results,()=>
        {
            var kind=Shape(shape);var w=CalculatorEngine.Measure(width.Text);var h=kind==ProductionShape.Rectangle?CalculatorEngine.Measure(height.Text):w;
            if(operation.SelectedIndex==0)
            {
                var q=CalculatorEngine.Quantity(quantity.Text);var r=CalculatorEngine.Adhesive(settings,kind,w,h,q,CalculatorEngine.Measure(maxWidth.Text));
                lastUse=(Convert.ToInt32(q),$"Adesivo {ShapeName(kind).ToLowerInvariant()} {Size(w,h,kind)} – {r.AreaSquareMeters:N2} m²");lastMeasures=(r.UsedWidth,r.UsedHeight);Show(results,"Material necessário",$"{ShapeName(kind)} · {Size(w,h,kind)} · {q:N0} un.",new[]{("Área total",$"{r.AreaSquareMeters:N4} m²"),("Comprimento",$"{r.UsedHeight:N2} cm\n{r.UsedHeight/100m:N3} m"),("Largura utilizada",$"{r.UsedWidth:N2} cm"),("Itens por linha",$"{r.Columns:N0}")},$"Linhas: {r.Rows}\nPosições vazias: {r.EmptyPositions}\nQuantidade: {q}\nOrientação: {r.Orientation}");
            }
            else
            {
                decimal physicalWidth,physicalHeight;
                if(preset.SelectedIndex==0){physicalWidth=physicalHeight=100m;}
                else if(preset.SelectedIndex==1){physicalWidth=settings.Adhesive.Width;physicalHeight=CalculatorEngine.Measure(area.Text)*10000m/physicalWidth;}
                else {physicalWidth=CalculatorEngine.Measure(areaWidth.Text);physicalHeight=CalculatorEngine.Measure(areaHeight.Text);}
                var r=CalculatorEngine.Fixed(new CalculatorSettings{FixedArea=settings.Adhesive},kind,w,h,physicalWidth,physicalHeight);
                lastUse=(Convert.ToInt32(r.Capacity),$"Adesivo {ShapeName(kind).ToLowerInvariant()} {Size(w,h,kind)} – área {physicalWidth:N0} × {physicalHeight:N0} cm");Show(results,"Quantos cabem na área?",$"Área física: {physicalWidth:N2} × {physicalHeight:N2} cm",new[]{("Quantidade máxima",$"{r.Capacity:N0} un."),("Colunas",$"{r.Columns:N0}"),("Linhas",$"{r.Rows:N0}"),("Orientação",r.Orientation)},$"Item: {Size(w,h,kind)}\nÁrea informada: {physicalWidth*physicalHeight/10000m:N4} m²\nMargens e espaçamentos aplicados normalmente.");
            }
        }),true));return TwoColumns(input,results);
    }

    UIElement BuildFixed()
    {
        var shape=ShapeBox();TextBox width=Input("5"),height=Input("5"),areaWidth=Input("100"),areaHeight=Input("100");var heightHost=new StackPanel();var results=ResultPanel();
        var input=InputCard("Área fixa",shape,width,height,heightHost,null,null);ShapeBehavior(shape,width,heightHost,"Lado (cm)","Diâmetro (cm)");input.Children.Add(Field("Largura disponível (cm)",areaWidth));input.Children.Add(Field("Altura disponível (cm)",areaHeight));
        input.Children.Add(ActionButton("Calcular",()=>Run(results,()=>
        {
            var kind=Shape(shape);var w=CalculatorEngine.Measure(width.Text);var h=kind==ProductionShape.Rectangle?CalculatorEngine.Measure(height.Text):w;var aw=CalculatorEngine.Measure(areaWidth.Text);var ah=CalculatorEngine.Measure(areaHeight.Text);var r=CalculatorEngine.Fixed(settings,kind,w,h,aw,ah);
            lastUse=(Convert.ToInt32(r.Capacity),$"{ShapeName(kind)} {Size(w,h,kind)} – área {aw:N0} × {ah:N0} cm");Show(results,"Aproveitamento da área",$"{ShapeName(kind)} · {Size(w,h,kind)}",new[]{("Quantidade máxima",$"{r.Capacity:N0} un."),("Colunas",$"{r.Columns:N0}"),("Linhas",$"{r.Rows:N0}"),("Orientação",r.Orientation)},$"Dimensão física: {aw:N2} × {ah:N2} cm\nÁrea informada: {aw*ah/10000m:N4} m²");
        }),true));return TwoColumns(input,results);
    }

    UIElement BuildSettings()
    {
        var panel=new StackPanel{Margin=new Thickness(0,14,0,0)};
        legacyNotice=new TextBlock{Text="Estas configurações estão só neste computador. Clique em \"Salvar configurações\" para valerem em todos os computadores e no celular.",TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12),Visibility=legacyFile?Visibility.Visible:Visibility.Collapsed};legacyNotice.SetResourceReference(TextBlock.ForegroundProperty,"Warning");panel.Children.Add(legacyNotice);
        panel.Children.Add(SettingsGroup("Folha A4","a4",settings.A4,true));
        panel.Children.Add(ValidatedSection());
        panel.Children.Add(SettingsGroup("Adesivo m²","adh",settings.Adhesive,false));
        panel.Children.Add(SettingsGroup("Área fixa","fix",settings.FixedArea,false));
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,10)};
        actions.Children.Add(ActionButton("Restaurar padrões",RestoreDefaults));actions.Children.Add(ActionButton("Salvar configurações",SaveSettings,true));panel.Children.Add(actions);
        return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    Border SettingsGroup(string title,string key,SheetSettings source,bool dimensions)
    {
        var panel=new StackPanel();panel.Children.Add(Section(title));var grid=new UniformGrid{Columns=4};
        void Add(string suffix,string label,decimal value){var box=Input(F(value));settingFields[key+suffix]=box;grid.Children.Add(Field(label,box));}
        if(key!="fix")Add("w",dimensions?"Largura padrão (cm)":"Largura máxima padrão (cm)",source.Width);if(dimensions)Add("h","Altura padrão (cm)",source.Height);
        Add("l","Margem esquerda (cm)",source.Left);Add("r","Margem direita (cm)",source.Right);Add("t","Margem superior (cm)",source.Top);Add("b","Margem inferior (cm)",source.Bottom);Add("hg","Espaçamento horizontal (cm)",source.HorizontalGap);Add("vg","Espaçamento vertical (cm)",source.VerticalGap);panel.Children.Add(grid);return Card(panel);
    }

    Border ValidatedSection()
    {
        var panel=new StackPanel();panel.Children.Add(Section("Aproveitamentos validados"));panel.Children.Add(new TextBlock{Text="Esses valores têm prioridade absoluta sobre a regra genérica A4.",Foreground=Muted(),Margin=new Thickness(0,0,0,10)});
        yieldsGrid=new DataGrid{AutoGenerateColumns=false,IsReadOnly=true,Height=245,ItemsSource=settings.ValidatedYields.OrderBy(x=>x.Shape).ThenBy(x=>x.Measure).ToList()};yieldsGrid.SetBinding(FrameworkElement.WidthProperty,new System.Windows.Data.Binding(nameof(FrameworkElement.ActualWidth)){Source=panel});
        yieldsGrid.Columns.Add(new DataGridTextColumn{Header="Formato",Binding=new System.Windows.Data.Binding("Format"),Width=180});yieldsGrid.Columns.Add(new DataGridTextColumn{Header="Medida",Binding=new System.Windows.Data.Binding("Measurement"),Width=150});yieldsGrid.Columns.Add(new DataGridTextColumn{Header="Quantidade por A4",Binding=new System.Windows.Data.Binding("PerSheet"),Width=190});yieldsGrid.SizeChanged+=(_,_)=>yieldsGrid.Columns[0].Width=new DataGridLength(Math.Max(180,yieldsGrid.ActualWidth-340));panel.Children.Add(yieldsGrid);
        var edit=new Grid{Margin=new Thickness(0,10,0,0)};for(int i=0;i<5;i++)edit.ColumnDefinitions.Add(new ColumnDefinition{Width=i==4?GridLength.Auto:new GridLength(1,GridUnitType.Star)});
        yieldShape=ShapeBox(false);yieldShape.ItemsSource=new[]{"Quadrado","Redondo"};yieldShape.SelectedIndex=0;yieldMeasure=Input("5");yieldCount=Input("15");
        var controls=new UIElement[]{Field("Formato",yieldShape),Field("Medida (cm)",yieldMeasure),Field("Quantidade por A4",yieldCount),ActionButton("Adicionar / alterar",SaveYield),ActionButton("Excluir",DeleteYield)};
        for(int i=0;i<controls.Length;i++){var item=controls[i];if(item is Button b)b.Margin=new Thickness(8,22,0,0);else if(item is FrameworkElement f)f.Margin=new Thickness(0,0,8,0);Grid.SetColumn(item,i);edit.Children.Add(item);}panel.Children.Add(edit);
        yieldsGrid.SelectionChanged+=(_,_)=>{if(yieldsGrid.SelectedItem is not ValidatedYield y)return;yieldShape.SelectedIndex=y.Shape==ProductionShape.Round?1:0;yieldMeasure.Text=F(y.Measure);yieldCount.Text=y.PerSheet.ToString();};return Card(panel);
    }

    void SaveYield()
    {
        try
        {
            var shape=yieldShape!.SelectedIndex==1?ProductionShape.Round:ProductionShape.Square;var measure=CalculatorEngine.Measure(yieldMeasure!.Text);var count=CalculatorEngine.Quantity(yieldCount!.Text);var old=settings.ValidatedYields.FirstOrDefault(x=>x.Shape==shape&&x.Measure==measure);
            if(old!=null&&!AlertWindow.Confirm(this,"Alterar aproveitamento validado",$"Confirmar alteração de {old.Format} {old.Measurement}: {old.PerSheet} para {count} por A4?"))return;
            if(old!=null)settings.ValidatedYields.Remove(old);settings.ValidatedYields.Add(new(shape,measure,count));RefreshYields();
        }
        catch(Exception ex){AlertWindow.Info(this,"Aproveitamento inválido",ex.Message);}
    }
    void DeleteYield()
    {
        if(yieldsGrid?.SelectedItem is not ValidatedYield y)return;if(!AlertWindow.Confirm(this,"Excluir aproveitamento validado",$"Excluir {y.Format} {y.Measurement} = {y.PerSheet} por A4?"))return;settings.ValidatedYields.Remove(y);RefreshYields();
    }
    void RefreshYields(){if(yieldsGrid!=null)yieldsGrid.ItemsSource=settings.ValidatedYields.OrderBy(x=>x.Shape).ThenBy(x=>x.Measure).ToList();}

    void SaveSettings()
    {
        try
        {
            settings.A4=ReadSettings("a4",true);settings.Adhesive=ReadSettings("adh",false);settings.FixedArea=ReadSettings("fix",false);Persist("Configurações salvas para todos os computadores e o celular.");
        }
        catch(Exception ex){AlertWindow.Info(this,"Confira as configurações",ex.Message);}
    }
    // Grava no banco (comando validado no servidor, com controle de versão), valendo para todos os PCs e o celular.
    async void Persist(string message)
    {
        try
        {
            var json=CalculatorSettingsStore.ToJson(settings);
            await BusinessUi.ExecuteAsync(store,"calculator.save",Guid.Empty,baseline,new CalculatorArgs(json),()=>store.Update(d=>{d.CalculatorSettingsJson=json;d.AuditTrail.Add(new AuditEntry{Action="Calculadora configurada",Description="Margens, espaçamentos e aproveitamentos validados atualizados"});return 0;}));
            baseline=json;legacyFile=false;if(legacyNotice!=null)legacyNotice.Visibility=Visibility.Collapsed;Toast.Show(this,message,tone:"ok");
        }
        catch(Exception ex){AlertWindow.Info(this,"Não foi possível salvar",ex.Message);}
    }
    void RestoreDefaults()
    {
        if(!AlertWindow.Confirm(this,"Restaurar padrões","Restaurar margens, dimensões e espaçamentos oficiais? A tabela de aproveitamentos validados será preservada."))return;
        var yields=settings.ValidatedYields;settings=CalculatorSettings.Defaults();settings.ValidatedYields=yields;FillSettings("a4",settings.A4,true);FillSettings("adh",settings.Adhesive,false);FillSettings("fix",settings.FixedArea,false);Persist("Padrões restaurados. Os aproveitamentos validados foram preservados.");
    }
    SheetSettings ReadSettings(string key,bool dimensions)=>new(){Width=key=="fix"?100m:V(key+"w"),Height=dimensions?V(key+"h"):key=="fix"?100m:0,Left=V(key+"l"),Right=V(key+"r"),Top=V(key+"t"),Bottom=V(key+"b"),HorizontalGap=V(key+"hg"),VerticalGap=V(key+"vg")};
    void FillSettings(string key,SheetSettings x,bool dimensions){if(key!="fix")settingFields[key+"w"].Text=F(x.Width);if(dimensions)settingFields[key+"h"].Text=F(x.Height);settingFields[key+"l"].Text=F(x.Left);settingFields[key+"r"].Text=F(x.Right);settingFields[key+"t"].Text=F(x.Top);settingFields[key+"b"].Text=F(x.Bottom);settingFields[key+"hg"].Text=F(x.HorizontalGap);settingFields[key+"vg"].Text=F(x.VerticalGap);}
    decimal V(string key)
    {
        if(!decimal.TryParse(settingFields[key].Text,NumberStyles.Number,Br,out var value)||value<0)throw new InvalidOperationException("Informe valores numéricos válidos; margens e espaçamentos podem ser zero.");return value;
    }

    async void CreateOrder()
    {
        var dialog=new ScheduledOrderDialog(store){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        try{await BusinessUi.SaveOrderAsync(store,dialog.Result!,dialog.CorrectionSaleId,dialog.CorrectedAmount,dialog.CorrectionReason);}
        catch(Exception ex){AlertWindow.Info(this,"Não foi possível criar o pedido",ex.Message);}
    }
    void FocusFirstField(){var field=FindAll<TextBox>((modes.SelectedItem as TabItem)?.Content as DependencyObject).FirstOrDefault(x=>x.IsVisible&&x.IsEnabled);if(field!=null){field.Focus();field.SelectAll();}}
    static IEnumerable<T> FindAll<T>(DependencyObject? root) where T:DependencyObject
    {
        if(root==null)yield break;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()){if(child is T match)yield return match;foreach(var nested in FindAll<T>(child))yield return nested;}
    }
    static ComboBox ShapeBox(bool rectangle=true)=>new(){ItemsSource=rectangle?new[]{"Quadrado","Retangular","Redondo"}:new[]{"Quadrado","Redondo"},SelectedIndex=0,Height=34};
    static TextBox Input(string text)=>new(){Text=text,MinWidth=110,Height=34};
    static string F(decimal value)=>value.ToString("0.##",Br);
    static string ShapeName(ProductionShape shape)=>shape switch{ProductionShape.Square=>"Quadrado",ProductionShape.Round=>"Redondo",_=>"Retangular"};
    static string Size(decimal width,decimal height,ProductionShape shape)=>shape==ProductionShape.Rectangle?$"{width:N2} × {height:N2} cm":$"{width:N2} cm";
    static ProductionShape Shape(ComboBox box)=>box.SelectedItem?.ToString() switch{"Redondo"=>ProductionShape.Round,"Retangular"=>ProductionShape.Rectangle,_=>ProductionShape.Square};
    static Brush Muted()=>GraficaCaixa.Wpf.ThemeManager.Get("Muted");

    void ShapeBehavior(ComboBox shape,TextBox width,StackPanel heightHost,string squareLabel,string roundLabel)
    {
        var height=(TextBox?)heightHost.Tag;heightHost.Visibility=Visibility.Collapsed;
        void Update(){var rectangle=shape.SelectedItem?.ToString()=="Retangular";var label=shape.SelectedItem?.ToString()=="Redondo"?roundLabel:rectangle?"Largura (cm)":squareLabel;heightHost.Visibility=rectangle?Visibility.Visible:Visibility.Collapsed;AutomationProperties.SetName(width,label);if(width.Parent is StackPanel field&&field.Children[0] is TextBlock text)text.Text=label;if(height!=null)AutomationProperties.SetName(height,"Altura (cm)");}
        shape.SelectionChanged+=(_,_)=>Update();Update();
    }
    StackPanel InputCard(string title,ComboBox shape,TextBox width,TextBox height,StackPanel heightHost,TextBox? quantity,string? quantityLabel)
    {
        var panel=new StackPanel();panel.Children.Add(Section(title));panel.Children.Add(Field("Formato",shape));panel.Children.Add(Field("Medida",width));heightHost.Tag=height;heightHost.Children.Add(Field("Altura (cm)",height));panel.Children.Add(heightHost);if(quantity!=null)panel.Children.Add(Field(quantityLabel!,quantity));return panel;
    }
    static StackPanel Field(string label,UIElement field)
    {
        var panel=new StackPanel{Margin=new Thickness(4,3,4,3)};panel.Children.Add(new TextBlock{Text=label,Style=Application.Current.TryFindResource("FieldLabel") as Style});panel.Children.Add(field);return panel;
    }
    TextBlock Section(string text)=>new(){Text=text,Style=TryFindResource("Eyebrow") as Style,Margin=new Thickness(6,2,0,8)};
    Border Card(UIElement child)=>new(){Child=child,Style=TryFindResource("Card") as Style,Background=GraficaCaixa.Wpf.ThemeManager.Get("Surface"),Padding=new Thickness(14),CornerRadius=new CornerRadius(14),Margin=new Thickness(0,0,0,10)};
    UIElement TwoColumns(UIElement input,UIElement results)
    {
        var grid=new Grid{Margin=new Thickness(0,12,0,0),HorizontalAlignment=HorizontalAlignment.Stretch};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(.88,GridUnitType.Star)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(16)});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.12,GridUnitType.Star)});var inputCard=Card(new ScrollViewer{Content=input,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});inputCard.Padding=new Thickness(14,14,14,12);grid.Children.Add(inputCard);var right=Card(new ScrollViewer{Content=results,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});right.VerticalAlignment=VerticalAlignment.Stretch;Grid.SetColumn(right,2);grid.Children.Add(right);return grid;
    }
    StackPanel ResultPanel()
    {
        var panel=new StackPanel();panel.Children.Add(Section("Resultado"));panel.Children.Add(new TextBlock{Text="Pronto para calcular",FontSize=19,FontWeight=FontWeights.SemiBold,Foreground=accent,Tag="status"});panel.Children.Add(new TextBlock{Text="Preencha os dados e clique em Calcular.",Foreground=Muted(),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,12),Tag="summary"});panel.Children.Add(new UniformGrid{Columns=2,Tag="metrics"});panel.Children.Add(new Expander{Header="Detalhes do cálculo",IsExpanded=false,Margin=new Thickness(0,8,0,0),Content=new TextBlock{Foreground=Muted(),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8),Tag="details"},Tag="expander"});return panel;
    }
    void Run(StackPanel results,Action action)
    {
        lastMeasures=null;if(measuresButton!=null)measuresButton.IsEnabled=false;
        try{action();if(measuresButton!=null)measuresButton.IsEnabled=measures!=null&&lastMeasures!=null;}catch(Exception ex){var status=results.Children.OfType<TextBlock>().First(x=>Equals(x.Tag,"status"));status.Text="Confira os dados";status.Foreground=GraficaCaixa.Wpf.ThemeManager.Get("Danger");results.Children.OfType<TextBlock>().First(x=>Equals(x.Tag,"summary")).Text=ex is OverflowException?"Os valores informados excedem o limite de cálculo.":ex.Message;}
    }
    // Último resultado (copiar / usar na tela de origem) e histórico das últimas contas (guardado no perfil deste computador).
    (int Quantity,string Product)? lastUse;string lastText="";Action<int,string>? insert;bool closingAnimated;
    (decimal Width,decimal Height)? lastMeasures;Action<decimal,decimal>? measures;
    public void SetMeasures(Action<decimal,decimal>? target){measures=target;measuresButton.Visibility=target==null?Visibility.Collapsed:Visibility.Visible;measuresButton.IsEnabled=target!=null&&lastMeasures!=null;}
    void UseMeasures(){if(measures==null||lastMeasures is not (decimal w,decimal h))return;measures(w,h);Toast.Show(Owner??this,$"Medidas enviadas ao orçamento: {Math.Ceiling(w):N0} × {Math.Ceiling(h):N0} cm",tone:"ok");Owner?.Activate();}
    Button copyButton=null!,useButton=null!,measuresButton=null!;Expander historyHeader=null!;readonly ListBox historyList=new(){MaxHeight=150,BorderThickness=new Thickness(0),Background=Brushes.Transparent};
    static string HistoryFile=>Path.Combine(AppProfile.Folder,"calculadora-historico.json");
    static List<string> LoadHistory(){try{return File.Exists(HistoryFile)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(HistoryFile))??[]:[];}catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){return [];}}
    void RefreshHistory(){var items=LoadHistory();historyList.ItemsSource=items;historyHeader.Header=items.Count==0?"Últimos cálculos (nenhum ainda)":$"Últimos cálculos ({items.Count})";}
    void AddHistory(string line){var items=LoadHistory();items.Insert(0,line);try{Directory.CreateDirectory(AppProfile.Folder);File.WriteAllText(HistoryFile,JsonSerializer.Serialize(items.Take(15).ToList()));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){}RefreshHistory();}
    public void SetInsert(Action<int,string>? target,string label){insert=target;useButton.Content=label;useButton.Visibility=target==null?Visibility.Collapsed:Visibility.Visible;useButton.IsEnabled=target!=null&&lastUse!=null;}
    void CopyResult(){if(lastText.Length==0)return;Clipboard.SetText(lastText);Toast.Show(this,"Resultado copiado.",tone:"ok");}
    void UseResult(){if(insert==null||lastUse is not (int quantity,string product))return;insert(quantity,product);Toast.Show(Owner??this,$"Item adicionado: {quantity} × {product}",tone:"ok");}
    void Show(StackPanel panel,string status,string summary,(string Label,string Value)[] metrics,string details)
    {
        lastText=$"{status}: {summary} → "+string.Join(" · ",metrics.Select(x=>$"{x.Label}: {x.Value.Replace("\n"," / ")}"));
        AddHistory($"{DateTime.Now:dd/MM HH:mm} · {summary} → {metrics[0].Label}: {metrics[0].Value.Replace("\n"," / ")}");
        copyButton.IsEnabled=true;useButton.IsEnabled=insert!=null&&lastUse!=null;
        var statusText=panel.Children.OfType<TextBlock>().First(x=>Equals(x.Tag,"status"));statusText.Text=status;statusText.Foreground=accent;panel.Children.OfType<TextBlock>().First(x=>Equals(x.Tag,"summary")).Text=summary;
        var grid=panel.Children.OfType<UniformGrid>().Single();grid.Children.Clear();for(var i=0;i<metrics.Length;i++){var metric=metrics[i];var stack=new StackPanel();stack.Children.Add(new TextBlock{Text=metric.Label,FontSize=11,Foreground=i==0?accent:Muted(),FontWeight=i==0?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap});stack.Children.Add(new TextBlock{Text=metric.Value,FontSize=i==0?27:20,FontWeight=FontWeights.SemiBold,Foreground=i==0?accent:GraficaCaixa.Wpf.ThemeManager.Get("Ink"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});var card=new Border{Child=stack,Background=i==0?GraficaCaixa.Wpf.ThemeManager.Get("AccentSoft"):GraficaCaixa.Wpf.ThemeManager.Get("SurfaceAlt"),CornerRadius=new CornerRadius(10),Padding=new Thickness(13),Margin=new Thickness(5)};grid.Children.Add(card);}
        var expander=panel.Children.OfType<Expander>().Single();((TextBlock)expander.Content).Text=details;
    }
    Button ActionButton(string text,Action action,bool primary=false)
    {
        var button=new Button{Content=text,Style=TryFindResource(primary?"PrimaryButton":"SoftButton") as Style,Margin=new Thickness(4),MinHeight=36};button.Click+=(_,_)=>action();return button;
    }
}
