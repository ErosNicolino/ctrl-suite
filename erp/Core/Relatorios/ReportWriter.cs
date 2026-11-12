using System.Globalization;
using System.Diagnostics;
using System.IO.Compression;
using System.Printing;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GraficaCaixa;

public static partial class ReportWriter
{
    const double PageW = 595, PageH = 842;
    const double FirstRowY = 700, RowHeight = 24, SafeBottomY = 64;
    static string fallbackOutputFolder = Path.Combine(AppContext.BaseDirectory, "Relatorios");
    public static Func<string>? OutputFolderResolver { get; set; }
    public static string OutputFolder { get => OutputFolderResolver?.Invoke() ?? fallbackOutputFolder; set => fallbackOutputFolder=value; }
    public static string LogoPath {get;set;}="";
    // Endereço do pedido no celular ("https://host:porta/app/#pedidos/"), definido pelo servidor quando o acesso pelo celular está ligado.
    // Vazio = ficha sem QR.
    public static string OrderLinkBase {get;set;}="";
    // Dados da gráfica no cabeçalho dos PDFs (Sistema › Geral e arquivos). Definido no login (bootstrap) e no servidor ao gerar PDFs.
    public static CompanyProfile Company {get;set;}=new();
    // Caminho já resolvido do carimbo próprio (vazio = carimbo original).
    public static string StampPath {get;set;}="";
    static readonly Lazy<PdfImage> DefaultOrderLogo = new(LoadOrderLogo);
    static PdfImage CurrentOrderLogo=>string.IsNullOrWhiteSpace(LogoPath)?DefaultOrderLogo.Value:LoadOrderLogo();

    public static string Write(CashSession session, CashClosureVersion? closureVersion=null)
    {
        var dir = OutputFolder; Directory.CreateDirectory(dir);
        var suffix=closureVersion?.Version>1?$"_Retificado_v{closureVersion.Version}":"_Original_v1";
        var path = Path.Combine(dir, $"Fechamento_{session.OpenedAt:dd_MM_yyyy_HH'h'mm}{suffix}.pdf");
        var pages = BuildPages(session,closureVersion); File.WriteAllBytes(path, MakePdf(pages)); return path;
    }

    public static string WriteOrder(ScheduledOrder order)
    {
        const double w=421,h=595;var dir=OutputFolder;Directory.CreateDirectory(dir);var path=Path.Combine(dir,BuildOrderSheetFileName([order]));var b=new StringBuilder();DrawOrder(b,order,0,0);File.WriteAllBytes(path,MakePdf(new[]{b.ToString()}.Concat(PaymentContinuation([order])).ToList(),w,h,CurrentOrderLogo));return path;
    }

    public static string BuildOrderSheetFileName(IEnumerable<ScheduledOrder> selectedOrders)
    {
        var names=selectedOrders.Select(x=>Slug(x.CustomerName)).Where(x=>x.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToList();if(names.Count==0)names.Add("pedidos");return $"{string.Join('-',names)}-{DateTime.Now:dd-MM-yy}.pdf";
    }

    public static string BuildAvailableOrderSheetPath(string folder,IEnumerable<ScheduledOrder> selectedOrders)
    {
        Directory.CreateDirectory(folder);var baseName=Path.GetFileNameWithoutExtension(BuildOrderSheetFileName(selectedOrders));var path=Path.Combine(folder,baseName+".pdf");if(!File.Exists(path))return path;
        for(var index=2;;index++){path=Path.Combine(folder,$"{baseName}-{index}.pdf");if(!File.Exists(path))return path;}
    }

    public static ProcessStartInfo BuildPrintStartInfo(string pdfPath)=>new(Path.GetFullPath(pdfPath)){UseShellExecute=true,Verb="print",WindowStyle=ProcessWindowStyle.Hidden};
    public static string PrintOrderSheet(string pdfPath)
    {
        if(!File.Exists(pdfPath))throw new FileNotFoundException("O PDF que seria impresso não foi encontrado.",pdfPath);
        var printSettings=ConfigureBestColorPrinting();
        var process=Process.Start(BuildPrintStartInfo(pdfPath));if(process==null)throw new InvalidOperationException("O Windows não conseguiu iniciar a impressão do PDF.");
        return printSettings;
    }

    public static string WriteOrderSheet(IEnumerable<ScheduledOrder> selectedOrders,string? outputPath=null)
    {
        var orders=selectedOrders.Take(3).ToList();if(orders.Count==0)throw new InvalidOperationException("Selecione ao menos um pedido.");if(orders.Count>2)throw new InvalidOperationException("Selecione no máximo dois pedidos por folha A4.");
        var path=outputPath;if(string.IsNullOrWhiteSpace(path)){var dir=OutputFolder;Directory.CreateDirectory(dir);path=Path.Combine(dir,BuildOrderSheetFileName(orders));}else Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var b=new StringBuilder();DrawOrder(b,orders[0],0,0);if(orders.Count==2)DrawOrder(b,orders[1],421,0);Line(b,421,0,421,595,.68,.63,.54);File.WriteAllBytes(path,MakePdf(new[]{b.ToString()}.Concat(PaymentContinuation(orders)).ToList(),842,595,CurrentOrderLogo));return path;
    }

    // ---- Paleta e primitivas do layout novo (UX 2.0, pacote 6). Cor só em texto e contornos finos na ficha: gasta pouca tinta. ----
    static readonly double[] CInk={.08,.10,.12},CMuted={.365,.412,.439},CRule={.80,.84,.83},CTeal={.055,.486,.439},CTealDark={.039,.353,.322},CBlue={.122,.373,.749},CGreen={.106,.541,.353},CRed={.753,.224,.169},CAmber={.718,.475,.122};
    // Largura do texto (Arial ≈ Helvetica) para alinhar à direita. Se a medição do WPF não estiver disponível na thread
    // (ex.: servidor gerando a ficha), usa uma estimativa segura em vez de falhar o PDF.
    static double TextWidth(string value,double size,bool bold=false)
    {
        try{return new System.Windows.Media.FormattedText(value??"",CultureInfo.GetCultureInfo("pt-BR"),System.Windows.FlowDirection.LeftToRight,new Typeface(new System.Windows.Media.FontFamily("Arial"),System.Windows.FontStyles.Normal,bold?System.Windows.FontWeights.Bold:System.Windows.FontWeights.Normal,System.Windows.FontStretches.Normal),size,Brushes.Black,1).WidthIncludingTrailingWhitespace;}
        catch(InvalidOperationException){return (value??"").Length*size*(bold?.6:.55);}
    }
    static void Tx(StringBuilder b,double x,double y,double size,string value,double[] c,bool bold=false)=>Text(b,x,y,size,value,c[0],c[1],c[2],bold);
    static void TxRight(StringBuilder b,double right,double y,double size,string value,double[] c,bool bold=false)=>Text(b,right-TextWidth(value,size,bold),y,size,value,c[0],c[1],c[2],bold);
    static void LineW(StringBuilder b,double x1,double y1,double x2,double y2,double[] c,double width)=>b.Append(C(c[0])).Append(' ').Append(C(c[1])).Append(' ').Append(C(c[2])).Append(" RG ").Append(C(width)).Append(" w ").Append(C(x1)).Append(' ').Append(C(y1)).Append(" m ").Append(C(x2)).Append(' ').Append(C(y2)).Append(" l S\n");
    static void StrokeBox(StringBuilder b,double x,double y,double w,double h,double[] c,double width)=>b.Append(C(c[0])).Append(' ').Append(C(c[1])).Append(' ').Append(C(c[2])).Append(" RG ").Append(C(width)).Append(" w ").Append(C(x)).Append(' ').Append(C(y)).Append(' ').Append(C(w)).Append(' ').Append(C(h)).Append(" re S\n");
    static void FillBox(StringBuilder b,double x,double y,double w,double h,double[] c)=>Rect(b,x,y,w,h,c[0],c[1],c[2]);
    static double[] Tint(double[] c,double amount)=>[1-(1-c[0])*amount,1-(1-c[1])*amount,1-(1-c[2])*amount];

    // Ficha do pedido (A5). Versão econômica aprovada: fundo branco; cor só em texto e contornos. Mesmos dados de antes;
    // sai o quadro de etapas e entra o quadro "Pagamento e retirada" para anotar à mão depois de imprimir.
    static void DrawOrder(StringBuilder b,ScheduledOrder order,double offsetX,double offsetY)
    {
        const double L=24,R=397,w=421;
        b.Append("q 1 0 0 1 ").Append(C(offsetX)).Append(' ').Append(C(offsetY)).Append(" cm\n");Rect(b,0,0,w,595,1,1,1);
        DrawLogo(b,L,539,81,52);
        TxRight(b,R,577,8,"PEDIDO",CInk,true);var number=$"#{order.Number}";TxRight(b,R,547,28,number,CInk,true);
        DrawOrderQr(b,order,R-Math.Max(TextWidth(number,28,true),TextWidth("PEDIDO",8,true))-14);
        LineW(b,L,531,R,531,CTeal,2);
        Tx(b,L,516,7,"CLIENTE",CInk,true);Tx(b,L,498,15,Limit(order.CustomerName,26),CInk,true);
        Tx(b,220,516,7,"TELEFONE",CInk,true);Tx(b,220,497,16,Limit(PhoneText.Format(order.Contact),18),CRed,true);
        // Cabeçalho em preto; só telefone e prazo em vermelho (o que a produção procura). Mantém o traço verde do topo.
        var pt=CultureInfo.GetCultureInfo("pt-BR");
        var deadline=order.DeadlineStarted?$"{order.DueDate:dd/MM/yyyy} · {order.DueDate.ToString("ddd",pt)}":$"{order.ProductionBusinessDays} dias úteis após a aprovação";
        StrokeBox(b,L,452,R-L,34,CInk,1.5);Tx(b,L+9,476,7,"PRAZO DE ENTREGA",CInk,true);Tx(b,L+9,460,13,deadline,CRed,true);
        string? flag=null;double[] flagColor=CInk;
        if(OrderOperations.IsImmediate(order)){flag="IMEDIATO";}
        else if(order.DeadlineStarted&&order.Stage!="Pronto para retirada"&&order.Stage!="Entregue"){var left=BusinessCalendar.RemainingBusinessDays(DateTime.Today,order.DueDate);if(left<0){flag=$"ATRASADO {-left} D";flagColor=CRed;}else if(left==0){flag="VENCE HOJE";flagColor=CRed;}}
        if(flag!=null){var fw=TextWidth(flag,8,true)+14;StrokeBox(b,R-9-fw,459,fw,17,flagColor,1.3);Tx(b,R-9-fw+7,464,8,flag,flagColor,true);}
        // Itens
        Tx(b,L,438,7,"QTD.",CInk,true);Tx(b,62,438,7,"PRODUTO / SERVIÇO",CInk,true);TxRight(b,R,438,7,"SUBTOTAL",CInk,true);LineW(b,L,432,R,432,CInk,1.2);
        var items=order.Items.Count>0?order.Items:[new OrderItem{Quantity=1,Product=order.Description}];double itemSize=10,itemStep=13;List<(OrderItem Item,List<string> Lines)> itemLayout=[];
        for(var candidate=10d;candidate>=4.5;candidate-=.5){var width=Math.Max(24,(int)Math.Floor(48*10/candidate));var layout=items.Select(x=>(x,WrapAll(x.Product,width))).ToList();var step=candidate+3;if(layout.Sum(x=>x.Item2.Count)*step<=105||candidate==4.5){itemSize=candidate;itemStep=step;itemLayout=layout;break;}}
        var iy=419d;foreach(var entry in itemLayout){for(var line=0;line<entry.Lines.Count;line++){if(line==0){Tx(b,L+2,iy,itemSize,entry.Item.Quantity.ToString(),CInk,true);TxRight(b,R,iy,Math.Min(8,itemSize),entry.Item.LineTotal.HasValue?Money(entry.Item.LineTotal.Value):"A definir",CInk,true);}Tx(b,62,iy,itemSize,entry.Lines[line],CInk);iy-=itemStep;}}
        var compact=itemLayout.Sum(x=>x.Lines.Count)>5||items.Count>4;
        var descriptionTitleY=compact?Math.Min(349,iy-5):349;var descriptionY=descriptionTitleY-17;Tx(b,L,descriptionTitleY,8,"DESCRIÇÃO",CMuted,true);var description=string.IsNullOrWhiteSpace(order.Notes)?"Sem descrição adicional.":order.Notes;DrawOrderParagraph(b,description,descriptionY,"descrição",order.CustomerName,L,62,compact?2:3);
        var registeredPayments=order.Payments.Where(x=>x.SaleId!=null).OrderBy(x=>x.PaidAt).ToList();
        var observationsTitleY=compact?Math.Min(294,descriptionY-31):294;Tx(b,L,observationsTitleY,8,"OBSERVAÇÕES",CMuted,true);for(var lineY=observationsTitleY-18;lineY>=(registeredPayments.Count>0?262:206);lineY-=20)LineW(b,L,lineY,R,lineY,CRule,.6);
        if(registeredPayments.Count>0){Tx(b,L,253,7,"PAGAMENTOS REGISTRADOS",CTealDark,true);var rows=(Math.Min(8,registeredPayments.Count)+1)/2;var step=Math.Min(11d,42d/rows);var size=Math.Min(7d,step-1);for(int i=0;i<Math.Min(8,registeredPayments.Count);i++){var payment=registeredPayments[i];Tx(b,L+(i%2)*190,241-(i/2)*step,size,$"{payment.PaymentType} - {Money(payment.Amount)}",CInk);}}
        if(registeredPayments.Count>8)Tx(b,L,202,6,"Demais pagamentos: ver continuação anexa.",CMuted);
        // Quadro para anotar à mão depois de imprimir: Sinal / Pago / Retirada.
        var paidFull=!order.PricePending&&order.RemainingAmount<=0&&order.PaidAmount>0;
        Tx(b,L,188,7,"PAGAMENTO E RETIRADA — anotar depois de imprimir",CMuted,true);TxRight(b,R,188,6.5,paidFull?"pedido já pago":"marque X e preencha à caneta",CMuted);
        var rowsDef=paidFull?new[]{("PAGO",CGreen,"quitado","R$"),("RETIRADA",CBlue,"retirado","por")}:new[]{("SINAL",CAmber,"recebido","R$"),("PAGO",CGreen,"quitado","R$"),("RETIRADA",CBlue,"retirado","por")};
        const double rowH=24,top=181;var blockH=rowH*rowsDef.Length;StrokeBox(b,L,top-blockH,R-L,blockH,CMuted,1);
        for(var i=0;i<rowsDef.Length;i++)
        {
            var (name,color,label,field)=rowsDef[i];var yTop=top-i*rowH;var yMid=yTop-rowH/2;
            if(i>0)LineW(b,L,yTop,R,yTop,CRule,.6);
            FillBox(b,L,yTop-rowH,3,rowH,color);Tx(b,L+10,yMid-3,8,name,color,true);
            StrokeBox(b,L+82,yMid-5,10,10,CInk,1.1);Tx(b,L+98,yMid-3,8,label,CInk);
            var prefilled=paidFull&&name=="PAGO";
            if(prefilled){LineW(b,L+83.5,yMid-3.5,L+90.5,yMid+3.5,CGreen,1.6);LineW(b,L+83.5,yMid+3.5,L+90.5,yMid-3.5,CGreen,1.6);}
            if(!prefilled)Tx(b,170,yMid-3,8,field,CMuted);Tx(b,272,yMid-3,8,"em",CMuted);
            if(prefilled){Tx(b,170,yMid-3,8,Money(order.PaidAmount),CGreen,true);Tx(b,288,yMid-3,8,registeredPayments.Count>0?registeredPayments.Last().PaidAt.ToString("dd/MM/yyyy"):"",CGreen,true);}
            else{LineW(b,field=="R$"?184:184,yMid-5,262,yMid-5,CMuted,.6);LineW(b,288,yMid-5,R-8,yMid-5,CMuted,.6);}
        }
        // Totais: contorno e número coloridos; rótulo sempre escrito (também legível em preto e branco).
        const double boxY=34,boxH=40,gap=8;var boxW=(R-L-2*gap)/3;var settled=!order.PricePending&&order.RemainingAmount<=0;
        void Total(int index,string label,string value,double[] color,double border){var x=L+index*(boxW+gap);StrokeBox(b,x,boxY,boxW,boxH,color,border);Tx(b,x+8,boxY+boxH-13,7,label,color,true);TxRight(b,x+boxW-8,boxY+9,13,value,color,true);}
        Total(0,"VALOR TOTAL",order.PricePending?"A definir":Money(order.OrderTotal),CBlue,1.5);Total(1,"RECEBIDO",Money(order.PaidAmount),CGreen,1.5);
        if(settled)Total(2,"SITUAÇÃO","QUITADO",CGreen,2.5);else Total(2,"PENDENTE",order.PricePending?"A definir":Money(order.RemainingAmount),CRed,2.5);
        Tx(b,L,18,7,$"Pedido #{order.Number} - Emitido em {DateTime.Now:dd/MM/yyyy HH:mm}",CMuted);b.Append("Q\n");
    }

    static IEnumerable<string> PaymentContinuation(IEnumerable<ScheduledOrder> orders)
    {
        foreach(var order in orders)foreach(var chunk in order.Payments.Where(x=>x.SaleId!=null).OrderBy(x=>x.PaidAt).Skip(8).Chunk(30))
        {var b=new StringBuilder();OrderText(b,24,560,12,$"Pedido #{order.Number} - Pagamentos (continuação)",0,0,0,true);double y=530;foreach(var p in chunk){OrderText(b,24,y,9,$"{p.PaidAt:dd/MM/yyyy} - {p.PaymentType} - {Money(p.Amount)}",0,0,0);y-=15;}yield return b.ToString();}
    }
    static void DrawOrderParagraph(StringBuilder b,string value,double y,string field,string customer,double x=20,int width=46,int maxLines=4)
    {
        var lines=new List<string>();
        foreach(var paragraph in (value??"").Replace("\r","").Split('\n'))
        {
            var current="";
            foreach(var word in paragraph.Split(' ',StringSplitOptions.RemoveEmptyEntries))
            {
                var rest=word;
                if(current.Length>0&&current.Length+1+rest.Length>width){lines.Add(current);current="";}
                while(rest.Length>width){lines.Add(rest[..width]);rest=rest[width..];}
                current=current.Length==0?rest:current+" "+rest;
            }
            lines.Add(current);
        }
        if(lines.Count>maxLines)lines=lines.Take(maxLines).ToList();foreach(var line in lines){OrderText(b,x,y,9,line,.15,.15,.14);y-=12;}
    }

    static void OrderText(StringBuilder b,double x,double y,double size,string value,double r,double g,double blue,bool bold=false)=>Text(b,x,y,size,value,r,g,blue,bold);
    static void OrderColoredText(StringBuilder b,double x,double y,double size,string value,double r,double g,double blue,bool bold=false)=>Text(b,x,y,size,value,r,g,blue,bold);
    static void OrderLine(StringBuilder b,double x1,double y1,double x2,double y2,double r,double g,double blue)=>Line(b,x1,y1,x2,y2,r,g,blue);
    // QR no cabeçalho da ficha: escaneado com o celular, abre o pedido no ERP do celular (precisa estar logado; o QR só leva o endereço).
    static void DrawOrderQr(StringBuilder b,ScheduledOrder order,double right)
    {
        if(string.IsNullOrWhiteSpace(OrderLinkBase)||QrCode.Encode(OrderLinkBase+order.Id.ToString("D")) is not bool[,] m)return;
        const double side=52,top=591;var count=m.GetLength(0);var cell=side/count;var x0=right-side;
        b.Append(C(CInk[0])).Append(' ').Append(C(CInk[1])).Append(' ').Append(C(CInk[2])).Append(" rg\n");
        for(var y=0;y<count;y++)for(var x=0;x<count;x++)if(m[y,x])b.Append(C(x0+x*cell)).Append(' ').Append(C(top-(y+1)*cell)).Append(' ').Append(C(cell+.02)).Append(' ').Append(C(cell+.02)).Append(" re\n");
        b.Append("f\n");
        TxRight(b,x0-6,569,6.5,"Abrir no",CMuted);TxRight(b,x0-6,560,6.5,"celular",CMuted);
    }
    static void DrawLogo(StringBuilder b,double x,double y,double width,double height)=>b.Append("q ").Append(C(width)).Append(" 0 0 ").Append(C(height)).Append(' ').Append(C(x)).Append(' ').Append(C(y)).Append(" cm /Logo Do Q\n");
    static void DrawReportLogo(StringBuilder b)
    {
        var logo=CurrentOrderLogo;var width=Math.Min(50d,55d*logo.Width/logo.Height);var height=width*logo.Height/logo.Width;
        DrawLogo(b,PageW-18-width,778,width,height);
    }

    static string ConfigureBestColorPrinting()
    {
        try
        {
            using var server=new LocalPrintServer();using var queue=server.DefaultPrintQueue;var capabilities=queue.GetPrintCapabilities();var ticket=queue.UserPrintTicket??queue.DefaultPrintTicket;var applied=new List<string>();
            if(capabilities.OutputColorCapability?.Contains(OutputColor.Color)==true){ticket.OutputColor=OutputColor.Color;applied.Add("cores");}
            var qualities=capabilities.OutputQualityCapability;
            if(qualities?.Contains(OutputQuality.Photographic)==true){ticket.OutputQuality=OutputQuality.Photographic;applied.Add("qualidade fotográfica");}
            else if(qualities?.Contains(OutputQuality.High)==true){ticket.OutputQuality=OutputQuality.High;applied.Add("qualidade alta");}
            if(capabilities.PageOrientationCapability?.Contains(PageOrientation.Landscape)==true)ticket.PageOrientation=PageOrientation.Landscape;
            var a4=capabilities.PageMediaSizeCapability?.FirstOrDefault(x=>x.PageMediaSizeName==PageMediaSizeName.ISOA4);if(a4!=null)ticket.PageMediaSize=a4;
            queue.UserPrintTicket=ticket;queue.Commit();return applied.Count==0?"configuração máxima aceita pela impressora":string.Join(" e ",applied);
        }
        catch(Exception ex){throw new InvalidOperationException("Não foi possível ativar a impressão colorida em alta qualidade na impressora padrão. Confira se ela está ligada e instalada.",ex);}
    }

    sealed record PdfImage(int Width,int Height,byte[] Rgb,byte[] Alpha);
    static PdfImage LoadOrderLogo()
    {
        using var stream=string.IsNullOrWhiteSpace(LogoPath)?typeof(ReportWriter).Assembly.GetManifestResourceStream("GraficaCaixa.OrderLogo.png")??throw new FileNotFoundException("O logotipo do pedido não foi encontrado no programa."):File.OpenRead(LogoPath);return LoadPdfImage(stream);
    }
    static PdfImage LoadPdfImage(Stream stream)
    {
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var source=decoder.Frames[0];var bitmap=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);var stride=bitmap.PixelWidth*4;var pixels=new byte[stride*bitmap.PixelHeight];bitmap.CopyPixels(pixels,stride,0);var rgb=new byte[bitmap.PixelWidth*bitmap.PixelHeight*3];var alpha=new byte[bitmap.PixelWidth*bitmap.PixelHeight];
        for(int sourceIndex=0,pixel=0,rgbIndex=0;sourceIndex<pixels.Length;sourceIndex+=4,pixel++,rgbIndex+=3){rgb[rgbIndex]=pixels[sourceIndex+2];rgb[rgbIndex+1]=pixels[sourceIndex+1];rgb[rgbIndex+2]=pixels[sourceIndex];alpha[pixel]=pixels[sourceIndex+3];}
        return new PdfImage(bitmap.PixelWidth,bitmap.PixelHeight,Flate(rgb),Flate(alpha));
    }
    static byte[] Flate(byte[] value){using var output=new MemoryStream();using(var zip=new ZLibStream(output,CompressionLevel.SmallestSize,true))zip.Write(value);return output.ToArray();}
    static byte[] PdfStream(string dictionary,byte[] value){var enc=Encoding.Latin1;using var output=new MemoryStream();output.Write(enc.GetBytes($"<< {dictionary} /Length {value.Length} >>\nstream\n"));output.Write(value);output.Write(enc.GetBytes("\nendstream"));return output.ToArray();}

    static string Slug(string value){var normalized=value.Normalize(System.Text.NormalizationForm.FormD);var chars=normalized.Where(c=>System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.NonSpacingMark).Select(c=>char.IsLetterOrDigit(c)?char.ToLowerInvariant(c):'-').ToArray();var slug=new string(chars);while(slug.Contains("--"))slug=slug.Replace("--","-");return slug.Trim('-');}

    static List<string> WrapAll(string value,int width)
    {
        var lines=new List<string>();foreach(var paragraph in (value??"").Replace("\r","").Split('\n')){var current="";foreach(var word in paragraph.Split(' ',StringSplitOptions.RemoveEmptyEntries)){var rest=word;if(current.Length>0&&current.Length+1+rest.Length>width){lines.Add(current);current="";}while(rest.Length>width){lines.Add(rest[..width]);rest=rest[width..];}current=current.Length==0?rest:current+" "+rest;}if(current.Length>0)lines.Add(current);else if(lines.Count==0)lines.Add("");}return lines.Count==0?[""]:lines;
    }

    static List<string> Wrap(string value,int max,int lines){var words=(value??"").Split(' ',StringSplitOptions.RemoveEmptyEntries);var result=new List<string>();var current="";foreach(var word in words){if((current+" "+word).Trim().Length<=max)current=(current+" "+word).Trim();else{if(current.Length>0)result.Add(current);current=word;if(result.Count==lines-1)break;}}if(result.Count<lines&&current.Length>0)result.Add(current);return result;}
    static List<string> BuildPages(CashSession s,CashClosureVersion? closureVersion=null)
    {
        var sales = s.Sales.OrderBy(x => x.CreatedAt).ToList();
        var firstCapacity = PageCapacity(s, true);
        var nextCapacity = PageCapacity(s, false);
        var chunks = new List<List<Sale>>();
        if (sales.Count == 0) chunks.Add([]);
        else
        {
            chunks.Add(sales.Take(firstCapacity).ToList());
            for (var start = firstCapacity; start < sales.Count; start += nextCapacity)
                chunks.Add(sales.Skip(start).Take(nextCapacity).ToList());
        }

        var withdrawalPages=new List<string>();if(s.Withdrawals.Count>0)AddWithdrawalPages(withdrawalPages,s,chunks.Count);if(s.Adjustments.Count>0)AddAdjustmentPages(withdrawalPages,s,chunks.Count);
        var pages = new List<string>();
        for (var page = 0; page < chunks.Count; page++)
        {
            var b = new StringBuilder(); Header(b, s, page + 1, chunks.Count+withdrawalPages.Count,closureVersion); double y = FirstRowY;
            if (page == 0) { Summary(b, s, ref y); y -= 20; PaymentSummary(b, s, ref y); y -= 22; }
            TableHeader(b, ref y); foreach (var x in chunks[page]) { Row(b, x, ref y); }
            Text(b, 42, 28, 8, "Relatório gerado pelo sistema de caixa", .42, .45, .5); Text(b, 500, 28, 8, $"Página {page + 1}", .42, .45, .5); pages.Add(b.ToString());
        }
        pages.AddRange(withdrawalPages);
        return pages;
    }

    static string Signed(decimal value)=>(value>0?"+":"")+Money(value);
    static void AddAdjustmentPages(List<string> pages,CashSession session,int precedingPages)
    {
        StringBuilder b=new();double y=0;
        void Header(){b=new StringBuilder();Text(b,42,790,18,"AJUSTES DO CAIXA",.08,.3,.3,true);Text(b,42,766,10,$"Caixa {session.OpenedAt:dd/MM/yyyy HH:mm} - correções do dinheiro esperado",.25,.25,.25);Text(b,42,742,11,$"Total ajustado: {Signed(session.AdjustmentsTotal)}",.1,.1,.1,true);DrawReportLogo(b);y=710;}
        void Finish(){Text(b,42,28,8,"Ajustes corrigem o dinheiro esperado. Não são vendas nem recebimentos.",.4,.4,.4);Text(b,500,28,8,$"Página {precedingPages+pages.Count+1}",.4,.4,.4);pages.Add(b.ToString());}
        Header();
        foreach(var adjustment in session.Adjustments.OrderBy(x=>x.CreatedAt))
        {
            var lines=WrapAll(adjustment.Reason,85);
            if(y<100){Finish();Header();}
            Text(b,42,y,10,$"{adjustment.CreatedAt:dd/MM/yyyy HH:mm} - {Signed(adjustment.Amount)}"+(string.IsNullOrWhiteSpace(adjustment.UserName)?"":$" - {adjustment.UserName}"),.1,.1,.1,true);y-=18;
            foreach(var line in lines){if(y<64){Finish();Header();}Text(b,42,y,9,line,.25,.25,.25);y-=14;}y-=14;
        }
        Finish();
    }

    static void AddWithdrawalPages(List<string> pages,CashSession session,int precedingPages)
    {
        StringBuilder b=new();double y=0;
        void Header(){b=new StringBuilder();Text(b,42,790,18,"SANGRIAS DO CAIXA",.08,.3,.3,true);Text(b,42,766,10,$"Caixa {session.OpenedAt:dd/MM/yyyy HH:mm} - retiradas de dinheiro físico",.25,.25,.25);Text(b,42,742,11,$"Total retirado: {Money(session.WithdrawalsTotal)}",.1,.1,.1,true);DrawReportLogo(b);y=710;}
        void Finish(){Text(b,42,28,8,"Sangrias reduzem o dinheiro esperado. Não são vendas nem recebimentos.",.4,.4,.4);Text(b,500,28,8,$"Página {precedingPages+pages.Count+1}",.4,.4,.4);pages.Add(b.ToString());}
        Header();
        foreach(var withdrawal in session.Withdrawals.OrderBy(x=>x.CreatedAt))
        {
            var lines=WrapAll(withdrawal.Reason,85);
            if(y<100){Finish();Header();}
            Text(b,42,y,10,$"{withdrawal.CreatedAt:dd/MM/yyyy HH:mm} - {Money(withdrawal.Amount)}",.1,.1,.1,true);y-=18;
            foreach(var line in lines){if(y<64){Finish();Header();}Text(b,42,y,9,line,.25,.25,.25);y-=14;}y-=14;
        }
        Finish();
    }

    static int PageCapacity(CashSession s, bool firstPage)
    {
        var scratch = new StringBuilder();
        double y = FirstRowY;
        if (firstPage) { Summary(scratch, s, ref y); y -= 20; PaymentSummary(scratch, s, ref y); y -= 22; }
        TableHeader(scratch, ref y);
        return Math.Max(1, (int)Math.Floor((y - SafeBottomY) / RowHeight) + 1);
    }
    // UX 2.0 (pacote 6): cabeçalho claro (logo à esquerda, tipo do documento e versão à direita, filete colorido).
    static void ReportHeader(StringBuilder b,string title,string chip,double[] chipColor,string line1,string? line2,string? line3,int page,int total)
    {
        var logo=CurrentOrderLogo;var lw=92d;var lh=Math.Min(52d,lw*logo.Height/logo.Width);if(lh<lw*logo.Height/logo.Width)lw=lh*logo.Width/logo.Height;DrawLogo(b,42,842-20-lh,lw,lh);
        TxRight(b,553,818,8.5,title,CMuted,true);var cw=TextWidth(chip,9,true)+18;FillBox(b,553-cw,790,cw,19,chipColor);Text(b,553-cw+9,796,9,chip,1,1,1,true);TxRight(b,553,775,9,$"{page}/{total}",CMuted);
        LineW(b,42,762,300,762,CTeal,2.5);LineW(b,300,762,553,762,CBlue,2.5);
        Tx(b,42,745,9,line1,CInk);if(line2!=null)Tx(b,42,732,9,line2,CMuted);if(line3!=null)Tx(b,42,719,9,line3,CMuted);
    }
    static void Header(StringBuilder b, CashSession s, int page, int total,CashClosureVersion? closureVersion)
    {
        var retified=closureVersion?.Version>1;
        ReportHeader(b,"RELATÓRIO DE FECHAMENTO DE CAIXA",retified?$"RETIFICADO · v{closureVersion!.Version}":"ORIGINAL · v1",CTeal,$"Caixa aberto em {s.OpenedAt:dd/MM/yyyy 'às' HH:mm} e fechado em {s.ClosedAt:dd/MM/yyyy 'às' HH:mm}",$"Responsável pela abertura: {s.OpenedBy}      Responsável pelo fechamento: {s.ClosedBy}",retified?$"{closureVersion!.UserName}: {closureVersion.Reason}":null,page,total);
    }
    static void Summary(StringBuilder b, CashSession s, ref double y)
    {
        Text(b, 42, y, 12, "RESUMO FINANCEIRO", .12, .16, .23, true); y -= 58;
        Box(b, 42, y, 118, 48, "FUNDO INICIAL", s.OpeningAmount); Box(b, 173, y, 118, 48, "TOTAL VENDIDO", s.SalesTotal, CBlue); Box(b, 304, y, 118, 48, "DINHEIRO ESPERADO", s.ExpectedCash); DifferenceBox(b, 435, y, 118, 48, (s.DeclaredClosingAmount ?? 0) - s.ExpectedCash); y -= 72;
        Text(b,42,y+19,9,$"Sangrias: {Money(s.WithdrawalsTotal)} (retiradas de dinheiro)"+(s.Adjustments.Count>0?$" - Ajustes de caixa: {Signed(s.AdjustmentsTotal)}":""),.25,.28,.33,true);Text(b, 42, y, 9, $"Dinheiro contado no fechamento: {Money(s.DeclaredClosingAmount ?? 0)}", .25, .28, .33, true); Text(b, 330, y, 9, $"Quantidade de lançamentos: {s.Sales.Count}", .25, .28, .33, true); y -= 28;
    }
    static void PaymentSummary(StringBuilder b, CashSession s, ref double y)
    {
        Text(b, 42, y, 11, "TOTAIS POR FORMA DE PAGAMENTO", .12, .16, .23, true); y -= 24; var x = 42d;
        foreach (var g in s.Sales.GroupBy(v => v.PaymentType)) { Rect(b, x, y - 20, 120, 34, .96, .97, .98); Text(b, x + 8, y, 8, Limit(g.Key, 19), .38, .41, .47); Text(b, x + 8, y - 13, 10, Money(g.Sum(v => v.Total)), .12, .16, .23, true); x += 128; if (x > 470) { x = 42; y -= 42; } } y -= 35;
    }
    static void TableHeader(StringBuilder b, ref double y) { Text(b, 42, y, 11, "DETALHAMENTO DOS LANÇAMENTOS", .12, .16, .23, true); y -= 26; Rect(b, 42, y - 17, 511, 28, CTeal[0], CTeal[1], CTeal[2]); string[] h = ["DATA", "CLIENTE", "PRODUTO", "QTD.", "PAGAMENTO", "TOTAL"]; double[] x = [48, 105, 210, 365, 405, 500]; for (var i = 0; i < h.Length; i++) Text(b, x[i], y - 7, 7, h[i], 1, 1, 1, true); y -= 34; }
    static void Row(StringBuilder b, Sale s, ref double y) { Line(b, 42, y - 12, 553, y - 12, .88, .89, .91); Text(b, 48, y, 7.5, s.CreatedAt.ToString("dd/MM HH:mm"), .25, .28, .33); Text(b, 105, y, 7.5, Limit(s.CustomerName, 18), .25, .28, .33); Text(b, 210, y, 7.5, Limit(s.Product, 26), .25, .28, .33); Text(b, 365, y, 7.5, s.Units.ToString(), .25, .28, .33); Text(b, 405, y, 7.5, Limit(s.PaymentType, 15), .25, .28, .33); Text(b, 500, y, 7.5, Money(s.Total), .10, .13, .19, true); y -= 24; }
    static void Box(StringBuilder b, double x, double y, double w, double h, string title, decimal value, double[]? tone = null) { var c = tone ?? new[] { .42, .45, .5 }; if (tone != null) FillBox(b, x, y, w, h, Tint(tone, .14)); else Rect(b, x, y, w, h, .96, .97, .99); Text(b, x + 10, y + 31, 7, title, c[0], c[1], c[2], true); Text(b, x + 10, y + 12, 12, Money(value), tone != null ? tone[0] : .12, tone != null ? tone[1] : .16, tone != null ? tone[2] : .23, true); }
    static void DifferenceBox(StringBuilder b, double x, double y, double w, double h, decimal value) { var c = value == 0 ? CGreen : value < 0 ? CRed : CAmber; var word = value == 0 ? "CONFERE" : value < 0 ? "FALTA" : "SOBRA"; FillBox(b, x, y, w, h, c); Text(b, x + 10, y + 31, 7, "DIFERENÇA — " + word, 1, 1, 1, true); Text(b, x + 10, y + 12, 12, Money(value), 1, 1, 1, true); }
    static string Money(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"));
    static string Limit(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
    static void Text(StringBuilder b, double x, double y, double size, string text, double r, double g, double bl, bool bold = false) => b.Append(C(r)).Append(' ').Append(C(g)).Append(' ').Append(C(bl)).Append(" rg BT /").Append(bold ? "F2" : "F1").Append(' ').Append(C(size)).Append(" Tf ").Append(C(x)).Append(' ').Append(C(y)).Append(" Td (").Append(Escape(text)).Append(") Tj ET\n");
    static void Rect(StringBuilder b, double x, double y, double w, double h, double r, double g, double bl) => b.Append(C(r)).Append(' ').Append(C(g)).Append(' ').Append(C(bl)).Append(" rg ").Append(C(x)).Append(' ').Append(C(y)).Append(' ').Append(C(w)).Append(' ').Append(C(h)).Append(" re f\n");
    static void Line(StringBuilder b, double x1, double y1, double x2, double y2, double r, double g, double bl) => b.Append(C(r)).Append(' ').Append(C(g)).Append(' ').Append(C(bl)).Append(" RG .5 w ").Append(C(x1)).Append(' ').Append(C(y1)).Append(" m ").Append(C(x2)).Append(' ').Append(C(y2)).Append(" l S\n");
    static string C(double n) => n.ToString("0.##", CultureInfo.InvariantCulture); static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\u2014", "\u0097").Replace("\u2013", "\u0096").Replace("\u2022", "\u0095").Replace("\u2026", "\u0085").Replace("\u201C", "\u0093").Replace("\u201D", "\u0094").Replace("\u2018", "\u0091").Replace("\u2019", "\u0092");
    static byte[] MakePdf(List<string> pages)=>MakePdf(pages,PageW,PageH,CurrentOrderLogo);
    static byte[] MakePdf(List<string> pages,double pageWidth,double pageHeight,PdfImage? logo=null,PdfImage? stamp=null)
    {
        var enc = Encoding.Latin1; var objects = new List<byte[]>(); void Add(string s) => objects.Add(enc.GetBytes(s));
        var firstPageId=5+(logo is null?0:2)+(stamp is null?0:2);Add("<< /Type /Catalog /Pages 2 0 R >>");var kids=string.Join(" ",Enumerable.Range(0,pages.Count).Select(i=>$"{firstPageId+i*2} 0 R"));Add($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        if(logo is not null){objects.Add(PdfStream($"/Type /XObject /Subtype /Image /Width {logo.Width} /Height {logo.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode",logo.Alpha));objects.Add(PdfStream($"/Type /XObject /Subtype /Image /Width {logo.Width} /Height {logo.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /SMask 5 0 R",logo.Rgb));}
        var stampId=0;if(stamp is not null){var maskId=objects.Count+1;objects.Add(PdfStream($"/Type /XObject /Subtype /Image /Width {stamp.Width} /Height {stamp.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode",stamp.Alpha));stampId=objects.Count+1;objects.Add(PdfStream($"/Type /XObject /Subtype /Image /Width {stamp.Width} /Height {stamp.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /SMask {maskId} 0 R",stamp.Rgb));}
        foreach(var p in pages){var contentId=objects.Count+2;var xObject=logo is null&&stamp is null?"":" /XObject << "+(logo is null?"":"/Logo 6 0 R ")+(stamp is null?"":$"/Stamp {stampId} 0 R ")+">>";Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {C(pageWidth)} {C(pageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >>{xObject} >> /Contents {contentId} 0 R >>");objects.Add(PdfStream("",enc.GetBytes(p)));}
        using var ms = new MemoryStream(); void W(string s) { var z = enc.GetBytes(s); ms.Write(z); } W("%PDF-1.4\n%âãÏÓ\n"); var offsets = new List<long> { 0 }; for (var i = 0; i < objects.Count; i++) { offsets.Add(ms.Position); W($"{i + 1} 0 obj\n"); ms.Write(objects[i]); W("\nendobj\n"); } var xref = ms.Position; W($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"); for (var i = 1; i <= objects.Count; i++) W($"{offsets[i]:D10} 00000 n \n"); W($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF"); return ms.ToArray();
    }
}
