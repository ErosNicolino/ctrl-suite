using System.Text.Json;
using Microsoft.Data.Sqlite;

static class DbAudit
{
    public static void Run(string path)
    {
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();
        string Scalar(string sql){using var cmd=c.CreateCommand();cmd.CommandText=sql;return cmd.ExecuteScalar()?.ToString()??"";}
        Console.WriteLine("integrity_check="+Scalar("PRAGMA integrity_check"));
        Console.WriteLine("Clientes="+Scalar("SELECT COUNT(*) FROM customers"));
        Console.WriteLine("Pedidos="+Scalar("SELECT COUNT(*) FROM scheduled_orders"));
        Console.WriteLine("VendasLancamentos="+Scalar("SELECT COUNT(*) FROM sales"));
        Console.WriteLine("CaixasAbertos="+Scalar("SELECT COUNT(*) FROM cash_sessions WHERE closed_at IS NULL OR trim(closed_at)=''"));
        Console.WriteLine("LancamentosCaixaAberto="+Scalar("SELECT COUNT(*) FROM sales s JOIN cash_sessions c ON c.id=s.session_id WHERE c.closed_at IS NULL OR trim(c.closed_at)=''"));
        Console.WriteLine("Auditorias="+Scalar("SELECT COUNT(*) FROM audit_trail"));
        using var meta=c.CreateCommand();meta.CommandText="SELECT key,value FROM metadata WHERE key IN ('products_v1','prompt12_v1','budget_rules')";using var r=meta.ExecuteReader();
        var products=0;var quotes=0;var linked=0;var quoteNumbers=new List<string>();var budgetProducts=new List<string>();
        while(r.Read())
        {
            using var doc=JsonDocument.Parse(r.GetString(1));if(r.GetString(0)=="products_v1"&&doc.RootElement.ValueKind==JsonValueKind.Array)products=doc.RootElement.GetArrayLength();
            if(r.GetString(0)=="prompt12_v1"&&doc.RootElement.TryGetProperty("Quotes",out var q)&&q.ValueKind==JsonValueKind.Array)foreach(var item in q.EnumerateArray()){quotes++;if(item.TryGetProperty("ConvertedOrderId",out var id)&&id.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(id.GetString()))linked++;if(item.TryGetProperty("Number",out var number))quoteNumbers.Add(number.ToString());}
            if(r.GetString(0)=="budget_rules"&&doc.RootElement.ValueKind==JsonValueKind.Array)foreach(var item in doc.RootElement.EnumerateArray())if(item.TryGetProperty("Product",out var name))budgetProducts.Add(name.GetString()??"");
        }
        Console.WriteLine("Produtos="+products);Console.WriteLine("OrcamentosProfissionais="+quotes);Console.WriteLine("OrcamentosProfissionaisComPedido="+linked);Console.WriteLine("NumerosOrcamentosProfissionais="+string.Join(',',quoteNumbers));Console.WriteLine("ProdutosParametrosOrcamento="+string.Join(" | ",budgetProducts));
    }
}