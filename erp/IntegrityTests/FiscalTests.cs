using GraficaCaixa;
using Microsoft.Data.Sqlite;

// NF registrada em dobro: a migração única tira só as cópias idênticas, mantém a primeira, audita e não roda de novo.
public static class FiscalTests
{
    static int passed;
    static void Check(bool ok,string name){if(!ok)throw new Exception("FALHOU: "+name);Console.WriteLine("OK "+name);passed++;}
    public static void Run()
    {
        using var c=new SqliteConnection("Data Source=:memory:");c.Open();
        void Sql(string text){using var cmd=c.CreateCommand();cmd.CommandText=text;cmd.ExecuteNonQuery();}
        string Value(string text){using var cmd=c.CreateCommand();cmd.CommandText=text;return cmd.ExecuteScalar()?.ToString()??"";}
        Sql("CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);CREATE TABLE audit_trail(id TEXT PRIMARY KEY,timestamp TEXT NOT NULL,action TEXT NOT NULL,description TEXT NOT NULL,computer TEXT NOT NULL,sale_id TEXT,user_name TEXT,entity_type TEXT,entity_id TEXT,old_value TEXT,new_value TEXT,reason TEXT,user_id TEXT,user_login TEXT,session_id TEXT)");
        var order=Guid.NewGuid();string Doc(string number,decimal amount)=>$"{{\"Id\":\"{Guid.NewGuid()}\",\"OrderId\":\"{order}\",\"Number\":\"{number}\",\"Date\":\"2026-10-26T00:00:00-03:00\",\"Amount\":{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"Type\":\"NFS\",\"Notes\":\"\",\"PdfPath\":\"D:\\nf.pdf\",\"XmlPath\":\"\"}}";
        Sql($"INSERT INTO metadata VALUES('prompt12_v1','{{\"Losses\":[],\"FiscalDocuments\":[{Doc("469",1993.00m)},{Doc("469",1993.00m)},{Doc("470",1993.00m)},{Doc("469",10m)}]}}')");
        Check(FiscalDedupe.Apply(c)==1,"remove só a cópia idêntica da NF 469");
        Check(Value("SELECT json_array_length(value,'$.FiscalDocuments') FROM metadata WHERE key='prompt12_v1'")=="3","mantém a primeira, a NF 470 e a 469 de outro valor");
        Check(Value("SELECT json_array_length(value,'$.Losses') FROM metadata WHERE key='prompt12_v1'")=="0","resto do estado preservado");
        Check(Value("SELECT COUNT(*) FROM audit_trail WHERE action='Documento fiscal duplicado removido' AND old_value LIKE '%469%'")=="1","remoção registrada na auditoria com a cópia removida");
        Sql($"UPDATE metadata SET value=json_insert(value,'$.FiscalDocuments[#]',json('{Doc("470",1993.00m)}')) WHERE key='prompt12_v1'");
        Check(FiscalDedupe.Apply(c)==0&&Value("SELECT json_array_length(value,'$.FiscalDocuments') FROM metadata WHERE key='prompt12_v1'")=="4","roda uma vez só (marca fiscal_dedupe_v1)");
        Console.WriteLine($"TOTAL: {passed} verificações fiscais.");
    }
}
