using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
namespace GraficaCaixa.Wpf;

public enum AppTheme { Automatico, Claro, Escuro }
public enum AppPalette { Viva, Sobria }

// Aparência por computador: tema (claro, escuro ou seguir o Windows), cores (Viva ou Sóbria), movimento e tamanho do texto.
// A tabela de cores é ÚNICA e fica aqui: cada chave vira um pincel em Application.Resources (Paper, Surface, Ink, Accent…);
// trocar o tema ou o modo altera esses pincéis e todas as telas abertas mudam na hora (DynamicResource).
// Regra de uso (protótipo UX 2.0): cores escuras (Danger, Warning, Info, AccentInk) no TEXTO; cores vivas (C*) em filetes,
// ícones e barras; K* são as cores de identidade dos cadastros; Kind* são as cores dos diálogos (no modo Sóbria = marca).
public static class ThemeManager
{
    static readonly Dictionary<string,(string Light,string Dark)> Palette=new()
    {
        // Base (chaves antigas mantidas; Muted e Warning ajustados ao protótipo para contraste AA).
        ["Paper"]=("#F7F9F8","#121618"),["Surface"]=("#FFFFFF","#1B2124"),["SurfaceAlt"]=("#F2F6F5","#232B2F"),["Hover"]=("#F2F6F5","#263034"),
        ["Ink"]=("#202A30","#E6ECEA"),["Muted"]=("#5B6970","#9AA8AE"),["Line"]=("#DCE5E2","#313C40"),
        ["Accent"]=("#147A70","#2A9D90"),["AccentInk"]=("#0E5F57","#6FD0C4"),["AccentSoft"]=("#E4F1ED","#18352F"),["OnAccent"]=("#FFFFFF","#06201C"),
        ["Selected"]=("#D6EBE5","#1F4842"),["SoftButtonBg"]=("#EEF2F1","#283236"),
        ["Danger"]=("#B54436","#E57366"),["DangerSoft"]=("#FDECE9","#3A2321"),["OnDanger"]=("#FFFFFF","#2A0F0B"),["Warning"]=("#8A5A12","#E0A955"),["WarningSoft"]=("#FFF4E1","#3A2E1A"),
        ["Info"]=("#1C6480","#6CB8D6"),["InfoSoft"]=("#E3F1F6","#16303A"),
        // Cores vivas (filetes, ícones, barras).
        ["CInfo"]=("#2563EB","#6C9BFF"),["CWarn"]=("#D59621","#E7B04A"),["CDanger"]=("#D94848","#F07A72"),["COk"]=("#27966B","#4FC08F"),["CViolet"]=("#7C5CD6","#A58BFF"),
        // Identidade dos cadastros e WhatsApp.
        ["KProd"]=("#7C5CD6","#A58BFF"),["KCli"]=("#0F7C86","#4FC3CE"),["KBuy"]=("#BF4F0C","#F0924F"),["KRem"]=("#A06A00","#E7B04A"),["KRec"]=("#52606D","#9FB0BD"),
        ["KPed"]=("#1F7BBF","#6CB8EE"),["KPedInk"]=("#185F94","#8CCBF5"),["KPedSoft"]=("#E6F2FB","#16303F"),
        ["Wa"]=("#128C7E","#3DBFA9"),["OnK"]=("#FFFFFF","#0E1417"),["Scrim"]=("#6B141C20","#99000000"),
    };
    // No modo Sóbria as cores vivas viram neutras (alertas continuam vermelho/âmbar no texto) e os diálogos usam a cor da marca.
    static readonly Dictionary<string,string> Sober=new(){["CInfo"]="Muted",["CWarn"]="Muted",["CDanger"]="Muted",["CViolet"]="Muted",["COk"]="Accent"};
    // Cores dos diálogos de inclusão/edição (Novo lançamento = verde da marca, Novo pedido = azul-claro, nos dois modos; cadastros = cor de identidade).
    public static readonly IReadOnlyDictionary<string,string> Kinds=new Dictionary<string,string>
    {
        ["Lancamento"]="Accent",["Pedido"]="KPed",["Produto"]="KProd",["Cliente"]="KCli",["Compra"]="KBuy",["Lembrete"]="KRem",["Recibo"]="KRec",["Neutro"]="Accent",["Perigo"]="Danger",
    };
    public static IEnumerable<string> TokenKeys=>Palette.Keys;
    public static AppTheme Theme{get;private set;}=AppTheme.Automatico;
    public static AppPalette Colors{get;private set;}=AppPalette.Viva;
    public static bool MotionOff{get;private set;}
    public static bool DailyPhrase{get;private set;}=true;
    public static double TextScale{get;private set;}=1;
    public static bool IsDark=>Theme==AppTheme.Escuro||Theme==AppTheme.Automatico&&WindowsUsesDarkTheme();
    // Animações só quando o usuário não desligou ("Sem movimento") e o Windows não pede movimento reduzido.
    public static bool MotionEnabled=>!MotionOff&&SystemParameters.ClientAreaAnimation;
    public static event Action? Changed;
    static string SettingsFile=>Path.Combine(AppProfile.Folder,"aparencia.txt");

    public static void Initialize()
    {
        try
        {
            foreach(var line in File.Exists(SettingsFile)?File.ReadAllLines(SettingsFile):[])
            {
                var parts=line.Split('=',2);if(parts.Length!=2)continue;
                if(parts[0]=="tema"&&Enum.TryParse<AppTheme>(parts[1],out var theme))Theme=theme;
                if(parts[0]=="cores"&&Enum.TryParse<AppPalette>(parts[1],out var colors))Colors=colors;
                if(parts[0]=="movimento")MotionOff=parts[1]=="off";
                if(parts[0]=="frase")DailyPhrase=parts[1]!="off";
                if(parts[0]=="texto"&&double.TryParse(parts[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var scale)&&scale is >=1 and <=1.4)TextScale=scale;
            }
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((s,_)=>{if(s is Window w)Decorate(w);}));
        ApplyColors();
    }
    public static void Set(AppTheme theme,double scale)=>Set(theme,scale,Colors,MotionOff,DailyPhrase);
    public static void Set(AppTheme theme,double scale,AppPalette colors,bool motionOff,bool dailyPhrase)
    {
        Theme=theme;TextScale=scale;Colors=colors;MotionOff=motionOff;DailyPhrase=dailyPhrase;ApplyColors();
        foreach(Window w in Application.Current.Windows)Decorate(w);
        try{Directory.CreateDirectory(AppProfile.Folder);File.WriteAllLines(SettingsFile,[$"tema={theme}",$"texto={scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}",$"cores={colors}",$"movimento={(motionOff?"off":"on")}",$"frase={(dailyPhrase?"on":"off")}"]);}catch(IOException){}catch(UnauthorizedAccessException){}
        Changed?.Invoke();
    }
    static Color Hex(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
    static Color ColorOf(string key,bool dark)=>Hex(dark?Palette[key].Dark:Palette[key].Light);
    // Mistura a cor sobre a superfície (equivale ao color-mix do protótipo).
    public static Color Mix(Color color,Color over,double amount)=>Color.FromRgb((byte)Math.Round(over.R+(color.R-over.R)*amount),(byte)Math.Round(over.G+(color.G-over.G)*amount),(byte)Math.Round(over.B+(color.B-over.B)*amount));
    static void ApplyColors()
    {
        var resources=Application.Current.Resources;var dark=IsDark;var sober=Colors==AppPalette.Sobria;
        var colors=new Dictionary<string,Color>();
        foreach(var key in Palette.Keys)colors[key]=sober&&Sober.TryGetValue(key,out var to)?ColorOf(to,dark):ColorOf(key,dark);
        // Pincéis base sem congelar (comportamento anterior: algumas telas leem e reaproveitam o pincel).
        foreach(var (key,color) in colors){resources[key]=new SolidColorBrush(color);resources[key+"Color"]=color;}
        // Diálogos: cor forte (Kind*), fundo suave (Kind*Soft = 9% sobre a superfície) e anel de foco (20%).
        var surface=colors["Surface"];
        foreach(var (kind,key) in Kinds)
        {
            var keep=kind is "Perigo" or "Pedido";var strong=sober&&!keep?colors["Accent"]:colors[key];
            var soft=sober&&!keep?colors["AccentSoft"]:kind=="Pedido"?colors["KPedSoft"]:Mix(strong,surface,.09);
            resources["Kind"+kind]=Frozen(strong);resources["Kind"+kind+"Soft"]=Frozen(soft);resources["Kind"+kind+"Band"]=Frozen(Mix(strong,surface,.14));
        }
        resources["FocusRing"]=Frozen(Color.FromArgb(0x55,colors["Accent"].R,colors["Accent"].G,colors["Accent"].B));
        resources["ShadowColor"]=dark?Color.FromArgb(0x80,0,0,0):Color.FromArgb(0x24,0x20,0x2A,0x30);
        // Logotipo vetorial: letras pretas no claro, brancas no escuro (nunca esticado; ver Assets\Logo.xaml).
        if(Application.Current.TryFindResource(dark?"LogoEscuro":"LogoClaro") is ImageSource logo)resources["Logo"]=logo;
    }
    static SolidColorBrush Frozen(Color c){var b=new SolidColorBrush(c);b.Freeze();return b;}
    public static Brush Get(string key)=>(Brush)Application.Current.Resources[key];
    // Conteúdo ampliado conforme o tamanho do texto; barra de título escura no tema escuro.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window,object> enlarged=new();
    static void Decorate(Window window)
    {
        // Na primeira abertura com texto ampliado, a janela (se não for maximizada nem automática) cresce junto.
        if(TextScale>1&&window.AllowsTransparency==false&&window.WindowState==WindowState.Normal&&window.SizeToContent==SizeToContent.Manual&&!enlarged.TryGetValue(window,out _)&&!double.IsNaN(window.Width))
        {
            enlarged.Add(window,new object());var area=SystemParameters.WorkArea;
            window.Width=Math.Min(window.Width*TextScale,area.Width-16);if(!double.IsNaN(window.Height))window.Height=Math.Min(window.Height*TextScale,area.Height-16);
            window.Left=Math.Max(area.Left,area.Left+(area.Width-window.Width)/2);window.Top=Math.Max(area.Top,area.Top+(area.Height-window.Height)/2);
        }
        if(window.Content is FrameworkElement content&&window.AllowsTransparency==false)
            content.LayoutTransform=TextScale==1?Transform.Identity:new ScaleTransform(TextScale,TextScale);
        // Fundo e texto do tema em toda janela que não definiu os seus (o estilo implícito não vale para janelas derivadas).
        if(window.AllowsTransparency==false&&window.ReadLocalValue(Control.BackgroundProperty)==DependencyProperty.UnsetValue)window.SetResourceReference(Control.BackgroundProperty,"Paper");
        if(window.ReadLocalValue(Control.ForegroundProperty)==DependencyProperty.UnsetValue)window.SetResourceReference(Control.ForegroundProperty,"Ink");
        var handle=new WindowInteropHelper(window).Handle;
        if(handle!=IntPtr.Zero){var on=IsDark?1:0;DwmSetWindowAttribute(handle,20,ref on,sizeof(int));}
    }
    static bool WindowsUsesDarkTheme()
    {
        try{using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");return key?.GetValue("AppsUseLightTheme") is int v&&v==0;}
        catch(Exception){return false;}
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
}
