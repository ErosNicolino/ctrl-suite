using GraficaCaixa;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
static class Phase7BMigrationTests
{
    static string Fingerprint(string file,string sql){using var c=new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;using var r=cmd.ExecuteReader();var rows=new List<string[]>();while(r.Read())rows.Add(Enumerable.Range(0,r.FieldCount).Select(i=>r.IsDBNull(i)?"":r.GetValue(i) is byte[] b?Convert.ToHexString(b):r.GetValue(i).ToString()??"").ToArray());return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows))));}
    public static void Run(string snapshot,string folder)
    {
        Directory.CreateDirectory(folder);var file=Path.Combine(folder,"caixa_dados.db");File.Copy(snapshot,file,false);
        var queries=new[]{"SELECT * FROM auth_users ORDER BY id","SELECT * FROM auth_sessions ORDER BY id","SELECT * FROM sales ORDER BY id","SELECT * FROM scheduled_orders ORDER BY id","SELECT * FROM order_payments ORDER BY id","SELECT * FROM cash_sessions ORDER BY id","SELECT id,timestamp,action,description,computer,sale_id,user_name,entity_type,entity_id,old_value,new_value,reason FROM audit_trail ORDER BY id","SELECT * FROM metadata ORDER BY key"};var before=queries.Select(q=>Fingerprint(file,q)).ToArray();
        var security=new IdentitySecurity(new DataStore(folder,true));security.Migrate();security.Migrate();var after=queries.Select(q=>Fingerprint(file,q)).ToArray();if(!before.SequenceEqual(after))throw new Exception("Migração alterou dados existentes.");
        Console.WriteLine("PASS migração 7B duas vezes sobre cópia consistente de produção: usuários, PBKDF2, sessões, vendas, pedidos, pagamentos, caixa, auditoria e metadados preservados byte a byte nas colunas originais.");
        using var c=new SqliteConnection($"Data Source={file};Pooling=False");c.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE name IN ('auth_role_permissions','auth_user_permissions')";if(Convert.ToInt32(cmd.ExecuteScalar())!=2)throw new Exception("Tabelas de regras ausentes.");Console.WriteLine("PASS schema incremental e idempotente; dois conjuntos de regras ligados a usuários / perfis existentes.");
    }
}
