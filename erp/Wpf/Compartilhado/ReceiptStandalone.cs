using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Globalization;
namespace GraficaCaixa;

public sealed record StandaloneReceipt(string Number,string Payer,string Document,decimal Amount,string Reference,string PaymentType,DateTime PaidAt,string City,string Notes);

public static partial class ReportWriter
{
    // Recibo avulso (cliente pede recibo com CNPJ/CPF para a empresa): mesmo cabeçalho, logo e carimbo do recibo de pedido.
    public static string WriteStandaloneReceipt(StandaloneReceipt r,string outputPath)
    {
        const double width=595.28,height=419.53,left=28,right=567.28;
        var stampImage=CurrentStamp;var stamp=stampImage??ReceiptStamp.Value;const double stampWidth=135,stampY=46.5;var stampHeight=stampWidth*stamp.Height/stamp.Width;
        var b=new StringBuilder();var pt=CultureInfo.GetCultureInfo("pt-BR");
        double Measure(string value,double size,bool bold=false)=>TextWidth(value,size,bold);
        List<string> Lines(string value,double available,double size,bool bold=false)
        {
            var result=new List<string>();
            foreach(var paragraph in (value??"").Replace("\r","").Split('\n'))
            {
                var line="";
                foreach(var word in paragraph.Split(' ',StringSplitOptions.RemoveEmptyEntries)){if(line.Length>0&&Measure(line+" "+word,size,bold)>available){result.Add(line);line="";}line=line.Length==0?word:line+" "+word;}
                if(line.Length>0)result.Add(line);
            }
            return result.Count>0?result:[""];
        }
        void Center(string value,double at,double size,bool bold=false)=>Text(b,148+(right-148-Measure(value,size,bold))/2,at,size,value,.13,.18,.2,bold);
        // UX 2.0 (pacote 6): faixa colorida no topo, título em selo e valor em caixa sólida; mesmo texto de antes.
        Rect(b,0,height-6,width*.7,6,CTeal[0],CTeal[1],CTeal[2]);Rect(b,width*.7,height-6,width*.3,6,CBlue[0],CBlue[1],CBlue[2]);
        var logo=CurrentOrderLogo;var lw=96d;var lh=lw*logo.Height/logo.Width;
        DrawLogo(b,left,height-80,lw,lh);
        Center(Company.Name,height-29,14,true);
        Center(CompanyLine("CNPJ: ",Company.Document),height-45,9);
        Center(Company.Address,height-59,9);
        Center(CompanyLine("Telefone: ",Company.Phone),height-73,9);
        Center(CompanyLine("Email: ",Company.Email),height-85,9);
        LineW(b,left,height-96,right,height-96,CTeal,1.5);
        FillBox(b,left,height-124,84,24,CTeal);Text(b,left+12,height-117,16,"RECIBO",1,1,1,true);
        Text(b,left,height-138,8,$"Nº {r.Number} - Emitido em {DateTime.Now:dd/MM/yyyy HH:mm}",.35,.35,.35);
        // Valor em destaque à direita do título.
        var valueText=Money(r.Amount);FillBox(b,right-170,height-142,170,38,CTeal);Text(b,right-162,height-116,8,"VALOR",.8,.93,.91,true);Text(b,right-8-Measure(valueText,17,true),height-135,17,valueText,1,1,1,true);
        var y=height-166d;
        void Paragraph(string value,double size=10,bool bold=false,double limit=right-left){foreach(var line in Lines(value,limit,size,bold)){Text(b,left,y,size,line,.12,.15,.18,bold);y-=size+5;}y-=4;}
        var who=r.Document.Length>0?$"{r.Payer}, {DocumentLabel(r.Document)} {r.Document},":$"{r.Payer},";
        Paragraph($"Recebemos de {who} a importância de {valueText} ({AmountInWords(r.Amount)}), referente a {r.Reference.TrimEnd('.')}.");
        Paragraph($"Forma de pagamento: {r.PaymentType}   -   Data do pagamento: {r.PaidAt:dd/MM/yyyy}",9);
        if(!string.IsNullOrWhiteSpace(r.Notes))Paragraph("Observações: "+r.Notes.Trim(),9,false,315-left);
        Paragraph("Para clareza, firmamos o presente recibo, dando plena quitação do valor acima.",9,false,315-left);
        Text(b,left,78,10,$"{r.City}, {r.PaidAt.ToString("dd 'de' MMMM 'de' yyyy",pt)}.",.12,.15,.18);
        if(stampImage!=null)b.Append("q ").Append(C(stampWidth)).Append(" 0 0 ").Append(C(stampHeight)).Append(' ').Append(C(355+(right-355-stampWidth)/2)).Append(' ').Append(C(stampY)).Append(" cm /Stamp Do Q\n");
        LineW(b,355,46,right,46,CInk,1);
        Text(b,355+(right-355-Measure("Assinatura / Carimbo",9))/2,33,9,"Assinatura / Carimbo",.3,.3,.3);
        Text(b,left,17,7,$"Recibo Nº {r.Number} - Documento não fiscal",.4,.4,.4);
        var full=Path.GetFullPath(outputPath);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full,MakePdf([$"q 1 0 0 1 0 {C(841.89-height)} cm\n{b}Q\n"],width,841.89,logo,stampImage));return full;
    }
    static string CompanyLine(string label,string value)=>string.IsNullOrWhiteSpace(value)?"":label+value.Trim();
    static string DocumentLabel(string document)=>PhoneText.Digits(document).Length==14?"CNPJ":PhoneText.Digits(document).Length==11?"CPF":"documento";

    // Valor por extenso em reais: 1.234,56 → "mil duzentos e trinta e quatro reais e cinquenta e seis centavos".
    public static string AmountInWords(decimal value)
    {
        value=Math.Round(Math.Abs(value),2);var reais=(long)Math.Floor(value);var cents=(int)Math.Round((value-reais)*100);
        var parts=new List<string>();
        if(reais>0)parts.Add(NumberInWords(reais)+(reais%1_000_000==0?" de":"")+(reais==1?" real":" reais"));
        if(cents>0)parts.Add(NumberInWords(cents)+(cents==1?" centavo":" centavos"));
        return parts.Count==0?"zero real":string.Join(" e ",parts);
    }
    static readonly string[] Units=["zero","um","dois","três","quatro","cinco","seis","sete","oito","nove","dez","onze","doze","treze","catorze","quinze","dezesseis","dezessete","dezoito","dezenove"];
    static readonly string[] Tens=["","","vinte","trinta","quarenta","cinquenta","sessenta","setenta","oitenta","noventa"];
    static readonly string[] Hundreds=["","cento","duzentos","trezentos","quatrocentos","quinhentos","seiscentos","setecentos","oitocentos","novecentos"];
    static string Below1000(int n)
    {
        if(n==100)return "cem";
        var words=new List<string>();if(n>=100){words.Add(Hundreds[n/100]);n%=100;}
        if(n>=20){words.Add(Tens[n/10]);n%=10;if(n>0)words.Add(Units[n]);}else if(n>0)words.Add(Units[n]);
        return string.Join(" e ",words);
    }
    public static string NumberInWords(long n)
    {
        if(n==0)return "zero";
        var groups=new List<(long Value,string Singular,string Plural)>{(n/1_000_000_000,"bilhão","bilhões"),(n/1_000_000%1000,"milhão","milhões"),(n/1000%1000,"mil","mil"),(n%1000,"","")};
        var words=new List<string>();
        for(var i=0;i<groups.Count;i++)
        {
            var (v,singular,plural)=groups[i];if(v==0)continue;
            var text=singular=="mil"&&v==1?"mil":singular.Length==0?Below1000((int)v):$"{Below1000((int)v)} {(v==1?singular:plural)}";
            if(words.Count>0&&i==groups.Count-1&&(v<100||v%100==0))words.Add("e");
            words.Add(text);
        }
        return string.Join(" ",words);
    }
}
