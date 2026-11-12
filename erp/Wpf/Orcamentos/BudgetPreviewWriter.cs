using System.Globalization;
using System.Text;
namespace GraficaCaixa;
public sealed record BudgetPreview(string Customer,string Document,string Contact,string Notes,IReadOnlyList<OrderItem> Items);
public static partial class ReportWriter
{
 // Prévia de orçamento no mesmo padrão do recibo: faixa no topo, logo, dados da gráfica, título em selo, total em destaque,
 // tabela com cabeçalho preenchido e valores à direita, assinatura/carimbo e rodapé com número e página.
 public static string WriteBudgetPreview(BudgetPreview quote,string path,string? number=null)
 {
  if(string.IsNullOrWhiteSpace(quote.Customer)||quote.Items.Count==0||quote.Items.Any(x=>string.IsNullOrWhiteSpace(x.Product)||x.Quantity<=0||x.UnitPrice is null or <0))throw new InvalidOperationException("Informe o cliente e ao menos um item com quantidade e preço válidos.");
  const double width=595.28,height=841.89,left=36,right=559.28,bottom=150;
  var stampImage=CurrentStamp;var stamp=stampImage??ReceiptStamp.Value;var logo=CurrentOrderLogo;number??=DateTime.Now.ToString("yyMMdd-HHmm");var total=quote.Items.Sum(x=>x.LineTotal!.Value);var totalText=Money(total);
  var pages=new List<string>();var b=new StringBuilder();double y=0;
  void Right(string value,double at,double y0,double size,bool bold=false,double[]? color=null){var c=color??CInk;Text(b,at-TextWidth(value,size,bold),y0,size,value,c[0],c[1],c[2],bold);}
  void Center(string value,double y0,double size,bool bold=false)=>Text(b,148+(right-148-TextWidth(value,size,bold))/2,y0,size,value,.13,.18,.2,bold);
  void Header(bool first)
  {
   b=new();Rect(b,0,height-6,width*.7,6,CTeal[0],CTeal[1],CTeal[2]);Rect(b,width*.7,height-6,width*.3,6,CBlue[0],CBlue[1],CBlue[2]);
   var lw=96d;DrawLogo(b,left,height-84,lw,lw*logo.Height/logo.Width);
   Center(Company.Name,height-31,14,true);Center(CompanyLine("CNPJ: ",Company.Document),height-47,9);Center(Company.Address,height-61,9);Center(CompanyLine("Telefone: ",Company.Phone),height-75,9);Center(CompanyLine("Email: ",Company.Email),height-87,9);
   LineW(b,left,height-98,right,height-98,CTeal,1.5);
   FillBox(b,left,height-126,136,24,CTeal);Text(b,left+12,height-119,16,"ORÇAMENTO",1,1,1,true);
   Text(b,left,height-140,8,$"Nº {number} - Emitido em {DateTime.Now:dd/MM/yyyy HH:mm}",.35,.35,.35);
   FillBox(b,right-170,height-144,170,38,CTeal);Text(b,right-162,height-118,8,"TOTAL DO ORÇAMENTO",.8,.93,.91,true);Right(totalText,right-8,height-137,17,true,[1,1,1]);
   y=height-170;
   if(!first)return;
   Text(b,left,y,11,"Cliente: "+quote.Customer,.12,.15,.18,true);y-=16;
   var details=new List<string>();if(!string.IsNullOrWhiteSpace(quote.Document))details.Add($"{DocumentLabel(quote.Document)}: {quote.Document}");if(!string.IsNullOrWhiteSpace(quote.Contact))details.Add("Telefone: "+quote.Contact);
   if(details.Count>0){Text(b,left,y,9,string.Join("   -   ",details),.3,.33,.36);y-=16;}
   y-=8;
  }
  void Columns(){FillBox(b,left,y-8,right-left,22,CTeal);Text(b,left+8,y-1,8.5,"QTD.",1,1,1,true);Text(b,left+56,y-1,8.5,"PRODUTO / SERVIÇO",1,1,1,true);Right("UNITÁRIO",right-98,y-1,8.5,true,[1,1,1]);Right("SUBTOTAL",right-8,y-1,8.5,true,[1,1,1]);y-=26;}
  void Footer(){Text(b,left,24,7,$"Orçamento Nº {number} - Página {pages.Count+1} - Documento não fiscal",.4,.4,.4);pages.Add(b.ToString());}
  void Ensure(double space,bool table=false){if(y-space>=bottom)return;Footer();Header(false);if(table)Columns();}
  void Paragraph(string text,double size=9.5,bool bold=false){foreach(var line in WrapAll(text,100)){Ensure(size+6);Text(b,left,y,size,line,.12,.15,.18,bold);y-=size+5;}y-=4;}
  Header(true);Columns();
  foreach(var item in quote.Items)
  {
   var lines=WrapAll(item.Product,62);
   for(var i=0;i<lines.Count;i++)
   {
    Ensure(15,true);
    if(i==0){Text(b,left+8,y,10,item.Quantity.ToString("0.##",CultureInfo.GetCultureInfo("pt-BR")),.12,.15,.18);Right(Money(item.UnitPrice!.Value),right-98,y,9.5);Right(Money(item.LineTotal!.Value),right-8,y,9.5,true);}
    Text(b,left+56,y,10,lines[i],.12,.15,.18);y-=14;
   }
   Line(b,left,y+3,right,y+3,.86,.89,.88);y-=9;
  }
  Ensure(40);y-=4;Text(b,right-250,y,11,"TOTAL DO ORÇAMENTO",CTeal[0],CTeal[1],CTeal[2],true);Right(totalText,right-8,y,13,true,CTeal);y-=30;
  if(!string.IsNullOrWhiteSpace(quote.Notes)){Paragraph("OBSERVAÇÕES, PRAZO E CONDIÇÕES",8.5,true);Paragraph(quote.Notes.Trim());}
  // Condições e validade em Sistema › Configurações › Regras.
  var rules=BusinessRules.Current;var validity=rules.BudgetValidityDays>0?$"Orçamento válido por {rules.BudgetValidityDays} dia(s), até {DateTime.Today.AddDays(rules.BudgetValidityDays):dd/MM/yyyy}. ":"";
  var conditions=validity+rules.BudgetConditions;if(conditions.Trim().Length>0)Paragraph(conditions.Trim(),8.5);
  const double stampWidth=135;var stampHeight=stampWidth*stamp.Height/stamp.Width;
  Text(b,left,86,10,$"{Company.City}, {DateTime.Now.ToString("dd 'de' MMMM 'de' yyyy",CultureInfo.GetCultureInfo("pt-BR"))}.",.12,.15,.18);
  if(stampImage!=null)b.Append("q ").Append(C(stampWidth)).Append(" 0 0 ").Append(C(stampHeight)).Append(' ').Append(C(355+(right-355-stampWidth)/2)).Append(" 60.5 cm /Stamp Do Q\n");
  LineW(b,355,60,right,60,CInk,1);Text(b,355+(right-355-TextWidth("Assinatura / Carimbo",9))/2,47,9,"Assinatura / Carimbo",.3,.3,.3);
  Footer();
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllBytes(path,MakePdf(pages,width,height,logo,stampImage));return path;
 }
}
