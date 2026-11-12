using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace GraficaCaixa.Wpf;

// Textos dos cartões de produto (tela Produtos): preço principal, como o preço funciona e custo/margem.
public sealed class ProductCardText : IValueConverter
{
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)
    {
        if(value is not Product p)return "";
        var unit=string.IsNullOrWhiteSpace(p.Unit)?"un":p.Unit.Trim();
        switch(parameter as string)
        {
            case "price":
                if(QuantityPricing.HasExactTable(p)){var first=p.ExactQuantityPrices.Where(x=>x.Quantity!=1).OrderBy(x=>x.Quantity).First();return string.Format(Pt,"{0:C2}",first.TotalPrice);}
                if(p.UseQuantityPricing&&p.QuantityPriceTiers.Count>0)return string.Format(Pt,"a partir de {0:C2}",p.QuantityPriceTiers.Min(x=>x.UnitPrice));
                return QuantityPricing.BasePrice(p) is decimal price?price.ToString("C2",Pt):"Sem preço";
            case "basis":
                if(QuantityPricing.HasExactTable(p)){var first=p.ExactQuantityPrices.Where(x=>x.Quantity!=1).OrderBy(x=>x.Quantity).First();var more=p.ExactQuantityPrices.Count(x=>x.Quantity!=1)-1;return string.Format(Pt,"por {0:N0} {1}",first.Quantity,unit)+(more>0?$" • +{more} quantidade(s)":"");}
                if(p.UseQuantityPricing&&p.QuantityPriceTiers.Count>0)return $"por {unit} • preço por faixa ({p.QuantityPriceTiers.Count})";
                return QuantityPricing.BasePrice(p).HasValue?$"por {unit}":"cadastre o preço de venda";
            case "table":
                if(QuantityPricing.HasExactTable(p))return string.Join("\n",p.ExactQuantityPrices.Where(x=>x.Quantity!=1).OrderBy(x=>x.Quantity).Select(x=>string.Format(Pt,"{0:N0} {1} — {2:C2}",x.Quantity,unit,x.TotalPrice)));
                if(p.UseQuantityPricing&&p.QuantityPriceTiers.Count>0)return string.Join("\n",p.QuantityPriceTiers.OrderBy(x=>x.From).Select(x=>x.Label));
                return null!;
            case "cost":
                return ProductCostInfo.Text(p);
            // Barra de margem (protótipo): largura 0–120 px pela margem, cor pela faixa (≥ 40% verde, 20–40% âmbar, abaixo vermelho).
            case "marginWidth":
                return ProductCostInfo.Of(p).Margin is decimal m?(double)Math.Clamp(m,0,100)*1.2:0d;
            case "marginBrush":
                return ThemeManager.Get(ProductCostInfo.Of(p).Margin is decimal mb?BusinessRules.Current.MarginTone(mb):"Line");
            case "costVisible":
                return ProductCostInfo.Of(p).HasCost?Visibility.Visible:Visibility.Collapsed;
            case "noCostVisible":
                return ProductCostInfo.Of(p).HasCost?Visibility.Collapsed:Visibility.Visible;
            case "hasPrice":
                return QuantityPricing.HasExactTable(p)||p.UseQuantityPricing&&p.QuantityPriceTiers.Count>0||QuantityPricing.BasePrice(p).HasValue?Visibility.Collapsed:Visibility.Visible;
            case "inactive":
                return p.Active?Visibility.Collapsed:Visibility.Visible;
        }
        return "";
    }
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}

// Quantas colunas de cartões cabem na largura disponível (parâmetro = largura mínima de cada cartão).
public sealed class CardColumns : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is double width&&width>0?Math.Max(1,(int)(width/double.Parse((string)parameter,CultureInfo.InvariantCulture))):1;
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
}