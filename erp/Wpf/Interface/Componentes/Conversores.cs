using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace GraficaCaixa.Wpf;

// Texto vazio/nulo → Collapsed (esconde rótulos, dicas e ícones opcionais nos componentes).
public sealed class EmptyToCollapsed:IValueConverter
{
    public static readonly EmptyToCollapsed Instance=new();
    public object Convert(object value,Type t,object p,CultureInfo c)=>value is null||value is string s&&s.Length==0?Visibility.Collapsed:Visibility.Visible;
    public object ConvertBack(object value,Type t,object p,CultureInfo c)=>Binding.DoNothing;
}
public sealed class NullToFalse:IValueConverter
{
    public static readonly NullToFalse Instance=new();
    public object Convert(object value,Type t,object p,CultureInfo c)=>value!=null;
    public object ConvertBack(object value,Type t,object p,CultureInfo c)=>Binding.DoNothing;
}
public sealed class EqualsConverter:IValueConverter
{
    public static readonly EqualsConverter Instance=new();
    public object Convert(object value,Type t,object p,CultureInfo c)=>Equals(value?.ToString(),p?.ToString());
    public object ConvertBack(object value,Type t,object p,CultureInfo c)=>Binding.DoNothing;
}
// Texto de status → tom do StatusTag (ícone + texto, nunca só cor). Desconhecido → neutro.
public sealed class StatusTone:IValueConverter
{
    public static readonly StatusTone Instance=new();
    public object Convert(object value,Type t,object p,CultureInfo c)=>(value?.ToString()??"").Trim() switch
    {
        "Ativo" or "Autorizado" or "Renovável" or "Entregue" or "Quitado" or "Concluído"=>"ok",
        "Pronto para retirada" or "Retirada"=>"ready",
        "Revogado" or "Bloqueado" or "Atrasado" or "Expirado"=>"late",
        _=>"neutral"
    };
    public object ConvertBack(object value,Type t,object p,CultureInfo c)=>Binding.DoNothing;
}
