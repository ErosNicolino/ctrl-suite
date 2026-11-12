using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
namespace GraficaCaixa.Wpf;
public sealed class UiIcon:Viewbox
{
    public static readonly DependencyProperty IconNameProperty=DependencyProperty.Register(nameof(IconName),typeof(string),typeof(UiIcon),new PropertyMetadata("",Changed));
    public static readonly DependencyProperty ForegroundProperty=TextElement.ForegroundProperty.AddOwner(typeof(UiIcon),new FrameworkPropertyMetadata(Brushes.Black,FrameworkPropertyMetadataOptions.Inherits,Changed));
    public string IconName{get=>(string)GetValue(IconNameProperty);set=>SetValue(IconNameProperty,value);}
    public Brush Foreground{get=>(Brush)GetValue(ForegroundProperty);set=>SetValue(ForegroundProperty,value);}
    // Plain=true: o ícone do WhatsApp usa a cor herdada (ex.: branco sobre o botão verde), em vez do verde fixo.
    public static readonly DependencyProperty PlainProperty=DependencyProperty.Register(nameof(Plain),typeof(bool),typeof(UiIcon),new PropertyMetadata(false,Changed));
    public bool Plain{get=>(bool)GetValue(PlainProperty);set=>SetValue(PlainProperty,value);}
    public static readonly DependencyProperty StrokeWidthProperty=DependencyProperty.Register(nameof(StrokeWidth),typeof(double),typeof(UiIcon),new PropertyMetadata(1.8,Changed));
    public double StrokeWidth{get=>(double)GetValue(StrokeWidthProperty);set=>SetValue(StrokeWidthProperty,value);}
    public UiIcon(){Width=20;Height=20;Stretch=Stretch.Uniform;IsHitTestVisible=false;}
    // Verde do WhatsApp nos botões que abrem o WhatsApp Desktop, em qualquer tema.
    static readonly Brush WhatsAppGreen=CreateFrozen("#25D366");
    static Brush CreateFrozen(string hex){var b=(Brush)new BrushConverter().ConvertFromString(hex)!;b.Freeze();return b;}
    static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)=>((UiIcon)d).Draw();
    void Draw()
    {
        // Sem ícone: recolhe (botões sem ícone não ficam com espaço sobrando). Com ícone: limpa o valor local para
        // que a visibilidade definida pelo estilo/modelo (ex.: ✓ só quando concluído) continue valendo.
        if(!UiIconData.Paths.TryGetValue(IconName??"",out var paths)){Visibility=Visibility.Collapsed;Child=null;return;}ClearValue(VisibilityProperty);
        var canvas=new Canvas{Width=24,Height=24};foreach(var data in paths)canvas.Children.Add(new System.Windows.Shapes.Path{Data=Geometry.Parse(data),Stroke=IconName=="whatsapp"&&!Plain?WhatsAppGreen:Foreground,StrokeThickness=StrokeWidth,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round});Child=canvas;
    }
}
public sealed class ButtonIconConverter:IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        if(value is not string text)return "";if(text=="×"||text=="\uE74D")return "trash";
        var label=ButtonLabelConverter.Clean(text).ToLowerInvariant();
        if(label.StartsWith("salvar")||label.StartsWith("gravar"))return "save";
        if(label.StartsWith("adicionar")||label.StartsWith("novo lançamento")||label.StartsWith("incluir"))return "plus";
        if(label.StartsWith("novo pedido")||label.StartsWith("criar pedido"))return "clipboard-list";
        if(label.StartsWith("novo produto"))return "package";
        if(label.Contains("categoria"))return "tag";
        if(label.StartsWith("editar")||label.StartsWith("alterar")||label.StartsWith("corrigir"))return "pencil";
        if(label=="novo"||label.StartsWith("nova ")||label.StartsWith("novo "))return "plus";
        if(label.StartsWith("limpar filtro"))return "list-filter";
        if(label.StartsWith("desfazer"))return "undo-2";
        if(label.StartsWith("remover")||label.StartsWith("excluir")||label.Contains("limpar"))return "trash";
        if(label.StartsWith("cancelar"))return "x";
        if(label.StartsWith("observa"))return "message-circle";
        if(label.StartsWith("calculadora"))return "calculator";
        if(label.StartsWith("whatsapp"))return "whatsapp";if(label.StartsWith("telefone"))return "phone";
        if(label.StartsWith("copiar"))return "copy";
        if(label.StartsWith("atualizar"))return "refresh-cw";
        if(label.StartsWith("imprimir"))return "printer";
        if(label.StartsWith("pdf")||label.StartsWith("gerar pdf")||label.StartsWith("gerar recibo")||label=="nf"||label.StartsWith("nota fiscal"))return "file-text";
        
        if(label.StartsWith("concluir")||label.StartsWith("confirmar")||label.StartsWith("aplicar"))return "check";
        if(label.StartsWith("voltar"))return "chevron-left";
        if(label.StartsWith("ver todos")||label.StartsWith("próximo"))return "chevron-right";
        if(label.StartsWith("pesquisar"))return "search";
        if(label.StartsWith("configura"))return "settings";
        if(label.StartsWith("sair"))return "log-out";
        return "";
    }
    public object ConvertBack(object value,Type t,object p,CultureInfo c)=>Binding.DoNothing;
}
public sealed class ButtonLabelConverter:IValueConverter
{
    public static string Clean(string text)=>Regex.Replace(text,"^[＋+▣♧☏✎⌕\\s]+|[▾▼]","").Trim();
    public object Convert(object value,Type t,object p,CultureInfo c)=>value is string text?(text=="×"||text=="\uE74D"?"":Clean(text)):value;
    public object ConvertBack(object value,Type t,object p,CultureInfo c)=>Binding.DoNothing;
}
public sealed class ButtonChevronConverter:IValueConverter
{
    public object Convert(object value,Type t,object p,CultureInfo c)=>value is string text&&(text.Contains('▾')||text.Contains('▼'))?"chevron-down":"";
    public object ConvertBack(object value,Type t,object p,CultureInfo c)=>Binding.DoNothing;
}
