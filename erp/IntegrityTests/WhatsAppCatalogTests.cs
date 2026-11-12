using GraficaCaixa;
using GraficaCaixa.Wpf;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Catálogo para WhatsApp: preço de venda (nunca custo), páginas de 12 produtos, imagem JPG por página e PDF A4 válido.
public static class WhatsAppCatalogTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");if(Application.Current==null){var app=new App();app.InitializeComponent();}
        var simple=new Product{Description="Caneca Porcelana",Price=40,Cost=12,Unit="un"};
        var tiers=new Product{Description="Adesivo",UseQuantityPricing=true,QuantityPriceTiers=[new QuantityPriceTier{From=1,UnitPrice=2},new QuantityPriceTier{From=100,UnitPrice=1.5m}]};
        var none=new Product{Description="Serviço especial"};
        Check(WhatsAppCatalog.Price(simple)==("R$ 40,00","por un"),"preço simples: R$ 40,00 por un");
        Check(WhatsAppCatalog.Price(tiers).Price=="a partir de R$ 1,50","faixas: a partir do menor preço");
        Check(WhatsAppCatalog.Price(none).Price=="Sob consulta","sem preço: Sob consulta");
        var company=new CompanyProfile();var photo=BitmapSource.Create(4,4,96,96,PixelFormats.Bgra32,null,Enumerable.Repeat((byte)200,64).ToArray(),16);photo.Freeze();
        var entries=Enumerable.Range(1,13).Select(i=>new WhatsAppCatalog.Entry(new Product{Description=$"Produto {i}",Price=i,Cost=999},photo)).ToList();
        var pages=WhatsAppCatalog.Pages(company,null,"Canecas",entries,new DateTime(2026,10,27));
        Check(pages.Count==2,"13 produtos com foto: 2 páginas (12 + 1)");
        var texts=new List<string>();List<string> sink=texts;void Walk(DependencyObject o){if(o is System.Windows.Controls.TextBlock t)sink.Add(t.Text);foreach(var child in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>())Walk(child);}
        var mixed=Enumerable.Range(1,6).Select(i=>new WhatsAppCatalog.Entry(new Product{Description=$"Com foto {i}",Price=i},photo)).Concat(Enumerable.Range(1,40).Select(i=>new WhatsAppCatalog.Entry(new Product{Description=$"Sem foto {i}",Price=i},null))).ToList();
        var mixedPages=WhatsAppCatalog.Pages(company,null,"Canecas",mixed,DateTime.Today);var mixedTexts=new List<string>();sink=mixedTexts;foreach(var page in mixedPages)Walk(page);sink=texts;
        Check(mixedPages.Count==2&&mixedTexts.Count(x=>x.StartsWith("Sem foto"))==40&&mixedTexts.Contains("Outros itens")&&!mixedTexts.Contains("Foto em breve"),"sem foto vira lista (Outros itens) no espaço que sobra e na página seguinte, sem quadro vazio");
        foreach(var page in pages)Walk(page);
        Check(texts.Contains("Produto 13")&&texts.Contains("R$ 13,00")&&texts.Any(x=>x.Contains("1/2")),"página mostra nome, preço e numeração");
        Check(!texts.Any(x=>x.Contains("999")),"custo nunca aparece no catálogo");
        var rendered=pages.Select(WhatsAppCatalog.Render).ToList();
        Check(rendered.All(x=>x.Width==1588&&x.Height==2246&&x.Jpeg[0]==0xFF&&x.Jpeg[1]==0xD8),"cada página vira JPG A4 em 2× (1588 × 2246)");
        var path=Path.Combine(Path.GetTempPath(),"erp-catalogo-"+Guid.NewGuid().ToString("N")[..8]+".pdf");
        try
        {
            WhatsAppCatalog.WritePdf(rendered,path);var pdf=System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
            Check(pdf.StartsWith("%PDF-1.4")&&pdf.TrimEnd().EndsWith("%%EOF")&&pdf.Contains("/Count 2")&&pdf.Split("/Subtype /Image").Length==3,"PDF com 2 páginas e 2 imagens");
            var xref=int.Parse(pdf[(pdf.LastIndexOf("startxref")+9)..].Trim().Split('\n')[0]);Check(pdf.Substring(xref,4)=="xref","tabela xref no endereço certo");
        }
        finally{File.Delete(path);}
        Check(WhatsAppCatalog.FileName("Canecas/Copos: \"Squeezes\"")=="Canecas Copos Squeezes","nome de arquivo sem caracteres inválidos");
        Console.WriteLine($"TOTAL: {passed} verificações do catálogo.");
    }
}
