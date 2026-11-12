using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
// UX 2.0 (pacote 3): diálogo violeta com abas Dados básicos · Precificação · Custos, cartão do produto ao vivo e um único Salvar.
// Os 3 modelos de preço continuam (único, por faixa, quantidade exata com unitário equivalente); o seletor só mostra o modelo
// escolhido — as linhas dos outros ficam guardadas (trocar não perde o digitado) e só o modelo escolhido é gravado.
public sealed class ProductEditor : Window
{
    // Fotos (até 3, a primeira é a Principal). Photos = null quando nada mudou (o produto mantém as fotos que tem).
    readonly string photoRoot;readonly DataStore? photoStore;readonly List<PhotoSlot> photos=[];bool photosChanged;readonly WrapPanel photoStrip=new();readonly TextBlock photoHint=new(){TextWrapping=TextWrapping.Wrap};
    public IReadOnlyList<PhotoSlot>? Photos=>photosChanged?photos.ToList():null;
    readonly Product? original; readonly TextBox description=new(){TextWrapping=TextWrapping.Wrap,MinHeight=72,Height=double.NaN,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalContentAlignment=VerticalAlignment.Top,Padding=new Thickness(0,8,0,8)},unit=new(),cost=new(),price=new();readonly ComboBox category=new(){IsEditable=true,ItemsSource=ProductOperations.Categories};readonly CheckBox active=new(){Content="Produto ativo (aparece nas buscas de pedido e lançamento)"};readonly TextBlock preview=new();
    readonly CheckBox useTiers=new(){Content="Usar preço por faixa de quantidade",Visibility=Visibility.Collapsed};readonly StackPanel tiersPanel=new(),exactPanel=new();readonly List<(TextBox From,TextBox Through,TextBox Price,TextBox Cost,Grid Row)> tierRows=[];readonly List<(TextBox Quantity,TextBox Total,TextBlock Unit,TextBox Cost,Grid Row)> exactRows=[];
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR"); public Product? Result{get;private set;}
    readonly TabControl tabs=new();readonly TabItem basicTab=new(){Header="Dados básicos"},priceTab=new(){Header="Precificação"},costTab=new(){Header="Custos"};
    readonly RadioButton modeSingle=new(){Content="Preço único",GroupName="modelo"},modeTiers=new(){Content="Por faixa",GroupName="modelo"},modeExact=new(){Content="Quantidade exata",GroupName="modelo"};
    readonly StackPanel tierArea=new(),exactArea=new();readonly TextBlock modelHint=new();
    readonly TextBlock cardName=new(){FontWeight=FontWeights.SemiBold,FontSize=15,TextWrapping=TextWrapping.Wrap},cardCategory=new(),cardFrom=new(){Text="a partir de",FontSize=12.5,Visibility=Visibility.Collapsed},cardPrice=new(){TextWrapping=TextWrapping.Wrap},cardModel=new(),cardCost=new(),cardProfit=new(),cardMargin=new();readonly Image cardPhoto=new(){Stretch=System.Windows.Media.Stretch.UniformToFill};readonly Border cardPhotoBox=new(){Height=110,CornerRadius=new CornerRadius(12),ClipToBounds=true};readonly StatusTag cardStatus=new();
    readonly TextBlock errorText=new(){TextWrapping=TextWrapping.Wrap,MaxWidth=460,FontWeight=FontWeights.SemiBold};readonly Border errorBanner=new(){CornerRadius=new CornerRadius(8),Padding=new Thickness(12,7,12,7),Visibility=Visibility.Collapsed};
    // Produto novo vindo de um pedido/lançamento: começa com a descrição e o preço digitados.
    public void Prefill(string text,decimal? unitPrice){if(original!=null)return;description.Text=char.ToUpper(text.Trim()[0],Pt)+text.Trim()[1..];if(unitPrice is decimal p)price.Text=p.ToString("0.00",Pt);}
    public ProductEditor(Product? product,string photoRoot="",DataStore? store=null)
    {
        this.photoRoot=photoRoot;photoStore=store;if(product!=null)photos.AddRange(ProductPhotoNames.All(product).Select(x=>new PhotoSlot{Path=x}));original=product;Title=product==null?"Novo produto":"Editar produto";Width=Math.Min(1060,SystemParameters.WorkArea.Width-32);Height=Math.Min(800,SystemParameters.WorkArea.Height-32);MinWidth=820;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
        // ---- Dados básicos ----
        var basic=new StackPanel{Margin=new Thickness(0,4,4,0)};
        basic.Children.Add(DialogKit.LabeledField("Descrição *",description).Panel);
        description.TextChanged+=(_,_)=>{var clean=description.Text.Replace("\r"," ").Replace("\n"," ");if(clean!=description.Text){var caret=description.CaretIndex;description.Text=clean;description.CaretIndex=Math.Min(caret,clean.Length);}RefreshCard();};
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(14)});row.ColumnDefinitions.Add(new ColumnDefinition());
        var categoryCell=new StackPanel();categoryCell.Children.Add(DialogKit.LabeledField("Categoria / grupo",category).Panel);row.Children.Add(categoryCell);
        unit.ToolTip="un, folha, m², cento, milheiro, serviço";unit.Tag="un, folha, m²…";var unitCell=DialogKit.LabeledField("Unidade",unit).Panel;Grid.SetColumn(unitCell,2);row.Children.Add(unitCell);basic.Children.Add(row);
        if(store!=null){var createCategory=new Button{Content="＋ Criar categoria",Style=(Style)FindResource("SmallButton"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,-4,0,12)};createCategory.Click+=async (_,_)=>{var window=new ProductCategoriesWindow(store){Owner=this};window.ShowDialog();var current=category.Text;category.ItemsSource=ProductOperations.AllCategories(await store.BusinessContextAsync("product"));category.Text=window.CreatedCategory??current;};categoryCell.Children.Add(createCategory);}
        if(store!=null)Loaded+=async (_,_)=>{try{var current=category.Text;category.ItemsSource=ProductOperations.AllCategories(await store.BusinessContextAsync("product"));category.Text=current;}catch(Exception ex){AlertWindow.Info(this,"Categorias",ex.Message);}};
        category.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,new TextChangedEventHandler((_,_)=>RefreshCard()));category.SelectionChanged+=(_,_)=>Dispatcher.BeginInvoke(RefreshCard);
        // Fotos (opcional): até 3; a primeira é a Principal (lista, cards e celular). Incluir, colar (Ctrl+V), remover e tornar principal.
        photoHint.Style=(Style)FindResource("Hint");photoHint.Margin=new Thickness(0,10,0,0);
        var photoCard=new StackPanel();photoCard.Children.Add(photoStrip);photoCard.Children.Add(photoHint);
        basic.Children.Add(new SectionCard{Header="Fotos (opcional)",IsOptional=true,Content=photoCard});
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.V&&System.Windows.Input.Keyboard.Modifiers==System.Windows.Input.ModifierKeys.Control&&(Clipboard.ContainsImage()||Clipboard.ContainsFileDropList()||Clipboard.ContainsData("PNG"))){e.Handled=true;tabs.SelectedItem=basicTab;AddPhoto(ProductPhotos.FromClipboard);}};
        Loaded+=async (_,_)=>{for(var i=0;i<photos.Count;i++){var slot=photos[i];if(slot.Path==null||slot.Preview!=null)continue;try{slot.Preview=photoStore!=null&&original!=null?await ProductPhotos.LoadAsync(photoStore,original.Id,i,slot.Path):ProductPhotos.Read(ProductPhotos.Resolve(photoRoot,slot.Path));}catch(Exception){}RenderPhotos();}};
        RenderPhotos();
        active.IsChecked=product?.Active??true;active.Margin=new Thickness(0,4,0,0);active.Style=(Style)FindResource("Switch");active.Click+=(_,_)=>RefreshCard();basic.Children.Add(active);
        basicTab.Content=new ScrollViewer{Content=basic,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        // ---- Precificação ----
        var pricing=new StackPanel{Margin=new Thickness(0,4,4,0)};
        var priceRow=new Grid();priceRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(220)});priceRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});priceRow.ColumnDefinitions.Add(new ColumnDefinition());price.TextAlignment=TextAlignment.Right;
        priceRow.Children.Add(DialogKit.LabeledField("Preço de venda (R$)",price).Panel);var priceHint=DialogKit.Hint("Preço base por unidade. Nos modelos por faixa e por quantidade exata, a tabela abaixo é que vale para o pedido.",0);priceHint.Margin=new Thickness(14,26,0,0);Grid.SetColumn(priceHint,2);priceRow.Children.Add(priceHint);var priceCalc=Calc(cost,price,false,"padrão (aba Custos)");priceCalc.Margin=new Thickness(10,26,0,0);priceCalc.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(priceCalc,1);priceRow.Children.Add(priceCalc);pricing.Children.Add(priceRow);
        var modes=new StackPanel{Orientation=Orientation.Horizontal};foreach(var m in new[]{modeSingle,modeTiers,modeExact}){m.Style=(Style)FindResource("Segment");modes.Children.Add(m);}
        pricing.Children.Add(new TextBlock{Text="Modelo de preço",Style=(Style)FindResource("FieldLabel")});pricing.Children.Add(new Border{Style=(Style)FindResource("SegmentBar"),Child=modes,Margin=new Thickness(0,0,0,6)});
        modelHint.Style=(Style)FindResource("Hint");modelHint.Margin=new Thickness(0,0,0,12);pricing.Children.Add(modelHint);
        // Faixas
        var tierHeader=new Grid();foreach(var width in new[]{100d,100d,130d,130d,104d,50d})tierHeader.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(width)});var tierLabels=new[]{"De","Até","Preço unitário","Custo unitário","",""};for(int i=0;i<tierLabels.Length;i++){var label=new TextBlock{Text=tierLabels[i],Style=(Style)FindResource("FieldLabel")};Grid.SetColumn(label,i);tierHeader.Children.Add(label);}tierArea.Children.Add(tierHeader);tierArea.Children.Add(tiersPanel);
        var addTier=new Button{Content="+ Adicionar faixa",Style=(Style)FindResource("SmallButton"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};addTier.Click+=(_,_)=>AddTier();tierArea.Children.Add(addTier);
        tierArea.Children.Add(DialogKit.Hint("Começa em 1, faixas seguidas sem sobreposição; a última fica sem limite (\"Até\" vazio)."));
        foreach(var tier in product?.QuantityPriceTiers??[])AddTier(tier);
        pricing.Children.Add(tierArea);
        // Quantidade exata
        exactArea.Children.Add(DialogKit.Hint("Cadastre a quantidade e o preço total. O valor unitário equivalente é calculado automaticamente.",0));
        var exactHeader=new Grid{Margin=new Thickness(0,8,0,0)};foreach(var width in new[]{100d,130d,170d,130d,104d,60d})exactHeader.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(width)});var exactLabels=new[]{"Quantidade","Preço total","Unitário equivalente","Custo total","",""};for(var i=0;i<exactLabels.Length;i++){var label=new TextBlock{Text=exactLabels[i],Style=(Style)FindResource("FieldLabel")};Grid.SetColumn(label,i);exactHeader.Children.Add(label);}exactArea.Children.Add(exactHeader);exactArea.Children.Add(exactPanel);
        var addExact=new Button{Content="+ Adicionar preço por quantidade",Style=(Style)FindResource("SmallButton"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};addExact.Click+=(_,_)=>AddExact();exactArea.Children.Add(addExact);
        foreach(var entry in product?.ExactQuantityPrices.OrderBy(x=>x.Quantity)??Enumerable.Empty<ExactQuantityPrice>())
            if(entry.Quantity!=1||entry.TotalPrice!=product?.Price||QuantityPricing.HasExactTable(product!))AddExact(entry);
        pricing.Children.Add(exactArea);pricing.Children.Add(useTiers);
        priceTab.Content=new ScrollViewer{Content=pricing,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        useTiers.IsChecked=product?.UseQuantityPricing??false;
        (useTiers.IsChecked==true?modeTiers:exactRows.Count>0?modeExact:modeSingle).IsChecked=true;
        foreach(var m in new[]{modeSingle,modeTiers,modeExact})m.Checked+=(_,_)=>ApplyModel();ApplyModel();
        // ---- Custos ----
        var costs=new StackPanel{Margin=new Thickness(0,4,4,0)};cost.TextAlignment=TextAlignment.Right;
        var costRow=new Grid();costRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(220)});costRow.ColumnDefinitions.Add(new ColumnDefinition());costRow.Children.Add(DialogKit.LabeledField("Custo padrão (R$)",cost).Panel);
        var costHint=DialogKit.Hint("Custo de uma unidade. Vazio = custo pendente (nunca zero nos relatórios). Nas faixas e quantidades exatas, informe o custo de cada linha na aba Precificação.",0);costHint.Margin=new Thickness(14,26,0,0);Grid.SetColumn(costHint,1);costRow.Children.Add(costHint);costs.Children.Add(costRow);
        preview.FontWeight=FontWeights.SemiBold;costs.Children.Add(new Border{Style=(Style)FindResource("NoteCalm"),Child=preview,Margin=new Thickness(0,4,0,0)});
        costTab.Content=costs;
        tabs.Items.Add(basicTab);tabs.Items.Add(priceTab);tabs.Items.Add(costTab);
        // Sem custo cadastrado: abre direto em Custos (pedido do protótipo); produto novo começa em Dados básicos.
        tabs.SelectedIndex=product!=null&&product.Cost==null&&!product.QuantityPriceTiers.Any(x=>x.UnitCost!=null)&&!product.ExactQuantityPrices.Any(x=>x.TotalCost!=null)?2:0;
        description.Text=product?.Description??"";category.Text=product?.Category??"";unit.Text=product?.Unit??"";cost.Text=product?.Cost?.ToString("N2",Pt)??"";price.Text=product?.Price?.ToString("N2",Pt)??"";
        cost.TextChanged+=(_,_)=>Preview();price.TextChanged+=(_,_)=>Preview();Preview();
        // ---- Cartão do produto (ao vivo) ----
        var card=new StackPanel();cardPhotoBox.SetResourceReference(Border.BackgroundProperty,"KindProdutoSoft");var ph=new Grid();ph.Children.Add(new UiIcon{IconName="package",Width=38,Height=38});((UiIcon)ph.Children[0]).SetResourceReference(UiIcon.ForegroundProperty,"KindProduto");ph.Children.Add(cardPhoto);cardPhotoBox.Child=ph;card.Children.Add(cardPhotoBox);
        cardCategory.Margin=new Thickness(0,10,0,2);cardCategory.SetResourceReference(TextBlock.ForegroundProperty,"Muted");cardCategory.FontSize=12.5;card.Children.Add(cardCategory);card.Children.Add(cardName);
        // "a partir de" em linha própria, acima do preço: no cartão estreito o texto junto com o valor cortava o preço.
        cardFrom.Margin=new Thickness(0,8,0,-4);cardFrom.SetResourceReference(TextBlock.ForegroundProperty,"Muted");card.Children.Add(cardFrom);
        cardPrice.Style=(Style)FindResource("SummaryValue");cardPrice.FontSize=26;cardPrice.Margin=new Thickness(0,6,0,0);card.Children.Add(cardPrice);cardModel.Style=(Style)FindResource("Hint");card.Children.Add(cardModel);
        card.Children.Add(DialogKit.Rule());card.Children.Add(DialogKit.Line("Custo",cardCost));card.Children.Add(DialogKit.Line("Lucro unitário",cardProfit));card.Children.Add(DialogKit.Line("Margem",cardMargin));
        cardStatus.Margin=new Thickness(0,10,0,0);card.Children.Add(cardStatus);
        errorText.SetResourceReference(TextBlock.ForegroundProperty,"Danger");errorBanner.SetResourceReference(Border.BackgroundProperty,"DangerSoft");errorBanner.Child=errorText;
        var footer=new StackPanel{Orientation=Orientation.Horizontal};var cancel=new Button{Content="Cancelar",Style=(Style)FindResource("GhostButton"),Margin=new Thickness(0,0,8,0),ToolTip="Esc"};cancel.Click+=(_,_)=>DialogResult=false;footer.Children.Add(cancel);
        var save=new Button{Content="Salvar produto",Style=(Style)FindResource("DialogPrimaryButton"),ToolTip="Ctrl+Enter ou Ctrl+S"};save.Click+=(_,_)=>Save();footer.Children.Add(save);
        Content=new DialogFrame{Kind="Produto",IconName="package",Title=Title,Subtitle="Dados básicos, preço e custo · Ctrl+Enter salva · Esc fecha",Content=tabs,Aside=DialogKit.Summary(card),AsideWidth=300,Footer=footer,FooterLeft=errorBanner,ScrollBody=false};
        KeyboardFlow.Attach(this,Save);
        UnsavedChanges.Attach(this,Save,Snapshot);
        Loaded+=(_,_)=>{if(tabs.SelectedIndex==0)description.Focus();else cost.Focus();RefreshCard();};
    }
    // Retrato dos campos do produto para "alterações não salvas" (usa o texto da categoria, não o índice da lista,
    // que muda quando as categorias terminam de carregar depois de abrir — falso aviso de alteração).
    string Snapshot()=>string.Join("|",new[]{description.Text,category.Text,unit.Text,cost.Text,price.Text,active.IsChecked.ToString(),modeSingle.IsChecked.ToString(),modeTiers.IsChecked.ToString(),modeExact.IsChecked.ToString()}.Concat(tierRows.SelectMany(x=>new[]{x.From.Text,x.Through.Text,x.Price.Text,x.Cost.Text})).Concat(exactRows.SelectMany(x=>new[]{x.Quantity.Text,x.Total.Text,x.Cost.Text})))+string.Join(",",photos.Select(x=>x.Path??Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(x.Bytes!))));
    void ApplyModel()
    {
        useTiers.IsChecked=modeTiers.IsChecked==true;
        tierArea.Visibility=modeTiers.IsChecked==true?Visibility.Visible:Visibility.Collapsed;exactArea.Visibility=modeExact.IsChecked==true?Visibility.Visible:Visibility.Collapsed;
        modelHint.Text=modeTiers.IsChecked==true?"O preço unitário muda conforme a quantidade (ex.: 1–99, 100–499, 500+).":modeExact.IsChecked==true?"Preço total fechado para quantidades exatas (ex.: 100, 250, 500 unidades).":"Um único preço por unidade.";
        var other=modeSingle.IsChecked==true&&(tierRows.Count>0||exactRows.Count>0)||modeTiers.IsChecked==true&&exactRows.Count>0||modeExact.IsChecked==true&&tierRows.Count>0;
        if(other)modelHint.Text+=" As linhas do outro modelo ficam guardadas aqui, mas só o modelo escolhido é salvo.";
        RefreshCard();
    }
    static bool OptionalMoney(string text,out decimal? value){value=null;if(string.IsNullOrWhiteSpace(text))return true;if(!decimal.TryParse(text,NumberStyles.Currency,Pt,out var amount)||amount<0)return false;value=amount;return true;}
    void Preview(){if(decimal.TryParse(cost.Text,NumberStyles.Currency,Pt,out var c)&&decimal.TryParse(price.Text,NumberStyles.Currency,Pt,out var p))preview.Text=$"Lucro unitário: {p-c:C2} • Margem: "+(p==0?"—":((p-c)/p*100).ToString("N2",Pt)+"%");else preview.Text="Confira custo e preço.";RefreshCard();}
    void RefreshCard()
    {
        if(cardStatus==null)return;
        cardName.Text=string.IsNullOrWhiteSpace(description.Text)?"Descrição do produto":description.Text.Trim();cardName.Opacity=string.IsNullOrWhiteSpace(description.Text)?.5:1;
        cardCategory.Text=string.IsNullOrWhiteSpace(category.Text)?"Sem categoria":category.Text.Trim();
        var hasPrice=decimal.TryParse(price.Text,NumberStyles.Currency,Pt,out var p);var hasCost=decimal.TryParse(cost.Text,NumberStyles.Currency,Pt,out var c);
        // Faixa ou quantidade exata: o custo fica na própria linha (1ª linha com custo), não no custo padrão.
        bool Money(string text,out decimal value)=>decimal.TryParse(text,NumberStyles.Currency,Pt,out value);
        if(modeExact.IsChecked==true&&exactRows.FirstOrDefault(x=>Money(x.Cost.Text,out _)&&Money(x.Total.Text,out _)) is var er&&er.Row!=null){hasCost=Money(er.Cost.Text,out c);hasPrice=Money(er.Total.Text,out p);}
        else if(modeTiers.IsChecked==true&&tierRows.FirstOrDefault(x=>Money(x.Cost.Text,out _)&&Money(x.Price.Text,out _)) is var tr&&tr.Row!=null){hasCost=Money(tr.Cost.Text,out c);hasPrice=Money(tr.Price.Text,out p);}
        decimal? from=null;if(modeExact.IsChecked==true)from=exactRows.Select(x=>int.TryParse(x.Quantity.Text,out var q)&&q>0&&decimal.TryParse(x.Total.Text,NumberStyles.Currency,Pt,out var v)?v/q:(decimal?)null).Where(x=>x!=null).DefaultIfEmpty().Min();else if(modeTiers.IsChecked==true)from=tierRows.Select(x=>decimal.TryParse(x.Price.Text,NumberStyles.Currency,Pt,out var v)?v:(decimal?)null).Where(x=>x!=null).DefaultIfEmpty().Min();
        var suffix=string.IsNullOrWhiteSpace(unit.Text)?"":$" / {unit.Text.Trim()}";cardPrice.Text=from is decimal f?f.ToString("C2",Pt)+suffix:hasPrice?p.ToString("C2",Pt)+suffix:"Sem preço";
        cardFrom.Visibility=from!=null?Visibility.Visible:Visibility.Collapsed;cardPrice.Margin=new Thickness(0,from!=null?0:6,0,0);
        cardModel.Text=modeTiers.IsChecked==true?$"Por faixa · {tierRows.Count} faixa(s)":modeExact.IsChecked==true?$"Quantidade exata · {exactRows.Count} opção(ões)":"Preço único";
        cardCost.Text=hasCost?c.ToString("C2",Pt):"Pendente";cardCost.SetResourceReference(TextBlock.ForegroundProperty,hasCost?"Ink":"Warning");
        cardProfit.Text=hasCost&&hasPrice?(p-c).ToString("C2",Pt):"—";cardMargin.Text=hasCost&&hasPrice&&p!=0?((p-c)/p*100).ToString("N1",Pt)+"%":"—";
        cardProfit.SetResourceReference(TextBlock.ForegroundProperty,hasCost&&hasPrice&&p-c<0?"Danger":"Ink");
        cardPhoto.Source=photos.FirstOrDefault()?.Preview;
        cardStatus.Tone=active.IsChecked==true?"ok":"neutral";cardStatus.Text=active.IsChecked==true?"Ativo":"Inativo";cardStatus.IconName=active.IsChecked==true?"check":"ban";
    }
    // Inclui uma foto (escolhida ou colada); a redução para JPG leve acontece aqui, antes de qualquer envio.
    void AddPhoto(Func<byte[]?> read)
    {
        if(photos.Count>=ProductPhotoNames.Max){AlertWindow.Info(this,"Limite de fotos","Cada produto tem até 3 fotos. Remova uma para incluir outra.");return;}
        try{var bytes=read();if(bytes==null)return;photos.Add(new PhotoSlot{Bytes=bytes,Preview=ProductPhotos.Preview(bytes)});photosChanged=true;RenderPhotos();}
        catch(Exception ex)when(ex is InvalidOperationException or IOException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException){AlertWindow.Info(this,"Confira a foto",ex.Message);}
    }
    void PickPhoto(){var picker=new Microsoft.Win32.OpenFileDialog{Title="Selecionar foto do produto",Filter="Fotos JPG, PNG e WebP|*.jpg;*.jpeg;*.png;*.webp"};if(picker.ShowDialog(this)==true)AddPhoto(()=>ProductPhotos.Optimize(picker.FileName));}
    void RenderPhotos()
    {
        photoStrip.Children.Clear();
        for(var i=0;i<photos.Count;i++)
        {
            var index=i;var slot=photos[i];var cell=new Grid();
            if(slot.Preview!=null)cell.Children.Add(new Image{Source=slot.Preview,Stretch=System.Windows.Media.Stretch.UniformToFill});
            else{var wait=new TextBlock{Text=slot.Path!=null?"Carregando…":"",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};wait.SetResourceReference(TextBlock.ForegroundProperty,"Muted");cell.Children.Add(wait);}
            if(index==0)cell.Children.Add(new StatusTag{Tone="ok",Text="Principal",HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(6)});
            var remove=new Button{Content=new UiIcon{IconName="x",Width=14,Height=14},Style=(Style)FindResource("SmallButton"),Padding=new Thickness(6),MinWidth=0,MinHeight=0,ToolTip="Remover foto",HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(4)};
            remove.Click+=(_,_)=>{photos.RemoveAt(index);photosChanged=true;RenderPhotos();};cell.Children.Add(remove);
            if(index>0){var main=new Button{Content="Tornar principal",Style=(Style)FindResource("SmallButton"),FontSize=11.5,Padding=new Thickness(8,3,8,3),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(4)};main.Click+=(_,_)=>{photos.RemoveAt(index);photos.Insert(0,slot);photosChanged=true;RenderPhotos();};cell.Children.Add(main);}
            var frame=new Border{Child=cell,Width=130,Height=130,CornerRadius=new CornerRadius(10),ClipToBounds=true,Margin=new Thickness(0,0,12,12),Cursor=System.Windows.Input.Cursors.Hand,ToolTip="Clique para ampliar",BorderThickness=new Thickness(index==0?2:0)};
            frame.SetResourceReference(Border.BackgroundProperty,"SurfaceAlt");frame.SetResourceReference(Border.BorderBrushProperty,"Accent");frame.MouseLeftButtonUp+=(_,_)=>ShowPhoto(index);
            photoStrip.Children.Add(frame);
        }
        if(photos.Count<ProductPhotoNames.Max)
        {
            var label=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center};label.Children.Add(new UiIcon{IconName="plus",Width=22,Height=22,Margin=new Thickness(0,0,0,6)});label.Children.Add(new TextBlock{Text="Incluir foto",HorizontalAlignment=HorizontalAlignment.Center});
            var add=new Button{Content=label,Style=(Style)FindResource("SoftButton"),Width=130,Height=130,Margin=new Thickness(0,0,12,12),ToolTip="Escolher arquivo (JPG, PNG ou WebP) — ou copie uma imagem e use Ctrl+V"};add.Click+=(_,_)=>PickPhoto();photoStrip.Children.Add(add);
        }
        photoHint.Text=photos.Count==0?"JPG, PNG ou WebP, ou copie uma imagem e use Ctrl+V. Fotos grandes são reduzidas automaticamente. Produtos sem foto funcionam normalmente."
            :$"{photos.Count} de {ProductPhotoNames.Max}. A Principal aparece na lista e no celular. Ctrl+V cola uma imagem copiada. "+(photosChanged?"As mudanças nas fotos valem ao salvar.":"Clique numa foto para ampliar.");
        RefreshCard();
    }
    // Foto ampliada; setas (← →) passam pelas fotos do produto.
    void ShowPhoto(int start)
    {
        var shown=photos.Where(x=>x.Preview!=null).ToList();if(shown.Count==0)return;var at=Math.Max(0,shown.IndexOf(photos[start]));
        var image=new Image{Stretch=System.Windows.Media.Stretch.Uniform,Margin=new Thickness(20)};var grid=new Grid();grid.Children.Add(image);
        var viewer=new Window{Width=820,Height=680,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=grid,ShowInTaskbar=false};viewer.SetResourceReference(BackgroundProperty,"Paper");
        void Show(int step){at=(at+step+shown.Count)%shown.Count;image.Source=shown[at].Preview;viewer.Title=shown.Count>1?$"Foto do produto · {at+1} de {shown.Count}":"Foto do produto";}
        if(shown.Count>1)foreach(var (icon,step,align) in new[]{("chevron-left",-1,HorizontalAlignment.Left),("chevron-right",1,HorizontalAlignment.Right)})
        {var button=new Button{Content=new UiIcon{IconName=icon,Width=22,Height=22},Style=(Style)FindResource("IconButton"),ToolTip=step<0?"Foto anterior (←)":"Próxima foto (→)",HorizontalAlignment=align,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8)};button.Click+=(_,_)=>Show(step);grid.Children.Add(button);}
        viewer.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Left)Show(-1);else if(e.Key==System.Windows.Input.Key.Right)Show(1);else if(e.Key==System.Windows.Input.Key.Escape)viewer.Close();};
        Show(0);viewer.ShowDialog();
    }
    // "Calc venda": custo da linha × multiplicador das Regras (frete já no custo) + arte só no preço total da quantidade exata.
    // Só sugere: "Usar este preço" preenche o campo, e o valor salvo é sempre o que estiver digitado.
    Button Calc(TextBox costBox,TextBox priceBox,bool artwork,string what)
    {
        var button=new Button{Content="Calc venda",Style=(Style)FindResource("SmallButton"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0),ToolTip="Preço de venda sugerido pelo custo"+(artwork?" (com a arte)":"")};
        button.Click+=(_,_)=>ShowSuggestion(button,costBox,priceBox,artwork,what);return button;
    }
    void ShowSuggestion(Button anchor,TextBox costBox,TextBox priceBox,bool artwork,string what)
    {
        var rules=BusinessRules.Current;var panel=new StackPanel{Width=270};
        var popup=new System.Windows.Controls.Primitives.Popup{PlacementTarget=anchor,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,StaysOpen=false,AllowsTransparency=true,VerticalOffset=4};
        TextBlock Line(string text,double size=12.5,bool bold=false,string brush="Muted"){var t=new TextBlock{Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap};t.SetResourceReference(TextBlock.ForegroundProperty,brush);return t;}
        panel.Children.Add(Line("Venda sugerida",14,true,"Ink"));
        if(!decimal.TryParse(costBox.Text,NumberStyles.Currency,Pt,out var value)||value<=0)panel.Children.Add(Line($"Informe o custo {what} para calcular."));
        else
        {
            var suggested=rules.SuggestedPrice(value,artwork);
            panel.Children.Add(Line($"{value.ToString("N2",Pt)} × {rules.SaleMultiplier.ToString("0.##",Pt)}"+(artwork&&rules.ArtworkFee>0?$" + {rules.ArtworkFee.ToString("N2",Pt)} (arte)":"")));
            panel.Children.Add(Line(suggested.ToString("C2",Pt),24,true,"Accent"));
            if(!artwork&&rules.ArtworkFee>0)panel.Children.Add(Line($"Arte ({rules.ArtworkFee.ToString("C2",Pt)}) cobrada à parte, cheia, no pedido."));
            var use=new Button{Content="Usar este preço",Style=(Style)FindResource("PrimaryButton"),Margin=new Thickness(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Left};
            use.Click+=(_,_)=>{priceBox.Text=suggested.ToString("N2",Pt);popup.IsOpen=false;priceBox.Focus();priceBox.SelectAll();};panel.Children.Add(use);
        }
        var note=Line("Multiplicador e arte: Sistema › Configurações › Regras.",11);note.Margin=new Thickness(0,8,0,0);panel.Children.Add(note);
        popup.Child=new Border{Style=(Style)FindResource("Card"),Padding=new Thickness(14),Child=panel};popup.IsOpen=true;
    }
    void AddTier(QuantityPriceTier? tier=null)
    {
        var row=new Grid{Margin=new Thickness(0,5,0,0)};foreach(var width in new[]{100d,100d,130d,130d,104d,50d})row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(width)});
        var from=new TextBox{Text=tier?.From.ToString()??""};var through=new TextBox{Text=tier?.Through?.ToString()??"",ToolTip="Deixe vazio para sem limite",Tag="sem limite",Margin=new Thickness(7,0,0,0)};var amount=new TextBox{Text=tier?.UnitPrice.ToString("N2",Pt)??"",Margin=new Thickness(7,0,0,0),TextAlignment=TextAlignment.Right};
        var remove=new Button{Style=(Style)FindResource("IconButton"),Tag="trash",ToolTip="Remover esta faixa",Margin=new Thickness(7,0,0,0)};remove.Click+=(_,_)=>{tierRows.RemoveAll(x=>x.Row==row);tiersPanel.Children.Remove(row);RefreshCard();};
        var tierCost=new TextBox{Text=tier?.UnitCost?.ToString("0.00##",Pt)??"",ToolTip="Custo unitário desta faixa (R$)",Margin=new Thickness(7,0,0,0),TextAlignment=TextAlignment.Right};var controls=new UIElement[]{from,through,amount,tierCost,Calc(tierCost,amount,false,"desta faixa"),remove};for(var i=0;i<controls.Length;i++){Grid.SetColumn(controls[i],i);row.Children.Add(controls[i]);}tierRows.Add((from,through,amount,tierCost,row));tiersPanel.Children.Add(row);RefreshCard();
    }
    void AddExact(ExactQuantityPrice? entry=null)
    {
        var row=new Grid{Margin=new Thickness(0,5,0,0),MinHeight=42};foreach(var width in new[]{100d,130d,170d,130d,104d,60d})row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(width)});
        var quantity=new TextBox{Text=entry?.Quantity.ToString()??"",Margin=new Thickness(0,0,8,0)};var total=new TextBox{Text=entry?.TotalPrice.ToString("N2",Pt)??"",Margin=new Thickness(0,0,8,0),TextAlignment=TextAlignment.Right};var unitPrice=new TextBlock{VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold};
        var remove=new Button{Content=new UiIcon{IconName="trash",Width=18,Height=18},Style=(Style)FindResource("DangerCompactButton"),Width=44,Height=42,MinWidth=44,MinHeight=42,Padding=new Thickness(0),Margin=new Thickness(7,0,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,ToolTip="Remover esta faixa de preço"};remove.Click+=(_,_)=>{exactRows.RemoveAll(x=>x.Row==row);exactPanel.Children.Remove(row);RefreshCard();};
        void Update(){if(int.TryParse(quantity.Text,out var q)&&q>0&&decimal.TryParse(total.Text,NumberStyles.Currency,Pt,out var value)&&value>=0)unitPrice.Text=(value/q).ToString("C2",Pt);else unitPrice.Text="—";}
        quantity.TextChanged+=(_,_)=>Update();total.TextChanged+=(_,_)=>Update();var exactCost=new TextBox{Text=entry?.TotalCost?.ToString("0.00##",Pt)??"",ToolTip="Custo total desta quantidade (R$)",Margin=new Thickness(0,0,8,0),TextAlignment=TextAlignment.Right};var controls=new UIElement[]{quantity,total,unitPrice,exactCost,Calc(exactCost,total,true,"desta quantidade"),remove};for(var i=0;i<controls.Length;i++){Grid.SetColumn(controls[i],i);row.Children.Add(controls[i]);}exactRows.Add((quantity,total,unitPrice,exactCost,row));exactPanel.Children.Add(row);Update();RefreshCard();
    }
    void Fail(TabItem tab,string message,Control? field=null)
    {
        foreach(var t in new[]{basicTab,priceTab,costTab})Ui.SetHasError(t,t==tab);tabs.SelectedItem=tab;errorText.Text=message;errorBanner.Visibility=Visibility.Visible;
        if(field!=null){Ui.SetHasError(field,true);Dispatcher.BeginInvoke(()=>{field.Focus();if(field is TextBox b)b.SelectAll();},System.Windows.Threading.DispatcherPriority.Input);void Clear(object? s,TextChangedEventArgs e){if(field is TextBox b2)b2.TextChanged-=Clear;Ui.SetHasError(field,false);}if(field is TextBox box)box.TextChanged+=Clear;}
    }
    void Save()
    {
        errorBanner.Visibility=Visibility.Collapsed;foreach(var t in new[]{basicTab,priceTab,costTab})Ui.SetHasError(t,false);
        if(string.IsNullOrWhiteSpace(description.Text)){Fail(basicTab,"Informe a descrição do produto.",description);return;}
        if(!OptionalMoney(price.Text,out _)){Fail(priceTab,"Informe um preço válido (zero ou maior).",price);return;}
        if(!OptionalMoney(cost.Text,out _)){Fail(costTab,"Informe um custo válido (zero ou maior) ou deixe vazio.",cost);return;}
        // Só o modelo escolhido entra no produto; as linhas do outro modelo, se houver, não são gravadas.
        var exactSource=modeExact.IsChecked==true?exactRows:[];
        try{if(!OptionalMoney(cost.Text,out var c)||!OptionalMoney(price.Text,out var p))throw new InvalidOperationException("Informe descrição, custo e preço válidos.");var exact=new List<ExactQuantityPrice>();foreach(var row in exactSource){if(!int.TryParse(row.Quantity.Text,out var quantity)||quantity<=0||!decimal.TryParse(row.Total.Text,NumberStyles.Currency,Pt,out var total)||total<0)throw new InvalidOperationException("Confira quantidade e preço total de cada preço exato.");if(!OptionalMoney(row.Cost.Text,out var exactCost))throw new InvalidOperationException("Confira o custo total de cada quantidade exata.");exact.Add(new ExactQuantityPrice{Quantity=quantity,TotalPrice=total,TotalCost=exactCost});}if(exact.GroupBy(x=>x.Quantity).Any(x=>x.Count()>1))throw new InvalidOperationException("Não é permitido repetir a mesma quantidade no produto.");if(exact.Count>0)useTiers.IsChecked=false;var tiers=useTiers.IsChecked==true?new List<QuantityPriceTier>():exact.Count>0?[]:original?.QuantityPriceTiers.ToList()??new List<QuantityPriceTier>();if(useTiers.IsChecked==true)foreach(var row in tierRows){if(!int.TryParse(row.From.Text,out var start)||!decimal.TryParse(row.Price.Text,NumberStyles.Currency,Pt,out var amount)||!string.IsNullOrWhiteSpace(row.Through.Text)&&!int.TryParse(row.Through.Text,out _))throw new InvalidOperationException("Confira os valores das faixas.");int? end=string.IsNullOrWhiteSpace(row.Through.Text)?null:int.Parse(row.Through.Text);if(!OptionalMoney(row.Cost.Text,out var tierCost))throw new InvalidOperationException("Confira o custo unitário de cada faixa.");tiers.Add(new QuantityPriceTier{From=start,Through=end,UnitPrice=amount,UnitCost=tierCost});}if(useTiers.IsChecked!=true&&original!=null&&!QuantityPricing.HasExactTable(original)&&original.ExactQuantityPrices.FirstOrDefault(x=>x.Quantity==1&&x.TotalPrice==original.Price) is {} hidden&&exact.All(x=>x.Quantity!=1))exact.Add(new ExactQuantityPrice{Quantity=1,TotalPrice=p??hidden.TotalPrice,TotalCost=hidden.TotalCost});var result=new Product{PhotoPath=original?.PhotoPath??"",ExtraPhotoPaths=original?.ExtraPhotoPaths,Id=original?.Id??Guid.NewGuid(),Description=description.Text.Trim(),Category=category.Text.Trim(),Unit=unit.Text.Trim(),Cost=c,Price=p,Active=active.IsChecked==true,UseQuantityPricing=useTiers.IsChecked==true,QuantityPriceTiers=tiers,ExactQuantityPrices=exact};QuantityPricing.Validate(result);Result=result;DialogResult=true;}catch(Exception ex){Fail(priceTab,ex.Message);}
    }
}
