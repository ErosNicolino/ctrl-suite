using System.Globalization;
using System.Text;

namespace GraficaCaixa;

// "Pix copia e cola" estático (BR Code do Banco Central, padrão EMV) com o valor da cobrança.
// Gerado no próprio ERP a partir da chave Pix da gráfica: não usa API do banco e não precisa de internet.
public static class PixBrCode
{
    // Chave como o banco espera: CPF/CNPJ só dígitos, telefone em +55DDDNÚMERO, e-mail em minúsculas, aleatória como digitada.
    public static string NormalizeKey(string? key)
    {
        var value=(key??"").Trim();if(value.Length==0)return "";
        if(value.Contains('@'))return value.ToLowerInvariant();
        if(System.Text.RegularExpressions.Regex.IsMatch(value,"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"))return value.ToLowerInvariant();
        var digits=new string(value.Where(char.IsAsciiDigit).ToArray());
        if(value.StartsWith('+'))return "+"+digits;
        if(digits.Length is 11 or 14&&value.IndexOfAny(['.','/','-'])>=0&&!value.Contains('('))return digits;
        if(digits.Length==14)return digits;
        return digits.Length==11&&IsCpf(digits)?digits:digits.Length is 10 or 11?"+55"+digits:digits.Length>0?digits:value;
    }

    public static string? Validate(string? key)
    {
        var value=NormalizeKey(key);if(value.Length==0)return null;
        if(value.Length>77)return "Chave Pix longa demais.";
        if(value.Contains('@'))return value.Count(c=>c=='@')==1&&value.IndexOf('@')>0&&value.Contains('.')?null:"E-mail da chave Pix inválido.";
        if(value.StartsWith('+'))return value.Length is 13 or 14?null:"Telefone da chave Pix: use +55, DDD e número.";
        if(value.Length==36&&value.Count(c=>c=='-')==4)return null;
        if(value.Length==11)return IsCpf(value)?null:"CPF da chave Pix inválido.";
        if(value.Length==14)return null;
        return "Chave Pix não reconhecida. Use CPF/CNPJ, e-mail, telefone com +55 ou a chave aleatória.";
    }

    // Trecho {pix} das mensagens: vazio sem chave Pix válida ou sem saldo a pagar.
    public static string MessageBlock(CompanyProfile? company,decimal amount,string? reference=null)
    {
        var key=company?.PixKey??"";
        if(company==null||amount<=0||NormalizeKey(key).Length==0||Validate(key)!=null)return "";
        return $"\n\nPara pagar por Pix ({amount.ToString("C2",CultureInfo.GetCultureInfo("pt-BR"))}), copie o código abaixo e cole no app do banco:\n{Build(key,company.Name,company.City,amount,reference)}";
    }

    public static string Build(string key,string merchant,string city,decimal amount,string? reference=null)
    {
        static string F(string id,string value)=>id+value.Length.ToString("00")+value;
        var account=F("00","br.gov.bcb.pix")+F("01",NormalizeKey(key));
        var txid=new string((reference??"").Where(char.IsAsciiLetterOrDigit).Take(25).ToArray());
        var body=F("00","01")+F("26",account)+F("52","0000")+F("53","986")+(amount>0?F("54",amount.ToString("0.00",CultureInfo.InvariantCulture)):"")
            +F("58","BR")+F("59",Ascii(merchant,25,"GRAFICA"))+F("60",Ascii(city,15,"SAO PAULO"))+F("62",F("05",txid.Length>0?txid:"***"))+"6304";
        return body+Crc16(body).ToString("X4");
    }

    // Nome e cidade sem acento e em maiúsculas (alguns aplicativos de banco recusam acentos no BR Code).
    static string Ascii(string text,int max,string fallback)
    {
        var plain=string.Concat((text??"").Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)).ToUpperInvariant();
        plain=new string(plain.Where(c=>c<128&&(char.IsLetterOrDigit(c)||c==' ')).ToArray()).Trim();
        if(plain.Length==0)plain=fallback;return plain.Length>max?plain[..max].Trim():plain;
    }

    public static ushort Crc16(string text)
    {
        ushort crc=0xFFFF;
        foreach(var b in Encoding.UTF8.GetBytes(text)){crc^=(ushort)(b<<8);for(int i=0;i<8;i++)crc=(crc&0x8000)!=0?(ushort)((crc<<1)^0x1021):(ushort)(crc<<1);}
        return crc;
    }

    static bool IsCpf(string d)
    {
        if(d.Length!=11||d.Distinct().Count()==1)return false;
        int Digit(int n){var sum=0;for(int i=0;i<n;i++)sum+=(d[i]-'0')*(n+1-i);var r=sum*10%11;return r==10?0:r;}
        return Digit(9)==d[9]-'0'&&Digit(10)==d[10]-'0';
    }
}
