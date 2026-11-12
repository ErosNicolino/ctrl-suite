using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace GraficaCaixa.Wpf;

// Propriedades anexadas do conjunto de componentes UX 2.0 (usadas pelos estilos do App.xaml e Componentes.xaml).
public static class Ui
{
    // Campo com erro: borda vermelha + anel suave (a mensagem fica junto do campo, em texto).
    public static readonly DependencyProperty HasErrorProperty=DependencyProperty.RegisterAttached("HasError",typeof(bool),typeof(Ui),new PropertyMetadata(false));
    public static bool GetHasError(DependencyObject o)=>(bool)o.GetValue(HasErrorProperty);
    public static void SetHasError(DependencyObject o,bool v)=>o.SetValue(HasErrorProperty,v);

    // Cor de fundo e de borda ao passar o mouse (cada estilo de botão define a sua).
    public static readonly DependencyProperty HoverBackgroundProperty=DependencyProperty.RegisterAttached("HoverBackground",typeof(Brush),typeof(Ui),new PropertyMetadata(null));
    public static Brush? GetHoverBackground(DependencyObject o)=>(Brush?)o.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject o,Brush? v)=>o.SetValue(HoverBackgroundProperty,v);
    public static readonly DependencyProperty HoverBorderProperty=DependencyProperty.RegisterAttached("HoverBorder",typeof(Brush),typeof(Ui),new PropertyMetadata(null,(d,e)=>d.SetValue(HasHoverBorderProperty,e.NewValue!=null)));
    public static Brush? GetHoverBorder(DependencyObject o)=>(Brush?)o.GetValue(HoverBorderProperty);
    public static void SetHoverBorder(DependencyObject o,Brush? v)=>o.SetValue(HoverBorderProperty,v);
    public static readonly DependencyProperty HasHoverBorderProperty=DependencyProperty.RegisterAttached("HasHoverBorder",typeof(bool),typeof(Ui),new PropertyMetadata(false));
    public static bool GetHasHoverBorder(DependencyObject o)=>(bool)o.GetValue(HasHoverBorderProperty);
    public static void SetHasHoverBorder(DependencyObject o,bool v)=>o.SetValue(HasHoverBorderProperty,v);

    // Etapa/seção concluída (✓).
    public static readonly DependencyProperty IsDoneProperty=DependencyProperty.RegisterAttached("IsDone",typeof(bool),typeof(Ui),new PropertyMetadata(false));
    public static bool GetIsDone(DependencyObject o)=>(bool)o.GetValue(IsDoneProperty);
    public static void SetIsDone(DependencyObject o,bool v)=>o.SetValue(IsDoneProperty,v);
    // Item atual do menu lateral (barra à esquerda).
    public static readonly DependencyProperty IsCurrentProperty=DependencyProperty.RegisterAttached("IsCurrent",typeof(bool),typeof(Ui),new PropertyMetadata(false));
    public static bool GetIsCurrent(DependencyObject o)=>(bool)o.GetValue(IsCurrentProperty);
    public static void SetIsCurrent(DependencyObject o,bool v)=>o.SetValue(IsCurrentProperty,v);

    // Cor do contexto (herdada): o diálogo central define e as seções, o resumo e o botão principal usam.
    public static readonly DependencyProperty AccentProperty=DependencyProperty.RegisterAttached("Accent",typeof(Brush),typeof(Ui),new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x14,0x7A,0x70)),FrameworkPropertyMetadataOptions.Inherits));
    public static Brush GetAccent(DependencyObject o)=>(Brush)o.GetValue(AccentProperty);
    public static void SetAccent(DependencyObject o,Brush v)=>o.SetValue(AccentProperty,v);
    public static readonly DependencyProperty AccentSoftProperty=DependencyProperty.RegisterAttached("AccentSoft",typeof(Brush),typeof(Ui),new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xE4,0xF1,0xED)),FrameworkPropertyMetadataOptions.Inherits));
    public static Brush GetAccentSoft(DependencyObject o)=>(Brush)o.GetValue(AccentSoftProperty);
    public static void SetAccentSoft(DependencyObject o,Brush v)=>o.SetValue(AccentSoftProperty,v);

    // Aplica a cor de um tipo de cadastro/ação (Lancamento, Pedido, Produto, Cliente, Compra, Lembrete, Recibo, Neutro, Perigo).
    public static void UseKind(FrameworkElement element,string kind)
    {
        if(!ThemeManager.Kinds.ContainsKey(kind))kind="Neutro";
        element.SetResourceReference(AccentProperty,"Kind"+kind);element.SetResourceReference(AccentSoftProperty,"Kind"+kind+"Soft");
    }
}

// Movimento: só opacidade/transformação, 150–250 ms, ease-out; desligado em "Sem movimento" ou com movimento reduzido no Windows.
public static class Motion
{
    static readonly IEasingFunction Ease=new CubicEase{EasingMode=EasingMode.EaseOut};
    // Entrada suave (opacidade + 6 px para cima). Não usar em listas grandes.
    public static void Enter(UIElement element,int delayMs=0,double fromY=6,int durationMs=220)
    {
        if(!ThemeManager.MotionEnabled){element.Opacity=1;return;}
        var move=new TranslateTransform(0,fromY);element.RenderTransform=move;element.Opacity=0;
        var begin=TimeSpan.FromMilliseconds(delayMs);var length=TimeSpan.FromMilliseconds(durationMs);
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,length){BeginTime=begin,EasingFunction=Ease});
        move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(fromY,0,length){BeginTime=begin,EasingFunction=Ease});
    }
    // Entrada de diálogo: escala .97 → 1 e opacidade (200 ms).
    public static void Pop(FrameworkElement element,int durationMs=200)
    {
        if(!ThemeManager.MotionEnabled)return;
        element.RenderTransformOrigin=new Point(.5,.5);var scale=new ScaleTransform(.97,.97);var move=new TranslateTransform(0,6);
        element.RenderTransform=new TransformGroup{Children={scale,move}};element.Opacity=0;var length=TimeSpan.FromMilliseconds(durationMs);
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,length){EasingFunction=Ease});
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.97,1,length){EasingFunction=Ease});
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.97,1,length){EasingFunction=Ease});
        move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(6,0,length){EasingFunction=Ease});
    }
    // Clique: o botão "afunda" para 98% e volta (150 ms cada). Só escala, sem cor; não mexe em botões que já têm transformação própria.
    public static void Press(FrameworkElement element,bool down)
    {
        if(!ThemeManager.MotionEnabled)return;
        if(element.RenderTransform is not ScaleTransform scale){if(element.RenderTransform!=null&&element.RenderTransform!=Transform.Identity)return;scale=new ScaleTransform(1,1);element.RenderTransform=scale;element.RenderTransformOrigin=new Point(.5,.5);}
        var to=down?.98:1d;var length=TimeSpan.FromMilliseconds(150);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(to,length){EasingFunction=Ease});scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(to,length){EasingFunction=Ease});
    }
    // Troca de aba ou de página: fade curto (150 ms) do conteúdo novo.
    public static void Switch(UIElement element)=>Fade(element,150);
    // Cards coloridos de resumo: contagem progressiva do número (R$ ou inteiro) do valor anterior até o novo, 450 ms ease-out.
    // Sem movimento, texto não numérico ou valor igual: escreve o texto final direto.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Controls.TextBlock,System.Windows.Threading.DispatcherTimer> counting=new();
    static readonly System.Globalization.CultureInfo Pt=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
    static bool TryNumber(string text,out decimal value,out bool money)
    {
        money=text.Contains("R$");
        return money?decimal.TryParse(text,System.Globalization.NumberStyles.Currency,Pt,out value):decimal.TryParse(text,System.Globalization.NumberStyles.Integer,Pt,out value);
    }
    // Valor que chegou com o card ainda invisível (tela montada antes de aparecer): a contagem começa quando ele aparece.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Windows.Controls.TextBlock,string> pending=new();
    public static void CountTo(System.Windows.Controls.TextBlock target,string text,int durationMs=450)
    {
        if(counting.TryGetValue(target,out var running)){running.Stop();counting.Remove(target);}
        var current=target.Text??"";
        if(!ThemeManager.MotionEnabled||current==text||!TryNumber(text,out var to,out var money)){pending.Remove(target);if(current!=text&&TryNumber(text,out _,out _))LogOnce(false);target.Text=text;return;}
        if(!target.IsVisible)
        {
            var first=!pending.TryGetValue(target,out _);pending.AddOrUpdate(target,text);
            if(first)target.IsVisibleChanged+=WhenShown;
            return;
        }
        pending.Remove(target);LogOnce(true);
        var from=TryNumber(current,out var previous,out var previousMoney)&&previousMoney==money?previous:0m;
        string Format(decimal v)=>money?v.ToString("C2",Pt):Math.Round(v).ToString("N0",Pt);
        var start=DateTime.UtcNow;var timer=new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};counting.AddOrUpdate(target,timer);
        timer.Tick+=(_,_)=>{var t=Math.Min(1,(DateTime.UtcNow-start).TotalMilliseconds/durationMs);var eased=(decimal)(1-Math.Pow(1-t,3));if(t>=1){timer.Stop();counting.Remove(target);target.Text=text;return;}target.Text=Format(from+(to-from)*eased);};
        target.Text=Format(from);timer.Start();
    }
    static void WhenShown(object sender,DependencyPropertyChangedEventArgs e)
    {
        if(sender is not System.Windows.Controls.TextBlock target||!target.IsVisible)return;
        target.IsVisibleChanged-=WhenShown;
        if(pending.TryGetValue(target,out var text)){pending.Remove(target);CountTo(target,text);}
    }
    // Uma linha por sessão em animacoes.log (pasta do programa): se a contagem dos cards rodou ou foi pulada, e por quê.
    static bool logged;
    static void LogOnce(bool ran)
    {
        if(logged)return;logged=true;
        var why=ran?"rodou":ThemeManager.MotionOff?"pulada: opção \"Sem movimento\" ligada no ERP (Sistema › Aparência › Animações)":!SystemParameters.ClientAreaAnimation?"pulada: Windows com animações desligadas (Configurações › Acessibilidade › Efeitos visuais › Efeitos de animação)":"pulada";
        var tier=System.Windows.Media.RenderCapability.Tier>>16;
        try{System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"animacoes.log"),$"{DateTime.Now:dd/MM/yyyy HH:mm:ss} • {Environment.MachineName} • animação dos cards: {why} • vídeo Tier {tier}{(tier==0?" (renderização por software)":"")}{Environment.NewLine}");}catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException){}
    }
    // Entrada escalonada (40 ms entre um card e outro).
    public static void Stagger(IEnumerable<UIElement> elements,int stepMs=40){var i=0;foreach(var element in elements)Enter(element,stepMs*i++,6,220);}
    // Elevação leve no hover: sobe 2 px e ganha sombra suave (150 ms). Sem movimento: só a sombra, sem deslocar.
    public static void Lift(FrameworkElement card)
    {
        var shadow=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=18,ShadowDepth=3,Direction=270,Opacity=0,Color=Colors.Black};card.Effect=shadow;
        void To(double opacity,double y)
        {
            var length=TimeSpan.FromMilliseconds(ThemeManager.MotionEnabled?150:0);shadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,new DoubleAnimation(opacity,length){EasingFunction=Ease});
            if(!ThemeManager.MotionEnabled)return;if(card.RenderTransform is not TranslateTransform move){if(card.RenderTransform is not null&&card.RenderTransform!=Transform.Identity)return;move=new TranslateTransform();card.RenderTransform=move;}
            move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(y,length){EasingFunction=Ease});
        }
        card.MouseEnter+=(_,_)=>To(.16,-2);card.MouseLeave+=(_,_)=>To(0,0);
    }
    public static void Fade(UIElement element,int durationMs=160)
    {
        if(!ThemeManager.MotionEnabled){element.Opacity=1;return;}
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(durationMs)){EasingFunction=Ease});
    }
}
