using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace GraficaCaixa.Wpf;

// Diálogo central (padrão P2/P3 do redesenho): faixa e selo na cor do tipo (Kind), título + subtítulo com atalhos,
// corpo rolável à esquerda, resumo ao vivo à direita (Aside, opcional) e rodapé fixo (Footer). Esc e o "×" passam
// pelo mesmo caminho de hoje (UnsavedChanges.Escape: pergunta se houver alteração não salva).
public class DialogFrame:ContentControl
{
    public static readonly DependencyProperty TitleProperty=DependencyProperty.Register(nameof(Title),typeof(string),typeof(DialogFrame),new PropertyMetadata(""));
    public static readonly DependencyProperty SubtitleProperty=DependencyProperty.Register(nameof(Subtitle),typeof(string),typeof(DialogFrame),new PropertyMetadata(""));
    public static readonly DependencyProperty IconNameProperty=DependencyProperty.Register(nameof(IconName),typeof(string),typeof(DialogFrame),new PropertyMetadata(""));
    public static readonly DependencyProperty KindProperty=DependencyProperty.Register(nameof(Kind),typeof(string),typeof(DialogFrame),new PropertyMetadata("Neutro",(d,e)=>((DialogFrame)d).ApplyKind()));
    public static readonly DependencyProperty AsideProperty=DependencyProperty.Register(nameof(Aside),typeof(object),typeof(DialogFrame),new PropertyMetadata(null,LogicalChanged));
    public static readonly DependencyProperty FooterProperty=DependencyProperty.Register(nameof(Footer),typeof(object),typeof(DialogFrame),new PropertyMetadata(null,LogicalChanged));
    public static readonly DependencyProperty FooterLeftProperty=DependencyProperty.Register(nameof(FooterLeft),typeof(object),typeof(DialogFrame),new PropertyMetadata(null,LogicalChanged));
    public static readonly DependencyProperty HeaderExtraProperty=DependencyProperty.Register(nameof(HeaderExtra),typeof(object),typeof(DialogFrame),new PropertyMetadata(null,LogicalChanged));
    public static readonly DependencyProperty ShowCloseProperty=DependencyProperty.Register(nameof(ShowClose),typeof(bool),typeof(DialogFrame),new PropertyMetadata(true));
    public static readonly DependencyProperty AsideWidthProperty=DependencyProperty.Register(nameof(AsideWidth),typeof(double),typeof(DialogFrame),new PropertyMetadata(330d));
    public static readonly DependencyProperty BandProperty=DependencyProperty.Register(nameof(Band),typeof(Brush),typeof(DialogFrame),new PropertyMetadata(Brushes.Transparent));
    public static readonly DependencyProperty ScrollBodyProperty=DependencyProperty.Register(nameof(ScrollBody),typeof(bool),typeof(DialogFrame),new PropertyMetadata(true));
    public string Title{get=>(string)GetValue(TitleProperty);set=>SetValue(TitleProperty,value);}
    public string Subtitle{get=>(string)GetValue(SubtitleProperty);set=>SetValue(SubtitleProperty,value);}
    public string IconName{get=>(string)GetValue(IconNameProperty);set=>SetValue(IconNameProperty,value);}
    public string Kind{get=>(string)GetValue(KindProperty);set=>SetValue(KindProperty,value);}
    public object? Aside{get=>GetValue(AsideProperty);set=>SetValue(AsideProperty,value);}
    public object? Footer{get=>GetValue(FooterProperty);set=>SetValue(FooterProperty,value);}
    public object? FooterLeft{get=>GetValue(FooterLeftProperty);set=>SetValue(FooterLeftProperty,value);}
    public object? HeaderExtra{get=>GetValue(HeaderExtraProperty);set=>SetValue(HeaderExtraProperty,value);}
    public bool ShowClose{get=>(bool)GetValue(ShowCloseProperty);set=>SetValue(ShowCloseProperty,value);}
    public double AsideWidth{get=>(double)GetValue(AsideWidthProperty);set=>SetValue(AsideWidthProperty,value);}
    public Brush Band{get=>(Brush)GetValue(BandProperty);set=>SetValue(BandProperty,value);}
    public bool ScrollBody{get=>(bool)GetValue(ScrollBodyProperty);set=>SetValue(ScrollBodyProperty,value);}
    // Resumo, rodapé e cabeçalho fazem parte da árvore lógica do diálogo (buscas por controles, UnsavedChanges, testes).
    static void LogicalChanged(DependencyObject d,DependencyPropertyChangedEventArgs e){var f=(DialogFrame)d;if(e.OldValue!=null&&LogicalTreeHelper.GetParent(e.OldValue as DependencyObject??new DependencyObject())==f)f.RemoveLogicalChild(e.OldValue);if(e.NewValue is DependencyObject n&&LogicalTreeHelper.GetParent(n)==null)f.AddLogicalChild(n);}
    protected override System.Collections.IEnumerator LogicalChildren=>Enumerate().GetEnumerator();
    IEnumerable<object> Enumerate(){var b=base.LogicalChildren;while(b!=null&&b.MoveNext())yield return b.Current;foreach(var x in new[]{Aside,Footer,FooterLeft,HeaderExtra})if(x is DependencyObject o&&LogicalTreeHelper.GetParent(o)==this)yield return o;}
    public DialogFrame()
    {
        ApplyKind();Focusable=false;
        Loaded+=(_,_)=>{ThemeManager.Changed+=TintFields;TintFields();if(!animated){animated=true;Motion.Pop(this);}};
        Unloaded+=(_,_)=>ThemeManager.Changed-=TintFields;
    }
    bool animated;
    void ApplyKind(){Ui.UseKind(this,Kind);SetResourceReference(BandProperty,"Kind"+(ThemeManager.Kinds.ContainsKey(Kind)?Kind:"Neutro")+"Band");if(IsLoaded)TintFields();}
    // Dentro do diálogo, o foco dos campos, caixas de seleção e botões principais usa a cor do tipo (protótipo: --k).
    void TintFields()
    {
        var kind=ThemeManager.Kinds.ContainsKey(Kind)?Kind:"Neutro";
        if(kind=="Neutro"){Resources.Remove("Accent");Resources.Remove("AccentSoft");Resources.Remove("AccentInk");Resources.Remove("FocusRing");return;}
        if(Application.Current.TryFindResource("Kind"+kind) is not SolidColorBrush strong||Application.Current.TryFindResource("Kind"+kind+"Soft") is not SolidColorBrush soft)return;
        Resources["Accent"]=strong;Resources["AccentSoft"]=soft;
        // Texto de destaque (aba selecionada, links) também na cor do tipo: produto violeta, cliente azul-petróleo etc.
        Resources["AccentInk"]=Application.Current.TryFindResource("Kind"+kind+"Ink") as Brush??strong;
        var ring=new SolidColorBrush(Color.FromArgb(0x55,strong.Color.R,strong.Color.G,strong.Color.B));ring.Freeze();Resources["FocusRing"]=ring;
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if(GetTemplateChild("PART_Close") is Button close)close.Click+=(_,_)=>{if(Window.GetWindow(this) is Window w)UnsavedChanges.Escape(w);};
    }
}

// Seção numerada do diálogo (círculo com número que vira ✓ quando a seção está completa).
public class SectionCard:HeaderedContentControl
{
    public static readonly DependencyProperty NumberProperty=DependencyProperty.Register(nameof(Number),typeof(string),typeof(SectionCard),new PropertyMetadata(""));
    public static readonly DependencyProperty HintProperty=DependencyProperty.Register(nameof(Hint),typeof(string),typeof(SectionCard),new PropertyMetadata(""));
    public static readonly DependencyProperty IsDoneProperty=DependencyProperty.Register(nameof(IsDone),typeof(bool),typeof(SectionCard),new PropertyMetadata(false));
    public static readonly DependencyProperty IsOptionalProperty=DependencyProperty.Register(nameof(IsOptional),typeof(bool),typeof(SectionCard),new PropertyMetadata(false));
    public static readonly DependencyProperty HeaderToolsProperty=DependencyProperty.Register(nameof(HeaderTools),typeof(object),typeof(SectionCard),new PropertyMetadata(null,(d,e)=>{var c=(SectionCard)d;if(e.OldValue!=null)c.RemoveLogicalChild(e.OldValue);if(e.NewValue is DependencyObject n&&LogicalTreeHelper.GetParent(n)==null)c.AddLogicalChild(n);}));
    // Ferramentas do cabeçalho fazem parte da árvore lógica (buscas, UnsavedChanges, testes).
    protected override System.Collections.IEnumerator LogicalChildren=>Enumerate().GetEnumerator();
    IEnumerable<object> Enumerate(){var b=base.LogicalChildren;while(b!=null&&b.MoveNext())yield return b.Current;if(HeaderTools is DependencyObject t&&LogicalTreeHelper.GetParent(t)==this)yield return t;}
    public string Number{get=>(string)GetValue(NumberProperty);set=>SetValue(NumberProperty,value);}
    public string Hint{get=>(string)GetValue(HintProperty);set=>SetValue(HintProperty,value);}
    public bool IsDone{get=>(bool)GetValue(IsDoneProperty);set=>SetValue(IsDoneProperty,value);}
    public bool IsOptional{get=>(bool)GetValue(IsOptionalProperty);set=>SetValue(IsOptionalProperty,value);}
    public object? HeaderTools{get=>GetValue(HeaderToolsProperty);set=>SetValue(HeaderToolsProperty,value);}
}

// Cartão de indicador (KPI) com faixa de cor no topo (igual ao Início): ok, info, warn, danger, violet (vazio = sem faixa).
public class KpiCard:Control
{
    public static readonly DependencyProperty LabelProperty=DependencyProperty.Register(nameof(Label),typeof(string),typeof(KpiCard),new PropertyMetadata(""));
    public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(string),typeof(KpiCard),new PropertyMetadata("",(d,e)=>((KpiCard)d).ShowValue()));
    public static readonly DependencyProperty HintProperty=DependencyProperty.Register(nameof(Hint),typeof(string),typeof(KpiCard),new PropertyMetadata(""));
    public static readonly DependencyProperty ToneProperty=DependencyProperty.Register(nameof(Tone),typeof(string),typeof(KpiCard),new PropertyMetadata("",(d,e)=>((KpiCard)d).ApplyTone()));
    public static readonly DependencyProperty StripeProperty=DependencyProperty.Register(nameof(Stripe),typeof(Brush),typeof(KpiCard),new PropertyMetadata(Brushes.Transparent));
    public static readonly DependencyProperty IsHeroProperty=DependencyProperty.Register(nameof(IsHero),typeof(bool),typeof(KpiCard),new PropertyMetadata(false));
    public static readonly DependencyProperty IsLoadingProperty=DependencyProperty.Register(nameof(IsLoading),typeof(bool),typeof(KpiCard),new PropertyMetadata(false));
    // Janela baixa: cartão mais fino e dica só no tooltip, para sobrar linhas na lista.
    public static readonly DependencyProperty IsCompactProperty=DependencyProperty.Register(nameof(IsCompact),typeof(bool),typeof(KpiCard),new PropertyMetadata(false));
    public bool IsCompact{get=>(bool)GetValue(IsCompactProperty);set=>SetValue(IsCompactProperty,value);}
    public static readonly DependencyProperty ValueBrushProperty=DependencyProperty.Register(nameof(ValueBrush),typeof(Brush),typeof(KpiCard),new PropertyMetadata(null));
    public string Label{get=>(string)GetValue(LabelProperty);set=>SetValue(LabelProperty,value);}
    public string Value{get=>(string)GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
    public string Hint{get=>(string)GetValue(HintProperty);set=>SetValue(HintProperty,value);}
    public string Tone{get=>(string)GetValue(ToneProperty);set=>SetValue(ToneProperty,value);}
    public Brush Stripe{get=>(Brush)GetValue(StripeProperty);set=>SetValue(StripeProperty,value);}
    public bool IsHero{get=>(bool)GetValue(IsHeroProperty);set=>SetValue(IsHeroProperty,value);}
    public bool IsLoading{get=>(bool)GetValue(IsLoadingProperty);set=>SetValue(IsLoadingProperty,value);}
    public Brush? ValueBrush{get=>(Brush?)GetValue(ValueBrushProperty);set=>SetValue(ValueBrushProperty,value);}
    public KpiCard(){Focusable=false;Motion.Lift(this);}
    TextBlock? valueText;
    public override void OnApplyTemplate(){base.OnApplyTemplate();valueText=GetTemplateChild("Val") as TextBlock;ShowValue();}
    void ShowValue(){if(valueText!=null)Motion.CountTo(valueText,Value??"");}
    void ApplyTone(){var key=Tone switch{"ok"=>"COk","info"=>"CInfo","warn"=>"CWarn","danger"=>"CDanger","violet"=>"CViolet",_=>""};if(key=="")Stripe=Brushes.Transparent;else SetResourceReference(StripeProperty,key);}
}

// Etiqueta de estado: SEMPRE ícone + texto (nunca só cor). Tons: late, now, ready, ok, neutral.
public class StatusTag:Control
{
    public static readonly DependencyProperty TextProperty=DependencyProperty.Register(nameof(Text),typeof(string),typeof(StatusTag),new PropertyMetadata(""));
    public static readonly DependencyProperty ToneProperty=DependencyProperty.Register(nameof(Tone),typeof(string),typeof(StatusTag),new PropertyMetadata("neutral",(d,e)=>((StatusTag)d).ApplyTone()));
    public static readonly DependencyProperty IconNameProperty=DependencyProperty.Register(nameof(IconName),typeof(string),typeof(StatusTag),new PropertyMetadata(""));
    public string Text{get=>(string)GetValue(TextProperty);set=>SetValue(TextProperty,value);}
    public string Tone{get=>(string)GetValue(ToneProperty);set=>SetValue(ToneProperty,value);}
    public string IconName{get=>(string)GetValue(IconNameProperty);set=>SetValue(IconNameProperty,value);}
    public StatusTag(){Focusable=false;ApplyTone();}
    // Coluna de tabela com etiqueta: texto do status e tom calculado pelo StatusTone.
    public static DataGridTemplateColumn Column(string header,string binding,DataGridLength width){var tag=new FrameworkElementFactory(typeof(StatusTag));tag.SetBinding(TextProperty,new System.Windows.Data.Binding(binding));tag.SetBinding(ToneProperty,new System.Windows.Data.Binding(binding){Converter=StatusTone.Instance});tag.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);return new DataGridTemplateColumn{Header=header,SortMemberPath=binding,Width=width,CellTemplate=new DataTemplate{VisualTree=tag}};}
    void ApplyTone()
    {
        var (bg,fg,icon)=Tone switch{"late"=>("DangerSoft","Danger","triangle-alert"),"now"=>("WarningSoft","Warning","zap"),"ready"=>("InfoSoft","Info","shopping-bag"),"ok"=>("AccentSoft","AccentInk","check"),_=>("SurfaceAlt","Muted","")};
        SetResourceReference(BackgroundProperty,bg);SetResourceReference(ForegroundProperty,fg);if(ReadLocalValue(IconNameProperty)==DependencyProperty.UnsetValue||string.IsNullOrEmpty(IconName))SetCurrentValue(IconNameProperty,icon);
    }
}

// Estado vazio: ícone, título, dica e ação opcional ("Limpar filtros", "Registrar agora"…).
public class EmptyState:Control
{
    public static readonly DependencyProperty IconNameProperty=DependencyProperty.Register(nameof(IconName),typeof(string),typeof(EmptyState),new PropertyMetadata("inbox"));
    public static readonly DependencyProperty TitleProperty=DependencyProperty.Register(nameof(Title),typeof(string),typeof(EmptyState),new PropertyMetadata(""));
    public static readonly DependencyProperty HintProperty=DependencyProperty.Register(nameof(Hint),typeof(string),typeof(EmptyState),new PropertyMetadata(""));
    public static readonly DependencyProperty ActionTextProperty=DependencyProperty.Register(nameof(ActionText),typeof(string),typeof(EmptyState),new PropertyMetadata(""));
    public string IconName{get=>(string)GetValue(IconNameProperty);set=>SetValue(IconNameProperty,value);}
    public string Title{get=>(string)GetValue(TitleProperty);set=>SetValue(TitleProperty,value);}
    public string Hint{get=>(string)GetValue(HintProperty);set=>SetValue(HintProperty,value);}
    public string ActionText{get=>(string)GetValue(ActionTextProperty);set=>SetValue(ActionTextProperty,value);}
    public event RoutedEventHandler? Action;
    public EmptyState(){Focusable=false;}
    public override void OnApplyTemplate(){base.OnApplyTemplate();if(GetTemplateChild("PART_Action") is Button b)b.Click+=(s,e)=>Action?.Invoke(this,e);}
}

// Esqueleto de carregamento (em vez de "R$ 0,00" enquanto os dados chegam). Aparece com fade de 200 ms; sem pulsar (padrão de movimento 150–250 ms).
public class Skeleton:Control
{
    public Skeleton(){Focusable=false;IsHitTestVisible=false;Loaded+=(_,_)=>Start();Unloaded+=(_,_)=>BeginAnimation(OpacityProperty,null);}
    void Start()
    {
        Opacity=.8;if(ThemeManager.MotionEnabled)BeginAnimation(OpacityProperty,new DoubleAnimation(0,.8,TimeSpan.FromMilliseconds(200)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
    }
}

// Alternância Lista ⇄ Cards. A escolha fica gravada por usuário e por tela (perfil deste computador).
public class ViewToggle:Control
{
    public static readonly DependencyProperty ModeProperty=DependencyProperty.Register(nameof(Mode),typeof(string),typeof(ViewToggle),new FrameworkPropertyMetadata("Lista",FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,(d,e)=>((ViewToggle)d).OnMode()));
    public static readonly DependencyProperty StorageKeyProperty=DependencyProperty.Register(nameof(StorageKey),typeof(string),typeof(ViewToggle),new PropertyMetadata("",(d,e)=>((ViewToggle)d).Restore()));
    // Segunda vista: "Cards" por padrão; em Pedidos é "Quadro" (o valor gravado continua "Cards").
    public static readonly DependencyProperty SecondLabelProperty=DependencyProperty.Register(nameof(SecondLabel),typeof(string),typeof(ViewToggle),new PropertyMetadata("Cards"));
    public static readonly DependencyProperty SecondIconProperty=DependencyProperty.Register(nameof(SecondIcon),typeof(string),typeof(ViewToggle),new PropertyMetadata("layout-grid"));
    public string SecondLabel{get=>(string)GetValue(SecondLabelProperty);set=>SetValue(SecondLabelProperty,value);}
    public string SecondIcon{get=>(string)GetValue(SecondIconProperty);set=>SetValue(SecondIconProperty,value);}
    public static void Store(string storageKey,string mode)
    {
        var key=$"{App.CurrentUser.UserName}|{storageKey}";
        try{Directory.CreateDirectory(AppProfile.Folder);var lines=(System.IO.File.Exists(File)?System.IO.File.ReadAllLines(File):[]).Where(l=>!l.StartsWith(key+"=")).Append($"{key}={mode}").ToArray();System.IO.File.WriteAllLines(File,lines);}
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    // Lê a vista gravada sem criar o controle (ex.: abrir Pedidos direto no quadro).
    public static string Stored(string storageKey,string fallback="Lista")
    {
        var key=$"{App.CurrentUser.UserName}|{storageKey}";
        try{foreach(var line in System.IO.File.Exists(File)?System.IO.File.ReadAllLines(File):[]){var p=line.Split('=',2);if(p.Length==2&&p[0]==key&&p[1] is "Lista" or "Cards")fallback=p[1];}}
        catch(IOException){}catch(UnauthorizedAccessException){}
        return fallback;
    }
    public string Mode{get=>(string)GetValue(ModeProperty);set=>SetValue(ModeProperty,value);}
    public string StorageKey{get=>(string)GetValue(StorageKeyProperty);set=>SetValue(StorageKeyProperty,value);}
    public event Action<string>? ModeChanged;
    bool restoring;
    public ViewToggle(){Focusable=false;}
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if(GetTemplateChild("PART_List") is ToggleButton list)list.Click+=(_,_)=>Mode="Lista";
        if(GetTemplateChild("PART_Cards") is ToggleButton cards)cards.Click+=(_,_)=>Mode="Cards";
    }
    static string File=>Path.Combine(AppProfile.Folder,"visoes.txt");
    string Key=>$"{App.CurrentUser.UserName}|{StorageKey}";
    void Restore()
    {
        if(StorageKey.Length==0)return;
        try{foreach(var line in System.IO.File.Exists(File)?System.IO.File.ReadAllLines(File):[]){var p=line.Split('=',2);if(p.Length==2&&p[0]==Key&&p[1] is "Lista" or "Cards"){restoring=true;Mode=p[1];restoring=false;}}}
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    void OnMode()
    {
        if(!restoring&&StorageKey.Length>0)
            try{Directory.CreateDirectory(AppProfile.Folder);var lines=(System.IO.File.Exists(File)?System.IO.File.ReadAllLines(File):[]).Where(l=>!l.StartsWith(Key+"=")).Append($"{Key}={Mode}").ToArray();System.IO.File.WriteAllLines(File,lines);}
            catch(IOException){}catch(UnauthorizedAccessException){}
        ModeChanged?.Invoke(Mode);
    }
}

// Botão do WhatsApp (mesmo comportamento de hoje: abre a conversa no WhatsApp Desktop). Telefone incompleto:
// fica cinza, informativo, com a dica "Informe o telefone com DDD…". OpenOnClick=false quando a tela trata o clique
// (ex.: menu de modelos de mensagem do pedido).
public class WhatsAppButton:Button
{
    public static readonly DependencyProperty PhoneProperty=DependencyProperty.Register(nameof(Phone),typeof(string),typeof(WhatsAppButton),new PropertyMetadata("",(d,e)=>((WhatsAppButton)d).Check()));
    public static readonly DependencyProperty CompactProperty=DependencyProperty.Register(nameof(Compact),typeof(bool),typeof(WhatsAppButton),new PropertyMetadata(false));
    static readonly DependencyPropertyKey IsCompleteKey=DependencyProperty.RegisterReadOnly(nameof(IsComplete),typeof(bool),typeof(WhatsAppButton),new PropertyMetadata(false));
    public static readonly DependencyProperty IsCompleteProperty=IsCompleteKey.DependencyProperty;
    public string Phone{get=>(string)GetValue(PhoneProperty);set=>SetValue(PhoneProperty,value);}
    public bool Compact{get=>(bool)GetValue(CompactProperty);set=>SetValue(CompactProperty,value);}
    public bool IsComplete=>(bool)GetValue(IsCompleteProperty);
    public bool OpenOnClick{get;set;}=true;
    public WhatsAppButton(){Content="WhatsApp";Check();}
    void Check()
    {
        var ok=CommunicationOperations.IsWhatsAppNumber(Phone);
        SetValue(IsCompleteKey,ok);ToolTip=ok?"Abrir conversa no WhatsApp":"Informe o telefone com DDD para abrir o WhatsApp";
    }
    protected override void OnClick(){base.OnClick();if(OpenOnClick&&Window.GetWindow(this) is Window w)WhatsAppActions.Open(w,Phone??"","");}
}

// Aviso não bloqueante (toast) no rodapé da janela, com "Desfazer" quando a ação é reversível.
// Some em 2,6 s (5 s com Desfazer). Não substitui confirmações de ações irreversíveis.
public static class Toast
{
    sealed class Layer:Adorner
    {
        public readonly StackPanel Panel=new(){VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,28)};
        readonly Grid host=new();
        public Layer(UIElement adorned):base(adorned){host.Children.Add(Panel);AddVisualChild(host);IsHitTestVisible=true;}
        protected override int VisualChildrenCount=>1;
        protected override Visual GetVisualChild(int index)=>host;
        protected override Size MeasureOverride(Size constraint){host.Measure(AdornedElement.RenderSize);return AdornedElement.RenderSize;}
        protected override Size ArrangeOverride(Size finalSize){host.Arrange(new Rect(finalSize));return finalSize;}
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement,Layer> layers=new();
    public static void Show(Window? owner,string message,string? actionText=null,Action? action=null,string tone="info")
    {
        owner??=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.IsActive)??Application.Current.MainWindow;
        // Páginas dentro da janela principal (PageNavigator) ficam sem conteúdo: o aviso aparece na janela principal.
        while(owner!=null&&owner.Content==null)owner=owner.Owner;
        if(owner?.Content is not UIElement root)return;
        var adorners=AdornerLayer.GetAdornerLayer(root);if(adorners==null)return;
        if(!layers.TryGetValue(root,out var layer)){layer=new Layer(root);layers.Add(root,layer);adorners.Add(layer);}
        var icon=tone switch{"ok"=>"circle-check","danger"=>"circle-alert","warn"=>"triangle-alert",_=>"info"};
        var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var ic=new UiIcon{IconName=icon,Width=18,Height=18,Margin=new Thickness(0,0,8,0)};ic.SetResourceReference(UiIcon.ForegroundProperty,"Paper");row.Children.Add(ic);
        var text=new TextBlock{Text=message,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxWidth=520};text.SetResourceReference(TextBlock.ForegroundProperty,"Paper");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(text,System.Windows.Automation.AutomationLiveSetting.Polite);row.Children.Add(text);
        var card=new Border{CornerRadius=new CornerRadius(10),Padding=new Thickness(14,10,14,10),Margin=new Thickness(0,8,0,0),Child=row,HorizontalAlignment=HorizontalAlignment.Center,
            Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=20,ShadowDepth=5,Direction=270,Opacity=.22}};
        card.SetResourceReference(Border.BackgroundProperty,"Ink");
        DispatchTimerClose(layer,card,actionText!=null?5000:2600);
        if(actionText!=null&&action!=null)
        {
            var undo=new Button{Content=actionText,Cursor=Cursors.Hand,Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Center,Template=LinkTemplate()};
            undo.SetResourceReference(ForegroundProperty,"Paper");
            undo.Click+=(_,_)=>{layer.Panel.Children.Remove(card);action();};row.Children.Add(undo);
        }
        layer.Panel.Children.Add(card);Motion.Pop(card,200);
    }
    static readonly DependencyProperty ForegroundProperty=Control.ForegroundProperty;
    static void DispatchTimerClose(Layer layer,Border card,int ms)
    {
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};
        timer.Tick+=(_,_)=>{if(card.IsMouseOver)return;timer.Stop();layer.Panel.Children.Remove(card);};timer.Start();
    }
    static ControlTemplate LinkTemplate()
    {
        var factory=new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Content"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        factory.SetValue(TextBlock.FontWeightProperty,FontWeights.Bold);factory.SetValue(TextBlock.TextDecorationsProperty,TextDecorations.Underline);
        factory.SetBinding(TextBlock.ForegroundProperty,new System.Windows.Data.Binding("Foreground"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        return new ControlTemplate(typeof(Button)){VisualTree=factory};
    }
}
