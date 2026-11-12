using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// "Calc venda" no cadastro do produto: custo × multiplicador (frete já no custo) + arte só no total da quantidade exata (Regras).
public static class PriceCalcTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    static bool Throws(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    static IEnumerable<DependencyObject> All(DependencyObject root){yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var x in All(child))yield return x;}
    public static void Run()
    {
        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");if(Application.Current==null){var app=new App();app.InitializeComponent();}
        var rules=new BusinessRules();
        Check(rules.SuggestedPrice(59.98m,true)==169.96m,"exemplo: 59,98 × 2 + 50,00 = 169,96");
        Check(rules.SuggestedPrice(1984.49m,true)==4018.98m,"50 un: 1.984,49 × 2 + 50 = 4.018,98");
        Check(rules.SuggestedPrice(0.35m,false)==0.70m,"faixa/preço único: sem a arte");
        rules.SaleMultiplier=2.5m;rules.ArtworkFee=30;Check(rules.SuggestedPrice(10,true)==55,"multiplicador e arte das Regras (2,5 e 30)");
        Check(Throws(new BusinessRules{SaleMultiplier=0.5m}.Validate)&&Throws(new BusinessRules{ArtworkFee=-1}.Validate)&&!Throws(new BusinessRules().Validate),"Regras recusam multiplicador < 1 e arte negativa");
        var product=new Product{Description="Bloco A4",Price=150,Cost=59.98m,ExactQuantityPrices=[new ExactQuantityPrice{Quantity=5,TotalPrice=420,TotalCost=215.98m},new ExactQuantityPrice{Quantity=10,TotalPrice=695,TotalCost=411.98m}],QuantityPriceTiers=[new QuantityPriceTier{From=1,UnitPrice=2,UnitCost=0.5m}]};
        var editor=new ProductEditor(product,"",null);
        var buttons=All(editor).OfType<Button>().Where(x=>x.Content as string=="Calc venda").ToList();
        Check(buttons.Count==4,"um \"Calc venda\" no preço único, em cada faixa e em cada quantidade exata");
        Console.WriteLine($"TOTAL: {passed} verificações do Calc venda.");
    }
}
