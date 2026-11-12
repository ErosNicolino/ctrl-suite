using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
namespace GraficaCaixa;

public static class LegacyCostMigration
{
    static JsonObject? Read(SqliteConnection c)
    {
        using var command=c.CreateCommand();command.CommandText="SELECT value FROM metadata WHERE key='order_details_v1'";
        return command.ExecuteScalar() is string text?JsonNode.Parse(text) as JsonObject:null;
    }
    public static void ArchiveForRead(SqliteConnection c,string backupFolder)
    {
        var details=Read(c);if(details!=null&&details.Any(x=>x.Value is JsonObject item&&item.ContainsKey("MaterialCost")))Archive(c,details,backupFolder);
    }
    static string Archive(SqliteConnection c,JsonObject details,string folder)
    {
        var orders=new Dictionary<string,(long Number,string Customer)>();
        using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT id,order_number,customer_name FROM scheduled_orders";using var reader=cmd.ExecuteReader();while(reader.Read())orders[reader.GetString(0)]=(reader.GetInt64(1),reader.GetString(2));}
        var entries=details.Where(x=>x.Value is JsonObject item&&item["MaterialCost"]!=null).Select(x=>new {PedidoId=x.Key,Numero=orders.GetValueOrDefault(x.Key).Number,Cliente=orders.GetValueOrDefault(x.Key).Customer,CustoGlobalAntigo=x.Value!["MaterialCost"]!.DeepClone()}).ToList();
        var text=JsonSerializer.Serialize(entries,new JsonSerializerOptions{WriteIndented=true});Directory.CreateDirectory(folder);
        var hash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var path=Path.Combine(folder,"custos-globais-antigos-"+hash+".json");
        if(!File.Exists(path)){using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);using var writer=new StreamWriter(file);writer.Write(text);writer.Flush();file.Flush(true);}
        return path;
    }
    public static void Retire(SqliteConnection c,string folder)
    {
        var details=Read(c);if(details==null||!details.Any(x=>x.Value is JsonObject item&&item.ContainsKey("MaterialCost")))return;
        Directory.CreateDirectory(folder);
        var backup=Path.Combine(folder,"antes-remover-custo-global-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".db");
        using(var destination=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=backup,Pooling=false}.ToString())){destination.Open();c.BackupDatabase(destination);}
        var archive=Archive(c,details,folder);
        foreach(var item in details.Select(x=>x.Value).OfType<JsonObject>())item.Remove("MaterialCost");
        using var tx=c.BeginTransaction();
        using(var cmd=c.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="UPDATE metadata SET value=@value WHERE key='order_details_v1'";cmd.Parameters.AddWithValue("@value",details.ToJsonString());cmd.ExecuteNonQuery();}
        using(var cmd=c.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="INSERT INTO metadata(key,value) VALUES('data_version','1') ON CONFLICT(key) DO UPDATE SET value=CAST(CAST(value AS INTEGER)+1 AS TEXT)";cmd.ExecuteNonQuery();}
        using(var cmd=c.CreateCommand())
        {
            cmd.Transaction=tx;cmd.CommandText="INSERT INTO audit_trail(id,timestamp,action,description,computer,sale_id,user_name,entity_type,entity_id,old_value,new_value,reason) VALUES(@id,@at,'Custo global antigo removido',@description,@computer,NULL,'Sistema','Migração','custo-global',@old,'Custo por item','Campo obsoleto; valores exportados antes da remoção')";
            cmd.Parameters.AddWithValue("@id",Guid.NewGuid().ToString());cmd.Parameters.AddWithValue("@at",DateTime.Now.ToString("O"));cmd.Parameters.AddWithValue("@description","Valores preservados em "+archive);cmd.Parameters.AddWithValue("@computer",Environment.MachineName);cmd.Parameters.AddWithValue("@old",Path.GetFileName(archive));cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
