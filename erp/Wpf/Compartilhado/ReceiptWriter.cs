using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Globalization;
namespace GraficaCaixa;

public static partial class ReportWriter
{
    static readonly Lazy<PdfImage> ReceiptStamp=new(()=>{using var stream=typeof(ReportWriter).Assembly.GetManifestResourceStream("GraficaCaixa.ReceiptStamp.png")??throw new FileNotFoundException("Carimbo oficial indisponível.");return LoadPdfImage(stream);});
    // Carimbo em uso: original, imagem configurada em Sistema › Geral ou nenhum (null).
    static readonly double[] CHeadFill={.94,.95,.95};
    static PdfImage? CurrentStamp{get{if(!Company.ShowStamp)return null;if(string.IsNullOrWhiteSpace(StampPath))return ReceiptStamp.Value;if(!File.Exists(StampPath))throw new FileNotFoundException("O carimbo configurado não foi encontrado: "+StampPath+". Confira em Sistema › Geral e arquivos.");using var stream=File.OpenRead(StampPath);return LoadPdfImage(stream);}}
    public static string WriteReceipt(ScheduledOrder order,string outputPath)
    {
        const double width=595.28,a4Height=841.89;
        var issuedAt=DateTime.Now;
        var pages=BuildReceiptPages(order,419.53,issuedAt);
        var full=Path.GetFullPath(outputPath);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full,MakePdf(pages,width,a4Height,CurrentOrderLogo,CurrentStamp));return full;
    }
    static List<string> BuildReceiptPages(ScheduledOrder order,double height,DateTime issuedAt)
    {
        const double left=28,right=567.28,bottom=38;
        const int maxItemsPerPage=10;
        var stampImage=CurrentStamp;var stamp=stampImage??ReceiptStamp.Value;const double stampWidth=110,stampY=23.5;var stampHeight=stampWidth*stamp.Height/stamp.Width;var stampTop=stampY+stampHeight;
        var pages=new List<string>();var b=new StringBuilder();double y=0;int pageItemCount=0;
        double Measure(string value,double size,bool bold=false)=>new FormattedText(value,CultureInfo.GetCultureInfo("pt-BR"),FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Arial"),FontStyles.Normal,bold?FontWeights.Bold:FontWeights.Normal,FontStretches.Normal),size,Brushes.Black,1).WidthIncludingTrailingWhitespace;
        List<string> Lines(string value,double available,double size,bool bold=false)
        {
            var result=new List<string>();
            foreach(var paragraph in (value??"").Replace("\r","").Split('\n'))
            {
                var line="";
                foreach(var word in paragraph.Split(' ',StringSplitOptions.RemoveEmptyEntries))
                {
                    if(line.Length>0&&Measure(line+" "+word,size,bold)>available){result.Add(line);line="";}
                    var rest=word;
                    while(Measure(rest,size,bold)>available)
                    {
                        int count=1;while(count<rest.Length&&Measure(rest[..(count+1)],size,bold)<=available)count++;
                        result.Add(rest[..count]);rest=rest[count..];
                    }
                    line=line.Length==0?rest:line+" "+rest;
                }
                if(line.Length>0)result.Add(line);
            }
            return result.Count>0?result:[""];
        }
        void Center(string value,double at,double size,bool bold=false)
        {
            var x=148+(right-148-Measure(value,size,bold))/2;
            Text(b,x,at,size,value,.13,.18,.2,bold);
        }
        void Header()
        {
            b=new StringBuilder();
            var logo=CurrentOrderLogo;var lw=80d;var lh=lw*logo.Height/logo.Width;
            DrawLogo(b,left,height-62,lw,lh);
            Center(Company.Name,height-17,12,true);
            Center(CompanyLine("CNPJ: ",Company.Document),height-29,8);
            Center(Company.Address,height-40,8);
            Center(CompanyLine("Telefone: ",Company.Phone),height-51,8);
            Center(CompanyLine("Email: ",Company.Email),height-62,8);
            LineW(b,left,height-70,right,height-70,CRule,1);
            var title=order.PaidAmount>0?"RECIBO DO PEDIDO":"COMPROVANTE DO PEDIDO";var titleWidth=Measure(title,11,true)+20;
            // Recibo sóbrio (bom em preto e branco): título escuro com um traço fino de destaque, sem faixas coloridas.
            Text(b,left,height-88,11,title,CInk[0],CInk[1],CInk[2],true);LineW(b,left,height-93,left+titleWidth-20,height-93,CTeal,1.5);
            var orderTag=$"Pedido #{order.Number}";Text(b,right-Measure(orderTag,16,true),height-90,16,orderTag,CInk[0],CInk[1],CInk[2],true);
            Text(b,left,height-104,7,$"Emitido em {issuedAt:dd/MM/yyyy HH:mm}",.35,.35,.35);
            y=height-118;pageItemCount=0;
            var contact=PhoneText.Format(CustomerBasicOperations.Clean(order.Contact));
            Paragraph("Cliente: "+order.CustomerName+(contact.Length>0?" | Telefone: "+contact:""),true);
            if(pages.Count>0)
            {
                Paragraph($"Continuação - Total: {(order.PricePending?"A definir":Money(order.OrderTotal))} - Recebido: {Money(order.PaidAmount)} - Saldo: {(order.PricePending?"A definir":Money(order.RemainingAmount))}",false,8);
            }
        }
        void Footer()
        {
            if(stampImage!=null)b.Append("q ").Append(C(stampWidth)).Append(" 0 0 ").Append(C(stampHeight)).Append(' ').Append(C(355+(right-355-stampWidth)/2)).Append(' ').Append(C(stampY)).Append(" cm /Stamp Do Q\n");
            LineW(b,355,22,right,22,CInk,1);
            Text(b,355+(right-355-Measure("Assinatura / Carimbo",9))/2,10,9,"Assinatura / Carimbo",.3,.3,.3);
            Text(b,left,10,7,$"Pedido #{order.Number} - Página {pages.Count+1} - Documento não fiscal",.4,.4,.4);
            pages.Add(b.ToString());
        }
        void Ensure(double space){if(y-space<bottom){Footer();Header();}}
        void Paragraph(string value,bool bold=false,double size=9,double[]? color=null)
        {
            var tone=color??new[]{.12,.15,.18};var fullLines=Lines(value,right-left-3,size,bold);var paragraphLines=y-fullLines.Count*12<stampTop+12?Lines(value,315-left,size,bold):fullLines;
            foreach(var line in paragraphLines){Ensure(12);Text(b,left,y,size,line,tone[0],tone[1],tone[2],bold);y-=12;}
            y-=2;
        }
        void TableHeader()
        {
            if(y-20<stampTop+20){Footer();Header();}Ensure(20);FillBox(b,left,y-12,right-left,18,CHeadFill);LineW(b,left,y-12,right,y-12,CRule,.8);
            Text(b,left+6,y-5,8,"QTD.",CInk[0],CInk[1],CInk[2],true);
            Text(b,75,y-5,8,"PRODUTO / SERVIÇO",CInk[0],CInk[1],CInk[2],true);
            Text(b,right-6-Measure("SUBTOTAL",8,true),y-5,8,"SUBTOTAL",CInk[0],CInk[1],CInk[2],true);y-=20;
        }
        Header();
        Ensure(46);var summaryY=y-35;
        var labels=new[]{"TOTAL DO PEDIDO","VALOR RECEBIDO","SALDO A PAGAR"};
        var amounts=new[]{order.PricePending?"A definir":Money(order.OrderTotal),Money(order.PaidAmount),order.PricePending?"A definir":Money(order.RemainingAmount)};
        var settled=!order.PricePending&&order.RemainingAmount<=0;
        // Três caixas brancas com borda fina; só o valor do saldo tem cor (vermelho se pendente, verde se quitado). Legível em preto e branco.
        const double boxGap=10;var boxW=(right-left-2*boxGap)/3;
        for(int i=0;i<3;i++){var x=left+i*(boxW+boxGap);StrokeBox(b,x,summaryY+1,boxW,36,CRule,.8);var ink=i<2?CInk:settled?CGreen:CRed;Text(b,x+8,summaryY+26,7,labels[i],CMuted[0],CMuted[1],CMuted[2],true);Text(b,x+8,summaryY+8,12,amounts[i],ink[0],ink[1],ink[2],true);}
        y=summaryY-13;
        if(order.CollectionSurcharge>0)Paragraph($"Acréscimo de cobrança: {Money(order.CollectionSurcharge)}",false,8);
        if(order.CustomerExtra>0)Paragraph($"Valor adicional do cliente: {Money(order.CustomerExtra)}",false,8);
        if(order.CollectionSurcharge+order.CustomerExtra>0)Paragraph($"Total cobrado: {Money(order.OrderTotal+order.CollectionSurcharge+order.CustomerExtra)}",false,8);
        if(order.DiscountAmount>0)Paragraph($"Original: {Money(order.OriginalTotal??order.OrderTotal)} - Desconto: {(order.FixedDiscount.HasValue?Money(order.DiscountAmount):$"{order.DiscountPercent:0.##}% ({Money(order.DiscountAmount)})")}",false,8);
        Paragraph(order.PaidAmount>0
            ?$"Recebemos do cliente acima {Money(order.PaidAmount)}, referente aos pagamentos do pedido #{order.Number}."
            :"Nenhum pagamento recebido. Este documento registra o pedido e não comprova quitação.",order.PaidAmount<=0,8);
        TableHeader();
        var items=order.Items.Count>0?order.Items:[new OrderItem{Quantity=1,Product=order.Description}];
        foreach(var item in items)
        {
            // Ten is a per-page limit, never a limit on the order's collection.
            if(pageItemCount>=maxItemsPerPage){Footer();Header();TableHeader();}
            var lines=Lines(item.Product,399,9);var countedOnPage=false;
            for(var i=0;i<lines.Count;i++)
            {
                if(y-12<Math.Max(bottom,stampTop+8))
                {
                    Footer();Header();TableHeader();countedOnPage=false;
                    if(i>0){Text(b,75,y,8,"Produto / serviço (continuação)",.35,.35,.35);y-=12;}
                }
                if(!countedOnPage){pageItemCount++;countedOnPage=true;}
                if(i==0)
                {
                    Text(b,left+6,y,9,item.Quantity.ToString(),.15,.15,.15);
                    var value=item.LineTotal.HasValue?Money(item.LineTotal.Value):"-";
                    Text(b,right-Measure(value,9),y,9,value,.15,.15,.15);
                }
                Text(b,75,y,9,lines[i],.15,.15,.15);y-=12;
            }
            y-=2;
        }
        if(order.Items.Count>0&&order.Items.All(x=>x.LineTotal.HasValue))
        {
            var sum=order.Items.Sum(x=>x.LineTotal!.Value);
            Paragraph($"Soma dos itens: {Money(sum)}",true,8);
            var original=order.OriginalTotal??order.OrderTotal+order.DiscountAmount;
            if(!order.PricePending&&!order.UseItemPrices&&original!=sum)
                Paragraph($"Ajuste do total manual do pedido: {(original>sum?"+":"-")}{Money(Math.Abs(original-sum))}",false,8);
        }
        var payments=order.Payments.Where(x=>x.SaleId!=null).OrderBy(x=>x.PaidAt).ToList();
        if(payments.Count>0)
        {
            y-=9;
            Paragraph("PAGAMENTOS REGISTRADOS",true,8);
            foreach(var payment in payments)Paragraph($"{payment.PaidAt:dd/MM/yyyy} - {payment.PaymentType} - {Money(payment.Amount)} - {payment.Note}",false,8);
        }
        Paragraph(order.PricePending?"Valor final a definir.":order.RemainingAmount>0?"Pagamento pendente. Comprova somente os valores efetivamente recebidos.":order.PaidAmount>0?"Pedido quitado.":"Sem valor a pagar. Nenhum pagamento registrado.",order.PricePending||order.RemainingAmount>0||order.PaidAmount>0,8);
        Footer();
        // Every receipt page occupies the upper half of A4, including continuations.
        return pages.Select(p=>$"q 1 0 0 1 0 {C(841.89-height)} cm\n{p}Q\n").ToList();
    }
}
